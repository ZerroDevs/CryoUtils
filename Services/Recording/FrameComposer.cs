using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CryoUtils.Services.Recording
{
    internal sealed class ClickPulse
    {
        public int X { get; init; }
        public int Y { get; init; }
        public long StartedMs { get; init; }
    }

    internal static class FrameComposer
    {
        public static byte[] Compose(
            byte[] source,
            int sourceWidth,
            int sourceHeight,
            int outputWidth,
            int outputHeight,
            int regionX,
            int regionY,
            RecordingOptions options,
            IReadOnlyList<ClickPulse> clicks,
            WebcamCaptureService? webcam)
        {
            byte[] output = new byte[outputWidth * outputHeight * 4];
            ScaleBgra(source, sourceWidth, sourceHeight, output, outputWidth, outputHeight);

            if (options.CursorHighlight || options.ClickEffects)
            {
                NativeMethods.GetCursorPos(out var cursor);
                int cursorX = (int)Math.Round((cursor.X - regionX) * outputWidth / (double)Math.Max(1, sourceWidth));
                int cursorY = (int)Math.Round((cursor.Y - regionY) * outputHeight / (double)Math.Max(1, sourceHeight));
                if (options.CursorHighlight && cursorX >= 0 && cursorY >= 0 &&
                    cursorX < outputWidth && cursorY < outputHeight)
                {
                    DrawRing(output, outputWidth, outputHeight, cursorX, cursorY, 20, 2, 255, 228, 92, 230);
                }

                if (options.ClickEffects)
                {
                    long now = Environment.TickCount64;
                    foreach (var click in clicks)
                    {
                        long age = now - click.StartedMs;
                        if (age < 0 || age > 650)
                        {
                            continue;
                        }

                        double progress = age / 650.0;
                        int radius = (int)(8 + 34 * progress);
                        int alpha = (int)(210 * (1 - progress));
                        int x = (int)Math.Round((click.X - regionX) * outputWidth / (double)Math.Max(1, sourceWidth));
                        int y = (int)Math.Round((click.Y - regionY) * outputHeight / (double)Math.Max(1, sourceHeight));
                        DrawRing(output, outputWidth, outputHeight, x, y, radius, 3, 255, 255, 255, alpha);
                        DrawRing(output, outputWidth, outputHeight, x, y, Math.Max(2, radius / 2), 2, 255, 196, 64, alpha);
                    }
                }
            }

            if (options.WebcamBubble && webcam != null && webcam.TryGetFrame(out var camera, out int cameraWidth, out int cameraHeight))
            {
                int bubbleWidth = Math.Max(80, outputWidth / 4);
                int bubbleHeight = Math.Max(60, bubbleWidth * cameraHeight / Math.Max(1, cameraWidth));
                if (bubbleHeight > outputHeight / 3)
                {
                    bubbleHeight = outputHeight / 3;
                    bubbleWidth = Math.Max(60, bubbleHeight * cameraWidth / Math.Max(1, cameraHeight));
                }

                int left = outputWidth - bubbleWidth - 18;
                int top = outputHeight - bubbleHeight - 18;
                DrawScaledBgra(camera, cameraWidth, cameraHeight, output, outputWidth, outputHeight,
                    left, top, bubbleWidth, bubbleHeight);
                DrawRectOutline(output, outputWidth, outputHeight, left - 2, top - 2,
                    bubbleWidth + 4, bubbleHeight + 4, 2, 92, 205, 255, 220);
            }

            return output;
        }

        private static void ScaleBgra(
            byte[] source,
            int sourceWidth,
            int sourceHeight,
            byte[] output,
            int outputWidth,
            int outputHeight)
        {
            if (sourceWidth == outputWidth && sourceHeight == outputHeight)
            {
                Buffer.BlockCopy(source, 0, output, 0, Math.Min(source.Length, output.Length));
                return;
            }

            int sourceStride = sourceWidth * 4;
            int outputStride = outputWidth * 4;
            for (int y = 0; y < outputHeight; y++)
            {
                double sourceY = (y + 0.5) * sourceHeight / outputHeight - 0.5;
                int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, sourceHeight - 1);
                int y1 = Math.Min(sourceHeight - 1, y0 + 1);
                double fy = Math.Clamp(sourceY - y0, 0, 1);
                int row0 = y0 * sourceStride;
                int row1 = y1 * sourceStride;
                int outRow = y * outputStride;

                for (int x = 0; x < outputWidth; x++)
                {
                    double sourceX = (x + 0.5) * sourceWidth / outputWidth - 0.5;
                    int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, sourceWidth - 1);
                    int x1 = Math.Min(sourceWidth - 1, x0 + 1);
                    double fx = Math.Clamp(sourceX - x0, 0, 1);
                    int p00 = row0 + x0 * 4;
                    int p10 = row0 + x1 * 4;
                    int p01 = row1 + x0 * 4;
                    int p11 = row1 + x1 * 4;
                    int destination = outRow + x * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        double top = source[p00 + channel] * (1 - fx) + source[p10 + channel] * fx;
                        double bottom = source[p01 + channel] * (1 - fx) + source[p11 + channel] * fx;
                        output[destination + channel] = (byte)Math.Clamp(
                            (int)Math.Round(top * (1 - fy) + bottom * fy), 0, 255);
                    }
                }
            }
        }

        private static void DrawScaledBgra(
            byte[] source,
            int sourceWidth,
            int sourceHeight,
            byte[] output,
            int outputWidth,
            int outputHeight,
            int left,
            int top,
            int width,
            int height)
        {
            for (int y = 0; y < height; y++)
            {
                int destinationY = top + y;
                if (destinationY < 0 || destinationY >= outputHeight)
                {
                    continue;
                }

                int sourceY = Math.Clamp(y * sourceHeight / Math.Max(1, height), 0, sourceHeight - 1);
                for (int x = 0; x < width; x++)
                {
                    int destinationX = left + x;
                    if (destinationX < 0 || destinationX >= outputWidth)
                    {
                        continue;
                    }

                    int sourceX = Math.Clamp(x * sourceWidth / Math.Max(1, width), 0, sourceWidth - 1);
                    int sourceIndex = (sourceY * sourceWidth + sourceX) * 4;
                    int destinationIndex = (destinationY * outputWidth + destinationX) * 4;
                    output[destinationIndex] = source[sourceIndex];
                    output[destinationIndex + 1] = source[sourceIndex + 1];
                    output[destinationIndex + 2] = source[sourceIndex + 2];
                    output[destinationIndex + 3] = 255;
                }
            }
        }

        private static void DrawRing(
            byte[] pixels,
            int width,
            int height,
            int centerX,
            int centerY,
            int radius,
            int thickness,
            byte red,
            byte green,
            byte blue,
            int alpha)
        {
            if (alpha <= 0 || radius <= 0)
            {
                return;
            }

            int outer = radius + thickness;
            for (int y = Math.Max(0, centerY - outer); y <= Math.Min(height - 1, centerY + outer); y++)
            {
                for (int x = Math.Max(0, centerX - outer); x <= Math.Min(width - 1, centerX + outer); x++)
                {
                    int dx = x - centerX;
                    int dy = y - centerY;
                    int distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared < radius * radius || distanceSquared > outer * outer)
                    {
                        continue;
                    }

                    int index = (y * width + x) * 4;
                    Blend(pixels, index, blue, green, red, alpha);
                }
            }
        }

        private static void DrawRectOutline(
            byte[] pixels,
            int width,
            int height,
            int left,
            int top,
            int rectWidth,
            int rectHeight,
            int thickness,
            byte red,
            byte green,
            byte blue,
            int alpha)
        {
            for (int t = 0; t < thickness; t++)
            {
                int x0 = left + t;
                int y0 = top + t;
                int x1 = left + rectWidth - 1 - t;
                int y1 = top + rectHeight - 1 - t;
                for (int x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                {
                    BlendAt(pixels, width, height, x, y0, blue, green, red, alpha);
                    BlendAt(pixels, width, height, x, y1, blue, green, red, alpha);
                }
                for (int y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
                {
                    BlendAt(pixels, width, height, x0, y, blue, green, red, alpha);
                    BlendAt(pixels, width, height, x1, y, blue, green, red, alpha);
                }
            }
        }

        private static void BlendAt(
            byte[] pixels,
            int width,
            int height,
            int x,
            int y,
            byte blue,
            byte green,
            byte red,
            int alpha)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            Blend(pixels, (y * width + x) * 4, blue, green, red, alpha);
        }

        private static void Blend(byte[] pixels, int index, byte blue, byte green, byte red, int alpha)
        {
            int inverse = 255 - alpha;
            pixels[index] = (byte)((blue * alpha + pixels[index] * inverse) / 255);
            pixels[index + 1] = (byte)((green * alpha + pixels[index + 1] * inverse) / 255);
            pixels[index + 2] = (byte)((red * alpha + pixels[index + 2] * inverse) / 255);
            pixels[index + 3] = 255;
        }
    }
}
