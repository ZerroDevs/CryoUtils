using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Encodes 48 kHz stereo PCM with the Windows Media Foundation AAC MFT.
    /// The sink writer cannot build a PCM-to-AAC topology on every Windows
    /// installation, so the encoder is activated by CLSID and its compressed
    /// samples are muxed through the sink writer as AAC input.
    /// </summary>
    internal sealed class AacMftEncoder : IDisposable
    {
        private const int SampleRate = 48_000;
        private const int Channels = 2;
        private const int BytesPerFrame = Channels * 2;
        private const int FramesPerChunk = 1_024;
        private const int AacFrameSamples = 1_024;
        private const int MftOutputProvidesSamples = 0x00000100;
        private const int MftMessageBeginStreaming = 0x10000000;
        private const int MftMessageEndStreaming = 0x10000001;
        private const int MftMessageCommandDrain = 0x00000001;
        private static readonly Guid AacEncoderClassId = new("93af0c51-2275-45d2-a35b-f2ba21caed00");

        private readonly IMFTransform _transform;
        private readonly IMFMediaType _outputType;
        private readonly List<short> _pending = new();
        private readonly MftOutputStreamInfo _outputInfo;
        private long _inputTime;
        private long _outputTime;
        private bool _began;
        private bool _draining;
        private bool _disposed;

        public IMFMediaType OutputType => _outputType;

        public AacMftEncoder()
        {
            MfRuntime.EnsureStarted();
            Type? classType = Type.GetTypeFromCLSID(AacEncoderClassId);
            if (classType == null)
            {
                throw new InvalidOperationException("The Microsoft AAC encoder is not registered.");
            }

            _transform = (IMFTransform)Activator.CreateInstance(classType)!;
            _outputType = FindOutputType(_transform);
            try
            {
                Check(_transform.SetOutputType(0, _outputType, 0), "AAC output type");
                Check(_transform.SetInputType(0, CreatePcmType(), 0), "AAC PCM input type");
                _outputInfo = GetOutputInfo(_transform);
                Check(_transform.ProcessMessage(MftMessageBeginStreaming, IntPtr.Zero), "start AAC encoder");
                _began = true;
            }
            catch
            {
                ReleaseCom(_outputType);
                ReleaseCom(_transform);
                throw;
            }
        }

        public void Encode(short[] samples, Action<IMFSample> write)
        {
            if (samples == null || samples.Length == 0 || _disposed)
            {
                return;
            }

            _pending.AddRange(samples);
            while (_pending.Count >= FramesPerChunk * Channels)
            {
                var chunk = new short[FramesPerChunk * Channels];
                _pending.CopyTo(0, chunk, 0, chunk.Length);
                _pending.RemoveRange(0, chunk.Length);
                ProcessChunk(chunk, write);
            }
        }

        public void Drain(Action<IMFSample> write)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending.Count > 0)
            {
                int frames = (_pending.Count + Channels - 1) / Channels;
                var chunk = new short[frames * Channels];
                _pending.CopyTo(chunk);
                _pending.Clear();
                ProcessChunk(chunk, write);
            }

            if (!_draining && _began)
            {
                Check(_transform.ProcessMessage(MftMessageCommandDrain, IntPtr.Zero), "drain AAC encoder");
                _draining = true;
            }

            DrainOutput(write);
        }

        private void ProcessChunk(short[] chunk, Action<IMFSample> write)
        {
            int frames = chunk.Length / Channels;
            int byteCount = frames * BytesPerFrame;
            Check(MfApi.MFCreateMemoryBuffer((uint)byteCount, out var buffer), "create AAC input buffer");
            try
            {
                Check(buffer.Lock(out IntPtr pointer, out _, out _), "lock AAC input buffer");
                try
                {
                    Marshal.Copy(chunk, 0, pointer, chunk.Length);
                }
                finally
                {
                    buffer.Unlock();
                }

                Check(buffer.SetCurrentLength((uint)byteCount), "set AAC input length");
                Check(MfApi.MFCreateSample(out var sample), "create AAC input sample");
                try
                {
                    Check(sample.AddBuffer(buffer), "add AAC input buffer");
                    long duration = (long)byteCount / BytesPerFrame * 10_000_000L / SampleRate;
                    Check(sample.SetSampleTime(_inputTime), "set AAC input timestamp");
                    Check(sample.SetSampleDuration(duration), "set AAC input duration");
                    _inputTime += duration;
                    Check(_transform.ProcessInput(0, sample, 0), "encode AAC input");
                }
                finally
                {
                    ReleaseCom(sample);
                }
            }
            finally
            {
                ReleaseCom(buffer);
            }

            DrainOutput(write);
        }

        private void DrainOutput(Action<IMFSample> write)
        {
            for (int pass = 0; pass < 256; pass++)
            {
                IMFSample? sample = null;
                IntPtr samplePointer = IntPtr.Zero;
                IntPtr bufferPointer = IntPtr.Zero;
                bool providesSamples = (_outputInfo.Flags & MftOutputProvidesSamples) != 0;
                if (!providesSamples)
                {
                    Check(
                        MfApi.MFCreateMemoryBuffer(
                            Math.Max(16_384u, _outputInfo.Size),
                            out var buffer),
                        "create AAC output buffer");
                    try
                    {
                        Check(MfApi.MFCreateSample(out sample), "create AAC output sample");
                        Check(sample.AddBuffer(buffer), "add AAC output buffer");
                        bufferPointer = Marshal.GetIUnknownForObject(sample);
                    }
                    finally
                    {
                        ReleaseCom(buffer);
                    }
                }

                var data = new MftOutputDataBuffer
                {
                    StreamId = 0,
                    Sample = bufferPointer,
                    Status = 0,
                    Events = IntPtr.Zero
                };
                IntPtr dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<MftOutputDataBuffer>());
                int hr;
                try
                {
                    Marshal.StructureToPtr(data, dataPointer, false);
                    hr = _transform.ProcessOutput(0, 1, dataPointer, out _);
                    data = Marshal.PtrToStructure<MftOutputDataBuffer>(dataPointer);
                }
                finally
                {
                    Marshal.FreeHGlobal(dataPointer);
                    if (bufferPointer != IntPtr.Zero)
                    {
                        Marshal.Release(bufferPointer);
                    }
                }

                if (hr == unchecked((int)0xC00D6D72)) // MF_E_TRANSFORM_NEED_MORE_INPUT
                {
                    ReleaseCom(sample);
                    return;
                }

                if (hr < 0)
                {
                    ReleaseCom(sample);
                    throw new InvalidOperationException($"AAC encode failed (0x{hr:X8}).");
                }

                IMFSample? output = sample;
                if (providesSamples)
                {
                    if (data.Sample == IntPtr.Zero)
                    {
                        return;
                    }
                    output = (IMFSample)Marshal.GetObjectForIUnknown(data.Sample);
                    samplePointer = data.Sample;
                }

                if (output == null)
                {
                    return;
                }

                try
                {
                    long duration = AacFrameSamples * 10_000_000L / SampleRate;
                    output.SetSampleTime(_outputTime);
                    output.SetSampleDuration(duration);
                    _outputTime += duration;
                    write(output);
                }
                finally
                {
                    if (providesSamples && samplePointer != IntPtr.Zero)
                    {
                        Marshal.Release(samplePointer);
                    }
                    ReleaseCom(output);
                }
            }
        }

        private static IMFMediaType FindOutputType(IMFTransform transform)
        {
            IMFMediaType? fallback = null;
            for (uint index = 0; ; index++)
            {
                int hr = transform.GetOutputAvailableType(0, index, out var type);
                if (hr < 0)
                {
                    break;
                }

                type.GetUINT32(MfGuids.MF_MT_AUDIO_NUM_CHANNELS, out uint channels);
                type.GetUINT32(MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND, out uint rate);
                if (channels == Channels && rate == SampleRate)
                {
                    ReleaseCom(fallback);
                    return type;
                }

                if (fallback == null)
                {
                    fallback = type;
                }
                else
                {
                    ReleaseCom(type);
                }
            }

            if (fallback != null)
            {
                return fallback;
            }

            throw new InvalidOperationException("The AAC encoder offered no output type.");
        }

        private static IMFMediaType CreatePcmType()
        {
            Check(MfApi.MFCreateMediaType(out var type), "create AAC PCM type");
            type.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Audio);
            type.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFAudioFormat_PCM);
            type.SetUInt32(MfGuids.MF_MT_AUDIO_NUM_CHANNELS, Channels);
            type.SetUInt32(MfGuids.MF_MT_AUDIO_SAMPLES_PER_SECOND, SampleRate);
            type.SetUInt32(MfGuids.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            type.SetUInt32(MfGuids.MF_MT_AUDIO_BLOCK_ALIGNMENT, BytesPerFrame);
            type.SetUInt32(MfGuids.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, SampleRate * BytesPerFrame);
            type.SetUInt32(MfGuids.MF_MT_AVG_BITRATE, SampleRate * BytesPerFrame * 8);
            type.SetUInt32(MfGuids.MF_MT_COMPRESSED, 0);
            type.SetUInt32(MfGuids.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
            type.SetUInt32(MfGuids.MF_MT_FIXED_SIZE_SAMPLES, 1);
            type.SetUInt32(MfGuids.MF_MT_AUDIO_PREFER_WAVEFORMATEX, 1);
            type.SetGuid(MfGuids.MF_MT_AM_FORMAT_TYPE, MfGuids.WaveFormatEx);
            return type;
        }

        private static MftOutputStreamInfo GetOutputInfo(IMFTransform transform)
        {
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MftOutputStreamInfo>());
            try
            {
                Check(transform.GetOutputStreamInfo(0, pointer), "read AAC output info");
                return Marshal.PtrToStructure<MftOutputStreamInfo>(pointer);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static void Check(int hr, string stage)
        {
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr, new IntPtr(-1));
                throw new InvalidOperationException($"{stage} failed (0x{hr:X8}).");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_began)
            {
                try
                {
                    _transform.ProcessMessage(MftMessageEndStreaming, IntPtr.Zero);
                }
                catch
                {
                }
            }

            ReleaseCom(_outputType);
            ReleaseCom(_transform);
        }

        private static void ReleaseCom(object? value)
        {
            if (value != null)
            {
                try
                {
                    Marshal.ReleaseComObject(value);
                }
                catch
                {
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftOutputStreamInfo
        {
            public uint Flags;
            public uint Size;
            public uint Alignment;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftOutputDataBuffer
        {
            public uint StreamId;
            public IntPtr Sample;
            public uint Status;
            public IntPtr Events;
        }
    }
}
