using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CryoUtils.Services;
using CryoUtils.Services.Recording;

namespace CryoUtils.Tests
{
    internal static class ReplayEndToEnd
    {
        public static int Run()
        {
            string[] durations = (Environment.GetEnvironmentVariable("CRYO_REPLAY_DURATIONS") ?? "15,120,600")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int failures = 0;
            foreach (string item in durations)
            {
                if (!int.TryParse(item, out int seconds))
                {
                    continue;
                }

                try
                {
                    RunOne(seconds);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"E2E duration={seconds} FAIL {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }

            Console.WriteLine($"E2E CHECKS={durations.Length} FAILURES={failures}");
            return failures == 0 ? 0 : 1;
        }

        private static void RunOne(int seconds)
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "CryoUtils.Tests",
                $"replay_{seconds}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                var options = new RecordingOptions
                {
                    Fps = 30,
                    Resolution = "Source",
                    Quality = RecorderQuality.Low,
                    ReplaySeconds = seconds,
                    RecordSystemAudio = true,
                    RecordMicrophone = false
                };

                string replayPath = Path.Combine(folder, $"replay_{seconds}.mp4");
                var frame = new byte[320 * 180 * 4];
                var audio = new short[1600 * 2];
                long lastAudioSample = 0;
                using (var ring = new ReplaySegmentRing(folder, options, 320, 180, 1_500_000))
                {
                    ring.Start();
                    if (!ring.HasAudio)
                    {
                        throw new InvalidOperationException("Replay ring started without AAC audio.");
                    }

                    int frames = seconds * options.NormalizedFps;
                    for (int index = 0; index < frames; index++)
                    {
                        long ticks = index * 10_000_000L / options.NormalizedFps;
                        byte level = (byte)(index / options.NormalizedFps % 2 == 0 ? 220 : 60);
                        for (int offset = 0; offset < frame.Length; offset += 4)
                        {
                            frame[offset] = level;
                            frame[offset + 1] = (byte)(level / 2);
                            frame[offset + 2] = (byte)(255 - level);
                            frame[offset + 3] = 255;
                        }

                        FillAudio(audio, lastAudioSample);
                        lastAudioSample += audio.Length / 2;
                        ring.WriteVideo(frame, ticks);
                        ring.WriteAudio(audio, ticks);
                    }

                    ring.SealCurrent();
                    var snapshot = ring.Snapshot(seconds);
                    if (snapshot.Count == 0)
                    {
                        throw new InvalidOperationException("Replay snapshot was empty.");
                    }

                    ring.Pin(snapshot);
                    try
                    {
                        ReplayRemuxer.Remux(snapshot, replayPath);
                    }
                    finally
                    {
                        ring.Unpin(snapshot);
                    }

                    Console.WriteLine(
                        $"E2E duration={seconds} segments={snapshot.Count} bytes={new FileInfo(replayPath).Length} ringError={ring.LastError}");
                }

                AssertTracks(replayPath, audio: true, video: true);
                var probe = Probe(replayPath);
                if (probe.VideoDuration <= 0 || probe.AudioDuration <= 0)
                {
                    throw new InvalidOperationException("Remux is missing a timed stream.");
                }

                double durationDelta = Math.Abs(probe.VideoDuration - probe.AudioDuration);
                if (durationDelta > 0.25)
                {
                    throw new InvalidOperationException(
                        $"A/V duration mismatch: video={probe.VideoDuration:F3}s audio={probe.AudioDuration:F3}s.");
                }

                Console.WriteLine(
                    $"E2E duration={seconds} video={probe.VideoDuration:F3}s audio={probe.AudioDuration:F3}s delta={durationDelta:F3}s start={probe.AudioStart:F3}");

                if (seconds == 15)
                {
                    CheckBeeps(replayPath);
                    CheckTrim(replayPath, folder);
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {
                }
            }
        }

        private static void FillAudio(short[] audio, long firstSample)
        {
            for (int frame = 0; frame < audio.Length / 2; frame++)
            {
                double seconds = (firstSample + frame) / 48_000.0;
                double phase = seconds - Math.Floor(seconds);
                short value = phase < 0.12
                    ? (short)(Math.Sin(2 * Math.PI * 440 * frame / 48_000.0) * 8_000)
                    : (short)0;
                audio[frame * 2] = value;
                audio[frame * 2 + 1] = value;
            }
        }

        private static void CheckBeeps(string path)
        {
            string output = Run(
                "ffmpeg",
                $"-hide_banner -i \"{path}\" -af silencedetect=noise=-35dB:d=0.20 -f null -");
            var ends = output
                .Split('\n')
                .Where(line => line.Contains("silence_end:", StringComparison.Ordinal))
                .Select(line =>
                {
                    int index = line.IndexOf("silence_end:", StringComparison.Ordinal) + 12;
                    string value = line.Substring(index).Trim().Split(' ')[0];
                    return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                        ? parsed
                        : -1;
                })
                .Where(value => value >= 0)
                .ToList();
            if (ends.Count < 3)
            {
                throw new InvalidOperationException($"Expected audio markers, found {ends.Count}.");
            }

            for (int index = 1; index < Math.Min(ends.Count, 8); index++)
            {
                double gap = ends[index] - ends[index - 1];
                if (Math.Abs(gap - 1.0) > 0.12)
                {
                    throw new InvalidOperationException($"Audio marker spacing was {gap:F3}s.");
                }
            }

            Console.WriteLine($"E2E audioMarkers={ends.Count} first={ends[0]:F3}s");
        }

        private static void CheckTrim(string path, string folder)
        {
            string output = Path.Combine(folder, "trim.mp4");
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    MediaConversionService.TrimVideoAsync(path, output, 2, 8).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new InvalidOperationException($"Trim failed: {error.Message}", error);
            }

            AssertTracks(output, audio: true, video: true);
            var probe = Probe(output);
            if (probe.VideoDuration < 5.0 || probe.VideoDuration > 7.5)
            {
                throw new InvalidOperationException($"Trim duration was {probe.VideoDuration:F3}s.");
            }

            Console.WriteLine(
                $"E2E trim video={probe.VideoDuration:F3}s audio={probe.AudioDuration:F3}s bytes={new FileInfo(output).Length}");
        }

        private static (double VideoDuration, double AudioDuration, double AudioStart) Probe(string path)
        {
            string output = Run(
                "ffprobe",
                $"-v error -show_entries stream=codec_type,start_time,duration -of csv=p=0 \"{path}\"");
            double video = -1;
            double audio = -1;
            double audioStart = -1;
            foreach (string line in output.Split('\n'))
            {
                string[] parts = line.Trim().Split(',');
                if (parts.Length < 3)
                {
                    continue;
                }

                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double start) ||
                    !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                {
                    continue;
                }

                if (parts[0] == "video")
                {
                    video = duration;
                }
                else if (parts[0] == "audio")
                {
                    audio = duration;
                    audioStart = start;
                }
            }

            return (video, audio, audioStart);
        }

        private static void AssertTracks(string path, bool audio, bool video)
        {
            int hr = MfApi.MFCreateSourceReaderFromURL(path, null, out var reader);
            if (hr < 0)
            {
                throw new InvalidOperationException($"Could not open {Path.GetFileName(path)} (0x{hr:X8}).");
            }

            try
            {
                if (video)
                {
                    hr = reader.GetNativeMediaType(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                        0,
                        out var type);
                    if (hr < 0)
                    {
                        throw new InvalidOperationException($"Video track missing (0x{hr:X8}).");
                    }
                    Marshal.ReleaseComObject(type);
                }

                if (audio)
                {
                    hr = reader.GetNativeMediaType(
                        unchecked((uint)MfGuids.MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                        0,
                        out var type);
                    if (hr < 0)
                    {
                        throw new InvalidOperationException($"Audio track missing (0x{hr:X8}).");
                    }
                    type.GetGUID(MfGuids.MF_MT_SUBTYPE, out Guid subtype);
                    Marshal.ReleaseComObject(type);
                    if (subtype != MfGuids.MFAudioFormat_AAC && subtype != MfGuids.MFAudioFormat_ADTS)
                    {
                        throw new InvalidOperationException($"Audio codec was {subtype}.");
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(reader);
            }
        }

        private static string Run(string fileName, string arguments)
        {
            var start = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException($"Could not start {fileName}.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"{fileName} failed: {stderr}");
            }

            return stdout + "\n" + stderr;
        }
    }
}
