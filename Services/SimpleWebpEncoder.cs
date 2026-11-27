using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    /// <summary>
    /// Pure-managed RIFF/VP8L lossless WebP encoder used when SkiaSharp is removed.
    /// Produces compliant WebP files readable by browsers, Windows Photos, and WIC WebP decoders.
    /// </summary>
    internal static class SimpleWebpEncoder
    {
        private static readonly int[] CodeLengthOrder =
            { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        public static byte[] Encode(BitmapSource source)
        {
            var converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = Math.Clamp(converted.PixelWidth, 1, 16384);
            int height = Math.Clamp(converted.PixelHeight, 1, 16384);
            int stride = width * 4;
            byte[] bgra = new byte[stride * height];
            converted.CopyPixels(bgra, stride, 0);

            var bw = new LsbBitWriter();
            // VP8L signature + header
            bw.WriteBits(0x2F, 8);
            bw.WriteBits(width - 1, 14);
            bw.WriteBits(height - 1, 14);
            bw.WriteBits(1, 1); // alpha_is_used = true
            bw.WriteBits(0, 3); // version_number = 0
            bw.WriteBits(0, 1); // transform_present = false
            bw.WriteBits(0, 1); // color_cache_used = false
            bw.WriteBits(0, 1); // use_meta_prefix = false

            // 5 Prefix code groups: Green+Len(280), Red(256), Blue(256), Alpha(256), Distance(40).
            WriteFixed8PrefixCode(bw, alphabetSize: 280); // symbols 0..255 length=8, 256..279 unused(0)
            WriteFixed8PrefixCode(bw, alphabetSize: 256); // Red
            WriteFixed8PrefixCode(bw, alphabetSize: 256); // Blue
            WriteFixed8PrefixCode(bw, alphabetSize: 256); // Alpha
            WriteSingleSymbolPrefixCode(bw, symbol: 0);   // Distance (unused)

            // Pixel stream in VP8L order: Green (MSB-bitrev 8-bit), Red, Blue, Alpha
            int totalPixels = width * height;
            for (int i = 0; i < totalPixels; i++)
            {
                int idx = i * 4;
                byte b = bgra[idx];
                byte g = bgra[idx + 1];
                byte r = bgra[idx + 2];
                byte a = bgra[idx + 3];

                bw.WriteBits(BitReverse8(g), 8);
                bw.WriteBits(BitReverse8(r), 8);
                bw.WriteBits(BitReverse8(b), 8);
                bw.WriteBits(BitReverse8(a), 8);
            }

            byte[] vp8l = bw.ToArray();
            int pad = vp8l.Length & 1;
            int riffPayload = 4 + 8 + vp8l.Length + pad;

            using var ms = new MemoryStream(8 + riffPayload);
            using var writer = new BinaryWriter(ms);
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write((uint)riffPayload);
            writer.Write(new[] { (byte)'W', (byte)'E', (byte)'B', (byte)'P' });
            writer.Write(new[] { (byte)'V', (byte)'P', (byte)'8', (byte)'L' });
            writer.Write((uint)vp8l.Length);
            writer.Write(vp8l);
            if (pad == 1)
            {
                writer.Write((byte)0);
            }

            return ms.ToArray();
        }

        private static void WriteSingleSymbolPrefixCode(LsbBitWriter bw, int symbol)
        {
            bw.WriteBits(1, 1); // is_simple = true
            bw.WriteBits(0, 1); // num_symbols - 1 = 0 (1 symbol)
            bw.WriteBits(1, 1); // is_first_8bits = true
            bw.WriteBits(symbol & 0xFF, 8);
        }

        private static void WriteFixed8PrefixCode(LsbBitWriter bw, int alphabetSize)
        {
            // Normal prefix code: symbols 0..255 all assigned length 8, remainder length 0.
            bw.WriteBits(0, 1); // is_simple = false
            // CodeLengthOrder[0..11] = {17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8}
            // Send 12 code-length code lengths => num_code_lengths - 4 = 8.
            bw.WriteBits(8, 4);
            bool needZero = alphabetSize > 256;
            for (int i = 0; i < 12; i++)
            {
                int sym = CodeLengthOrder[i];
                int len = needZero
                    ? (sym == 0 || sym == 8 ? 1 : 0)
                    : (sym == 8 ? 1 : 0);
                bw.WriteBits(len, 3);
            }

            // Canonical code assignment among {0, 8} of length 1:
            // Symbol 0 => code 0 (1 bit: 0), Symbol 8 => code 1 (1 bit: 1).
            // If only {8} has length 1: Symbol 8 => code 0 (1 bit: 0).
            bw.WriteBits(0, 1); // use_max_symbol = false
            int codeForEight = needZero ? 1 : 0;
            for (int s = 0; s < 256; s++)
            {
                bw.WriteBits(codeForEight, 1);
            }
            for (int s = 256; s < alphabetSize; s++)
            {
                bw.WriteBits(0, 1);
            }
        }

        private static int BitReverse8(byte v)
        {
            int r = 0;
            for (int i = 0; i < 8; i++)
            {
                r = (r << 1) | ((v >> i) & 1);
            }
            return r;
        }

        private sealed class LsbBitWriter
        {
            private readonly MemoryStream _stream = new();
            private ulong _buf;
            private int _bits;

            public void WriteBits(int value, int bitCount)
            {
                _buf |= ((ulong)(uint)value & ((1UL << bitCount) - 1)) << _bits;
                _bits += bitCount;
                while (_bits >= 8)
                {
                    _stream.WriteByte((byte)(_buf & 0xFF));
                    _buf >>= 8;
                    _bits -= 8;
                }
            }

            public byte[] ToArray()
            {
                if (_bits > 0)
                {
                    _stream.WriteByte((byte)(_buf & 0xFF));
                    _buf = 0;
                    _bits = 0;
                }
                return _stream.ToArray();
            }
        }
    }
}
