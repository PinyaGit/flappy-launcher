using System;
using System.Threading;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Thread-safe pause/resume coordinator for HTTP downloads and extraction.
    /// </summary>
    internal static class DownloadControl
    {
        private static readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
        public static bool IsPaused { get; private set; }

        public static event Action<bool> PauseStateChanged;

        public static void Pause()
        {
            IsPaused = true;
            _pauseEvent.Reset();
            PauseStateChanged?.Invoke(true);
            LauncherLog.Info("Downloads paused");
        }

        public static void Resume()
        {
            IsPaused = false;
            _pauseEvent.Set();
            PauseStateChanged?.Invoke(false);
            LauncherLog.Info("Downloads resumed");
        }

        public static void Toggle()
        {
            if (IsPaused) Resume();
            else Pause();
        }

        public static void Reset()
        {
            IsPaused = false;
            _pauseEvent.Set();
            PauseStateChanged?.Invoke(false);
        }

        public static void WaitIfPaused(CancellationToken cancel)
        {
            if (!IsPaused) return;
            while (IsPaused)
            {
                cancel.ThrowIfCancellationRequested();
                _pauseEvent.Wait(200, cancel);
            }
        }
    }

    /// <summary>
    /// Token-bucket bandwidth limiter across all parallel download sockets.
    /// </summary>
    internal static class SpeedLimiter
    {
        private static long _tokens;
        private static long _lastRefillTick = Environment.TickCount64;
        private static readonly object _sync = new object();

        public static void Throttle(int byteCount, CancellationToken cancel = default)
        {
            int limitMBps = LauncherSettings.DownloadSpeedLimitMBps;
            if (limitMBps <= 0) return;

            long limitBytesPerSec = (long)limitMBps * 1024 * 1024;
            lock (_sync)
            {
                long now = Environment.TickCount64;
                long elapsed = now - _lastRefillTick;
                if (elapsed > 0)
                {
                    long newTokens = (elapsed * limitBytesPerSec) / 1000;
                    _tokens = Math.Min(limitBytesPerSec, _tokens + newTokens);
                    _lastRefillTick = now;
                }

                _tokens -= byteCount;
                if (_tokens < 0)
                {
                    long deficit = -_tokens;
                    int sleepMs = (int)Math.Min(500, Math.Max(1, (deficit * 1000) / limitBytesPerSec));
                    if (sleepMs > 0)
                    {
                        Thread.Sleep(sleepMs);
                    }
                }
            }
        }
    }
}
