using System;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Coordinates view areas using mediator pattern to eliminate circular dependencies.
    /// REPLACES the 4 competing navigation systems in MainViewModel.
    /// </summary>
    public class ViewAreaCoordinator : IViewAreaCoordinator
    {
        private readonly IViewModelFactory _viewModelFactory;
        private readonly INavigationMediator _navigationMediator;
        private readonly IWorkspaceManagementMediator _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;

        public SideMenuViewModel SideMenu { get; }
        public HeaderAreaViewModel HeaderArea { get; }
        public WorkspaceContentViewModel WorkspaceContent { get; }
        public INavigationMediator NavigationMediator => _navigationMediator;
        public IWorkspaceManagementMediator WorkspaceManagement => _workspaceManagementMediator;
        
        // ViewModels for reuse
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private object? _projectContent;
        private object? _requirementsContent;

        public ViewAreaCoordinator(IViewModelFactory viewModelFactory, INavigationMediator navigationMediator, 
            IWorkspaceManagementMediator workspaceManagementMediator, ITestCaseGenerationMediator testCaseGenerationMediator,
            TestCaseAnythingLLMService? testCaseAnythingLLMService = null)
        {
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            
            // Initialize UI area view models with proper dependencies
            SideMenu = new SideMenuViewModel(_workspaceManagementMediator, _navigationMediator, _testCaseGenerationMediator, testCaseAnythingLLMService);
            HeaderArea = new HeaderAreaViewModel();
            WorkspaceContent = new WorkspaceContentViewModel();

            // Subscribe to navigation events
            SetupNavigationHandlers();
            
            // Set initial workspace content
            SetInitialContent();
        }
        
        private void SetupNavigationHandlers()
        {
            // Subscribe to navigation mediator events
            _navigationMediator.Subscribe<NavigationEvents.SectionChangeRequested>(OnSectionChangeRequested);
            _navigationMediator.Subscribe<NavigationEvents.StepChangeRequested>(OnStepChangeRequested);
            _navigationMediator.Subscribe<NavigationEvents.ContentChanged>(OnContentChanged);
            
            // Subscribe to workspace management events
            _workspaceManagementMediator.Subscribe<WorkspaceManagementEvents.ProjectClosed>(OnProjectClosed);
            
            // Wire up side menu selection to mediator
            SideMenu.SectionChanged += (section) => 
            {
                if (!string.IsNullOrEmpty(section))
                {
                    _navigationMediator.NavigateToSection(section);
                }
            };
        }
        
        private void OnSectionChangeRequested(NavigationEvents.SectionChangeRequested request)
        {
            // Update side menu selection
            SideMenu.SelectedSection = request.SectionName;
            
            // Route to appropriate handler based on section
            switch (request.SectionName?.ToLowerInvariant())
            {
                case "project": HandleProjectNavigation(request.Context); break;
                case "requirements": HandleRequirementsNavigation(request.Context); break;
                case "testcase": 
                case "test case creator": HandleTestCaseGeneratorNavigation(request.Context); break;
                case "testflow": HandleTestFlowNavigation(request.Context); break;
                case "import": HandleImportNavigation(request.Context); break;
                case "newproject": HandleNewProjectNavigation(request.Context); break;
                default: HandleDefaultNavigation(request.Context); break;
            }
        }
        
        private void OnStepChangeRequested(NavigationEvents.StepChangeRequested request)
        {
            // Handle step navigation within current section
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Step change requested: {request.StepId}");
        }
        
        private void OnContentChanged(NavigationEvents.ContentChanged contentChanged)
        {
            // Update the workspace content when mediator publishes content changes
            WorkspaceContent.CurrentContent = contentChanged.ContentViewModel;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Content updated to: {contentChanged.ContentViewModel?.GetType().Name ?? "<null>"}");
        }
        
        private void OnProjectClosed(WorkspaceManagementEvents.ProjectClosed projectClosed)
        {
            // Clear navigation state when project is closed/unloaded
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Project closed: {projectClosed.WorkspacePath}");
            _navigationMediator.ClearNavigationState();
            
            // Navigate to initial/empty state
            HandleDefaultNavigation(null);
        }
        
        // Public navigation methods (implement IViewAreaCoordinator)
        public void NavigateToProject() => _navigationMediator.NavigateToSection("Project");
        public void NavigateToRequirements() => _navigationMediator.NavigateToSection("Requirements");
        public void NavigateToTestCaseGenerator() => _navigationMediator.NavigateToSection("TestCase");
        public void NavigateToTestFlow() => _navigationMediator.NavigateToSection("TestFlow");
        public void NavigateToImport() => _navigationMediator.NavigateToSection("Import");
        public void NavigateToNewProject() => _navigationMediator.NavigateToSection("NewProject");
        
        // Private navigation handlers
        private void HandleProjectNavigation(object? context)
        {
            EnsureWorkspaceHeader();
            _navigationMediator.SetActiveHeader(_workspaceHeader);
            HeaderArea.ShowWorkspaceHeader(_workspaceHeader);
            
            // Show project content
            if (_projectContent == null)
            {
                _projectContent = _viewModelFactory.CreateProjectViewModel();
            }
            _navigationMediator.SetMainContent(_projectContent);
        }

        private void HandleRequirementsNavigation(object? context)
        {
            EnsureTestCaseGeneratorHeader();
            _navigationMediator.SetActiveHeader(_testCaseGeneratorHeader);
            HeaderArea.ShowTestCaseGeneratorHeader(_testCaseGeneratorHeader);
            
            // Show dedicated requirements workspace view - reuse existing instance to preserve state
            if (_requirementsContent == null)
            {
                _requirementsContent = _viewModelFactory.CreateRequirementsWorkspaceViewModel();
            }
            _navigationMediator.SetMainContent(_requirementsContent);
        }

        private void HandleTestCaseGeneratorNavigation(object? context)
        {
            EnsureTestCaseGeneratorHeader();
            _navigationMediator.SetActiveHeader(_testCaseGeneratorHeader);
            HeaderArea.ShowTestCaseGeneratorHeader(_testCaseGeneratorHeader);
            
            // Show test case generator splash screen
            var testCaseWorkflow = _viewModelFactory.CreateTestCaseGeneratorSplashScreenViewModel();
            _navigationMediator.SetMainContent(testCaseWorkflow);
        }

        private void HandleTestFlowNavigation(object? context)
        {
            EnsureWorkspaceHeader();
            _navigationMediator.SetActiveHeader(_workspaceHeader);
            HeaderArea.ShowWorkspaceHeader(_workspaceHeader);
            
            // Show test flow workflow
            var testFlowWorkflow = _viewModelFactory.CreatePlaceholderViewModel();
            _navigationMediator.SetMainContent(testFlowWorkflow);
        }

        private void HandleImportNavigation(object? context)
        {
            EnsureWorkspaceHeader();
            _navigationMediator.SetActiveHeader(_workspaceHeader);
            HeaderArea.ShowWorkspaceHeader(_workspaceHeader);
            
            // Show import workflow
            var importWorkflow = _viewModelFactory.CreateImportWorkflowViewModel();
            _navigationMediator.SetMainContent(importWorkflow);
        }

        private void HandleNewProjectNavigation(object? context)
        {
            EnsureWorkspaceHeader();
            _navigationMediator.SetActiveHeader(_workspaceHeader);
            HeaderArea.ShowWorkspaceHeader(_workspaceHeader);
            
            // Show new project workflow  
            var newProjectWorkflow = _viewModelFactory.CreateNewProjectWorkflowViewModel();
            _navigationMediator.SetMainContent(newProjectWorkflow);
        }
        
        private void HandleDefaultNavigation(object? context)
        {
            EnsureWorkspaceHeader();
            _navigationMediator.SetActiveHeader(_workspaceHeader);
            HeaderArea.ShowWorkspaceHeader(_workspaceHeader);
            
            var placeholder = _viewModelFactory.CreatePlaceholderViewModel();
            _navigationMediator.SetMainContent(placeholder);
        }

        private void EnsureWorkspaceHeader()
        {
            if (_workspaceHeader == null)
            {
                _workspaceHeader = _viewModelFactory.CreateWorkspaceHeaderViewModel();
            }
            
            // Update save status from workspace management mediator
            _workspaceHeader.UpdateSaveStatus(_workspaceManagementMediator);
        }

        private void EnsureTestCaseGeneratorHeader()
        {
            if (_testCaseGeneratorHeader == null)
            {
                _testCaseGeneratorHeader = _viewModelFactory.CreateTestCaseGeneratorHeaderViewModel(_testCaseGenerationMediator);
            }
        }
        
        private void SetInitialContent()
        {
            // Use the mediator to publish initial content - views will be subscribed to this
            var initialStateViewModel = new InitialStateViewModel();
            _navigationMediator.SetMainContent(initialStateViewModel);
        }
    }
}