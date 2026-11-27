using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Native Media Foundation default-camera preview for the recording bubble,
    /// avoiding Microsoft.Windows.SDK.NET MediaCapture overhead.
    /// </summary>
    public sealed class WebcamCaptureService : IDisposable
    {
        private readonly object _gate = new();
        private Thread? _thread;
        private IMFSourceReader? _reader;
        private IMFMediaSource? _source;
        private byte[]? _latest;
        private int _width;
        private int _height;
        private volatile bool _running;

        public bool IsRunning => _running;
        public string LastError { get; private set; } = "";

        public Task<bool> StartAsync()
        {
            if (_running)
            {
                return Task.FromResult(true);
            }

            return Task.Run(() => StartInternal());
        }

        private bool StartInternal()
        {
            IntPtr ppDevices = IntPtr.Zero;
            uint count = 0;
            try
            {
                MfRuntime.EnsureStarted();
                int hr = MfApi.MFCreateAttributes(out var enumAttrs, 1);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    enumAttrs.SetGuid(
                        MfGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                        MfGuids.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
                    hr = MfApi.MFEnumDeviceSources(enumAttrs, out ppDevices, out count);
                    Marshal.ThrowExceptionForHR(hr);
                }
                finally
                {
                    Marshal.ReleaseComObject(enumAttrs);
                }

                if (count == 0 || ppDevices == IntPtr.Zero)
                {
                    LastError = "No camera was found.";
                    return false;
                }

                IntPtr activatePtr = Marshal.ReadIntPtr(ppDevices, 0);
                var activate = (IMFActivate)Marshal.GetObjectForIUnknown(activatePtr);
                try
                {
                    Guid iidSource = typeof(IMFMediaSource).GUID;
                    hr = activate.ActivateObject(ref iidSource, out IntPtr srcPtr);
                    Marshal.ThrowExceptionForHR(hr);
                    _source = (IMFMediaSource)Marshal.GetObjectForIUnknown(srcPtr);
                    Marshal.Release(srcPtr);
                }
                finally
                {
                    Marshal.ReleaseComObject(activate);
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr ptr = Marshal.ReadIntPtr(ppDevices, (int)(i * IntPtr.Size));
                        if (ptr != IntPtr.Zero) Marshal.Release(ptr);
                    }
                    Marshal.FreeCoTaskMem(ppDevices);
                    ppDevices = IntPtr.Zero;
                }

                hr = MfApi.MFCreateAttributes(out var readerAttrs, 1);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    readerAttrs.SetUInt32(MfGuids.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
                    hr = MfApi.MFCreateSourceReaderFromMediaSource(_source, readerAttrs, out _reader);
                    Marshal.ThrowExceptionForHR(hr);
                }
                finally
                {
                    Marshal.ReleaseComObject(readerAttrs);
                }

                hr = MfApi.MFCreateMediaType(out var rgbType);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    rgbType.SetGuid(MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
                    rgbType.SetGuid(MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_RGB32);
                    hr = _reader.SetCurrentMediaType(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                        IntPtr.Zero,
                        rgbType);
                    Marshal.ThrowExceptionForHR(hr);
                }
                finally
                {
                    Marshal.ReleaseComObject(rgbType);
                }

                hr = _reader.GetCurrentMediaType(
                    unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                    out var actualType);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Guid fsKey = MfGuids.MF_MT_FRAME_SIZE;
                    actualType.GetUINT64(ref fsKey, out ulong packedSize);
                    _width = Math.Max(1, (int)(packedSize >> 32));
                    _height = Math.Max(1, (int)(packedSize & 0xFFFFFFFF));
                }
                finally
                {
                    Marshal.ReleaseComObject(actualType);
                }

                _running = true;
                _thread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "CryoUtils Webcam"
                };
                _thread.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Dispose();
                return false;
            }
        }

        private void CaptureLoop()
        {
            while (_running && _reader != null)
            {
                try
                {
                    int hr = _reader.ReadSample(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                        0,
                        out _,
                        out uint flags,
                        out _,
                        out var sample);
                    if (hr < 0 || (flags & MfGuids.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                    {
                        break;
                    }

                    if (sample == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    try
                    {
                        sample.ConvertToContiguousBuffer(out var buffer);
                        if (buffer != null)
                        {
                            try
                            {
                                buffer.Lock(out IntPtr ptr, out _, out uint len);
                                try
                                {
                                    int expected = _width * _height * 4;
                                    if (len >= expected && expected > 0)
                                    {
                                        var pixels = new byte[expected];
                                        Marshal.Copy(ptr, pixels, 0, expected);
                                        // Force alpha opaque on RGB32 webcam output
                                        for (int i = 3; i < expected; i += 4)
                                        {
                                            pixels[i] = 255;
                                        }
                                        lock (_gate)
                                        {
                                            _latest = pixels;
                                        }
                                    }
                                }
                                finally
                                {
                                    buffer.Unlock();
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(buffer);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(sample);
                    }
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(20);
                }
            }
        }

        public bool TryGetFrame(out byte[] pixels, out int width, out int height)
        {
            lock (_gate)
            {
                if (_latest == null)
                {
                    pixels = Array.Empty<byte>();
                    width = 0;
                    height = 0;
                    return false;
                }

                pixels = _latest;
                width = _width;
                height = _height;
                return true;
            }
        }

        public Task StopAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _running = false;
            _thread?.Join(700);
            _thread = null;

            if (_reader != null)
            {
                try { Marshal.ReleaseComObject(_reader); } catch { }
                _reader = null;
            }
            if (_source != null)
            {
                try { _source.Shutdown(); Marshal.ReleaseComObject(_source); } catch { }
                _source = null;
            }
            lock (_gate)
            {
                _latest = null;
            }
        }
    }
}
