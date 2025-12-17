using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the main workspace content area.
    /// Manages which content is currently displayed in the main workspace.
    /// </summary>
    public partial class WorkspaceContentViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? currentContent;

        [ObservableProperty]
        private string currentContentType = "None";

        /// <summary>
        /// Shows project-related content
        /// </summary>
        public void ShowProjectContent(object projectContent)
        {
            CurrentContent = projectContent;
            CurrentContentType = "Project";
        }

        /// <summary>
        /// Shows requirements list content
        /// </summary>
        public void ShowRequirementsContent(object requirementsContent)
        {
            CurrentContent = requirementsContent;
            CurrentContentType = "Requirements";
        }

        /// <summary>
        /// Shows test case generator content
        /// </summary>
        public void ShowTestCaseGeneratorContent(object testCaseContent)
        {
            CurrentContent = testCaseContent;
            CurrentContentType = "TestCaseGenerator";
        }

        /// <summary>
        /// Shows workflow content (Import, New Project, etc.)
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
        }
    }
}