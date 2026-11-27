using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    /// <summary>
    /// Minimalist pure-managed PDF 1.4 writer embedding raster pages as JPEG (DCTDecode) or
    /// zlib-compressed RGB/Alpha streams (FlateDecode), removing the SkiaSharp PDF dependency.
    /// </summary>
    internal sealed class SimplePdfWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly List<(long Offset, byte[] Body)> _objects = new();
        private readonly List<int> _pageObjectIds = new();
        private int _nextObjectId = 3; // 1 = Catalog, 2 = Pages

        public SimplePdfWriter(Stream output)
        {
            _stream = output ?? throw new ArgumentNullException(nameof(output));
            byte[] header = new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4', (byte)'\n', (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' };
            _stream.Write(header, 0, header.Length);
        }

        public void AddJpegPage(byte[] jpegBytes, int width, int height)
        {
            int imgId = _nextObjectId++;
            int contentId = _nextObjectId++;
            int pageId = _nextObjectId++;

            byte[] imgHeader = Encoding.ASCII.GetBytes(
                $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
            byte[] imgFooter = Encoding.ASCII.GetBytes("\nendstream");
            AddObject(imgId, Combine(imgHeader, jpegBytes, imgFooter));

            AddPageWithImage(pageId, contentId, imgId, width, height);
        }

        public void AddBitmapPage(BitmapSource source, bool preferJpeg = false, int jpegQuality = 85)
        {
            int width = Math.Max(1, source.PixelWidth);
            int height = Math.Max(1, source.PixelHeight);

            if (preferJpeg)
            {
                var flattened = ImageIO.FlattenOnWhiteIfTransparent(source);
                byte[] jpeg = ImageIO.EncodeJpeg(flattened, jpegQuality);
                AddJpegPage(jpeg, width, height);
                return;
            }

            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            byte[] bgra = new byte[stride * height];
            converted.CopyPixels(bgra, stride, 0);

            int pixelCount = width * height;
            byte[] rgb = new byte[pixelCount * 3];
            byte[] alpha = new byte[pixelCount];
            bool hasTransparency = false;

            for (int i = 0; i < pixelCount; i++)
            {
                int srcIdx = i * 4;
                int rgbIdx = i * 3;
                rgb[rgbIdx] = bgra[srcIdx + 2];
                rgb[rgbIdx + 1] = bgra[srcIdx + 1];
                rgb[rgbIdx + 2] = bgra[srcIdx];
                byte a = bgra[srcIdx + 3];
                alpha[i] = a;
                if (a != 255)
                {
                    hasTransparency = true;
                }
            }

            byte[] rgbCompressed = DeflateZlib(rgb);
            int? smaskId = null;
            if (hasTransparency)
            {
                byte[] alphaCompressed = DeflateZlib(alpha);
                int maskObjId = _nextObjectId++;
                smaskId = maskObjId;
                byte[] maskHead = Encoding.ASCII.GetBytes(
                    $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {alphaCompressed.Length} >>\nstream\n");
                AddObject(maskObjId, Combine(maskHead, alphaCompressed, Encoding.ASCII.GetBytes("\nendstream")));
            }

            int imgId = _nextObjectId++;
            int contentId = _nextObjectId++;
            int pageId = _nextObjectId++;

            string smaskRef = smaskId.HasValue ? $" /SMask {smaskId.Value} 0 R" : "";
            byte[] imgHead = Encoding.ASCII.GetBytes(
                $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8{smaskRef} /Filter /FlateDecode /Length {rgbCompressed.Length} >>\nstream\n");
            AddObject(imgId, Combine(imgHead, rgbCompressed, Encoding.ASCII.GetBytes("\nendstream")));

            AddPageWithImage(pageId, contentId, imgId, width, height);
        }

        private void AddPageWithImage(int pageId, int contentId, int imgId, int width, int height)
        {
            string wStr = width.ToString(CultureInfo.InvariantCulture);
            string hStr = height.ToString(CultureInfo.InvariantCulture);
            string contentStr = $"q\n{wStr} 0 0 {hStr} 0 0 cm\n/Im0 Do\nQ\n";
            byte[] contentBytes = Encoding.ASCII.GetBytes(contentStr);
            AddObject(contentId, Encoding.ASCII.GetBytes(
                $"<< /Length {contentBytes.Length} >>\nstream\n{contentStr}endstream"));

            AddObject(pageId, Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {wStr} {hStr}] /Resources << /XObject << /Im0 {imgId} 0 R >> >> /Contents {contentId} 0 R >>"));
            _pageObjectIds.Add(pageId);
        }

        private void AddObject(int id, byte[] body)
        {
            while (_objects.Count < id)
            {
                _objects.Add((0, Array.Empty<byte>()));
            }
            _objects[id - 1] = (0, body);
        }

        public void Finish()
        {
            if (_pageObjectIds.Count == 0)
            {
                AddObject(3, Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"));
                _pageObjectIds.Add(3);
            }

            var kids = new StringBuilder();
            foreach (int pid in _pageObjectIds)
            {
                kids.Append(pid).Append(" 0 R ");
            }

            AddObject(1, Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            AddObject(2, Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [ {kids}] /Count {_pageObjectIds.Count} >>"));

            var offsets = new long[_objects.Count + 1];
            for (int i = 0; i < _objects.Count; i++)
            {
                offsets[i + 1] = _stream.Position;
                byte[] prefix = Encoding.ASCII.GetBytes($"{i + 1} 0 obj\n");
                byte[] suffix = Encoding.ASCII.GetBytes("\nendobj\n");
                _stream.Write(prefix, 0, prefix.Length);
                _stream.Write(_objects[i].Body, 0, _objects[i].Body.Length);
                _stream.Write(suffix, 0, suffix.Length);
            }

            long xrefPos = _stream.Position;
            var xref = new StringBuilder();
            xref.Append($"xref\n0 {_objects.Count + 1}\n");
            xref.Append("0000000000 65535 f \n");
            for (int i = 1; i <= _objects.Count; i++)
            {
                xref.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }
            xref.Append($"trailer\n<< /Size {_objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
            byte[] xrefBytes = Encoding.ASCII.GetBytes(xref.ToString());
            _stream.Write(xrefBytes, 0, xrefBytes.Length);
            _stream.Flush();
        }

        private static byte[] DeflateZlib(byte[] input)
        {
            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(input, 0, input.Length);
            }
            return ms.ToArray();
        }

        private static byte[] Combine(byte[] a, byte[] b, byte[] c)
        {
            byte[] result = new byte[a.Length + b.Length + c.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            Buffer.BlockCopy(c, 0, result, a.Length + b.Length, c.Length);
            return result;
        }

        public void Dispose()
        {
            _objects.Clear();
        }
    }
}
