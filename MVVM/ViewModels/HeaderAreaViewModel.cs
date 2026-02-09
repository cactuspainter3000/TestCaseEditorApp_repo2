using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the header area that displays different headers based on current context.
    /// Implements idempotent view updates - only changes when actually needed.
    /// </summary>
    public partial class HeaderAreaViewModel : ObservableObject
    {
        [ObservableProperty]
        private object? activeHeader;

        [ObservableProperty]
        private string currentContext = "Workspace";
        
        private ViewConfiguration? _currentConfiguration;

        /// <summary>
        /// Apply view configuration with idempotency check
        /// </summary>
        public bool ApplyViewConfiguration(ViewConfiguration configuration)
        {
            if (configuration.IsEquivalentTo(_currentConfiguration))
            {
                System.Diagnostics.Debug.WriteLine($"[HeaderAreaViewModel] Already showing header for {configuration.SectionName} - skipping update");
                return false; // No change needed
            }

            System.Diagnostics.Debug.WriteLine($"[HeaderAreaViewModel] Switching header from {_currentConfiguration?.SectionName ?? "none"} to {configuration.SectionName}");
            
            ActiveHeader = configuration.HeaderViewModel;
            CurrentContext = configuration.SectionName;
            _currentConfiguration = configuration;
            
            return true; // Header was changed
        }

        /// <summary>
        /// Sets the workspace header as active (legacy method for compatibility)
        /// </summary>
        public void ShowWorkspaceHeader(WorkspaceHeaderViewModel workspaceHeader)
        {
            ActiveHeader = workspaceHeader;
            CurrentContext = "Workspace";
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
            _currentConfiguration = null;
        }
    }
}