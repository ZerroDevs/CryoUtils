using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    /// <summary>
    /// Streaming GIF89a writer with one global palette built from sampled frames.
    /// Frames are quantized to a 15-bit lookup, so memory stays flat while encoding.
    /// </summary>
    public sealed class GifWriter : IDisposable
    {
        private const int PaletteSize = 256;
        private readonly Stream _stream;
        private readonly int _width;
        private readonly int _height;
        private readonly int _delayCentiseconds;
        private readonly byte[] _palette = new byte[PaletteSize * 3];
        private readonly byte[] _lookup = new byte[32768];
        private bool _finished;

        public GifWriter(
            Stream stream,
            int width,
            int height,
            int fps,
            IReadOnlyList<BitmapSource> paletteFrames)
        {
            _stream = stream;
            _width = width;
            _height = height;
            _delayCentiseconds = Math.Max(2, (int)Math.Round(100.0 / Math.Max(1, fps)));
            BuildPalette(paletteFrames);
            WriteHeader();
        }

        private void BuildPalette(IReadOnlyList<BitmapSource> frames)
        {
            var histogram = new int[32768];
            foreach (var frame in frames)
            {
                AccumulateHistogram(frame, histogram);
            }

            var colors = new List<int>();
            for (int i = 0; i < histogram.Length; i++)
            {
                if (histogram[i] > 0)
                {
                    colors.Add(i);
                }
            }

            var selected = MedianCut(colors, histogram, PaletteSize);
            for (int i = 0; i < PaletteSize; i++)
            {
                int rgb = i < selected.Count ? selected[i] : (i * 0x010101) & 0xFFFFFF;
                _palette[i * 3] = (byte)((rgb >> 16) & 0xFF);
                _palette[i * 3 + 1] = (byte)((rgb >> 8) & 0xFF);
                _palette[i * 3 + 2] = (byte)(rgb & 0xFF);
            }

            for (int bucket = 0; bucket < 32768; bucket++)
            {
                int r = ((bucket >> 10) & 31) * 255 / 31;
                int g = ((bucket >> 5) & 31) * 255 / 31;
                int b = (bucket & 31) * 255 / 31;
                int best = 0;
                int bestDistance = int.MaxValue;
                for (int i = 0; i < PaletteSize; i++)
                {
                    int dr = r - _palette[i * 3];
                    int dg = g - _palette[i * 3 + 1];
                    int db = b - _palette[i * 3 + 2];
                    int distance = dr * dr + dg * dg + db * db;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }
                _lookup[bucket] = (byte)best;
            }
        }

        private static void AccumulateHistogram(BitmapSource frame, int[] histogram)
        {
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;
            var converted = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            int stepX = Math.Max(1, width / 220);
            int stepY = Math.Max(1, height / 220);
            for (int y = 0; y < height; y += stepY)
            {
                int row = y * stride;
                for (int x = 0; x < width; x += stepX)
                {
                    int index = row + x * 4;
                    int b = pixels[index];
                    int g = pixels[index + 1];
                    int r = pixels[index + 2];
                    int bucket = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                    histogram[bucket]++;
                }
            }
        }

        private static List<int> MedianCut(IReadOnlyList<int> colors, int[] histogram, int target)
        {
            if (colors.Count == 0)
            {
                return new List<int>();
            }

            var boxes = new List<List<int>> { colors.ToList() };
            while (boxes.Count < target)
            {
                List<int>? splitBox = null;
                int splitChannel = 0;
                int largestRange = -1;
                foreach (var box in boxes)
                {
                    if (box.Count < 2)
                    {
                        continue;
                    }

                    int minR = 255, minG = 255, minB = 255;
                    int maxR = 0, maxG = 0, maxB = 0;
                    foreach (int bucket in box)
                    {
                        int r = (bucket >> 10) & 31;
                        int g = (bucket >> 5) & 31;
                        int b = bucket & 31;
                        minR = Math.Min(minR, r);
                        maxR = Math.Max(maxR, r);
                        minG = Math.Min(minG, g);
                        maxG = Math.Max(maxG, g);
                        minB = Math.Min(minB, b);
                        maxB = Math.Max(maxB, b);
                    }

                    int range = Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
                    bool enoughWeight = box.Sum(bucket => histogram[bucket]) > 0;
                    if (range > largestRange && enoughWeight)
                    {
                        largestRange = range;
                        splitBox = box;
                        splitChannel = (maxR - minR) >= (maxG - minG) && (maxR - minR) >= (maxB - minB)
                            ? 2
                            : (maxG - minG) >= (maxB - minB) ? 1 : 0;
                    }
                }

                if (splitBox == null)
                {
                    break;
                }

                splitBox.Sort((a, b) => Channel(a, splitChannel).CompareTo(Channel(b, splitChannel)));
                long total = splitBox.Sum(bucket => (long)histogram[bucket]);
                long running = 0;
                int splitIndex = splitBox.Count / 2;
                for (int i = 0; i < splitBox.Count - 1; i++)
                {
                    running += histogram[splitBox[i]];
                    if (running >= total / 2)
                    {
                        splitIndex = i + 1;
                        break;
                    }
                }

                var second = splitBox.GetRange(splitIndex, splitBox.Count - splitIndex);
                splitBox.RemoveRange(splitIndex, splitBox.Count - splitIndex);
                if (second.Count > 0)
                {
                    boxes.Add(second);
                }
            }

            var result = new List<int>(boxes.Count);
            foreach (var box in boxes)
            {
                long weight = 0;
                long r = 0, g = 0, b = 0;
                foreach (int bucket in box)
                {
                    long count = Math.Max(1, histogram[bucket]);
                    weight += count;
                    r += ((bucket >> 10) & 31) * 255 / 31 * count;
                    g += ((bucket >> 5) & 31) * 255 / 31 * count;
                    b += (bucket & 31) * 255 / 31 * count;
                }

                if (weight == 0)
                {
                    continue;
                }

                int cr = (int)(r / weight);
                int cg = (int)(g / weight);
                int cb = (int)(b / weight);
                result.Add((cr << 16) | (cg << 8) | cb);
            }

            return result;
        }

        private static int Channel(int bucket, int channel)
        {
            return channel switch
            {
                2 => (bucket >> 10) & 31,
                1 => (bucket >> 5) & 31,
                _ => bucket & 31
            };
        }

        private void WriteHeader()
        {
            WriteAscii("GIF89a");
            WriteShort(_width);
            WriteShort(_height);
            _stream.WriteByte(0xF7); // Global table, 256 colors.
            _stream.WriteByte(0);
            _stream.WriteByte(0);
            _stream.Write(_palette, 0, _palette.Length);

            // Loop forever.
            _stream.WriteByte(0x21);
            _stream.WriteByte(0xFF);
            _stream.WriteByte(0x0B);
            WriteAscii("NETSCAPE2.0");
            _stream.WriteByte(0x03);
            _stream.WriteByte(0x01);
            _stream.WriteByte(0x00);
            _stream.WriteByte(0x00);
            _stream.WriteByte(0x00);
        }

        public void AddFrame(BitmapSource frame)
        {
            if (_finished)
            {
                return;
            }

            var converted = frame.Format == PixelFormats.Bgra32
                ? frame
                : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = _width * 4;
            var pixels = new byte[stride * _height];
            converted.CopyPixels(pixels, stride, 0);

            var indices = new byte[_width * _height];
            for (int y = 0; y < _height; y++)
            {
                int row = y * stride;
                int outRow = y * _width;
                for (int x = 0; x < _width; x++)
                {
                    int index = row + x * 4;
                    int bucket = ((pixels[index + 2] >> 3) << 10) |
                                 ((pixels[index + 1] >> 3) << 5) |
                                 (pixels[index] >> 3);
                    indices[outRow + x] = _lookup[bucket];
                }
            }

            // Graphic control extension.
            _stream.WriteByte(0x21);
            _stream.WriteByte(0xF9);
            _stream.WriteByte(0x04);
            _stream.WriteByte(0x04); // Disposal: restore to background.
            WriteShort(_delayCentiseconds);
            _stream.WriteByte(0);
            _stream.WriteByte(0);

            // Image descriptor.
            _stream.WriteByte(0x2C);
            WriteShort(0);
            WriteShort(0);
            WriteShort(_width);
            WriteShort(_height);
            _stream.WriteByte(0);

            _stream.WriteByte(0x08); // LZW minimum code size.
            WriteLzw(indices);
            _stream.WriteByte(0x00);
        }

        private void WriteLzw(byte[] indices)
        {
            var subBlocks = new SubBlockWriter(_stream);
            var bits = new BitWriter(subBlocks);
            var dictionary = new Dictionary<int, int>(4096);

            const int clearCode = 256;
            const int endCode = 257;
            int codeSize = 9;
            int nextCode = 258;

            bits.Write(clearCode, codeSize);
            int prefix = indices.Length > 0 ? indices[0] : 0;

            for (int i = 1; i < indices.Length; i++)
            {
                int value = indices[i];
                int key = (prefix << 8) | value;
                if (dictionary.TryGetValue(key, out int existing))
                {
                    prefix = existing;
                    continue;
                }

                bits.Write(prefix, codeSize);
                if (nextCode < 4096)
                {
                    dictionary[key] = nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }
                }
                else
                {
                    bits.Write(clearCode, codeSize);
                    dictionary.Clear();
                    codeSize = 9;
                    nextCode = 258;
                }

                prefix = value;
            }

            bits.Write(prefix, codeSize);
            bits.Write(endCode, codeSize);
            bits.Flush();
            subBlocks.Flush();
        }

        public void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _stream.WriteByte(0x3B);
            _stream.Flush();
        }

        public void Dispose()
        {
            Finish();
        }

        private void WriteAscii(string text)
        {
            byte[] data = Encoding.ASCII.GetBytes(text);
            _stream.Write(data, 0, data.Length);
        }

        private void WriteShort(int value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        private sealed class BitWriter
        {
            private readonly SubBlockWriter _output;
            private int _bitBuffer;
            private int _bitCount;

            public BitWriter(SubBlockWriter output)
            {
                _output = output;
            }

            public void Write(int code, int bits)
            {
                _bitBuffer |= code << _bitCount;
                _bitCount += bits;
                while (_bitCount >= 8)
                {
                    _output.WriteByte((byte)(_bitBuffer & 0xFF));
                    _bitBuffer >>= 8;
                    _bitCount -= 8;
                }
            }

            public void Flush()
            {
                if (_bitCount > 0)
                {
                    _output.WriteByte((byte)(_bitBuffer & 0xFF));
                    _bitBuffer = 0;
                    _bitCount = 0;
                }
            }
        }

        private sealed class SubBlockWriter
        {
            private readonly Stream _stream;
            private readonly byte[] _buffer = new byte[255];
            private int _count;

            public SubBlockWriter(Stream stream)
            {
                _stream = stream;
            }

            public void WriteByte(byte value)
            {
                if (_count == 255)
                {
                    FlushBlock();
                }
                _buffer[_count++] = value;
            }

            private void FlushBlock()
            {
                if (_count == 0)
                {
                    return;
                }

                _stream.WriteByte((byte)_count);
                _stream.Write(_buffer, 0, _count);
                _count = 0;
            }

            public void Flush()
            {
                FlushBlock();
            }
        }

        public static void EncodePngFramesToGif(
            IReadOnlyList<byte[]> pngFrames,
            int width,
            int height,
            int fps,
            string path)
        {
            var paletteSamples = new List<BitmapSource>();
            if (pngFrames.Count > 0)
            {
                int sampleCount = Math.Min(24, pngFrames.Count);
                for (int i = 0; i < sampleCount; i++)
                {
                    int index = sampleCount == 1 ? 0 : i * (pngFrames.Count - 1) / (sampleCount - 1);
                    paletteSamples.Add(ImageIO.DecodeBytes(pngFrames[index]));
                }
            }

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
            using var writer = new GifWriter(stream, width, height, fps, paletteSamples);
            foreach (byte[] png in pngFrames)
            {
                writer.AddFrame(ImageIO.DecodeBytes(png));
            }
            writer.Finish();
        }
    }
}
