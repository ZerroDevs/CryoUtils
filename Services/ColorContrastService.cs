using System;
using System.Windows.Media;

namespace CryoUtils.Services
{
    public sealed record ContrastEvaluation(
        double Ratio,
        bool NormalTextAa,
        bool NormalTextAaa,
        bool LargeTextAa,
        bool LargeTextAaa,
        bool UiComponentAa)
    {
        public bool AaNormal => NormalTextAa;
        public bool AaLarge => LargeTextAa;
        public bool AaaNormal => NormalTextAaa;
        public bool AaaLarge => LargeTextAaa;
    }

    public static class ColorContrastService
    {
        public static double CalculateLuminance(Color color)
        {
            double r = ConvertChannel(color.R / 255.0);
            double g = ConvertChannel(color.G / 255.0);
            double b = ConvertChannel(color.B / 255.0);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double ConvertChannel(double channel)
        {
            return channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        public static double CalculateRatio(Color c1, Color c2)
        {
            double l1 = CalculateLuminance(c1);
            double l2 = CalculateLuminance(c2);
            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        public static ContrastEvaluation Evaluate(Color c1, Color c2)
        {
            double ratio = Math.Round(CalculateRatio(c1, c2), 2);
            return Evaluate(ratio);
        }

        public static ContrastEvaluation Evaluate(double ratio)
        {
            double r = Math.Round(ratio, 2);
            return new ContrastEvaluation(
                Ratio: r,
                NormalTextAa: r >= 4.5,
                NormalTextAaa: r >= 7.0,
                LargeTextAa: r >= 3.0,
                LargeTextAaa: r >= 4.5,
                UiComponentAa: r >= 3.0);
        }

        public static string FormatRatio(double ratio) => $"{ratio:0.00}:1";
    }
}
