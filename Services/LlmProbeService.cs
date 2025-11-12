using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Optional: probe a local Ollama HTTP endpoint periodically and update the global LlmConnectionManager.
    /// Safe: single shared HttpClient, concurrency guard, DispatcherTimer for UI-thread-friendly scheduling.
    /// </summary>
    public class LlmProbeService : IDisposable
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly DispatcherTimer? _timer;
        private readonly string _probeUrl;
        private int _isChecking;
        private bool _disposed;

        public LlmProbeService(string probeUrl = "http://localhost:11434/api/tags", TimeSpan? pollInterval = null)
        {
            _probeUrl = probeUrl ?? throw new ArgumentNullException(nameof(probeUrl));
            if (pollInterval.HasValue)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = pollInterval.Value
                };
                _timer.Tick += async (_, __) => await CheckNowAsync().ConfigureAwait(false);
            }
        }

        // Start periodic monitoring; if no timer configured this still triggers a one-off check.
        public void Start(TimeSpan? interval = null)
        {
            if (interval.HasValue && _timer != null) _timer.Interval = interval.Value;

            // Fire an immediate check and start timer if present
            _ = CheckNowAsync();
            try { if (_timer != null && !_timer.IsEnabled) _timer.Start(); } catch { }
        }

        public void Stop()
        {
            try { _timer?.Stop(); } catch { }
        }

        public async Task CheckNowAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _isChecking, 1) == 1) return;

            try
            {
                HttpResponseMessage? resp = null;
                try
                {
                    resp = await _http.GetAsync(_probeUrl).ConfigureAwait(false);
                }
                catch
                {
                    resp = null;
                }

                var reachable = resp != null && resp.IsSuccessStatusCode;

                // Report to global manager (decide policy here — we set connected = reachable)
                // If you want integration-on-only semantics (reachable && UseIntegratedLlm),
                // call SetConnected from where you know the UseIntegratedLlm flag.
                try
                {
                    LlmConnectionManager.SetConnected(reachable);
                }
                catch { /* swallow if manager unavailable */ }
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _timer?.Stop(); } catch { }
        }
    }
}