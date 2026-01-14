using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the main workspace content area.
    /// Implements idempotent view updates - only changes when actually needed.
    /// </summary>
    public partial class WorkspaceContentViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? currentContent;

        [ObservableProperty]
        private string currentContentType = "None";
        
        private ViewConfiguration? _currentConfiguration;

        /// <summary>
        /// Apply view configuration with idempotency check
        /// </summary>
        public bool ApplyViewConfiguration(ViewConfiguration configuration)
        {
            if (configuration.IsEquivalentTo(_currentConfiguration))
            {
                System.Diagnostics.Debug.WriteLine($"[WorkspaceContentViewModel] Already showing content for {configuration.SectionName} - skipping update");
                return false; // No change needed
            }

            System.Diagnostics.Debug.WriteLine($"[WorkspaceContentViewModel] Switching content from {_currentConfiguration?.SectionName ?? "none"} to {configuration.SectionName}");
            
            CurrentContent = configuration.ContentViewModel;
            CurrentContentType = configuration.SectionName;
            _currentConfiguration = configuration;
            
            return true; // Content was changed
        }

        /// <summary>
        /// Shows project-related content (legacy method for compatibility)
        /// </summary>
        public void ShowProjectContent(object projectContent)
        {
            CurrentContent = projectContent;
            CurrentContentType = "Project";
        }

        /// <summary>
        /// Shows requirements list content (legacy method for compatibility)
        /// </summary>
        public void ShowRequirementsContent(object requirementsContent)
        {
            CurrentContent = requirementsContent;
            CurrentContentType = "Requirements";
        }

        /// <summary>
        /// Shows test case generator content (legacy method for compatibility)
        /// </summary>
        public void ShowTestCaseGeneratorContent(object testCaseContent)
        {
            CurrentContent = testCaseContent;
            CurrentContentType = "TestCaseGenerator";
        }

        /// <summary>
        /// Shows workflow content (Import, New Project, etc.) (legacy method for compatibility)
        /// </summary>
        public void ShowWorkflowContent(object workflowContent, string workflowType)
        {
            CurrentContent = workflowContent;
            CurrentContentType = workflowType;
        }

        /// <summary>
        /// Clears the current content
        /// </summary>
        public void ClearContent()
        {
            CurrentContent = null;
            CurrentContentType = "None";
            _currentConfiguration = null;
        }
    }
}