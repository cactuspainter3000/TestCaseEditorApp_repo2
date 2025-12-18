using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Coordinates communication between domain mediators (TestCaseGeneration ↔ TestFlow).
    /// Handles cross-domain requests, responses, and broadcasts while maintaining domain isolation.
    /// </summary>
    public interface IDomainCoordinator
    {
        /// <summary>
        /// Handle a cross-domain action request from any domain mediator
        /// </summary>
        Task<T?> HandleCrossDomainRequestAsync<T>(object request, string requestingDomain) where T : class;

        /// <summary>
        /// Broadcast a notification to all registered domains
        /// </summary>
        Task BroadcastNotificationAsync<T>(T notification, string originatingDomain) where T : class;

        /// <summary>
        /// Register a domain mediator for cross-domain coordination
        /// </summary>
        void RegisterDomainMediator(string domainName, object mediator);

        /// <summary>
        /// Unregister a domain mediator
        /// </summary>
        void UnregisterDomainMediator(string domainName);

        /// <summary>
        /// Check if a specific domain is available for requests
        /// </summary>
        bool IsDomainAvailable(string domainName);

        /// <summary>
        /// Get all registered domain names
        /// </summary>
        string[] GetRegisteredDomains();

        /// <summary>
        /// Event fired when cross-domain communication occurs (for debugging/monitoring)
        /// </summary>
        event EventHandler<CrossDomainCommunicationEventArgs> CrossDomainCommunicationOccurred;
    }

    /// <summary>
    /// Event arguments for cross-domain communication monitoring
    /// </summary>
    public class CrossDomainCommunicationEventArgs : EventArgs
    {
        public string RequestingDomain { get; set; } = string.Empty;
        public string RespondingDomain { get; set; } = string.Empty;
        public string RequestType { get; set; } = string.Empty;
        public string ResponseType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}