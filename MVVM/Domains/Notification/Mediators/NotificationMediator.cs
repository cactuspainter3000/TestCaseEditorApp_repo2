using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Notification.Events;
using TestCaseEditorApp.MVVM.Domains.Notification.Mediators;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Notification.Mediators
{
    /// <summary>
    /// Mediator for the Notification domain
    /// Coordinates notification display and aggregates status information from all domains
    /// </summary>
    public class NotificationMediator : BaseDomainMediator<NotificationEvents>, INotificationMediator
    {
        public NotificationMediator(
            ILogger<NotificationMediator> logger,
            IDomainUICoordinator uiCoordinator,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Notification", performanceMonitor, eventReplay)
        {
            _logger.LogInformation("NotificationMediator initialized");
        }

        /// <inheritdoc />
        public void UpdateLlmStatus(bool isConnected, string statusText, string? provider = null, string? model = null)
        {
            var eventData = new NotificationEvents.LlmStatusChanged
            {
                IsConnected = isConnected,
                StatusText = statusText,
                Provider = provider,
                Model = model
            };

            PublishEvent(eventData);
            _logger.LogDebug("LLM status updated: Connected={Connected}, Text={StatusText}, Provider={Provider}, Model={Model}", 
                isConnected, statusText, provider, model);
        }

        /// <inheritdoc />
        public void UpdateRequirementsProgress(int total, int analyzed, int withTestCases, string sourceDomain)
        {
            var eventData = new NotificationEvents.RequirementsProgressChanged
            {
                TotalRequirements = total,
                AnalyzedRequirements = analyzed,
                RequirementsWithTestCases = withTestCases,
                SourceDomain = sourceDomain
            };

            PublishEvent(eventData);
            _logger.LogDebug("Requirements progress updated from {SourceDomain}: {Analyzed}/{Total} analyzed, {WithTestCases}/{Total} with test cases", 
                sourceDomain, analyzed, total, withTestCases, total);
        }

        /// <inheritdoc />
        public void UpdateCurrentRequirement(string? requirementId, string? verificationMethod, string? title, string sourceDomain)
        {
            var eventData = new NotificationEvents.CurrentRequirementChanged
            {
                RequirementId = requirementId,
                VerificationMethod = verificationMethod,
                RequirementTitle = title,
                SourceDomain = sourceDomain
            };

            PublishEvent(eventData);
            _logger.LogDebug("Current requirement updated from {SourceDomain}: {RequirementId}, Method={VerificationMethod}", 
                sourceDomain, requirementId, verificationMethod);
        }

        /// <inheritdoc />
        public void UpdateDomainStatus(string domainName, string statusMessage, string statusType = "Info")
        {
            var eventData = new NotificationEvents.DomainStatusChanged
            {
                DomainName = domainName,
                StatusMessage = statusMessage,
                StatusType = statusType
            };

            PublishEvent(eventData);
            _logger.LogDebug("Domain status updated: {DomainName} - {StatusType}: {StatusMessage}", 
                domainName, statusType, statusMessage);
        }

        /// <inheritdoc />
        public void SetNotificationAreaVisibility(bool isVisible, string? customMessage = null, string? requestingDomain = null)
        {
            var eventData = new NotificationEvents.NotificationAreaUpdate
            {
                IsVisible = isVisible,
                CustomMessage = customMessage,
                RequestingDomain = requestingDomain
            };

            PublishEvent(eventData);
            _logger.LogDebug("Notification area visibility updated by {RequestingDomain}: Visible={IsVisible}, Message={CustomMessage}", 
                requestingDomain, isVisible, customMessage);
        }

        /// <inheritdoc />
        public void ResetAllStatus()
        {
            // Publish reset events for each status type
            UpdateLlmStatus(false, "LLM: Disconnected");
            UpdateRequirementsProgress(0, 0, 0, "System");
            UpdateCurrentRequirement(null, null, null, "System");
            SetNotificationAreaVisibility(true, "Ready", "System");

            _logger.LogInformation("All notification status reset");
        }

        /// <inheritdoc />
        public new void Subscribe<T>(Action<T> handler) where T : class
        {
            base.Subscribe(handler);
        }

        /// <inheritdoc />
        public new void PublishEvent<T>(T eventData) where T : class
        {
            base.PublishEvent(eventData);
        }

        /// <summary>
        /// Handle broadcast notifications from other domains
        /// </summary>
        public void HandleBroadcastNotification<T>(T notification) where T : class
        {
            _logger.LogDebug("Received broadcast notification: {NotificationType}", typeof(T).Name);
            
            // Process cross-domain notifications and convert to notification events as needed
            // This allows other domains to trigger notification updates without direct coupling
        }

        /// <summary>
        /// Mark mediator as registered and ready
        /// </summary>
        public new void MarkAsRegistered()
        {
            base.MarkAsRegistered();
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logger.LogDebug("NotificationMediator disposing");
            }
            base.Dispose(disposing);
        }
    }
}