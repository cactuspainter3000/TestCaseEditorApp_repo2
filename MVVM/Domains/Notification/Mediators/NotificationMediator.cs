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
            
            // Handle Requirements domain events
            if (notification is TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementSelected reqSelected)
            {
                // Translate to notification event
                var notificationEvent = new NotificationEvents.CurrentRequirementChanged
                {
                    RequirementId = reqSelected.Requirement?.GlobalId ?? "Unknown",
                    RequirementTitle = reqSelected.Requirement?.Name ?? "Unknown",
                    VerificationMethod = reqSelected.Requirement?.Method.ToString(),
                    SourceDomain = "Requirements"
                };
                PublishEvent(notificationEvent);
                _logger.LogDebug("Translated RequirementSelected to CurrentRequirementChanged");
            }
            else if (notification is TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.RequirementsCollectionChanged reqCollectionChanged)
            {
                // Update requirements progress
                UpdateRequirementsProgress(
                    reqCollectionChanged.NewCount,
                    reqCollectionChanged.NewCount, // Assume all are "analyzed" for now
                    0, // Test cases count - would need more detailed tracking
                    "Requirements");
                _logger.LogDebug("Translated RequirementsCollectionChanged to RequirementsProgressChanged");
            }
            else if (notification is TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.WorkflowStateChanged workflowChanged)
            {
                // Translate to domain status change
                var statusEvent = new NotificationEvents.DomainStatusChanged
                {
                    DomainName = "Requirements",
                    StatusMessage = workflowChanged.PropertyName == "IsDirty" && (bool)(workflowChanged.NewValue ?? false) ? "Modified" : "Ready",
                    StatusType = "Info",
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["PropertyName"] = workflowChanged.PropertyName ?? "Unknown",
                        ["NewValue"] = workflowChanged.NewValue ?? "null"
                    }
                };
                PublishEvent(statusEvent);
                _logger.LogDebug("Translated WorkflowStateChanged to DomainStatusChanged");
            }
            // Handle OpenProject domain events - update requirements progress when project is opened
            else if (notification is TestCaseEditorApp.MVVM.Domains.OpenProject.Events.OpenProjectEvents.ProjectOpened openProjectOpened)
            {
                var requirementCount = openProjectOpened.Workspace?.Requirements?.Count ?? 0;
                UpdateRequirementsProgress(
                    requirementCount,
                    0, // No requirements analyzed yet on fresh open
                    0, // No test cases yet on fresh open
                    "OpenProject");
                _logger.LogInformation("Updated requirements progress from OpenProjectEvents.ProjectOpened: {Count} requirements", requirementCount);
            }
            // Handle NewProject domain events - update requirements progress when project is created/opened
            else if (notification is TestCaseEditorApp.MVVM.Domains.NewProject.Events.NewProjectEvents.ProjectOpened newProjectOpened)
            {
                var requirementCount = newProjectOpened.Workspace?.Requirements?.Count ?? 0;
                UpdateRequirementsProgress(
                    requirementCount,
                    0, // No requirements analyzed yet on fresh open
                    0, // No test cases yet on fresh open
                    "NewProject");
                _logger.LogInformation("Updated requirements progress from NewProjectEvents.ProjectOpened: {Count} requirements", requirementCount);
            }
            // Handle NewProject domain events - update requirements progress when project is created
            else if (notification is TestCaseEditorApp.MVVM.Domains.NewProject.Events.NewProjectEvents.ProjectCreated projectCreated)
            {
                var requirementCount = projectCreated.Workspace?.Requirements?.Count ?? 0;
                UpdateRequirementsProgress(
                    requirementCount,
                    0, // No requirements analyzed yet on fresh creation
                    0, // No test cases yet on fresh creation
                    "NewProject");
                _logger.LogInformation("Updated requirements progress from NewProjectEvents.ProjectCreated: {Count} requirements", requirementCount);
            }
        }

        /// <summary>
        /// Mark mediator as registered and ready
        /// </summary>
        public new void MarkAsRegistered()
        {
            base.MarkAsRegistered();
        }

        // ===== NAVIGATION METHODS (Required by BaseDomainMediator) =====
        // Notification domain doesn't need navigation, so these are no-ops
        
        public override bool CanNavigateBack() => false;
        public override bool CanNavigateForward() => false;
        public override void NavigateToInitialStep() { } // No-op - always at "initial" state
        public override void NavigateToFinalStep() { } // No-op - always at "final" state

        /// <summary>
        /// Clean up resources
        /// </summary>
        public override void Dispose()
        {
            _logger.LogDebug("NotificationMediator disposing");
            base.Dispose();
        }
    }
}