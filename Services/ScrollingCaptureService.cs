using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryoUtils.Services
{
    public static class ScrollingCaptureService
    {
        private const int MaxStitchedHeight = 16000;

        public static BitmapSource StitchVertical(IReadOnlyList<BitmapSource> frames)
        {
            if (frames.Count == 0)
            {
                throw new InvalidOperationException("No scrolling frames were captured.");
            }
            if (frames.Count == 1)
            {
                return frames[0];
            }

            int width = frames[0].PixelWidth;
            int height = frames[0].PixelHeight;
            int stride = width * 4;
            var converted = new List<byte[]>();
            foreach (var frame in frames)
            {
                var bgra = frame.Format == PixelFormats.Bgra32
                    ? frame
                    : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                byte[] pixels = new byte[stride * bgra.PixelHeight];
                bgra.CopyPixels(pixels, stride, 0);
                converted.Add(pixels);
            }

            var offsets = new List<int> { 0 };
            var overlaps = new List<int> { 0 };
            int totalHeight = height;
            for (int index = 1; index < converted.Count; index++)
            {
                int overlap = FindOverlap(converted[index - 1], converted[index], width, height, stride);
                int append = Math.Max(1, height - overlap);
                if (totalHeight + append > MaxStitchedHeight)
                {
                    break;
                }
                offsets.Add(totalHeight - overlap);
                overlaps.Add(overlap);
                totalHeight += append;
            }

            var output = new byte[stride * totalHeight];
            for (int index = 0; index < converted.Count && index < offsets.Count; index++)
            {
                int top = offsets[index];
                int overlap = overlaps[index];
                int rows = index == 0 ? height : Math.Max(1, height - overlap);
                rows = Math.Min(rows, totalHeight - top);
                Buffer.BlockCopy(
                    converted[index],
                    overlap * stride,
                    output,
                    top * stride,
                    rows * stride);
            }

            var bitmap = BitmapSource.Create(
                width,
                totalHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                output,
                stride);
            bitmap.Freeze();
            return bitmap;
        }

        public static Point PlaceControlBar(Rect selection, Size bar, Rect workArea)
        {
            double left = Math.Clamp(selection.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - bar.Width));
            if (selection.Bottom + bar.Height + 10 <= workArea.Bottom)
            {
                return new Point(left, selection.Bottom + 8);
            }
            if (selection.Top - bar.Height - 10 >= workArea.Top)
            {
                return new Point(left, selection.Top - bar.Height - 8);
            }
            return new Point(
                Math.Max(workArea.Left + 8, workArea.Right - bar.Width - 16),
                Math.Max(workArea.Top + 8, workArea.Bottom - bar.Height - 16));
        }

        private static int FindOverlap(byte[] previous, byte[] next, int width, int height, int stride)
        {
            int minOverlap = Math.Max(12, height / 8);
            int maxOverlap = Math.Min(height - 8, height * 9 / 10);
            int bestOverlap = 0;
            double bestScore = double.MaxValue;
            int sampleStep = Math.Max(1, width / 140);

            for (int overlap = maxOverlap; overlap >= minOverlap; overlap -= Math.Max(1, height / 60))
            {
                double score = 0;
                for (int y = 0; y < overlap; y += Math.Max(1, overlap / 40))
                {
                    int previousOffset = (height - overlap + y) * stride;
                    int nextOffset = y * stride;
                    for (int x = 0; x < width; x += sampleStep)
                    {
                        int p = previousOffset + x * 4;
                        int n = nextOffset + x * 4;
                        score += Math.Abs(previous[p] - next[n]);
                        score += Math.Abs(previous[p + 1] - next[n + 1]);
                        score += Math.Abs(previous[p + 2] - next[n + 2]);
                    }
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestOverlap = overlap;
                }
            }

            return bestOverlap;
        }
    }
}
