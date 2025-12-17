using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the header area that displays different headers based on current context.
    /// Manages which header is currently active and provides a slot for header content.
    /// </summary>
    public partial class HeaderAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? activeHeader;

        [ObservableProperty]
        private string currentContext = "Workspace";

        /// <summary>
        /// Sets the workspace header as active
        /// </summary>
        public void ShowWorkspaceHeader(WorkspaceHeaderViewModel workspaceHeader)
        {
            ActiveHeader = workspaceHeader;
            CurrentContext = "Workspace";
        }

        /// <summary>
        /// Sets the test case generator header as active
        /// </summary>
        public void ShowTestCaseGeneratorHeader(TestCaseGenerator_HeaderVM testCaseHeader)
        {
            ActiveHeader = testCaseHeader;
            CurrentContext = "TestCaseGenerator";
        }

        /// <summary>
        /// Sets a custom header as active
        /// </summary>
        public void ShowCustomHeader(object headerViewModel, string context)
        {
            ActiveHeader = headerViewModel;
            CurrentContext = context;
        }

        /// <summary>
        /// Clears the current header
        /// </summary>
        public void ClearHeader()
        {
            ActiveHeader = null;
            CurrentContext = "None";
        }
    }
}