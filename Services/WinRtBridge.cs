using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;

namespace CryoUtils.Services
{
    /// <summary>
    /// Minimal WinRT COM ABI bridge replacing the 24.2 MB Microsoft.Windows.SDK.NET projection
    /// for Windows OCR, Windows.Data.Pdf rendering, and Windows.Graphics.Capture.
    /// </summary>
    internal static class WinRtBridge
    {
        private static int _roInitialized;

        // Well-known WinRT / COM IIDs verified from Windows SDK WinMetadata
        private static readonly Guid IidIAsyncInfo = new("00000036-0000-0000-C000-000000000046");
        private static readonly Guid IidIRandomAccessStream = new("905A0FE1-BC53-11DF-8C49-001E4FC686DA");
        private static readonly Guid IidIBufferFactory = new("71AF914D-C10F-484B-BC50-14BC623B3A27");
        private static readonly Guid IidIBufferByteAccess = new("905A0FEF-BC53-11DF-8C49-001E4FC686DA");
        private static readonly Guid IidIBuffer = new("905A0FE0-BC53-11DF-8C49-001E4FC686DA");
        private static readonly Guid IidISoftwareBitmapStatics = new("DF0385DB-672F-4A9D-806E-C2442F343E86");
        private static readonly Guid IidIClosable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
        private static readonly Guid IidIOcrEngineStatics = new("5BFFA85A-3384-3540-9940-699120D428A8");
        private static readonly Guid IidIPdfDocumentStatics = new("433A0B5F-C007-4788-90F2-08143D922599");
        private static readonly Guid IidIGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid IidIGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        private static readonly Guid IidIDirect3D11CaptureFramePoolStatics2 = new("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
        private static readonly Guid IidIGraphicsCaptureSession2 = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");

        [StructLayout(LayoutKind.Sequential)]
        public struct SizeInt32
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WinRtSize
        {
            public float Width;
            public float Height;
        }

        public static void EnsureRoInitialized()
        {
            if (Interlocked.Exchange(ref _roInitialized, 1) == 0)
            {
                // RO_INIT_MULTITHREADED = 1; RPC_E_CHANGED_MODE is acceptable on STA UI threads.
                int hr = RoInitialize(1);
                if (hr < 0 && hr != unchecked((int)0x80010106))
                {
                    Marshal.ThrowExceptionForHR(hr);
                }
            }
        }

        private static unsafe IntPtr* Vtbl(IntPtr obj) => *(IntPtr**)obj;

        public static unsafe IntPtr QueryInterface(IntPtr obj, in Guid iid)
        {
            if (obj == IntPtr.Zero) return IntPtr.Zero;
            Guid g = iid;
            void* result = null;
            var qi = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, void**, int>)Vtbl(obj)[0];
            int hr = qi(obj, &g, &result);
            return hr >= 0 ? (IntPtr)result : IntPtr.Zero;
        }

        public static unsafe void SafeRelease(ref IntPtr obj)
        {
            if (obj != IntPtr.Zero)
            {
                var rel = (delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(obj)[2];
                rel(obj);
                obj = IntPtr.Zero;
            }
        }

        public static unsafe void CloseAndRelease(ref IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            IntPtr closable = QueryInterface(obj, IidIClosable);
            if (closable != IntPtr.Zero)
            {
                try
                {
                    var close = (delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(closable)[6];
                    close(closable);
                }
                catch
                {
                }
                SafeRelease(ref closable);
            }
            SafeRelease(ref obj);
        }

        private static IntPtr GetActivationFactory(string className, in Guid iid)
        {
            EnsureRoInitialized();
            int hr = WindowsCreateString(className, (uint)className.Length, out IntPtr hStr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                Guid g = iid;
                hr = RoGetActivationFactory(hStr, ref g, out IntPtr factory);
                Marshal.ThrowExceptionForHR(hr);
                return factory;
            }
            finally
            {
                WindowsDeleteString(hStr);
            }
        }

        private static IntPtr ActivateInstance(string className)
        {
            EnsureRoInitialized();
            int hr = WindowsCreateString(className, (uint)className.Length, out IntPtr hStr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr = RoActivateInstance(hStr, out IntPtr instance);
                Marshal.ThrowExceptionForHR(hr);
                return instance;
            }
            finally
            {
                WindowsDeleteString(hStr);
            }
        }

        private static unsafe string ReadAndFreeHString(IntPtr hStr)
        {
            if (hStr == IntPtr.Zero) return "";
            try
            {
                char* ptr = WindowsGetStringRawBuffer(hStr, out uint length);
                return ptr != null && length > 0 ? new string(ptr, 0, (int)length) : "";
            }
            finally
            {
                WindowsDeleteString(hStr);
            }
        }

        private static unsafe IntPtr CallGetResultsPtr(IntPtr asyncOp)
        {
            var getResults = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(asyncOp)[8];
            IntPtr result = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(getResults(asyncOp, &result));
            return result;
        }

        private static unsafe void CallGetResultsAction(IntPtr asyncAction)
        {
            var getResults = (delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(asyncAction)[8];
            Marshal.ThrowExceptionForHR(getResults(asyncAction));
        }

        private static unsafe int PollAsyncInfo(IntPtr asyncInfo, out int errHr)
        {
            var getStatus = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(asyncInfo)[7];
            var getErrorCode = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(asyncInfo)[8];
            int status = 0;
            Marshal.ThrowExceptionForHR(getStatus(asyncInfo, &status));
            int err = 0;
            if (status == 3)
            {
                getErrorCode(asyncInfo, &err);
            }
            errHr = err;
            return status;
        }

        private static async Task WaitAsyncInfoAsync(IntPtr asyncObj)
        {
            IntPtr info = QueryInterface(asyncObj, IidIAsyncInfo);
            if (info == IntPtr.Zero)
            {
                throw new InvalidOperationException("WinRT async object does not implement IAsyncInfo.");
            }

            try
            {
                while (true)
                {
                    int status = PollAsyncInfo(info, out int errHr);
                    if (status == 1) return;
                    if (status == 2) throw new TaskCanceledException();
                    if (status == 3) Marshal.ThrowExceptionForHR(errHr != 0 ? errHr : unchecked((int)0x80004005));
                    await Task.Delay(5).ConfigureAwait(false);
                }
            }
            finally
            {
                SafeRelease(ref info);
            }
        }

        private static async Task<IntPtr> AwaitAsyncOperationPtr(IntPtr asyncOp)
        {
            try
            {
                await WaitAsyncInfoAsync(asyncOp).ConfigureAwait(false);
                return CallGetResultsPtr(asyncOp);
            }
            finally
            {
                SafeRelease(ref asyncOp);
            }
        }

        private static async Task AwaitAsyncAction(IntPtr asyncAction)
        {
            try
            {
                await WaitAsyncInfoAsync(asyncAction).ConfigureAwait(false);
                CallGetResultsAction(asyncAction);
            }
            finally
            {
                SafeRelease(ref asyncAction);
            }
        }

        private static IntPtr CreateRandomAccessStreamFromBytes(byte[]? data, out IStream? backingStream)
        {
            EnsureRoInitialized();
            IStream? iStream = SHCreateMemStream(data, (uint)(data?.Length ?? 0));
            if (iStream == null)
            {
                throw new OutOfMemoryException("Failed to create Win32 memory IStream.");
            }

            backingStream = iStream;
            Guid iid = IidIRandomAccessStream;
            int hr = CreateRandomAccessStreamOverStream(iStream, 0, ref iid, out IntPtr ras);
            Marshal.ThrowExceptionForHR(hr);
            return ras;
        }

        private static byte[] ReadAllBytesFromIStream(IStream stream)
        {
            stream.Seek(0, 0, IntPtr.Zero);
            stream.Stat(out System.Runtime.InteropServices.ComTypes.STATSTG stat, 1);
            int total = checked((int)stat.cbSize);
            byte[] buffer = new byte[total];
            int offset = 0;
            IntPtr readPtr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                while (offset < total)
                {
                    int chunk = Math.Min(65536, total - offset);
                    byte[] temp = chunk == total && offset == 0 ? buffer : new byte[chunk];
                    stream.Read(temp, chunk, readPtr);
                    int got = Marshal.ReadInt32(readPtr);
                    if (got <= 0) break;
                    if (!ReferenceEquals(temp, buffer))
                    {
                        Buffer.BlockCopy(temp, 0, buffer, offset, got);
                    }
                    offset += got;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(readPtr);
            }

            if (offset == total) return buffer;
            Array.Resize(ref buffer, offset);
            return buffer;
        }

        // ==================== Windows.Media.Ocr ====================
        private static unsafe IntPtr CreateSoftwareBitmapFromPixels(byte[] bgraPixels, int width, int height)
        {
            IntPtr sbStatics = GetActivationFactory("Windows.Graphics.Imaging.SoftwareBitmap", IidISoftwareBitmapStatics);
            IntPtr bufFactory = GetActivationFactory("Windows.Storage.Streams.Buffer", IidIBufferFactory);
            IntPtr buffer = IntPtr.Zero;
            try
            {
                var createBuf = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(bufFactory)[6];
                Marshal.ThrowExceptionForHR(createBuf(bufFactory, (uint)bgraPixels.Length, &buffer));

                IntPtr byteAccess = QueryInterface(buffer, IidIBufferByteAccess);
                try
                {
                    var getPtr = (delegate* unmanaged[Stdcall]<IntPtr, byte**, int>)Vtbl(byteAccess)[3];
                    byte* dstPtr = null;
                    Marshal.ThrowExceptionForHR(getPtr(byteAccess, &dstPtr));
                    Marshal.Copy(bgraPixels, 0, (IntPtr)dstPtr, bgraPixels.Length);
                }
                finally
                {
                    SafeRelease(ref byteAccess);
                }

                IntPtr ibuf = QueryInterface(buffer, IidIBuffer);
                try
                {
                    var putLen = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(ibuf)[8];
                    Marshal.ThrowExceptionForHR(putLen(ibuf, (uint)bgraPixels.Length));

                    var createCopy = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, int, int, IntPtr*, int>)Vtbl(sbStatics)[10];
                    IntPtr softwareBitmap = IntPtr.Zero;
                    Marshal.ThrowExceptionForHR(createCopy(sbStatics, ibuf, 87, width, height, 1, &softwareBitmap));
                    return softwareBitmap;
                }
                finally
                {
                    SafeRelease(ref ibuf);
                }
            }
            finally
            {
                SafeRelease(ref buffer);
                SafeRelease(ref bufFactory);
                SafeRelease(ref sbStatics);
            }
        }

        private static unsafe List<IntPtr> CreateOcrEngines()
        {
            IntPtr ocrStatics = GetActivationFactory("Windows.Media.Ocr.OcrEngine", IidIOcrEngineStatics);
            var engines = new List<IntPtr>();
            try
            {
                var getAvailLangs = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(ocrStatics)[7];
                var tryCreateLang = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(ocrStatics)[9];
                var tryCreateProfile = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(ocrStatics)[10];

                IntPtr langList = IntPtr.Zero;
                if (getAvailLangs(ocrStatics, &langList) >= 0 && langList != IntPtr.Zero)
                {
                    try
                    {
                        var getSize = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(langList)[7];
                        var getAt = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(langList)[6];
                        uint count = 0;
                        if (getSize(langList, &count) >= 0)
                        {
                            for (uint i = 0; i < count; i++)
                            {
                                IntPtr lang = IntPtr.Zero;
                                if (getAt(langList, i, &lang) >= 0 && lang != IntPtr.Zero)
                                {
                                    try
                                    {
                                        IntPtr eng = IntPtr.Zero;
                                        if (tryCreateLang(ocrStatics, lang, &eng) >= 0 && eng != IntPtr.Zero)
                                        {
                                            engines.Add(eng);
                                        }
                                    }
                                    finally { SafeRelease(ref lang); }
                                }
                            }
                        }
                    }
                    finally { SafeRelease(ref langList); }
                }

                if (engines.Count == 0)
                {
                    IntPtr fallback = IntPtr.Zero;
                    if (tryCreateProfile(ocrStatics, &fallback) >= 0 && fallback != IntPtr.Zero)
                    {
                        engines.Add(fallback);
                    }
                }
                return engines;
            }
            finally
            {
                SafeRelease(ref ocrStatics);
            }
        }

        private static unsafe IntPtr StartRecognizeAsync(IntPtr engine, IntPtr softwareBitmap)
        {
            var recAsync = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(engine)[6];
            IntPtr op = IntPtr.Zero;
            return recAsync(engine, softwareBitmap, &op) >= 0 ? op : IntPtr.Zero;
        }

        public static async Task<string> RecognizeOcrAsync(byte[] bgraPixels, int width, int height)
        {
            IntPtr softwareBitmap = CreateSoftwareBitmapFromPixels(bgraPixels, width, height);
            var engines = CreateOcrEngines();
            if (engines.Count == 0)
            {
                CloseAndRelease(ref softwareBitmap);
                throw new InvalidOperationException("No Windows OCR language pack is installed.");
            }

            try
            {
                string best = "";
                foreach (IntPtr eng in engines)
                {
                    try
                    {
                        IntPtr op = StartRecognizeAsync(eng, softwareBitmap);
                        if (op == IntPtr.Zero) continue;

                        IntPtr res = await AwaitAsyncOperationPtr(op).ConfigureAwait(false);
                        if (res == IntPtr.Zero) continue;
                        try
                        {
                            string candidate = ReadOcrResultText(res);
                            if (candidate.Length > best.Length) best = candidate;
                        }
                        finally { SafeRelease(ref res); }
                    }
                    catch
                    {
                    }
                }
                return best.Trim();
            }
            finally
            {
                for (int i = 0; i < engines.Count; i++)
                {
                    IntPtr eng = engines[i];
                    SafeRelease(ref eng);
                }
                CloseAndRelease(ref softwareBitmap);
            }
        }

        public static unsafe uint GetOcrMaxImageDimension()
        {
            IntPtr ocrStatics = GetActivationFactory("Windows.Media.Ocr.OcrEngine", IidIOcrEngineStatics);
            try
            {
                var getMax = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(ocrStatics)[6];
                uint maxDim = 4096;
                getMax(ocrStatics, &maxDim);
                return maxDim > 0 ? maxDim : 4096u;
            }
            finally { SafeRelease(ref ocrStatics); }
        }

        private static unsafe string ReadOcrResultText(IntPtr ocrResult)
        {
            var lines = new List<string>();
            var getLines = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(ocrResult)[6];
            var getFullText = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(ocrResult)[8];

            IntPtr linesVec = IntPtr.Zero;
            if (getLines(ocrResult, &linesVec) >= 0 && linesVec != IntPtr.Zero)
            {
                try
                {
                    var getSize = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(linesVec)[7];
                    var getAt = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(linesVec)[6];
                    uint count = 0;
                    if (getSize(linesVec, &count) >= 0)
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            IntPtr line = IntPtr.Zero;
                            if (getAt(linesVec, i, &line) >= 0 && line != IntPtr.Zero)
                            {
                                try
                                {
                                    var getText = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(line)[7];
                                    IntPtr hText = IntPtr.Zero;
                                    if (getText(line, &hText) >= 0)
                                    {
                                        lines.Add(ReadAndFreeHString(hText));
                                    }
                                }
                                finally { SafeRelease(ref line); }
                            }
                        }
                    }
                }
                finally { SafeRelease(ref linesVec); }
            }

            if (lines.Count > 0) return string.Join(Environment.NewLine, lines);
            IntPtr hFull = IntPtr.Zero;
            return getFullText(ocrResult, &hFull) >= 0 ? ReadAndFreeHString(hFull) : "";
        }

        // ==================== Windows.Data.Pdf ====================
        private static unsafe IntPtr StartLoadPdfAsync(IntPtr pdfStatics, IntPtr ras)
        {
            var loadStreamAsync = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(pdfStatics)[8];
            IntPtr op = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(loadStreamAsync(pdfStatics, ras, &op));
            return op;
        }

        private static unsafe uint ReadPdfPageCount(IntPtr doc)
        {
            var getCount = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(doc)[7];
            uint count = 0;
            Marshal.ThrowExceptionForHR(getCount(doc, &count));
            return count;
        }

        private static unsafe IntPtr StartRenderPdfPage(
            IntPtr doc, uint pageIndex, double scale,
            out IntPtr page, out IntPtr opts, out IntPtr outRas, out IStream? outIStream)
        {
            var getPage = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(doc)[6];
            IntPtr p = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(getPage(doc, pageIndex, &p));
            page = p;

            var getSize = (delegate* unmanaged[Stdcall]<IntPtr, WinRtSize*, int>)Vtbl(p)[10];
            WinRtSize size = default;
            Marshal.ThrowExceptionForHR(getSize(p, &size));

            opts = ActivateInstance("Windows.Data.Pdf.PdfPageRenderOptions");
            var putW = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(opts)[9];
            var putH = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(opts)[11];
            Marshal.ThrowExceptionForHR(putW(opts, (uint)Math.Max(100, size.Width * scale)));
            Marshal.ThrowExceptionForHR(putH(opts, (uint)Math.Max(100, size.Height * scale)));

            outRas = CreateRandomAccessStreamFromBytes(null, out outIStream);
            var renderAsync = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)Vtbl(p)[7];
            IntPtr renderOp = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(renderAsync(p, outRas, opts, &renderOp));
            return renderOp;
        }

        public static async Task<uint> GetPdfPageCountAsync(string pdfPath)
        {
            byte[] bytes = await File.ReadAllBytesAsync(pdfPath).ConfigureAwait(false);
            IntPtr ras = CreateRandomAccessStreamFromBytes(bytes, out var backing);
            IntPtr pdfStatics = GetActivationFactory("Windows.Data.Pdf.PdfDocument", IidIPdfDocumentStatics);
            IntPtr doc = IntPtr.Zero;
            try
            {
                doc = await AwaitAsyncOperationPtr(StartLoadPdfAsync(pdfStatics, ras)).ConfigureAwait(false);
                return ReadPdfPageCount(doc);
            }
            finally
            {
                SafeRelease(ref doc);
                SafeRelease(ref pdfStatics);
                CloseAndRelease(ref ras);
                if (backing != null) Marshal.ReleaseComObject(backing);
            }
        }

        public static async Task RenderPdfPagesAsync(
            string pdfPath,
            double scale,
            Action<uint, byte[]> onPagePngBytes,
            ISet<uint>? pages = null)
        {
            byte[] bytes = await File.ReadAllBytesAsync(pdfPath).ConfigureAwait(false);
            IntPtr ras = CreateRandomAccessStreamFromBytes(bytes, out var docStream);
            IntPtr pdfStatics = GetActivationFactory("Windows.Data.Pdf.PdfDocument", IidIPdfDocumentStatics);
            IntPtr doc = IntPtr.Zero;
            try
            {
                doc = await AwaitAsyncOperationPtr(StartLoadPdfAsync(pdfStatics, ras)).ConfigureAwait(false);
                uint pageCount = ReadPdfPageCount(doc);

                for (uint i = 0; i < pageCount; i++)
                {
                    if (pages != null && !pages.Contains(i)) continue;

                    IntPtr page = IntPtr.Zero;
                    IntPtr opts = IntPtr.Zero;
                    IntPtr outRas = IntPtr.Zero;
                    IStream? outIStream = null;
                    try
                    {
                        IntPtr renderOp = StartRenderPdfPage(doc, i, scale, out page, out opts, out outRas, out outIStream);
                        await AwaitAsyncAction(renderOp).ConfigureAwait(false);

                        byte[] pngBytes = ReadAllBytesFromIStream(outIStream!);
                        if (pngBytes.Length > 0)
                        {
                            onPagePngBytes(i, pngBytes);
                        }
                    }
                    finally
                    {
                        CloseAndRelease(ref outRas);
                        if (outIStream != null) Marshal.ReleaseComObject(outIStream);
                        SafeRelease(ref opts);
                        CloseAndRelease(ref page);
                    }
                }
            }
            finally
            {
                SafeRelease(ref doc);
                SafeRelease(ref pdfStatics);
                CloseAndRelease(ref ras);
                if (docStream != null) Marshal.ReleaseComObject(docStream);
            }
        }

        // ==================== Windows.Graphics.Capture ====================
        public static unsafe IntPtr CreateCaptureItemForMonitor(IntPtr hMonitor, out SizeInt32 size)
        {
            IntPtr interop = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", IidIGraphicsCaptureItemInterop);
            try
            {
                var createForMonitor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Vtbl(interop)[4];
                Guid iid = IidIGraphicsCaptureItem;
                IntPtr item = IntPtr.Zero;
                Marshal.ThrowExceptionForHR(createForMonitor(interop, hMonitor, &iid, &item));

                SizeInt32 sz = default;
                var getSize = (delegate* unmanaged[Stdcall]<IntPtr, SizeInt32*, int>)Vtbl(item)[7];
                getSize(item, &sz);
                size = sz;
                return item;
            }
            finally { SafeRelease(ref interop); }
        }

        public static unsafe IntPtr CreateFreeThreadedFramePool(IntPtr d3dWinRtDevice, int bufferCount, SizeInt32 size)
        {
            IntPtr statics2 = GetActivationFactory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IidIDirect3D11CaptureFramePoolStatics2);
            try
            {
                var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, SizeInt32, IntPtr*, int>)Vtbl(statics2)[6];
                IntPtr pool = IntPtr.Zero;
                Marshal.ThrowExceptionForHR(create(statics2, d3dWinRtDevice, 87, bufferCount, size, &pool));
                return pool;
            }
            finally { SafeRelease(ref statics2); }
        }

        public static unsafe long AddFrameArrivedHandler(IntPtr framePool, IntPtr handlerComPtr)
        {
            var add = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)Vtbl(framePool)[8];
            long token = 0;
            Marshal.ThrowExceptionForHR(add(framePool, handlerComPtr, &token));
            return token;
        }

        public static unsafe void RemoveFrameArrivedHandler(IntPtr framePool, long token)
        {
            if (framePool == IntPtr.Zero || token == 0) return;
            var remove = (delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(framePool)[9];
            remove(framePool, token);
        }

        public static unsafe IntPtr CreateCaptureSession(IntPtr framePool, IntPtr captureItem)
        {
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(framePool)[10];
            IntPtr session = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(create(framePool, captureItem, &session));

            IntPtr session2 = QueryInterface(session, IidIGraphicsCaptureSession2);
            if (session2 != IntPtr.Zero)
            {
                try
                {
                    var putCursor = (delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Vtbl(session2)[7];
                    putCursor(session2, 1);
                }
                catch { }
                SafeRelease(ref session2);
            }
            return session;
        }

        public static unsafe void StartCaptureSession(IntPtr session)
        {
            var start = (delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(session)[6];
            Marshal.ThrowExceptionForHR(start(session));
        }

        public static unsafe IntPtr TryGetNextCaptureFrame(IntPtr framePool)
        {
            var tryGet = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(framePool)[7];
            IntPtr frame = IntPtr.Zero;
            return tryGet(framePool, &frame) >= 0 ? frame : IntPtr.Zero;
        }

        public static unsafe IntPtr GetCaptureFrameSurface(IntPtr frame)
        {
            var getSurface = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(frame)[6];
            IntPtr surface = IntPtr.Zero;
            return getSurface(frame, &surface) >= 0 ? surface : IntPtr.Zero;
        }

        private static unsafe IntPtr StartSoftwareBitmapFromSurfaceAsync(IntPtr direct3DSurface)
        {
            IntPtr sbStatics = GetActivationFactory("Windows.Graphics.Imaging.SoftwareBitmap", IidISoftwareBitmapStatics);
            try
            {
                var createFromSurfAsync = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl(sbStatics)[11];
                IntPtr op = IntPtr.Zero;
                return createFromSurfAsync(sbStatics, direct3DSurface, &op) >= 0 ? op : IntPtr.Zero;
            }
            finally { SafeRelease(ref sbStatics); }
        }

        private static unsafe (byte[] Pixels, int Width, int Height)? ExtractPixelsFromSoftwareBitmap(IntPtr softwareBitmap)
        {
            var getW = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(softwareBitmap)[8];
            var getH = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(softwareBitmap)[9];
            int w = 0, h = 0;
            getW(softwareBitmap, &w);
            getH(softwareBitmap, &h);
            if (w <= 0 || h <= 0) return null;

            int byteCount = w * h * 4;
            IntPtr bufFactory = GetActivationFactory("Windows.Storage.Streams.Buffer", IidIBufferFactory);
            IntPtr buffer = IntPtr.Zero;
            try
            {
                var createBuf = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(bufFactory)[6];
                Marshal.ThrowExceptionForHR(createBuf(bufFactory, (uint)byteCount, &buffer));

                IntPtr ibuf = QueryInterface(buffer, IidIBuffer);
                try
                {
                    var putLen = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(ibuf)[8];
                    Marshal.ThrowExceptionForHR(putLen(ibuf, (uint)byteCount));
                    var copyToBuf = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(softwareBitmap)[18];
                    Marshal.ThrowExceptionForHR(copyToBuf(softwareBitmap, ibuf));
                }
                finally { SafeRelease(ref ibuf); }

                IntPtr byteAccess = QueryInterface(buffer, IidIBufferByteAccess);
                try
                {
                    var getPtr = (delegate* unmanaged[Stdcall]<IntPtr, byte**, int>)Vtbl(byteAccess)[3];
                    byte* srcPtr = null;
                    Marshal.ThrowExceptionForHR(getPtr(byteAccess, &srcPtr));
                    byte[] pixels = new byte[byteCount];
                    Marshal.Copy((IntPtr)srcPtr, pixels, 0, byteCount);
                    return (pixels, w, h);
                }
                finally { SafeRelease(ref byteAccess); }
            }
            finally
            {
                SafeRelease(ref buffer);
                SafeRelease(ref bufFactory);
            }
        }

        public static async Task<(byte[] Pixels, int Width, int Height)?> ReadSurfaceFallbackAsync(IntPtr direct3DSurface)
        {
            IntPtr op = StartSoftwareBitmapFromSurfaceAsync(direct3DSurface);
            if (op == IntPtr.Zero) return null;

            IntPtr softwareBitmap = IntPtr.Zero;
            try
            {
                softwareBitmap = await AwaitAsyncOperationPtr(op).ConfigureAwait(false);
                return softwareBitmap != IntPtr.Zero ? ExtractPixelsFromSoftwareBitmap(softwareBitmap) : null;
            }
            finally
            {
                CloseAndRelease(ref softwareBitmap);
            }
        }

        public sealed unsafe class AgileTypedEventHandler : IDisposable
        {
            private readonly Action _callback;
            private readonly IntPtr _objectPtr;
            private readonly IntPtr _vtablePtr;
            private readonly GCHandle _selfHandle;
            private bool _disposed;

            public IntPtr ComPointer => _objectPtr;

            public AgileTypedEventHandler(Action callback)
            {
                _callback = callback;
                _selfHandle = GCHandle.Alloc(this);
                _vtablePtr = (IntPtr)NativeMemory.AllocZeroed((nuint)(4 * sizeof(IntPtr)));
                IntPtr* vtbl = (IntPtr*)_vtablePtr;
                vtbl[0] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&Qi;
                vtbl[1] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&AddRef;
                vtbl[2] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&Release;
                vtbl[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)&Invoke;

                _objectPtr = (IntPtr)NativeMemory.AllocZeroed((nuint)(2 * sizeof(IntPtr)));
                IntPtr* obj = (IntPtr*)_objectPtr;
                obj[0] = _vtablePtr;
                obj[1] = GCHandle.ToIntPtr(_selfHandle);
            }

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
            private static int Qi(IntPtr thisPtr, Guid* iid, IntPtr* ppv)
            {
                if (ppv == null || iid == null) return unchecked((int)0x80004003);
                *ppv = thisPtr;
                return 0;
            }

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
            private static uint AddRef(IntPtr thisPtr) => 1;

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
            private static uint Release(IntPtr thisPtr) => 1;

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
            private static int Invoke(IntPtr thisPtr, IntPtr sender, IntPtr args)
            {
                try
                {
                    IntPtr* obj = (IntPtr*)thisPtr;
                    var handle = GCHandle.FromIntPtr(obj[1]);
                    if (handle.Target is AgileTypedEventHandler self && !self._disposed)
                    {
                        self._callback();
                    }
                }
                catch { }
                return 0;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_selfHandle.IsAllocated) _selfHandle.Free();
                if (_objectPtr != IntPtr.Zero) NativeMemory.Free((void*)_objectPtr);
                if (_vtablePtr != IntPtr.Zero) NativeMemory.Free((void*)_vtablePtr);
            }
        }

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoInitialize(int initType);

        [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int WindowsCreateString(string sourceString, uint length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static unsafe extern char* WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoActivateInstance(IntPtr activatableClassId, out IntPtr instance);

        [DllImport("shlwapi.dll", ExactSpelling = true)]
        private static extern IStream? SHCreateMemStream(byte[]? pInit, uint cbInit);

        [DllImport("shcore.dll", ExactSpelling = true)]
        private static extern int CreateRandomAccessStreamOverStream(
            IStream stream,
            uint options,
            ref Guid riid,
            out IntPtr ppv);
    }
}
