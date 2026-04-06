using System;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Notification types for domain UI coordination
    /// </summary>
    public enum DomainNotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Global UI coordinator that provides domain-aware progress and notification management.
    /// Handles conflicts between domains and ensures clear user feedback with domain context.
    /// </summary>
    public interface IDomainUICoordinator
    {
        /// <summary>
        /// Show progress with domain context. Only one domain can show progress at a time.
        /// </summary>
        /// <param name="domainName">Domain name (e.g., "Test Case Generator", "Test Flow Generator")</param>
        /// <param name="message">Progress message (e.g., "Importing requirements...")</param>
        /// <param name="percentage">Progress percentage (0-100)</param>
        /// <returns>True if progress was shown, false if another domain is already showing progress</returns>
        bool ShowDomainProgress(string domainName, string message, double percentage);

        /// <summary>
        /// Update existing domain progress. Only works if this domain is currently showing progress.
        /// </summary>
        /// <param name="domainName">Domain name that owns the current progress</param>
        /// <param name="message">Updated message</param>
        /// <param name="percentage">Updated percentage (0-100)</param>
        /// <returns>True if updated, false if this domain doesn't own current progress</returns>
        bool UpdateDomainProgress(string domainName, string message, double percentage);

        /// <summary>
        /// Hide progress for a specific domain. Only works if this domain owns current progress.
        /// </summary>
        /// <param name="domainName">Domain name that owns the progress</param>
        /// <returns>True if hidden, false if this domain doesn't own current progress</returns>
        bool HideDomainProgress(string domainName);

        /// <summary>
        /// Show notification with domain context. Notifications are queued and don't conflict.
        /// </summary>
        /// <param name="domainName">Domain name for context</param>
        /// <param name="message">Notification message</param>
        /// <param name="type">Type of notification</param>
        /// <param name="durationSeconds">Duration in seconds (0 = manual dismiss)</param>
        void ShowDomainNotification(string domainName, string message, DomainNotificationType type, int durationSeconds = 4);

        /// <summary>
        /// Current domain that owns progress display, if any
        /// </summary>
        string? CurrentProgressDomain { get; }

        /// <summary>
        /// Current progress message being displayed
        /// </summary>
        string? CurrentProgressMessage { get; }

        /// <summary>
        /// Current progress percentage (0-100)
        /// </summary>
        double CurrentProgressPercentage { get; }

        /// <summary>
        /// Whether any domain is currently showing progress
        /// </summary>
        bool IsProgressActive { get; }

        /// <summary>
        /// Event fired when domain progress changes
        /// </summary>
        event EventHandler<DomainProgressChangedEventArgs>? DomainProgressChanged;

        /// <summary>
        /// Event fired when domain notification is shown
        /// </summary>
        event EventHandler<DomainNotificationEventArgs>? DomainNotificationShown;
    }

    /// <summary>
    /// Event args for domain progress changes
    /// </summary>
    public class DomainProgressChangedEventArgs : EventArgs
    {
        public string? DomainName { get; init; }
        public string? Message { get; init; }
        public double Percentage { get; init; }
        public bool IsActive { get; init; }
    }

    /// <summary>
    /// Event args for domain notifications
    /// </summary>
    public class DomainNotificationEventArgs : EventArgs
    {
        public string DomainName { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public DomainNotificationType Type { get; init; }
        public int DurationSeconds { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
    }
}