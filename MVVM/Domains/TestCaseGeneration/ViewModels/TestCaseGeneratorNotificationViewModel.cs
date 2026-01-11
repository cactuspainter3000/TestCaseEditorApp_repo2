using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Domain-specific notification ViewModel for Test Case Generator
    /// Shows AnythingLLM status and requirements progress when Test Case Generator is active
    /// </summary>
    public partial class TestCaseGeneratorNotificationViewModel : BaseDomainViewModel, IDisposable
    {

        // === STATUS INDICATORS ===
        
        /// <summary>
        /// AnythingLLM connection status indicator
        /// </summary>
        [ObservableProperty]
        private bool isAnythingLlmConnected = false;

        /// <summary>
        /// Display text for AnythingLLM status
        /// </summary>
        [ObservableProperty]
        private string anythingLlmStatusText = "LLM: Disconnected";

        /// <summary>
        /// Color indicator for AnythingLLM status
        /// </summary>
        [ObservableProperty]
        private string anythingLlmStatusColor = "#FF6B6B"; // Red for disconnected

        /// <summary>
        /// Total number of requirements in current project
        /// </summary>
        [ObservableProperty]
        private int totalRequirements = 0;

        /// <summary>
        /// Number of requirements that have test cases generated
        /// </summary>
        [ObservableProperty]
        private int requirementsWithTestCases = 0;

        /// <summary>
        /// Display text for requirements progress
        /// </summary>
        [ObservableProperty]
        private string requirementsProgressText = "Requirements w/Test Cases: 0/0";

        /// <summary>
        /// Progress percentage for requirements (0-100)
        /// </summary>
        [ObservableProperty]
        private double requirementsProgress = 0.0;

        /// <summary>
        /// Verification method of the currently selected requirement
        /// </summary>
        [ObservableProperty]
        private string currentRequirementVerificationMethod = "No requirement selected";

        public TestCaseGeneratorNotificationViewModel(
            ITestCaseGenerationMediator mediator,
            ILogger<TestCaseGeneratorNotificationViewModel> logger)
            : base(mediator, logger)
        {
            _logger?.LogInformation("TestCaseGeneratorNotificationViewModel initialized");
            
            // Subscribe to requirement selection changes
            if (mediator != null)
            {
                mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
                _logger?.LogInformation("TestCaseGeneratorNotificationViewModel subscribed to requirement selection events");
            }
            
            // Subscribe to AnythingLLM status updates
            AnythingLLMMediator.StatusUpdated += OnAnythingLlmStatusUpdated;
            
            // Request current status in case we're subscribing after initial startup
            AnythingLLMMediator.RequestCurrentStatus();
        }

        // === STATUS UPDATES ===

        /// <summary>
        /// Update AnythingLLM connection status
        /// </summary>
        public void UpdateAnythingLlmStatus(bool isConnected, string? customText = null)
        {
            IsAnythingLlmConnected = isConnected;
            
            if (isConnected)
            {
                AnythingLlmStatusText = customText ?? "LLM: AnythingLLM";
                AnythingLlmStatusColor = "#51CF66"; // Green for connected
            }
            else
            {
                AnythingLlmStatusText = customText ?? "LLM: Disconnected";
                AnythingLlmStatusColor = "#FF6B6B"; // Red for disconnected
            }

            _logger?.LogInformation("AnythingLLM status updated: Connected={Connected}, Text={StatusText}", isConnected, AnythingLlmStatusText);
        }

        /// <summary>
        /// Update requirements progress information
        /// </summary>
        public void UpdateRequirementsProgress(int total, int withTestCases)
        {
            TotalRequirements = total;
            RequirementsWithTestCases = withTestCases;
            RequirementsProgressText = $"Requirements w/Test Cases: {withTestCases}/{total}";
            RequirementsProgress = total > 0 ? (double)withTestCases / total * 100 : 0.0;

            _logger?.LogInformation("Requirements progress updated: {WithTestCases}/{Total} ({Progress:F1}%)", 
                withTestCases, total, RequirementsProgress);
        }

        /// <summary>
        /// Update the verification method of the currently selected requirement
        /// </summary>
        public void UpdateCurrentRequirementVerificationMethod(string? verificationMethod, string? requirementId = null)
        {
            CurrentRequirementVerificationMethod = string.IsNullOrWhiteSpace(verificationMethod) 
                ? "No verification method" 
                : verificationMethod;

            _logger?.LogInformation("Current requirement verification method updated: {VerificationMethod} for requirement {RequirementId}", 
                CurrentRequirementVerificationMethod, requirementId ?? "unknown");
        }

        /// <summary>
        /// Reset all status indicators (e.g., when project is closed)
        /// </summary>
        public void ResetStatus()
        {
            UpdateAnythingLlmStatus(false);
            UpdateRequirementsProgress(0, 0);
            UpdateCurrentRequirementVerificationMethod(null);
            _logger?.LogInformation("Test case generator notification status reset");
        }

        /// <summary>
        /// Handle AnythingLLM status updates from mediator
        /// </summary>
        private void OnAnythingLlmStatusUpdated(AnythingLLMStatus status)
        {
            _logger?.LogInformation("AnythingLLM status update received: Available={Available}, Starting={Starting}, Message={Message}", 
                status.IsAvailable, status.IsStarting, status.StatusMessage);
                
            if (status.IsStarting)
            {
                // Show connecting status
                UpdateAnythingLlmStatus(false, "LLM: Connecting...");
            }
            else if (status.IsAvailable)
            {
                // Connected successfully
                UpdateAnythingLlmStatus(true, "LLM: AnythingLLM");
            }
            else
            {
                // Disconnected or failed
                UpdateAnythingLlmStatus(false, "LLM: Disconnected");
            }
        }

        /// <summary>
        /// Handle requirement selection changes from mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            if (e.Requirement != null)
            {
                var verificationMethod = !string.IsNullOrWhiteSpace(e.Requirement.VerificationMethodText) 
                    ? e.Requirement.VerificationMethodText 
                    : e.Requirement.Method.ToString();
                    
                UpdateCurrentRequirementVerificationMethod(verificationMethod, e.Requirement.GlobalId);
            }
            else
            {
                UpdateCurrentRequirementVerificationMethod(null);
            }
        }

        // Implementation of abstract methods from BaseDomainViewModel
        protected override Task SaveAsync()
        {
            // Notification ViewModel has no save functionality
            return Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // Clear any notification state if needed
            _logger?.LogDebug("[TestCaseGeneratorNotificationViewModel] Cancel called");
        }

        protected override Task RefreshAsync()
        {
            // Refresh notification state
            _logger?.LogDebug("[TestCaseGeneratorNotificationViewModel] Refresh requested");
            return Task.CompletedTask;
        }

        protected override bool CanSave() => false; // No save functionality for notifications
        protected override bool CanCancel() => false;
        protected override bool CanRefresh() => true;

        /// <summary>
        /// Cleanup subscriptions when the ViewModel is disposed
        /// </summary>
        public override void Dispose()
        {
            AnythingLLMMediator.StatusUpdated -= OnAnythingLlmStatusUpdated;
            
            // Unsubscribe from mediator events
            if (_mediator is ITestCaseGenerationMediator testCaseMediator)
            {
                testCaseMediator.Unsubscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            }
            
            base.Dispose();
        }
    }
}