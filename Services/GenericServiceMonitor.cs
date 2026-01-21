using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Generic service monitoring configuration for different service types
    /// </summary>
    public class ServiceMonitorConfig
    {
        public string Name { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(10);
        public ServiceType Type { get; set; }
        public Action<ServiceStatus>? OnStatusChanged { get; set; }
    }

    public enum ServiceType
    {
        AnythingLLM,
        SAP,
        Generic
    }

    public class ServiceStatus
    {
        public string ServiceName { get; set; } = "";
        public bool IsAvailable { get; set; }
        public bool IsStarting { get; set; }
        public string StatusMessage { get; set; } = "";
        public DateTime LastChecked { get; set; }
        public ServiceType Type { get; set; }
    }

    /// <summary>
    /// Generic service monitoring service that can monitor multiple services simultaneously.
    /// Configurable for different service types (AnythingLLM, SAP, etc.) with appropriate notifications.
    /// </summary>
    public class GenericServiceMonitor : IDisposable
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly Dictionary<string, ServiceMonitorConfig> _services = new();
        private readonly Dictionary<string, DispatcherTimer> _timers = new();
        private readonly Dictionary<string, bool> _lastKnownStatus = new();
        private readonly Dictionary<string, bool> _checkingInProgress = new();
        private readonly object _lockObject = new();
        private bool _disposed;

        /// <summary>
        /// Add a service to monitor
        /// </summary>
        public void AddService(ServiceMonitorConfig config)
        {
            if (_disposed) return;
            
            var serviceName = config.Name;
            _services[serviceName] = config;
            _lastKnownStatus[serviceName] = false;
            _checkingInProgress[serviceName] = false;

            // Create timer for this service
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = config.CheckInterval
            };
            
            timer.Tick += async (_, __) => await CheckServiceAsync(serviceName).ConfigureAwait(false);
            _timers[serviceName] = timer;
        }

        /// <summary>
        /// Start monitoring all configured services
        /// </summary>
        public void StartAll()
        {
            if (_disposed) return;

            foreach (var kvp in _services)
            {
                StartService(kvp.Key);
            }
        }

        /// <summary>
        /// Start monitoring a specific service
        /// </summary>
        public void StartService(string serviceName)
        {
            if (_disposed || !_services.ContainsKey(serviceName)) return;

            // Immediate check
            _ = CheckServiceAsync(serviceName);

            // Start periodic monitoring
            try
            {
                var timer = _timers[serviceName];
                if (!timer.IsEnabled)
                    timer.Start();
            }
            catch
            {
                // Ignore timer start errors
            }
        }

        /// <summary>
        /// Stop monitoring a specific service
        /// </summary>
        public void StopService(string serviceName)
        {
            if (_timers.ContainsKey(serviceName))
            {
                try { _timers[serviceName].Stop(); } catch { }
            }
        }

        /// <summary>
        /// Stop monitoring all services
        /// </summary>
        public void StopAll()
        {
            foreach (var timer in _timers.Values)
            {
                try { timer.Stop(); } catch { }
            }
        }

        /// <summary>
        /// Check a specific service status immediately
        /// </summary>
        public async Task CheckServiceAsync(string serviceName)
        {
            if (_disposed || !_services.ContainsKey(serviceName)) return;

            // Prevent concurrent checks for the same service
            lock (_lockObject)
            {
                if (_checkingInProgress[serviceName]) return;
                _checkingInProgress[serviceName] = true;
            }

            try
            {
                var config = _services[serviceName];
                var status = new ServiceStatus
                {
                    ServiceName = serviceName,
                    Type = config.Type,
                    LastChecked = DateTime.Now
                };

                try
                {
                    using var response = await _httpClient.GetAsync(config.Endpoint).ConfigureAwait(false);
                    
                    // For AnythingLLM, auth errors (401, 403) indicate service is running but needs auth
                    if (config.Type == ServiceType.AnythingLLM)
                    {
                        status.IsAvailable = response.IsSuccessStatusCode || 
                                           response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                           response.StatusCode == System.Net.HttpStatusCode.Forbidden;
                    }
                    else
                    {
                        status.IsAvailable = response.IsSuccessStatusCode;
                    }
                    
                    status.IsStarting = false;
                    status.StatusMessage = status.IsAvailable 
                        ? $"{serviceName} service is available" 
                        : $"{serviceName} returned {response.StatusCode}";
                }
                catch (HttpRequestException ex)
                {
                    status.IsAvailable = false;
                    status.IsStarting = false;
                    status.StatusMessage = $"{serviceName} service unavailable: {ex.Message}";
                }
                catch (TaskCanceledException)
                {
                    status.IsAvailable = false;
                    status.IsStarting = true;
                    status.StatusMessage = $"{serviceName} service timeout (possibly starting)";
                }
                catch (Exception ex)
                {
                    status.IsAvailable = false;
                    status.IsStarting = false;
                    status.StatusMessage = $"{serviceName} check failed: {ex.Message}";
                }

                // Only notify if status changed
                if (status.IsAvailable != _lastKnownStatus[serviceName])
                {
                    _lastKnownStatus[serviceName] = status.IsAvailable;
                    
                    // Notify via appropriate channel based on service type
                    NotifyStatusChange(status);
                    
                    // Also call custom callback if provided
                    config.OnStatusChanged?.Invoke(status);

                    TestCaseEditorApp.Services.Logging.Log.Info(
                        $"[ServiceMonitor] {serviceName} status changed - Available: {status.IsAvailable}, Message: {status.StatusMessage}");
                }
            }
            finally
            {
                lock (_lockObject)
                {
                    _checkingInProgress[serviceName] = false;
                }
            }
        }

        private void NotifyStatusChange(ServiceStatus status)
        {
            switch (status.Type)
            {
                case ServiceType.AnythingLLM:
                    // Notify via AnythingLLM mediator
                    TestCaseEditorApp.Services.Logging.Log.Info($"[ServiceMonitor] MEDIATOR DEBUG: NotifyStatusChange for AnythingLLM - Available={status.IsAvailable}, Starting={status.IsStarting}, Message={status.StatusMessage}");
                    var anythingLLMStatus = new AnythingLLMStatus
                    {
                        IsAvailable = status.IsAvailable,
                        IsStarting = status.IsStarting,
                        StatusMessage = status.StatusMessage
                    };
                    AnythingLLMMediator.NotifyStatusUpdated(anythingLLMStatus);
                    break;
                    
                case ServiceType.SAP:
                    // TODO: Add SAP status notification when SAP mediator is available
                    TestCaseEditorApp.Services.Logging.Log.Info($"[ServiceMonitor] SAP status: {status.StatusMessage}");
                    break;
                    
                case ServiceType.Generic:
                    // Generic logging only
                    TestCaseEditorApp.Services.Logging.Log.Info($"[ServiceMonitor] {status.ServiceName} status: {status.StatusMessage}");
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopAll();
            
            foreach (var timer in _timers.Values)
            {
                timer?.Stop();
            }
            
            _timers.Clear();
            _services.Clear();
            _lastKnownStatus.Clear();
            _checkingInProgress.Clear();
        }
    }
}