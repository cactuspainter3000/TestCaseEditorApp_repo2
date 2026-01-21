namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Mediator for AnythingLLM status updates across ViewModels.
    /// Provides decoupled communication when AnythingLLM connection state changes.
    /// </summary>
    public static class AnythingLLMMediator
    {
        public delegate void StatusUpdatedEventHandler(AnythingLLMStatus status);
        
        /// <summary>
        /// Fired when AnythingLLM status has been updated.
        /// </summary>
        public static event StatusUpdatedEventHandler? StatusUpdated;
        
        /// <summary>
        /// Stores the last known status for late subscribers
        /// </summary>
        private static AnythingLLMStatus? _lastStatus;

        /// <summary>
        /// Notify all subscribers that AnythingLLM status has changed.
        /// </summary>
        /// <param name="status">The current AnythingLLM status</param>
        public static void NotifyStatusUpdated(AnythingLLMStatus status)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMMediator] MEDIATOR DEBUG: NotifyStatusUpdated called - Available={status.IsAvailable}, Starting={status.IsStarting}, Message={status.StatusMessage}");
            _lastStatus = status; // Store for late subscribers
            StatusUpdated?.Invoke(status);
            
            // Bridge to NotificationMediator for unified notification system
            try
            {
                var notificationMediator = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator)) as TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator;
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMMediator] MEDIATOR DEBUG: NotificationMediator resolved: {(notificationMediator != null ? "SUCCESS" : "FAILED")}");
                if (notificationMediator != null)
                {
                    string statusText;
                    if (status.IsStarting)
                    {
                        statusText = "LLM: Connecting...";
                    }
                    else if (status.IsAvailable)
                    {
                        statusText = "LLM: AnythingLLM";
                    }
                    else
                    {
                        statusText = "LLM: Disconnected";
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMMediator] MEDIATOR DEBUG: Calling UpdateLlmStatus - Connected={status.IsAvailable}, Text={statusText}");
                    notificationMediator.UpdateLlmStatus(status.IsAvailable, statusText, "AnythingLLM", null);
                }
            }
            catch (System.Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLMMediator] Failed to notify NotificationMediator: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Requests the current status to be broadcast. Used when ViewModels are created 
        /// after initial status updates have already occurred.
        /// </summary>
        public static void RequestCurrentStatus()
        {
            if (_lastStatus != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMMediator] Broadcasting last known status via NotifyStatusUpdated: Available={_lastStatus.IsAvailable}, Starting={_lastStatus.IsStarting}, Message={_lastStatus.StatusMessage}");
                NotifyStatusUpdated(_lastStatus); // Use NotifyStatusUpdated to trigger the bridge
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[AnythingLLMMediator] No previous status available - triggering fresh status check");
                // Trigger a fresh status check from the service
                TriggerFreshStatusCheck();
            }
        }

        /// <summary>
        /// Trigger a fresh status check when no cached status is available
        /// </summary>
        private static async void TriggerFreshStatusCheck()
        {
            try
            {
                var anythingLLMService = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.Services.AnythingLLMService)) as TestCaseEditorApp.Services.AnythingLLMService;
                if (anythingLLMService != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[AnythingLLMMediator] Triggering fresh status check from AnythingLLMService");
                    // This will call OnStatusUpdated which calls NotifyStatusUpdated
                    var isAvailable = await anythingLLMService.IsServiceAvailableAsync();
                    string statusMessage = isAvailable ? "AnythingLLM ready" : "AnythingLLM unavailable";
                    
                    var freshStatus = new AnythingLLMStatus
                    {
                        IsAvailable = isAvailable,
                        IsStarting = false,
                        StatusMessage = statusMessage
                    };
                    NotifyStatusUpdated(freshStatus);
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[AnythingLLMMediator] AnythingLLMService not available for fresh status check");
                    // Fallback: Report disconnected status
                    var fallbackStatus = new AnythingLLMStatus
                    {
                        IsAvailable = false,
                        IsStarting = false,
                        StatusMessage = "LLM: Service not available"
                    };
                    NotifyStatusUpdated(fallbackStatus);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLMMediator] Fresh status check failed");
                // Fallback: Report disconnected status
                var errorStatus = new AnythingLLMStatus
                {
                    IsAvailable = false,
                    IsStarting = false,
                    StatusMessage = "LLM: Status check failed"
                };
                NotifyStatusUpdated(errorStatus);
            }
        }
        
        /// <summary>
        /// Gets the last known status without triggering any events or health checks.
        /// Returns null if no status has been recorded yet.
        /// </summary>
        public static AnythingLLMStatus? GetLastKnownStatus()
        {
            return _lastStatus;
        }
    }

    /// <summary>
    /// Represents the current status of AnythingLLM connection and startup state
    /// </summary>
    public class AnythingLLMStatus
    {
        public bool IsAvailable { get; set; }
        public bool IsStarting { get; set; }
        public string StatusMessage { get; set; } = "Initializing AnythingLLM...";
    }
}