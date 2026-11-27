using System;
using CryoUtils.Models;

namespace CryoUtils.Services.Recording
{
    public enum RecorderQuality
    {
        Low,
        Medium,
        High,
        Custom
    }

    /// <summary>
    /// Normalized settings for the live recorder. All values are clamped so the
    /// capture and Media Foundation layers can rely on them.
    /// </summary>
    public sealed class RecordingOptions
    {
        public const int MinimumReplaySeconds = 15;
        public const int MaximumReplaySeconds = 600;
        public const int SegmentSeconds = 4;

        public int Fps { get; init; } = 30;
        public string Resolution { get; init; } = "Source";
        public RecorderQuality Quality { get; init; } = RecorderQuality.Medium;
        public int BitrateMbps { get; init; } = 0;
        public int MaxSeconds { get; init; } = 300;
        public bool RecordSystemAudio { get; init; }
        public bool RecordMicrophone { get; init; }
        public string SystemAudioDeviceId { get; init; } = "";
        public string MicrophoneDeviceId { get; init; } = "";
        public bool CursorHighlight { get; init; } = true;
        public bool ClickEffects { get; init; } = true;
        public bool KeystrokeHud { get; init; }
        public bool WebcamBubble { get; init; }
        public bool TrimBeforeSave { get; init; } = true;
        public int ReplaySeconds { get; init; } = 30;
        public string ReplayMonitor { get; init; } = "Primary";
        public string ReplayHotkeyModifiers { get; init; } = "Ctrl+Shift";
        public string ReplayHotkeyKey { get; init; } = "R";
        public long ReplayDiskBudgetBytes { get; init; }

        public int NormalizedFps => Fps >= 60 ? 60 : 30;

        public int NormalizedMaxSeconds => MaxSeconds switch
        {
            <= 60 => 60,
            <= 300 => 300,
            <= 600 => 600,
            <= 1800 => 1800,
            _ => 3600
        };

        public int NormalizedReplaySeconds =>
            Math.Clamp(ReplaySeconds, MinimumReplaySeconds, MaximumReplaySeconds);

        public static RecordingOptions FromSettings(AppSettings settings)
        {
            return new RecordingOptions
            {
                Fps = settings.RecorderFps >= 60 ? 60 : 30,
                Resolution = settings.RecorderResolution,
                Quality = ParseQuality(settings.RecorderQuality),
                BitrateMbps = Math.Clamp(settings.RecorderBitrateMbps, 0, 100),
                MaxSeconds = Math.Clamp(settings.MaxRecordingSeconds, 60, 3600),
                RecordSystemAudio = settings.RecorderSystemAudio,
                RecordMicrophone = settings.RecorderMicrophone,
                SystemAudioDeviceId = settings.RecorderSystemAudioDeviceId,
                MicrophoneDeviceId = settings.RecorderMicrophoneDeviceId,
                CursorHighlight = settings.RecorderCursorHighlight,
                ClickEffects = settings.RecorderClickEffects,
                KeystrokeHud = settings.RecorderKeystrokeHud,
                WebcamBubble = settings.RecorderWebcam,
                TrimBeforeSave = settings.RecorderTrimBeforeSave,
                ReplaySeconds = Math.Clamp(
                    settings.ReplaySeconds,
                    MinimumReplaySeconds,
                    MaximumReplaySeconds),
                ReplayMonitor = settings.ReplayMonitor,
                ReplayHotkeyModifiers = settings.ReplayHotkeyModifiers,
                ReplayHotkeyKey = settings.ReplayHotkeyKey,
                ReplayDiskBudgetBytes = Math.Max(
                    0,
                    settings.ReplayDiskBudgetGb) * 1024L * 1024L * 1024L
            };
        }

        public (int Width, int Height) ResolveSize(int sourceWidth, int sourceHeight)
        {
            int width = Math.Max(2, sourceWidth & ~1);
            int height = Math.Max(2, sourceHeight & ~1);
            int targetHeight = Resolution switch
            {
                "720p" => 720,
                "1080p" => 1080,
                "1440p" => 1440,
                "2160p" => 2160,
                _ => height
            };

            if (targetHeight >= height)
            {
                return (width, height);
            }

            double scale = targetHeight / (double)height;
            return (
                Math.Max(2, ((int)Math.Round(width * scale)) & ~1),
                Math.Max(2, targetHeight & ~1));
        }

        public int ResolveBitrate(int width, int height)
        {
            if (Quality == RecorderQuality.Custom && BitrateMbps > 0)
            {
                return BitrateMbps * 1_000_000;
            }

            // Keep the curve restrained enough for software H.264 fallback.
            double bitsPerPixel = Quality switch
            {
                RecorderQuality.Low => 0.06,
                RecorderQuality.High => 0.16,
                _ => 0.10
            };
            long computed = (long)(width * (double)height * NormalizedFps * bitsPerPixel);
            return (int)Math.Clamp(computed, 2_000_000, 60_000_000);
        }

        public static RecorderQuality ParseQuality(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "low" => RecorderQuality.Low,
                "high" => RecorderQuality.High,
                "custom" => RecorderQuality.Custom,
                _ => RecorderQuality.Medium
            };
        }

        public static string QualityName(RecorderQuality quality) => quality.ToString();
    }

    public static class ReplayMath
    {
        public static int RetentionCount(int replaySeconds, int segmentSeconds)
        {
            int duration = Math.Clamp(
                replaySeconds,
                RecordingOptions.MinimumReplaySeconds,
                RecordingOptions.MaximumReplaySeconds);
            int segment = Math.Max(1, segmentSeconds);
            return Math.Max(2, (int)Math.Ceiling(duration / (double)segment) + 1);
        }

        public static long EstimatedDiskBytes(int bitrate, int replaySeconds)
        {
            long seconds = Math.Clamp(
                replaySeconds,
                RecordingOptions.MinimumReplaySeconds,
                RecordingOptions.MaximumReplaySeconds);
            return Math.Max(0, bitrate) * seconds / 8L;
        }

        public static bool HasEnoughFreeSpace(long freeBytes, long requiredBytes)
        {
            if (requiredBytes <= 0)
            {
                return true;
            }

            return freeBytes >= requiredBytes * 2L;
        }

        public static DateTime ClipStart(DateTime newestEnd, int replaySeconds)
        {
            return newestEnd - TimeSpan.FromSeconds(Math.Clamp(
                replaySeconds,
                RecordingOptions.MinimumReplaySeconds,
                RecordingOptions.MaximumReplaySeconds));
        }
    }
}
