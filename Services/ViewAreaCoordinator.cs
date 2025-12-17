using System;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Default implementation of view area coordination.
    /// Manages the interaction between side menu, header, and workspace content areas.
    /// </summary>
    public class ViewAreaCoordinator : IViewAreaCoordinator
    {
        private readonly IViewModelFactory _viewModelFactory;
        private string _currentSection = "Project";

        public SideMenuViewModel SideMenu { get; }
        public HeaderAreaViewModel HeaderArea { get; }
        public WorkspaceContentViewModel WorkspaceContent { get; }
        
        public string CurrentSection => _currentSection;
        public event Action<string>? SectionChanged;

        public ViewAreaCoordinator(IViewModelFactory viewModelFactory)
        {
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            
            // Initialize UI area view models
            SideMenu = new SideMenuViewModel();
            HeaderArea = new HeaderAreaViewModel();
            WorkspaceContent = new WorkspaceContentViewModel();

            // Wire up side menu selection changes
            SideMenu.SectionChanged += OnSideMenuSelectionChanged;
        }

        public void NavigateToProject()
        {
            ChangeSection("Project");
            // TODO: Set appropriate header and content for Project
        }

        public void NavigateToRequirements()
        {
            ChangeSection("Requirements");
            // TODO: Set appropriate header and content for Requirements
        }

        public void NavigateToTestCaseGenerator()
        {
            ChangeSection("TestCases");
            // TODO: Set test case generator header and content
        }

        public void NavigateToTestFlow()
        {
            ChangeSection("TestFlow");
            // TODO: Set test flow header and content
        }

        public void NavigateToImport()
        {
            ChangeSection("Import");
            // TODO: Set import workflow header and content
        }

        public void NavigateToNewProject()
        {
            ChangeSection("NewProject");
            // TODO: Set new project workflow header and content
        }

        private void OnSideMenuSelectionChanged(string? selectedSection)
        {
            if (!string.IsNullOrEmpty(selectedSection))
            {
                ChangeSection(selectedSection);
            }
        }

        private void ChangeSection(string newSection)
        {
            if (_currentSection != newSection)
            {
                _currentSection = newSection;
                SideMenu.SelectedSection = newSection;
                SectionChanged?.Invoke(newSection);
                
                // Log section change for debugging
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Section changed to: {newSection}");
            }
        }
    }
}