using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Domain-specific notification ViewModel for Test Case Generator
    /// Shows AnythingLLM status and requirements progress when Test Case Generator is active
    /// </summary>
    public partial class TestCaseGeneratorNotificationViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<TestCaseGeneratorNotificationViewModel>? _logger;

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

        public TestCaseGeneratorNotificationViewModel(ILogger<TestCaseGeneratorNotificationViewModel>? logger = null)
        {
            _logger = logger;
            _logger?.LogInformation("TestCaseGeneratorNotificationViewModel initialized");
            
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
        /// Reset all status indicators (e.g., when project is closed)
        /// </summary>
        public void ResetStatus()
        {
            UpdateAnythingLlmStatus(false);
            UpdateRequirementsProgress(0, 0);
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
        /// Cleanup subscriptions when the ViewModel is disposed
        /// </summary>
        public void Dispose()
        {
            AnythingLLMMediator.StatusUpdated -= OnAnythingLlmStatusUpdated;
        }
    }
}