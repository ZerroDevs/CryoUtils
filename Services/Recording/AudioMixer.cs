using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CryoUtils.Services.Recording
{
    /// <summary>
    /// Joins the system loopback and microphone streams at 48 kHz stereo. Each
    /// capture service already normalizes its format; this mixer only combines
    /// the two queues and protects against clipping.
    /// </summary>
    public sealed class AudioMixer : IDisposable
    {
        private readonly ConcurrentQueue<short[]> _system = new();
        private readonly ConcurrentQueue<short[]> _microphone = new();
        private readonly WasapiCaptureService? _systemCapture;
        private readonly WasapiCaptureService? _microphoneCapture;
        private readonly CancellationTokenSource _stop = new();
        private Thread? _thread;
        private volatile bool _running;
        private volatile bool _paused;
        private volatile float _currentPeakLevel;

        public event Action<short[]>? MixedSamples;
        public bool IsRunning => _running;
        public float CurrentPeakLevel => _currentPeakLevel;
        public string LastError { get; private set; } = "";

        public AudioMixer(
            bool includeSystem,
            bool includeMicrophone,
            string? systemDeviceId = null,
            string? microphoneDeviceId = null)
        {
            if (includeSystem)
            {
                _systemCapture = new WasapiCaptureService(loopback: true, systemDeviceId);
                _systemCapture.SamplesAvailable += samples => Enqueue(_system, samples);
            }

            if (includeMicrophone)
            {
                _microphoneCapture = new WasapiCaptureService(loopback: false, microphoneDeviceId);
                _microphoneCapture.SamplesAvailable += samples => Enqueue(_microphone, samples);
            }
        }

        public bool Start()
        {
            if (_running)
            {
                return true;
            }

            bool system = _systemCapture == null || _systemCapture.Start();
            bool microphone = _microphoneCapture == null || _microphoneCapture.Start();
            if (!system && !microphone)
            {
                LastError = _systemCapture?.LastError
                    ?? _microphoneCapture?.LastError
                    ?? "Audio capture is unavailable.";
                return false;
            }

            if (!system)
            {
                LastError = _systemCapture?.LastError ?? "";
            }
            if (!microphone)
            {
                LastError = _microphoneCapture?.LastError ?? LastError;
            }

            _running = true;
            _thread = new Thread(MixLoop)
            {
                IsBackground = true,
                Name = "CryoUtils audio mixer"
            };
            _thread.Start();
            return true;
        }

        private static void Enqueue(ConcurrentQueue<short[]> queue, short[] samples)
        {
            if (samples.Length == 0)
            {
                return;
            }

            queue.Enqueue(samples);
            while (queue.Count > 120)
            {
                queue.TryDequeue(out _);
            }
        }

        private void MixLoop()
        {
            while (_running)
            {
                try
                {
                    if (_paused)
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    _system.TryDequeue(out var system);
                    _microphone.TryDequeue(out var microphone);
                    if (system == null && microphone == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    int frames = Math.Max(
                        (system?.Length ?? 0) / 2,
                        (microphone?.Length ?? 0) / 2);
                    if (frames <= 0)
                    {
                        continue;
                    }

                    var mixed = new short[frames * 2];
                    int peak = 0;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        int left = system != null && frame * 2 < system.Length ? system[frame * 2] : 0;
                        int right = system != null && frame * 2 + 1 < system.Length ? system[frame * 2 + 1] : 0;
                        int micLeft = microphone != null && frame * 2 < microphone.Length ? microphone[frame * 2] : 0;
                        int micRight = microphone != null && frame * 2 + 1 < microphone.Length ? microphone[frame * 2 + 1] : 0;
                        short outL = (short)Math.Clamp(left + micLeft, short.MinValue, short.MaxValue);
                        short outR = (short)Math.Clamp(right + micRight, short.MinValue, short.MaxValue);
                        mixed[frame * 2] = outL;
                        mixed[frame * 2 + 1] = outR;
                        int absL = Math.Abs((int)outL);
                        int absR = Math.Abs((int)outR);
                        if (absL > peak) peak = absL;
                        if (absR > peak) peak = absR;
                    }

                    float normalized = peak / 32768f;
                    _currentPeakLevel = Math.Max(normalized, _currentPeakLevel * 0.65f);
                    MixedSamples?.Invoke(mixed);
                }
                catch
                {
                    break;
                }
            }
        }

        public void Pause() => _paused = true;

        public void Resume() => _paused = false;

        public void Stop()
        {
            _running = false;
            _thread?.Join(800);
            _thread = null;
            _systemCapture?.Stop();
            _microphoneCapture?.Stop();
        }

        public void Dispose()
        {
            Stop();
            _stop.Cancel();
            _systemCapture?.Dispose();
            _microphoneCapture?.Dispose();
            _stop.Dispose();
        }
    }
}
