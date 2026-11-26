using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Shared-mode WASAPI capture. The event-driven API is intentionally avoided
    /// here: a short polling loop is enough for screen recordings and is much
    /// easier to tear down when a device disappears.
    /// </summary>
    public sealed class WasapiCaptureService : IDisposable
    {
        private const int AUDCLNT_SHAREMODE_SHARED = 0;
        private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const int DEVICE_STATE_ACTIVE = 0x00000001;
        private const int STGM_READ = 0;
        private const int CLSCTX_ALL = 23;

        private readonly bool _loopback;
        private readonly string? _deviceId;
        private readonly object _gate = new();
        private IMMDevice? _device;
        private IAudioClient? _audioClient;
        private IAudioCaptureClient? _captureClient;
        private Thread? _thread;
        private volatile bool _running;
        private int _sourceRate;
        private int _channels;
        private int _bits;
        private bool _isFloat;
        private int _lastLeft;
        private int _lastRight;
        private double _resamplePosition;

        public event Action<short[]>? SamplesAvailable;
        public bool IsRunning => _running;
        public string LastError { get; private set; } = "";

        public WasapiCaptureService(bool loopback, string? deviceId = null)
        {
            _loopback = loopback;
            _deviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        }

        public bool Start()
        {
            if (_running)
            {
                return true;
            }

            try
            {
                var enumerator = CreateEnumerator();
                IMMDevice? device = null;
                int hr;
                try
                {
                    if (_deviceId != null)
                    {
                        hr = enumerator.GetDevice(_deviceId, out device);
                        if (hr < 0)
                        {
                            device = null;
                        }
                    }

                    if (device == null)
                    {
                        hr = enumerator.GetDefaultAudioEndpoint(
                            _loopback ? 0 : 1,
                            0,
                            out device);
                        if (hr < 0 || device == null)
                        {
                            LastError = "No audio device is available.";
                            return false;
                        }
                    }
                }
                finally
                {
                    Release(enumerator);
                }

                _device = device;
                Guid iid = typeof(IAudioClient).GUID;
                hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out IntPtr clientPtr);
                if (hr < 0)
                {
                    LastError = "The audio device could not be activated.";
                    return false;
                }

                try
                {
                    _audioClient = (IAudioClient)Marshal.GetObjectForIUnknown(clientPtr);
                }
                finally
                {
                    Marshal.Release(clientPtr);
                }

                hr = _audioClient.GetMixFormat(out IntPtr formatPtr);
                if (hr < 0 || formatPtr == IntPtr.Zero)
                {
                    LastError = "The audio mix format is unavailable.";
                    return false;
                }

                try
                {
                    if (!ReadFormat(formatPtr))
                    {
                        LastError = "The audio format is not supported.";
                        return false;
                    }

                    uint flags = _loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0;
                    hr = _audioClient.Initialize(
                        AUDCLNT_SHAREMODE_SHARED,
                        flags,
                        1_000_000,
                        0,
                        formatPtr,
                        IntPtr.Zero);
                    if (hr < 0)
                    {
                        LastError = "The audio device could not be initialized.";
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(formatPtr);
                }

                Guid captureIid = typeof(IAudioCaptureClient).GUID;
                hr = _audioClient.GetService(ref captureIid, out IntPtr capturePtr);
                if (hr < 0)
                {
                    LastError = "Audio capture is unavailable.";
                    return false;
                }

                try
                {
                    _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
                }
                finally
                {
                    Marshal.Release(capturePtr);
                }

                hr = _audioClient.Start();
                if (hr < 0)
                {
                    LastError = "The audio device could not start.";
                    return false;
                }

                _running = true;
                _thread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = _loopback ? "CryoUtils system audio" : "CryoUtils microphone"
                };
                _thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Stop();
                return false;
            }
        }

        private bool ReadFormat(IntPtr pointer)
        {
            var wave = Marshal.PtrToStructure<WAVEFORMATEX>(pointer);
            _channels = Math.Max(1, (int)wave.nChannels);
            _sourceRate = Math.Max(8000, (int)wave.nSamplesPerSec);
            _bits = wave.wBitsPerSample;
            _isFloat = wave.wFormatTag == 3;
            if (wave.wFormatTag == 0xFFFE)
            {
                var extensible = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(pointer);
                _isFloat = extensible.SubFormat == MFAudioFormatIeeeFloat;
                _bits = extensible.Format.wBitsPerSample;
            }

            return _isFloat && _bits == 32 || !_isFloat && _bits == 16;
        }

        private void CaptureLoop()
        {
            int blockAlign = Math.Max(1, _channels * (_bits / 8));
            while (_running)
            {
                try
                {
                    bool hadData = false;
                    while (_running)
                    {
                        int hr = _captureClient!.GetNextPacketSize(out uint packetFrames);
                        if (hr < 0 || packetFrames == 0)
                        {
                            break;
                        }

                        hadData = true;
                        hr = _captureClient.GetBuffer(
                            out IntPtr data,
                            out uint frames,
                            out uint flags,
                            out _,
                            out _);
                        if (hr < 0)
                        {
                            break;
                        }

                        try
                        {
                            var converted = ConvertFrames(data, (int)frames, (flags & 0x2) != 0, blockAlign);
                            if (converted.Length > 0)
                            {
                                SamplesAvailable?.Invoke(converted);
                            }
                        }
                        finally
                        {
                            _captureClient.ReleaseBuffer(frames);
                        }
                    }

                    if (!hadData)
                    {
                        Thread.Sleep(10);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private short[] ConvertFrames(IntPtr data, int frames, bool silent, int blockAlign)
        {
            if (frames <= 0)
            {
                return Array.Empty<short>();
            }

            var stereo = new short[frames * 2];
            if (!silent && data != IntPtr.Zero)
            {
                if (_isFloat)
                {
                    unsafe
                    {
                        float* source = (float*)data;
                        for (int frame = 0; frame < frames; frame++)
                        {
                            int index = frame * _channels;
                            float left = source[index];
                            float right = _channels > 1 ? source[index + 1] : left;
                            stereo[frame * 2] = ToPcm(left);
                            stereo[frame * 2 + 1] = ToPcm(right);
                        }
                    }
                }
                else
                {
                    unsafe
                    {
                        short* source = (short*)data;
                        for (int frame = 0; frame < frames; frame++)
                        {
                            int index = frame * _channels;
                            stereo[frame * 2] = source[index];
                            stereo[frame * 2 + 1] = _channels > 1 ? source[index + 1] : source[index];
                        }
                    }
                }
            }

            return Resample(stereo);
        }

        private short[] Resample(short[] source)
        {
            if (_sourceRate == 48000)
            {
                return source;
            }

            int sourceFrames = source.Length / 2;
            double ratio = 48000.0 / _sourceRate;
            int targetFrames = Math.Max(1, (int)Math.Round(sourceFrames * ratio));
            var result = new short[targetFrames * 2];
            for (int frame = 0; frame < targetFrames; frame++)
            {
                double position = _resamplePosition + frame / ratio;
                int index = (int)Math.Floor(position);
                double fraction = position - index;
                int next = Math.Min(sourceFrames - 1, index + 1);
                index = Math.Clamp(index, 0, sourceFrames - 1);
                result[frame * 2] = (short)Math.Clamp(
                    source[index * 2] * (1 - fraction) + source[next * 2] * fraction,
                    short.MinValue,
                    short.MaxValue);
                result[frame * 2 + 1] = (short)Math.Clamp(
                    source[index * 2 + 1] * (1 - fraction) + source[next * 2 + 1] * fraction,
                    short.MinValue,
                    short.MaxValue);
            }

            _resamplePosition += sourceFrames / ratio - targetFrames;
            _lastLeft = source[^2];
            _lastRight = source[^1];
            return result;
        }

        private static short ToPcm(float value)
        {
            float clamped = Math.Clamp(value, -1f, 1f);
            return (short)Math.Round(clamped * short.MaxValue);
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _audioClient?.Stop();
            }
            catch
            {
            }

            _thread?.Join(700);
            _thread = null;

            Release(_captureClient);
            _captureClient = null;
            Release(_audioClient);
            _audioClient = null;
            Release(_device);
            _device = null;
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Lists active WASAPI endpoints. Output endpoints are used for system
        /// loopback and input endpoints for the microphone.
        /// </summary>
        public static IReadOnlyList<AudioDeviceInfo> GetDevices(bool input)
        {
            var devices = new List<AudioDeviceInfo>();
            IMMDeviceEnumerator? enumerator = null;
            IMMDeviceCollection? collection = null;
            IMMDevice? defaultDevice = null;
            try
            {
                enumerator = CreateEnumerator();
                string? defaultId = null;
                int defaultResult = enumerator.GetDefaultAudioEndpoint(
                    input ? 1 : 0,
                    0,
                    out defaultDevice);
                if (defaultResult >= 0 && defaultDevice != null)
                {
                    defaultDevice.GetId(out defaultId);
                }

                int hr = enumerator.EnumAudioEndpoints(
                    input ? 1 : 0,
                    DEVICE_STATE_ACTIVE,
                    out IntPtr collectionPointer);
                if (hr < 0 || collectionPointer == IntPtr.Zero)
                {
                    return devices;
                }

                try
                {
                    collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collectionPointer);
                }
                finally
                {
                    Marshal.Release(collectionPointer);
                }

                collection.GetCount(out uint count);
                for (uint index = 0; index < count; index++)
                {
                    if (collection.Item(index, out var device) < 0 || device == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (device.GetId(out string id) < 0 || string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        string name = GetFriendlyName(device);
                        devices.Add(new AudioDeviceInfo
                        {
                            Id = id,
                            Name = string.IsNullOrWhiteSpace(name) ? "Audio device" : name,
                            IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    finally
                    {
                        Release(device);
                    }
                }

                return devices
                    .OrderByDescending(device => device.IsDefault)
                    .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch
            {
                return devices;
            }
            finally
            {
                Release(defaultDevice);
                Release(collection);
                Release(enumerator);
            }
        }

        private static string GetFriendlyName(IMMDevice device)
        {
            IPropertyStore? store = null;
            try
            {
                if (device.OpenPropertyStore(STGM_READ, out store) < 0 || store == null)
                {
                    return "";
                }

                var key = FriendlyNameKey;
                if (store.GetValue(ref key, out PROPVARIANT value) < 0)
                {
                    return "";
                }

                try
                {
                    return value.vt == 31 && value.pointerValue != IntPtr.Zero
                        ? Marshal.PtrToStringUni(value.pointerValue) ?? ""
                        : "";
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            catch
            {
                return "";
            }
            finally
            {
                Release(store);
            }
        }

        private static IMMDeviceEnumerator CreateEnumerator()
        {
            Guid clsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
            Guid iid = typeof(IMMDeviceEnumerator).GUID;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iid, out IntPtr pointer);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            try
            {
                return (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }

        private static void Release(object? value)
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

        private static readonly Guid MFAudioFormatIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");
        private static readonly PROPERTYKEY FriendlyNameKey = new(
            new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            14);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;

            public PROPERTYKEY(Guid formatId, uint propertyId)
            {
                fmtid = formatId;
                pid = propertyId;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pointerValue;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEXTENSIBLE
        {
            public WAVEFORMATEX Format;
            public ushort Samples;
            public uint ChannelMask;
            public Guid SubFormat;
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int Commit();
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr interfacePointer);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }

        [ComImport]
        [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out uint bufferFrames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
        }

        [ComImport]
        [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out long devicePosition, out long qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int CoCreateInstance(
            ref Guid clsid,
            IntPtr outer,
            int clsContext,
            ref Guid iid,
            out IntPtr instance);

        [DllImport("ole32.dll", ExactSpelling = true)]
        private static extern int PropVariantClear(ref PROPVARIANT value);
    }
}
