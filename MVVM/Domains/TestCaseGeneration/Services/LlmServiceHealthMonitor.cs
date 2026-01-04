using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    /// <summary>
    /// Service health monitor for LLM services with automatic fallback and recovery.
    /// Monitors LLM service availability and provides graceful fallback to NoopTextGenerationService.
    /// </summary>
    public sealed class LlmServiceHealthMonitor : IDisposable
    {
        public enum HealthStatus
        {
            Unknown,
            Healthy,
            Degraded,
            Unavailable
        }

        public sealed class HealthReport
        {
            public HealthStatus Status { get; init; }
            public string ServiceType { get; init; } = string.Empty;
            public TimeSpan ResponseTime { get; init; }
            public string? LastError { get; init; }
            public DateTime LastChecked { get; init; }
            public bool IsUsingFallback { get; init; }
        }

        private readonly ITextGenerationService _primaryService;
        private readonly ITextGenerationService _fallbackService;
        private readonly ILogger<LlmServiceHealthMonitor> _logger;
        private readonly Timer _healthCheckTimer;
        private readonly SemaphoreSlim _healthCheckSemaphore;

        private volatile HealthReport _lastHealthReport;
        private volatile bool _isUsingFallback;

        /// <summary>
        /// Event fired when health status changes
        /// </summary>
        public event Action<HealthReport>? HealthStatusChanged;

        /// <summary>
        /// Current health status of the primary LLM service
        /// </summary>
        public HealthReport CurrentHealth => _lastHealthReport;

        /// <summary>
        /// Whether the monitor is currently using fallback service
        /// </summary>
        public bool IsUsingFallback => _isUsingFallback;

        public LlmServiceHealthMonitor(
            ITextGenerationService primaryService,
            ILogger<LlmServiceHealthMonitor> logger,
            TimeSpan? healthCheckInterval = null)
        {
            _primaryService = primaryService ?? throw new ArgumentNullException(nameof(primaryService));
            _fallbackService = new NoopTextGenerationService("[LLM Unavailable] Fallback service response");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _healthCheckSemaphore = new SemaphoreSlim(1, 1);

            _lastHealthReport = new HealthReport
            {
                Status = HealthStatus.Unknown,
                ServiceType = GetServiceType(_primaryService),
                LastChecked = DateTime.MinValue,
                IsUsingFallback = false
            };

            // Start health check timer (default 30 seconds)
            var interval = healthCheckInterval ?? TimeSpan.FromSeconds(30);
            _healthCheckTimer = new Timer(
                async _ => await PerformHealthCheckAsync(),
                null,
                TimeSpan.Zero, // Check immediately on startup
                interval);

            _logger.LogInformation("[LlmHealthMonitor] Started health monitoring for {ServiceType}", GetServiceType(_primaryService));
        }

        /// <summary>
        /// Get an ITextGenerationService that automatically falls back to NoopTextGenerationService
        /// if the primary service is unavailable.
        /// </summary>
        public ITextGenerationService GetHealthyService()
        {
            if (_isUsingFallback || _lastHealthReport.Status == HealthStatus.Unavailable)
            {
                _logger.LogDebug("[LlmHealthMonitor] Returning fallback service due to primary service unavailability");
                return _fallbackService;
            }

            return new HealthAwareServiceProxy(_primaryService, this);
        }

        /// <summary>
        /// Perform an immediate health check and return the result.
        /// </summary>
        public async Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            if (!await _healthCheckSemaphore.WaitAsync(5000, cancellationToken))
            {
                _logger.LogWarning("[LlmHealthMonitor] Health check timeout - semaphore wait exceeded");
                return _lastHealthReport;
            }

            try
            {
                return await PerformHealthCheckInternalAsync(cancellationToken);
            }
            finally
            {
                _healthCheckSemaphore.Release();
            }
        }

        /// <summary>
        /// Force a switch to fallback service (useful for testing or manual override)
        /// </summary>
        public void ForceUsingFallback(bool useFallback, string? reason = null)
        {
            var previousState = _isUsingFallback;
            _isUsingFallback = useFallback;

            if (previousState != useFallback)
            {
                _logger.LogInformation("[LlmHealthMonitor] Manually {Action} fallback service. Reason: {Reason}",
                    useFallback ? "activated" : "deactivated", reason ?? "Manual override");

                var report = new HealthReport
                {
                    Status = useFallback ? HealthStatus.Unavailable : _lastHealthReport.Status,
                    ServiceType = GetServiceType(_primaryService),
                    LastChecked = DateTime.UtcNow,
                    IsUsingFallback = useFallback,
                    LastError = reason
                };

                _lastHealthReport = report;
                HealthStatusChanged?.Invoke(report);
            }
        }

        private async Task PerformHealthCheckAsync()
        {
            if (!await _healthCheckSemaphore.WaitAsync(1000)) // Quick timeout for timer-based checks
                return;

            try
            {
                await PerformHealthCheckInternalAsync();
            }
            finally
            {
                _healthCheckSemaphore.Release();
            }
        }

        private async Task<HealthReport> PerformHealthCheckInternalAsync(CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var serviceType = GetServiceType(_primaryService);

            try
            {
                // Lightweight health check with short timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var testPrompt = "Return only: {\"status\":\"ok\"}";
                var response = await _primaryService.GenerateAsync(testPrompt, combinedCts.Token);

                var responseTime = DateTime.UtcNow - startTime;
                var isHealthy = !string.IsNullOrWhiteSpace(response) && 
                               (response.Contains("status") || response.Contains("ok"));

                HealthStatus status;
                if (isHealthy)
                {
                    status = responseTime > TimeSpan.FromSeconds(5) ? HealthStatus.Degraded : HealthStatus.Healthy;
                }
                else
                {
                    status = HealthStatus.Unavailable;
                }

                var wasPreviouslyUsingFallback = _isUsingFallback;
                _isUsingFallback = status == HealthStatus.Unavailable;

                var report = new HealthReport
                {
                    Status = status,
                    ServiceType = serviceType,
                    ResponseTime = responseTime,
                    LastChecked = DateTime.UtcNow,
                    IsUsingFallback = _isUsingFallback
                };

                // Log status changes
                if (wasPreviouslyUsingFallback && !_isUsingFallback)
                {
                    _logger.LogInformation("[LlmHealthMonitor] {ServiceType} service recovered - switching back from fallback", serviceType);
                }
                else if (!wasPreviouslyUsingFallback && _isUsingFallback)
                {
                    _logger.LogWarning("[LlmHealthMonitor] {ServiceType} service unavailable - switching to fallback", serviceType);
                }

                _lastHealthReport = report;

                // Notify listeners of status changes
                HealthStatusChanged?.Invoke(report);

                return report;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Let cancellation bubble up
            }
            catch (Exception ex)
            {
                var responseTime = DateTime.UtcNow - startTime;
                var wasPreviouslyUsingFallback = _isUsingFallback;
                _isUsingFallback = true;

                var report = new HealthReport
                {
                    Status = HealthStatus.Unavailable,
                    ServiceType = serviceType,
                    ResponseTime = responseTime,
                    LastError = ex.Message,
                    LastChecked = DateTime.UtcNow,
                    IsUsingFallback = _isUsingFallback
                };

                if (!wasPreviouslyUsingFallback)
                {
                    _logger.LogError(ex, "[LlmHealthMonitor] {ServiceType} health check failed - switching to fallback", serviceType);
                }

                _lastHealthReport = report;
                HealthStatusChanged?.Invoke(report);

                return report;
            }
        }

        private static string GetServiceType(ITextGenerationService service)
        {
            return service.GetType().Name switch
            {
                "OllamaTextGenerationService" => "Ollama",
                "OpenAITextGenerationService" => "OpenAI",
                "NoopTextGenerationService" => "Noop",
                _ => service.GetType().Name
            };
        }

        public void Dispose()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckSemaphore?.Dispose();
            _logger.LogInformation("[LlmHealthMonitor] Disposed health monitor");
        }

        /// <summary>
        /// Proxy service that automatically falls back on failures
        /// </summary>
        private sealed class HealthAwareServiceProxy : ITextGenerationService
        {
            private readonly ITextGenerationService _primary;
            private readonly LlmServiceHealthMonitor _monitor;

            public HealthAwareServiceProxy(ITextGenerationService primary, LlmServiceHealthMonitor monitor)
            {
                _primary = primary;
                _monitor = monitor;
            }

            public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
            {
                try
                {
                    return await _primary.GenerateAsync(prompt, ct);
                }
                catch when (_monitor._isUsingFallback)
                {
                    // Already using fallback, use fallback service
                    return await _monitor._fallbackService.GenerateAsync(prompt, ct);
                }
                catch (Exception ex)
                {
                    // Primary failed, switch to fallback and retry
                    _monitor._isUsingFallback = true;
                    _monitor._logger.LogWarning(ex, "[LlmHealthMonitor] Primary service failed during generation, switching to fallback");
                    
                    // Update health status immediately
                    var report = new HealthReport
                    {
                        Status = HealthStatus.Unavailable,
                        ServiceType = GetServiceType(_primary),
                        LastError = ex.Message,
                        LastChecked = DateTime.UtcNow,
                        IsUsingFallback = true
                    };
                    _monitor._lastHealthReport = report;
                    _monitor.HealthStatusChanged?.Invoke(report);

                    return await _monitor._fallbackService.GenerateAsync(prompt, ct);
                }
            }

            public async Task<string> GenerateWithSystemAsync(string systemMessage, string contextMessage, CancellationToken ct = default)
            {
                try
                {
                    return await _primary.GenerateWithSystemAsync(systemMessage, contextMessage, ct);
                }
                catch when (_monitor._isUsingFallback)
                {
                    // Already using fallback, use fallback service
                    return await _monitor._fallbackService.GenerateWithSystemAsync(systemMessage, contextMessage, ct);
                }
                catch (Exception ex)
                {
                    // Primary failed, switch to fallback and retry
                    _monitor._isUsingFallback = true;
                    _monitor._logger.LogWarning(ex, "[LlmHealthMonitor] Primary service failed during system generation, switching to fallback");
                    
                    // Update health status immediately
                    var report = new HealthReport
                    {
                        Status = HealthStatus.Unavailable,
                        ServiceType = GetServiceType(_primary),
                        LastError = ex.Message,
                        LastChecked = DateTime.UtcNow,
                        IsUsingFallback = true
                    };
                    _monitor._lastHealthReport = report;
                    _monitor.HealthStatusChanged?.Invoke(report);

                    return await _monitor._fallbackService.GenerateWithSystemAsync(systemMessage, contextMessage, ct);
                }
            }
        }
    }
}