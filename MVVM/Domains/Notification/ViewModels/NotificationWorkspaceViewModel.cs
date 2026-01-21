using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Notification.Mediators;
using TestCaseEditorApp.MVVM.Domains.Notification.Events;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Notification.ViewModels
{
    /// <summary>
    /// Domain-agnostic notification workspace ViewModel
    /// Aggregates status information from all domains and displays unified notification area
    /// Replaces legacy TestCaseGeneratorNotificationViewModel
    /// </summary>
    public partial class NotificationWorkspaceViewModel : BaseDomainViewModel, IDisposable
    {
        private new readonly INotificationMediator _mediator;

        // === LLM STATUS ===
        
        /// <summary>
        /// LLM connection status indicator
        /// </summary>
        [ObservableProperty]
        private bool isLlmConnected = false;

        /// <summary>
        /// Display text for LLM status
        /// </summary>
        [ObservableProperty]
        private string llmStatusText = "LLM: Disconnected";

        /// <summary>
        /// Color indicator for LLM status
        /// </summary>
        [ObservableProperty]
        private string llmStatusColor = "#FF6B6B"; // Red for disconnected

        /// <summary>
        /// LLM provider name (e.g., "OpenAI", "Ollama")
        /// </summary>
        [ObservableProperty]
        private string? llmProvider;

        /// <summary>
        /// LLM model name (e.g., "gpt-4", "phi4-mini")
        /// </summary>
        [ObservableProperty]
        private string? llmModel;

        // === REQUIREMENTS PROGRESS ===

        /// <summary>
        /// Total number of requirements in current project
        /// </summary>
        [ObservableProperty]
        private int totalRequirements = 0;

        /// <summary>
        /// Number of requirements that have been analyzed
        /// </summary>
        [ObservableProperty]
        private int analyzedRequirements = 0;

        /// <summary>
        /// Number of requirements that have test cases generated
        /// </summary>
        [ObservableProperty]
        private int requirementsWithTestCases = 0;

        /// <summary>
        /// Display text for requirements progress
        /// </summary>
        [ObservableProperty]
        private string requirementsProgressText = "0 requirements | 0% analyzed | 0% with test cases";

        // === CURRENT REQUIREMENT ===

        /// <summary>
        /// Verification method of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string currentRequirementVerificationMethod = "No requirement selected";

        /// <summary>
        /// ID of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string? currentRequirementId;

        /// <summary>
        /// Title of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string? currentRequirementTitle;

        // === VISIBILITY & STATUS ===

        /// <summary>
        /// Whether the notification area is visible
        /// </summary>
        [ObservableProperty]
        private bool isVisible = true;

        /// <summary>
        /// Custom status message from requesting domain
        /// </summary>
        [ObservableProperty]
        private string? customStatusMessage;

        /// <summary>
        /// Domain that last updated the notification area
        /// </summary>
        [ObservableProperty]
        private string lastUpdatedByDomain = "System";

        public NotificationWorkspaceViewModel(
            INotificationMediator mediator,
            ILogger<NotificationWorkspaceViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger?.LogInformation("NotificationWorkspaceViewModel initialized");
            TestCaseEditorApp.Services.Logging.Log.Info($"[NotificationWorkspaceVM] MEDIATOR DEBUG: NotificationWorkspaceViewModel created and subscribing to events");
            
            // Subscribe to all notification events
            SubscribeToEvents();
            
            // Initialize with default state
            ResetToDefaults();
        }

        /// <summary>
        /// Subscribe to notification events from the mediator
        /// </summary>
        private void SubscribeToEvents()
        {
            _mediator.Subscribe<NotificationEvents.LlmStatusChanged>(OnLlmStatusChanged);
            _mediator.Subscribe<NotificationEvents.RequirementsProgressChanged>(OnRequirementsProgressChanged);
            _mediator.Subscribe<NotificationEvents.CurrentRequirementChanged>(OnCurrentRequirementChanged);
            _mediator.Subscribe<NotificationEvents.DomainStatusChanged>(OnDomainStatusChanged);
            _mediator.Subscribe<NotificationEvents.NotificationAreaUpdate>(OnNotificationAreaUpdate);
            
            _logger?.LogDebug("Subscribed to all notification events");
        }

        /// <summary>
        /// Handle LLM status changes
        /// </summary>
        private void OnLlmStatusChanged(NotificationEvents.LlmStatusChanged eventData)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[NotificationWorkspaceVM] MEDIATOR DEBUG: OnLlmStatusChanged received - Connected={eventData.IsConnected}, Text={eventData.StatusText}");
            IsLlmConnected = eventData.IsConnected;
            LlmStatusText = eventData.StatusText;
            LlmProvider = eventData.Provider;
            LlmModel = eventData.Model;
            
            // Update status color
            LlmStatusColor = eventData.IsConnected ? "#51CF66" : "#FF6B6B"; // Green/Red

            _logger?.LogDebug("LLM status updated: Connected={Connected}, Text={StatusText}", 
                eventData.IsConnected, eventData.StatusText);
        }

        /// <summary>
        /// Handle requirements progress changes
        /// </summary>
        private void OnRequirementsProgressChanged(NotificationEvents.RequirementsProgressChanged eventData)
        {
            TotalRequirements = eventData.TotalRequirements;
            AnalyzedRequirements = eventData.AnalyzedRequirements;
            RequirementsWithTestCases = eventData.RequirementsWithTestCases;
            LastUpdatedByDomain = eventData.SourceDomain;
            
            // Calculate percentages
            var analyzedPercentage = TotalRequirements > 0 ? (double)AnalyzedRequirements / TotalRequirements * 100 : 0.0;
            var testCasesPercentage = TotalRequirements > 0 ? (double)RequirementsWithTestCases / TotalRequirements * 100 : 0.0;
            
            // Format progress text
            RequirementsProgressText = $"{TotalRequirements} requirements | {analyzedPercentage:F0}% analyzed | {testCasesPercentage:F0}% with test cases";

            _logger?.LogDebug("Requirements progress updated from {SourceDomain}: {Analyzed}/{Total} analyzed, {WithTestCases}/{Total} with test cases", 
                eventData.SourceDomain, eventData.AnalyzedRequirements, eventData.TotalRequirements, 
                eventData.RequirementsWithTestCases, eventData.TotalRequirements);
        }

        /// <summary>
        /// Handle current requirement changes
        /// </summary>
        private void OnCurrentRequirementChanged(NotificationEvents.CurrentRequirementChanged eventData)
        {
            CurrentRequirementId = eventData.RequirementId;
            CurrentRequirementTitle = eventData.RequirementTitle;
            CurrentRequirementVerificationMethod = string.IsNullOrWhiteSpace(eventData.VerificationMethod) 
                ? "No verification method" 
                : eventData.VerificationMethod;
            LastUpdatedByDomain = eventData.SourceDomain;

            _logger?.LogDebug("Current requirement updated from {SourceDomain}: {RequirementId}, Method={VerificationMethod}", 
                eventData.SourceDomain, eventData.RequirementId, eventData.VerificationMethod);
        }

        /// <summary>
        /// Handle domain status changes
        /// </summary>
        private void OnDomainStatusChanged(NotificationEvents.DomainStatusChanged eventData)
        {
            // This can be used for domain-specific status indicators in the future
            LastUpdatedByDomain = eventData.DomainName;
            
            _logger?.LogDebug("Domain status updated: {DomainName} - {StatusType}: {StatusMessage}", 
                eventData.DomainName, eventData.StatusType, eventData.StatusMessage);
        }

        /// <summary>
        /// Handle notification area visibility and content updates
        /// </summary>
        private void OnNotificationAreaUpdate(NotificationEvents.NotificationAreaUpdate eventData)
        {
            IsVisible = eventData.IsVisible;
            CustomStatusMessage = eventData.CustomMessage;
            
            if (!string.IsNullOrWhiteSpace(eventData.RequestingDomain))
            {
                LastUpdatedByDomain = eventData.RequestingDomain;
            }

            _logger?.LogDebug("Notification area updated by {RequestingDomain}: Visible={IsVisible}, Message={CustomMessage}", 
                eventData.RequestingDomain, eventData.IsVisible, eventData.CustomMessage);
        }

        /// <summary>
        /// Reset all status indicators to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            IsLlmConnected = false;
            LlmStatusText = "LLM: Disconnected";
            LlmStatusColor = "#FF6B6B";
            LlmProvider = null;
            LlmModel = null;
            
            TotalRequirements = 0;
            AnalyzedRequirements = 0;
            RequirementsWithTestCases = 0;
            RequirementsProgressText = "0 requirements | 0% analyzed | 0% with test cases";
            
            CurrentRequirementId = null;
            CurrentRequirementTitle = null;
            CurrentRequirementVerificationMethod = "No requirement selected";
            
            IsVisible = true;
            CustomStatusMessage = null;
            LastUpdatedByDomain = "System";

            _logger?.LogInformation("Notification workspace reset to defaults");
        }

        // ===== BASE DOMAIN VIEWMODEL IMPLEMENTATIONS =====
        // Notification ViewModels don't need save/cancel/refresh operations
        
        protected override bool CanSave() => false; // No save operations for notifications
        protected override bool CanCancel() => false; // No cancel operations for notifications
        protected override bool CanRefresh() => true; // Can refresh status
        
        protected override async Task SaveAsync()
        {
            // No save operation needed for notification display
            await Task.CompletedTask;
        }
        
        protected override void Cancel()
        {
            // No cancel operation needed for notification display

        }
        
        protected override async Task RefreshAsync()
        {
            // Refresh by resetting to defaults and requesting current status from all domains
            ResetToDefaults();
            _logger?.LogInformation("Notification workspace refreshed");
            await Task.CompletedTask;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public override void Dispose()
        {
            _logger?.LogDebug("NotificationWorkspaceViewModel disposing");
            base.Dispose();
        }
    }
}