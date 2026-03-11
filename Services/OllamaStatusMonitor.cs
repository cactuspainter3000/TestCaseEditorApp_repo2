using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services.Logging;
using Timer = System.Timers.Timer;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Monitors Ollama model loading status via /api/ps endpoint
    /// </summary>
    public interface IOllamaStatusMonitor : IDisposable
    {
        /// <summary>
        /// Event fired when Ollama status changes
        /// </summary>
        event EventHandler<OllamaStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// Current status of Ollama model
        /// </summary>
        OllamaModelStatus CurrentStatus { get; }

        /// <summary>
        /// Name of currently loaded model (null if no model loaded)
        /// </summary>
        string? LoadedModelName { get; }

        /// <summary>
        /// Size of loaded model in bytes (0 if no model loaded)
        /// </summary>
        long LoadedModelSize { get; }

        /// <summary>
        /// Start monitoring Ollama status
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stop monitoring Ollama status
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Check Ollama status immediately (bypasses polling interval)
        /// </summary>
        Task CheckStatusNowAsync();
    }

    public enum OllamaModelStatus
    {
        Unknown,        // Initial state or error checking status
        NotLoaded,      // Ollama running but no model in memory
        Loading,        // Model is currently loading (detected by transition)
        Loaded          // Model is loaded and ready
    }

    public class OllamaStatusChangedEventArgs : EventArgs
    {
        public OllamaModelStatus Status { get; set; }
        public string? ModelName { get; set; }
        public long ModelSize { get; set; }
    }

    public class OllamaStatusMonitor : IOllamaStatusMonitor
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OllamaStatusMonitor>? _logger;
        private readonly Timer _pollingTimer;
        private readonly SemaphoreSlim _checkLock = new SemaphoreSlim(1, 1);

        private OllamaModelStatus _currentStatus = OllamaModelStatus.Unknown;
        private string? _loadedModelName;
        private long _loadedModelSize;
        private DateTime? _lastStatusChange;
        private bool _disposed;

        public event EventHandler<OllamaStatusChangedEventArgs>? StatusChanged;

        public OllamaModelStatus CurrentStatus => _currentStatus;
        public string? LoadedModelName => _loadedModelName;
        public long LoadedModelSize => _loadedModelSize;

        public OllamaStatusMonitor(ILogger<OllamaStatusMonitor>? logger = null)
        {
            _logger = logger;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:11434/"),
                Timeout = TimeSpan.FromSeconds(2) // Quick timeout for polling
            };

            _pollingTimer = new Timer(3000); // Poll every 3 seconds
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;
        }

        public void StartMonitoring()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OllamaStatusMonitor));
            
            System.Diagnostics.Debug.WriteLine("[OllamaStatusMonitor] ===== STARTING OLLAMA MONITORING =====");
            System.Console.WriteLine("[OllamaStatusMonitor] ===== STARTING OLLAMA MONITORING =====");
            Log.Info("[OllamaStatusMonitor] Starting Ollama status monitoring (polling every 3 seconds)");
            _pollingTimer.Start();
            
            // Check immediately
            _ = Task.Run(async () => await CheckStatusNowAsync());
        }

        public void StopMonitoring()
        {
            Log.Info("[OllamaStatusMonitor] Stopping Ollama status monitoring");
            _pollingTimer.Stop();
        }

        private async void OnPollingTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            await CheckStatusNowAsync();
        }

        public async Task CheckStatusNowAsync()
        {
            if (_disposed) return;
            
            // Prevent concurrent checks
            if (!await _checkLock.WaitAsync(0))
                return;

            try
            {
                var response = await _httpClient.GetAsync("api/ps");
                
                if (!response.IsSuccessStatusCode)
                {
                    UpdateStatus(OllamaModelStatus.Unknown, null, 0);
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                if (!doc.RootElement.TryGetProperty("models", out var modelsArray) ||
                    modelsArray.ValueKind != JsonValueKind.Array)
                {
                    UpdateStatus(OllamaModelStatus.Unknown, null, 0);
                    return;
                }

                var modelCount = 0;
                foreach (var _ in modelsArray.EnumerateArray())
                    modelCount++;

                if (modelCount == 0)
                {
                    // No models loaded
                    UpdateStatus(OllamaModelStatus.NotLoaded, null, 0);
                }
                else
                {
                    // Get first model (usually only one loaded at a time)
                    var firstModel = modelsArray.EnumerateArray().GetEnumerator();
                    if (firstModel.MoveNext())
                    {
                        var model = firstModel.Current;
                        var modelName = model.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                        var modelSize = model.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

                        UpdateStatus(OllamaModelStatus.Loaded, modelName, modelSize);
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Ollama not responding - probably not running
                UpdateStatus(OllamaModelStatus.Unknown, null, 0);
            }
            catch (TaskCanceledException)
            {
                // Timeout - Ollama might be stuck or not responding
                UpdateStatus(OllamaModelStatus.Unknown, null, 0);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking Ollama status");
                UpdateStatus(OllamaModelStatus.Unknown, null, 0);
            }
            finally
            {
                _checkLock.Release();
            }
        }

        private void UpdateStatus(OllamaModelStatus newStatus, string? modelName, long modelSize)
        {
            var statusChanged = _currentStatus != newStatus;
            var modelChanged = _loadedModelName != modelName;

            System.Diagnostics.Debug.WriteLine($"[OllamaStatusMonitor] Status: {_currentStatus} → {newStatus}, Model: {modelName ?? "none"}");
            
            if (!statusChanged && !modelChanged)
                return; // No change

            var oldStatus = _currentStatus;
            _currentStatus = newStatus;
            _loadedModelName = modelName;
            _loadedModelSize = modelSize;
            _lastStatusChange = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[OllamaStatusMonitor] *** STATUS CHANGE DETECTED *** {oldStatus} → {newStatus}");
            System.Console.WriteLine($"[OllamaStatusMonitor] *** STATUS CHANGE DETECTED *** {oldStatus} → {newStatus}");
            Log.Info($"[OllamaStatusMonitor] Status changed: {oldStatus} → {newStatus}" + 
                     (modelName != null ? $" | Model: {modelName} ({FormatBytes(modelSize)})" : ""));

            // Raise event on thread pool to avoid blocking timer
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    StatusChanged?.Invoke(this, new OllamaStatusChangedEventArgs
                    {
                        Status = newStatus,
                        ModelName = modelName,
                        ModelSize = modelSize
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in StatusChanged event handler");
                }
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _httpClient?.Dispose();
            _checkLock?.Dispose();
        }
    }
}
