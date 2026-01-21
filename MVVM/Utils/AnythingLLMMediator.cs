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
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLMMediator] Broadcasting last known status: Available={_lastStatus.IsAvailable}, Starting={_lastStatus.IsStarting}, Message={_lastStatus.StatusMessage}");
                StatusUpdated?.Invoke(_lastStatus);
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[AnythingLLMMediator] No previous status available to broadcast");
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