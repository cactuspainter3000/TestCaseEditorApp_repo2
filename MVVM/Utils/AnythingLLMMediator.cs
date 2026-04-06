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
            _lastStatus = status; // Store for late subscribers
            StatusUpdated?.Invoke(status);
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