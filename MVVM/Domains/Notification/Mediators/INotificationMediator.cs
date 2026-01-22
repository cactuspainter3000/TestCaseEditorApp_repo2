using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Notification.Events;

namespace TestCaseEditorApp.MVVM.Domains.Notification.Mediators
{
    /// <summary>
    /// Interface for Notification domain mediator
    /// Coordinates notification display and cross-domain status updates
    /// </summary>
    public interface INotificationMediator
    {
        /// <summary>
        /// Update LLM connection status
        /// </summary>
        void UpdateLlmStatus(bool isConnected, string statusText, string? provider = null, string? model = null);

        /// <summary>
        /// Update requirements progress information
        /// </summary>
        void UpdateRequirementsProgress(int total, int analyzed, int withTestCases, string sourceDomain);

        /// <summary>
        /// Update current requirement information
        /// </summary>
        void UpdateCurrentRequirement(string? requirementId, string? verificationMethod, string? title, string sourceDomain);

        /// <summary>
        /// Update domain-specific status
        /// </summary>
        void UpdateDomainStatus(string domainName, string statusMessage, string statusType = "Info");

        /// <summary>
        /// Show/hide notification area
        /// </summary>
        void SetNotificationAreaVisibility(bool isVisible, string? customMessage = null, string? requestingDomain = null);

        /// <summary>
        /// Reset all notification status (e.g., when project is closed)
        /// </summary>
        void ResetAllStatus();

        /// <summary>
        /// Subscribe to notification events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;

        /// <summary>
        /// Publish notification events
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;

        /// <summary>
        /// Handle broadcast notifications from other domains for translation to notification events
        /// </summary>
        void HandleBroadcastNotification<T>(T notification) where T : class;
    }
}