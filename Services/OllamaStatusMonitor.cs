using System;
using System.Collections.Generic;
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
    /// Monitors Ollama model loading status via /api/ps endpoint.
    /// </summary>
    public interface IOllamaStatusMonitor : IDisposable
    {
        /// <summary>
        /// Event fired when Ollama status changes.
        /// </summary>
        event EventHandler<OllamaStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// Current status of Ollama model.
        /// </summary>
        OllamaModelStatus CurrentStatus { get; }

        /// <summary>
        /// Name of currently loaded model (null if no model loaded).
        /// </summary>
        string? LoadedModelName { get; }

        /// <summary>
        /// Size of loaded model in bytes (0 if no model loaded).
        /// </summary>
        long LoadedModelSize { get; }

        /// <summary>
        /// Start monitoring Ollama status.
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stop monitoring Ollama status.
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Check Ollama status immediately (bypasses polling interval).
        /// </summary>
        Task CheckStatusNowAsync();
    }

    public enum OllamaModelStatus
    {
        Unknown,
        NotLoaded,
        Loading,
        Loaded
    }

    public class OllamaStatusChangedEventArgs : EventArgs
    {
        public OllamaModelStatus Status { get; set; }
        public string? ModelName { get; set; }
        public long ModelSize { get; set; }
    }

    public class OllamaStatusMonitor : IOllamaStatusMonitor
    {
        private const int DefaultStatusTimeoutSeconds = 8;
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
            var timeoutSeconds = GetStatusTimeoutSeconds();
            var handler = new HttpClientHandler
            {
                // Work PCs may enforce system proxy settings that can delay loopback calls.
                UseProxy = false
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            _pollingTimer = new Timer(3000);
            _pollingTimer.Elapsed += OnPollingTimerElapsed;
            _pollingTimer.AutoReset = true;

            Log.Info($"[OllamaStatusMonitor] HTTP timeout configured: {timeoutSeconds}s");
        }

        public void StartMonitoring()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OllamaStatusMonitor));

            Log.Info("[OllamaStatusMonitor] Starting Ollama status monitoring (polling every 3 seconds)");
            _pollingTimer.Start();

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
            if (_disposed)
            {
                return;
            }

            if (!await _checkLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                var psEndpoints = GetOllamaPsEndpoints();
                Log.Info($"[OllamaStatusMonitor][DIAG] Probing /api/ps endpoints: {string.Join(", ", psEndpoints)}");
                var response = await TryGetResponseAsync(psEndpoints, "/api/ps");
                if (response == null)
                {
                    var tagsEndpoints = GetOllamaTagsEndpoints();
                    Log.Info($"[OllamaStatusMonitor][DIAG] Probing /api/tags endpoints: {string.Join(", ", tagsEndpoints)}");
                    if (await IsOllamaReachableViaTagsAsync())
                    {
                        Log.Info("[OllamaStatusMonitor][DIAG] /api/tags reachable, but no model loaded.");
                        UpdateStatus(OllamaModelStatus.NotLoaded, null, 0);
                        return;
                    }

                    Log.Info("[OllamaStatusMonitor][DIAG] Ollama unreachable on all endpoints. Status: Unknown");
                    UpdateStatus(OllamaModelStatus.Unknown, null, 0);
                    return;
                }

                using (response)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    if (!doc.RootElement.TryGetProperty("models", out var modelsArray) ||
                        modelsArray.ValueKind != JsonValueKind.Array)
                    {
                        Log.Info("[OllamaStatusMonitor][DIAG] /api/ps response missing 'models' array. Status: Unknown");
                        UpdateStatus(OllamaModelStatus.Unknown, null, 0);
                        return;
                    }

                    int modelCount = 0;
                    foreach (var _ in modelsArray.EnumerateArray())
                    {
                        modelCount++;
                    }

                    if (modelCount == 0)
                    {
                        Log.Info("[OllamaStatusMonitor][DIAG] /api/ps returned 0 models. Status: NotLoaded");
                        UpdateStatus(OllamaModelStatus.NotLoaded, null, 0);
                        return;
                    }

                    var firstModel = modelsArray.EnumerateArray().GetEnumerator();
                    if (firstModel.MoveNext())
                    {
                        var model = firstModel.Current;
                        string? modelName = null;
                        if (model.TryGetProperty("name", out var nameEl))
                        {
                            modelName = nameEl.GetString();
                        }
                        else if (model.TryGetProperty("model", out var modelEl))
                        {
                            modelName = modelEl.GetString();
                        }

                        long modelSize = 0;
                        if (model.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var parsedSize))
                        {
                            modelSize = parsedSize;
                        }

                        Log.Info($"[OllamaStatusMonitor][DIAG] /api/ps returned loaded model: {modelName} ({FormatBytes(modelSize)})");
                        UpdateStatus(OllamaModelStatus.Loaded, modelName, modelSize);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Log.Info($"[OllamaStatusMonitor][DIAG] HttpRequestException: {ex.Message}");
                UpdateStatus(OllamaModelStatus.Unknown, null, 0);
            }
            catch (TaskCanceledException ex)
            {
                Log.Info($"[OllamaStatusMonitor][DIAG] TaskCanceledException: {ex.Message}");
                UpdateStatus(OllamaModelStatus.Unknown, null, 0);
            }
            catch (Exception ex)
            {
                Log.Info($"[OllamaStatusMonitor][DIAG] Exception: {ex.Message}");
                _logger?.LogWarning(ex, "Error checking Ollama status");
                UpdateStatus(OllamaModelStatus.Unknown, null, 0);
            }
            finally
            {
                _checkLock.Release();
            }
        }

        private async Task<HttpResponseMessage?> TryGetResponseAsync(IEnumerable<string> endpoints, string apiType)
        {
            foreach (var endpoint in endpoints)
            {
                try
                {
                    Log.Info($"[OllamaStatusMonitor][DIAG] Attempting {apiType} endpoint: {endpoint}");
                    var response = await _httpClient.GetAsync(endpoint);
                    if (response.IsSuccessStatusCode)
                    {
                        Log.Info($"[OllamaStatusMonitor][DIAG] Success: {endpoint}");
                        return response;
                    }
                    Log.Info($"[OllamaStatusMonitor][DIAG] HTTP {(int)response.StatusCode} from {endpoint}");
                    response.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Info($"[OllamaStatusMonitor][DIAG] Exception on {endpoint}: {ex.Message}");
                }
            }
            Log.Info($"[OllamaStatusMonitor][DIAG] All {apiType} endpoints failed.");
            return null;
        }

        private async Task<bool> IsOllamaReachableViaTagsAsync()
        {
            var response = await TryGetResponseAsync(GetOllamaTagsEndpoints(), "/api/tags");
            if (response == null)
            {
                return false;
            }
            response.Dispose();
            return true;
        }

        private static IEnumerable<string> GetOllamaPsEndpoints()
        {
            return BuildOllamaEndpoints("/api/ps");
        }

        private static IEnumerable<string> GetOllamaTagsEndpoints()
        {
            return BuildOllamaEndpoints("/api/tags");
        }

        private static IEnumerable<string> BuildOllamaEndpoints(string apiPath)
        {
            var endpoints = new List<string>();

            AddEndpoint(endpoints, "http://127.0.0.1:11434", apiPath);
            AddEndpoint(endpoints, "http://localhost:11434", apiPath);

            var envHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
            if (!string.IsNullOrWhiteSpace(envHost))
            {
                AddEndpoint(endpoints, envHost.Trim(), apiPath);
            }

            return endpoints;
        }

        private static int GetStatusTimeoutSeconds()
        {
            var envTimeout = Environment.GetEnvironmentVariable("OLLAMA_STATUS_TIMEOUT_SECONDS");
            if (int.TryParse(envTimeout, out var parsedTimeout))
            {
                return Math.Clamp(parsedTimeout, 2, 30);
            }

            return DefaultStatusTimeoutSeconds;
        }

        private static void AddEndpoint(List<string> endpoints, string baseOrFullValue, string apiPath)
        {
            if (string.IsNullOrWhiteSpace(baseOrFullValue))
            {
                return;
            }

            var value = baseOrFullValue.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "http://" + value;
            }

            string endpoint;
            if (value.EndsWith(apiPath, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = value;
            }
            else
            {
                endpoint = value.TrimEnd('/') + apiPath;
            }

            foreach (var existing in endpoints)
            {
                if (string.Equals(existing, endpoint, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            endpoints.Add(endpoint);
        }

        private void UpdateStatus(OllamaModelStatus newStatus, string? modelName, long modelSize)
        {
            var statusChanged = _currentStatus != newStatus;
            var modelChanged = _loadedModelName != modelName;

            if (!statusChanged && !modelChanged)
            {
                return;
            }

            var oldStatus = _currentStatus;
            _currentStatus = newStatus;
            _loadedModelName = modelName;
            _loadedModelSize = modelSize;
            _lastStatusChange = DateTime.Now;

            Log.Info($"[OllamaStatusMonitor] Status changed: {oldStatus} -> {newStatus}" +
                     (modelName != null ? $" | Model: {modelName} ({FormatBytes(modelSize)})" : ""));

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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            _httpClient?.Dispose();
            _checkLock?.Dispose();
        }
    }
}
