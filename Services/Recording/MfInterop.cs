using System;
using System.Runtime.InteropServices;

namespace CryoUtils.Services.Recording
{
    internal static class MfGuids
    {
        public static readonly Guid MF_MT_AM_FORMAT_TYPE = new("73d1072d-1870-4174-a063-29ff4ff6c11e");
        public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
        public static readonly Guid MF_MT_MAX_KEYFRAME_SPACING = new("c16eb52b-73a1-476f-8d62-839d6a020652");
        public static readonly Guid MF_MT_AAC_PAYLOAD_TYPE = new("bfbabe79-7434-4d1c-94f0-72a3b9e17188");
        public static readonly Guid MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION = new("7632f0e6-9538-4d61-acda-ea29c8c14456");
        public static readonly Guid MF_MT_FIXED_SIZE_SAMPLES = new("b8ebefaf-b718-4e04-b0a9-116775e3321b");
        public static readonly Guid MF_MT_COMPRESSED = new("3afd0cee-18f2-4ba5-a110-8bea502e1f92");
        public static readonly Guid MF_MT_USER_DATA = new("b6bc765f-4c3b-40a4-bd51-2535b66fe09d");
        public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
        public static readonly Guid MF_MT_AUDIO_PREFER_WAVEFORMATEX = new("a901aaba-e037-458a-bdf6-545be2074042");

        public static readonly Guid WaveFormatEx = new("05589f81-c356-11ce-bf01-00aa0055595a");
        public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_AAC = new("000000A6-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_ADTS = new("00001610-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00AA00389B71");

        public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");

        public const int MF_VERSION = 0x00020070;
        public const int MFSTARTUP_FULL = 0;
        public const int MF_SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
        public const int MF_SOURCE_READER_FIRST_AUDIO_STREAM = unchecked((int)0xFFFFFFFD);
        public const int MF_SOURCE_READER_ALL_STREAMS = unchecked((int)0xFFFFFFFE);
        public const int MF_SOURCE_READER_ANY_STREAM = unchecked((int)0xFFFFFFFE);
        public const int MF_SOURCE_READERF_ENDOFSTREAM = 0x00000002;
        public const int MF_SOURCE_READERF_STREAMTICK = 0x00000100;
    }

    internal static class MfRuntime
    {
        private static readonly object Gate = new();
        private static bool _started;

        public static void EnsureStarted()
        {
            lock (Gate)
            {
                if (_started)
                {
                    return;
                }

                int hr = MFStartup(MfGuids.MF_VERSION, MfGuids.MFSTARTUP_FULL);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                _started = true;
            }
        }

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(int version, int flags);
    }

    internal static class MfAttributesExtensions
    {
        public static void SetGuid(this IMFAttributes attributes, Guid key, Guid value)
        {
            Guid localKey = key;
            Guid localValue = value;
            attributes.SetGUID(ref localKey, ref localValue);
        }

        public static void SetUInt32(this IMFAttributes attributes, Guid key, uint value)
        {
            Guid localKey = key;
            attributes.SetUINT32(ref localKey, value);
        }

        public static void SetUInt64(this IMFAttributes attributes, Guid key, ulong value)
        {
            Guid localKey = key;
            attributes.SetUINT64(ref localKey, value);
        }
    }

    internal static class MfApi
    {
        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMemoryBuffer(uint maxLength, out IMFMediaBuffer buffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateSample(out IMFSample sample);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MFCreateSinkWriterFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string outputUrl,
            IntPtr byteStream,
            IMFAttributes? attributes,
            out IMFSinkWriter sinkWriter);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MFCreateSourceReaderFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string url,
            IMFAttributes? attributes,
            out IMFSourceReader sourceReader);
    }

    [ComImport]
    [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
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
    internal interface IMFMediaType : IMFAttributes
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
    internal interface IMFSinkWriter
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
    [Guid("BF94C121-5B05-4E6F-8000-BA598961414D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFTransform
    {
        [PreserveSig] int GetStreamLimits(out uint inputMinimum, out uint inputMaximum, out uint outputMinimum, out uint outputMaximum);
        [PreserveSig] int GetStreamCount(out uint inputStreams, out uint outputStreams);
        [PreserveSig] int GetStreamIDs(uint inputIdSize, IntPtr inputIds, uint outputIdSize, IntPtr outputIds);
        [PreserveSig] int GetInputStreamInfo(uint inputStreamId, IntPtr streamInfo);
        [PreserveSig] int GetOutputStreamInfo(uint outputStreamId, IntPtr streamInfo);
        [PreserveSig] int GetAttributes(out IMFAttributes attributes);
        [PreserveSig] int GetInputStreamAttributes(uint inputStreamId, out IMFAttributes attributes);
        [PreserveSig] int GetOutputStreamAttributes(uint outputStreamId, out IMFAttributes attributes);
        [PreserveSig] int DeleteInputStream(uint streamId);
        [PreserveSig] int AddInputStreams(uint streamCount, IntPtr streamIds);
        [PreserveSig] int GetInputAvailableType(uint inputStreamId, uint typeIndex, out IMFMediaType type);
        [PreserveSig] int GetOutputAvailableType(uint outputStreamId, uint typeIndex, out IMFMediaType type);
        [PreserveSig] int SetInputType(uint inputStreamId, IMFMediaType? type, uint flags);
        [PreserveSig] int SetOutputType(uint outputStreamId, IMFMediaType? type, uint flags);
        [PreserveSig] int GetInputCurrentType(uint inputStreamId, out IMFMediaType type);
        [PreserveSig] int GetOutputCurrentType(uint outputStreamId, out IMFMediaType type);
        [PreserveSig] int GetInputStatus(uint inputStreamId, out uint flags);
        [PreserveSig] int GetOutputStatus(out uint flags);
        [PreserveSig] int SetOutputBounds(long lowerBound, long upperBound);
        [PreserveSig] int ProcessEvent(uint inputStreamId, IntPtr mediaEvent);
        [PreserveSig] int ProcessMessage(int message, IntPtr data);
        [PreserveSig] int ProcessInput(uint inputStreamId, IMFSample sample, uint flags);
        [PreserveSig] int ProcessOutput(uint flags, uint outputBufferCount, IntPtr outputBuffers, out uint status);
    }

    [ComImport]
    [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
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
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out uint length);
        [PreserveSig] int SetCurrentLength(uint length);
        [PreserveSig] int GetMaxLength(out uint length);
    }

    [ComImport]
    [Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);
        [PreserveSig] int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
        [PreserveSig] int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType mediaType);
        [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType mediaType);
        [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType mediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid timeFormat, IntPtr position);
        [PreserveSig] int ReadSample(
            uint streamIndex,
            uint controlFlags,
            out uint actualStreamIndex,
            out uint streamFlags,
            out long timestamp,
            out IMFSample? sample);
        [PreserveSig] int Flush(uint streamIndex);
        [PreserveSig] int GetServiceForStream(uint streamIndex, ref Guid service, ref Guid riid, out IntPtr value);
        [PreserveSig] int GetPresentationAttribute(uint streamIndex, ref Guid attribute, IntPtr value);
    }
}
