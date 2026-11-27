using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    /// <summary>
    /// Minimal H.264 writer over the Windows Media Foundation sink writer.
    /// MP4 export is optional; failures surface as a normal encoding error.
    /// </summary>
    public static class Mp4Writer
    {
        private const int MFVersion = 0x00020070;

        private static Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        private static Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        private static Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        private static Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        private static Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
        private static Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
        private static Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");

        public static void Write(RecordingResult result, string path)
        {
            if (result.FramePaths.Count == 0)
            {
                throw new InvalidOperationException("No frames to encode.");
            }

            int width = Math.Max(2, result.Width & ~1);
            int height = Math.Max(2, result.Height & ~1);
            int fps = Math.Clamp(result.Fps, 5, 30);
            int bitrate = Math.Clamp(width * height * fps / 6, 2_000_000, 30_000_000);

            int startup = MFStartup(MFVersion, 0);
            if (startup < 0)
            {
                Marshal.ThrowExceptionForHR(startup);
            }

            IMFSinkWriter? writer = null;
            try
            {
                int hr = MFCreateMediaType(out var outputType);
                Marshal.ThrowExceptionForHR(hr);
                outputType.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Video);
                outputType.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_H264);
                outputType.SetUINT32(ref MF_MT_AVG_BITRATE, (uint)bitrate);
                outputType.SetUINT32(ref MF_MT_INTERLACE_MODE, 2);
                outputType.SetUINT64(ref MF_MT_FRAME_SIZE, ((ulong)width << 32) | (uint)height);
                outputType.SetUINT64(ref MF_MT_FRAME_RATE, ((ulong)fps << 32) | 1);
                outputType.SetUINT64(ref MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);

                hr = MFCreateSinkWriterFromURL(path, IntPtr.Zero, null, out writer);
                Marshal.ThrowExceptionForHR(hr);

                hr = writer.AddStream(outputType, out uint streamIndex);
                Marshal.ThrowExceptionForHR(hr);

                hr = MFCreateMediaType(out var inputType);
                Marshal.ThrowExceptionForHR(hr);
                inputType.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Video);
                inputType.SetGUID(ref MF_MT_SUBTYPE, ref MFVideoFormat_RGB32);
                inputType.SetUINT32(ref MF_MT_INTERLACE_MODE, 2);
                inputType.SetUINT64(ref MF_MT_FRAME_SIZE, ((ulong)width << 32) | (uint)height);
                inputType.SetUINT64(ref MF_MT_FRAME_RATE, ((ulong)fps << 32) | 1);
                inputType.SetUINT64(ref MF_MT_PIXEL_ASPECT_RATIO, (1UL << 32) | 1);

                hr = writer.SetInputMediaType(streamIndex, inputType, null);
                Marshal.ThrowExceptionForHR(hr);
                hr = writer.BeginWriting();
                Marshal.ThrowExceptionForHR(hr);

                long frameDuration = 10_000_000L / fps;
                long time = 0;
                foreach (string framePath in result.FramePaths)
                {
                    if (!File.Exists(framePath))
                    {
                        continue;
                    }
                    var frame = ImageIO.DecodeBytes(File.ReadAllBytes(framePath));
                    WriteFrame(writer, streamIndex, frame, width, height, time, frameDuration);
                    time += frameDuration;
                }

                writer.DoFinalize();
            }
            finally
            {
                if (writer != null)
                {
                    Marshal.ReleaseComObject(writer);
                }
                MFShutdown();
            }
        }

        private static void WriteFrame(
            IMFSinkWriter writer,
            uint streamIndex,
            BitmapSource frame,
            int width,
            int height,
            long time,
            long duration)
        {
            int sourceWidth = frame.PixelWidth;
            int sourceHeight = frame.PixelHeight;
            int sourceStride = sourceWidth * 4;
            var converted = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var source = new byte[sourceStride * sourceHeight];
            converted.CopyPixels(source, sourceStride, 0);

            int destinationStride = width * 4;
            int bufferSize = destinationStride * height;
            int hr = MFCreateMemoryBuffer((uint)bufferSize, out var buffer);
            Marshal.ThrowExceptionForHR(hr);
            hr = buffer.Lock(out IntPtr pointer, out _, out _);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                unsafe
                {
                    byte* destination = (byte*)pointer;
                    for (int y = 0; y < height; y++)
                    {
                        int sourceY = sourceHeight - 1 - Math.Min(y, sourceHeight - 1);
                        int sourceRow = sourceY * sourceStride;
                        int destinationRow = y * destinationStride;
                        int copyWidth = Math.Min(width, sourceWidth) * 4;
                        for (int x = 0; x < copyWidth; x++)
                        {
                            destination[destinationRow + x] = source[sourceRow + x];
                        }
                        for (int x = copyWidth; x < destinationStride; x++)
                        {
                            destination[destinationRow + x] = 0;
                        }
                    }
                }
            }
            finally
            {
                buffer.Unlock();
            }

            hr = buffer.SetCurrentLength((uint)bufferSize);
            Marshal.ThrowExceptionForHR(hr);

            hr = MFCreateSample(out var sample);
            Marshal.ThrowExceptionForHR(hr);
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            hr = writer.WriteSample(streamIndex, sample);

            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buffer);
            Marshal.ThrowExceptionForHR(hr);
        }


        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateSample(out IMFSample sample);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string outputUrl,
            IntPtr byteStream,
            IMFAttributes? attributes,
            out IMFSinkWriter sinkWriter);

        [ComImport]
        [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int type);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
            [PreserveSig] int Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out uint value);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong value);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double value);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out uint length);
            [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, uint bufferSize, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint size);
            [PreserveSig] int GetBlob(ref Guid guidKey, byte[] buffer, uint bufferSize, out uint size);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr buffer, out uint size);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, uint value);
            [PreserveSig] int SetUINT64(ref Guid guidKey, ulong value);
            [PreserveSig] int SetDouble(ref Guid guidKey, double value);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid guidKey, byte[] buffer, uint size);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid guidKey, IntPtr value);
            [PreserveSig] int CopyAllItems(IMFAttributes destination);
        }

        [ComImport]
        [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType : IMFAttributes
        {
            [PreserveSig] int GetMajorType(out Guid majorType);
            [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);
            [PreserveSig] int IsEqual(IMFMediaType other, out uint flags);
            [PreserveSig] int GetRepresentation(Guid representation, out IntPtr value);
            [PreserveSig] int FreeRepresentation(Guid representation, IntPtr value);
        }

        [ComImport]
        [Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSinkWriter
        {
            [PreserveSig] int AddStream(IMFMediaType targetMediaType, out uint streamIndex);
            [PreserveSig] int SetInputMediaType(uint streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
            [PreserveSig] int BeginWriting();
            [PreserveSig] int WriteSample(uint streamIndex, IMFSample sample);
            [PreserveSig] int SendStreamTick(uint streamIndex, long timestamp);
            [PreserveSig] int PlaceMarker(uint streamIndex, IntPtr context);
            [PreserveSig] int NotifyEndOfSegment(uint streamIndex);
            [PreserveSig] int Flush(uint streamIndex);
            [PreserveSig] int DoFinalize();
            [PreserveSig] int GetServiceForStream(uint streamIndex, ref Guid service, ref Guid riid, out IntPtr value);
            [PreserveSig] int GetStatistics(uint streamIndex, IntPtr statistics);
        }

        [ComImport]
        [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            // IMFAttributes methods, flattened for the native vtable.
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int type);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
            [PreserveSig] int Compare(IMFAttributes theirs, int matchType, [MarshalAs(UnmanagedType.Bool)] out bool result);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out uint value);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out ulong value);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double value);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out uint length);
            [PreserveSig] int GetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, uint bufferSize, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out uint size);
            [PreserveSig] int GetBlob(ref Guid guidKey, byte[] buffer, uint bufferSize, out uint size);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr buffer, out uint size);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid guidKey, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, uint value);
            [PreserveSig] int SetUINT64(ref Guid guidKey, ulong value);
            [PreserveSig] int SetDouble(ref Guid guidKey, double value);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid guidKey, byte[] buffer, uint size);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid guidKey, IntPtr value);
            [PreserveSig] int CopyAllItems(IMFAttributes destination);

            // IMFSample methods start after the inherited attributes block.
            [PreserveSig] int GetSampleFlags(out uint flags);
            [PreserveSig] int SetSampleFlags(uint flags);
            [PreserveSig] int GetSampleTime(out long time);
            [PreserveSig] int SetSampleTime(long time);
            [PreserveSig] int GetSampleDuration(out long duration);
            [PreserveSig] int SetSampleDuration(long duration);
            [PreserveSig] int GetBufferCount(out uint count);
            [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
            [PreserveSig] int RemoveBufferByIndex(uint index);
            [PreserveSig] int RemoveAllBuffers();
            [PreserveSig] int GetTotalLength(out uint length);
            [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
        }

        [ComImport]
        [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out uint length);
            [PreserveSig] int SetCurrentLength(uint length);
            [PreserveSig] int GetMaxLength(out uint length);
        }
    }
}
