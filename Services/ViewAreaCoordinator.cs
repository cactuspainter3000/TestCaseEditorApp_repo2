using System;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Events;

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
        public object NotificationArea { get; private set; }
        public INavigationMediator NavigationMediator => _navigationMediator;
        public IWorkspaceManagementMediator WorkspaceManagement => _workspaceManagementMediator;
        
        // UI Area ViewModels
        private readonly SideMenuViewModel _sideMenu;
        private readonly HeaderAreaViewModel _headerArea;
        private readonly WorkspaceContentViewModel _workspaceContent;
        
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
            NotificationArea = _viewModelFactory.CreateDefaultNotificationViewModel(); // Start with default notification

            // Subscribe to navigation events
            SetupNavigationHandlers();
            
            // Set initial header first
            HandleDefaultNavigation(null); // Set initial header
            
            // Then set initial workspace content to welcome screen
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
            _workspaceManagementMediator.Subscribe<WorkspaceManagementEvents.ProjectCreated>(OnProjectCreated);
            _workspaceManagementMediator.Subscribe<WorkspaceManagementEvents.ProjectOpened>(OnProjectOpened);
            
            // Wire up side menu selection to mediator
            SideMenu.SectionChanged += (section) => 
            {
                if (!string.IsNullOrEmpty(section))
                {
                    _navigationMediator.NavigateToSection(section);
                }
            };
        }
        
        private void SetNotificationArea(object notificationViewModel)
        {
            // Dispose previous notification if it's disposable
            if (NotificationArea is IDisposable disposableNotification)
            {
                disposableNotification.Dispose();
            }
            
            NotificationArea = notificationViewModel;
        }
        
        private void OnSectionChangeRequested(NavigationEvents.SectionChangeRequested request)
        {
            // Update side menu selection
            SideMenu.SelectedSection = request.SectionName;
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Section change requested: '{request.SectionName}' (lowercase: '{request.SectionName?.ToLowerInvariant()}')");
            
            // Route to appropriate handler based on section
            switch (request.SectionName?.ToLowerInvariant())
            {
                case "project": 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleProjectNavigation");
                    HandleProjectNavigation(request.Context); break;
                case "requirements": 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleRequirementsNavigation");
                    HandleRequirementsNavigation(request.Context); break;
                case "testcase": 
                case "test case creator": 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleTestCaseGeneratorNavigation");
                    HandleTestCaseGeneratorNavigation(request.Context); break;
                case "testflow": 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleTestFlowNavigation");
                    HandleTestFlowNavigation(request.Context); break;
                case "import": 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleImportNavigation");
                    HandleImportNavigation(request.Context); break;
                case "newproject": 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleNewProjectNavigation");
                    HandleNewProjectNavigation(request.Context); break;
                default: 
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewAreaCoordinator] Routing to HandleDefaultNavigation");
                    HandleDefaultNavigation(request.Context); break;
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
        
        private void OnProjectCreated(WorkspaceManagementEvents.ProjectCreated projectCreated)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Project created: {projectCreated.WorkspaceName}");
        }
        
        private void OnProjectOpened(WorkspaceManagementEvents.ProjectOpened projectOpened)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewAreaCoordinator] Project opened: {projectOpened.WorkspacePath}");
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
            
            // CRITICAL: Set header in HeaderArea FIRST so the UI binding can see it
            HeaderArea.ShowTestCaseGeneratorHeader(_testCaseGeneratorHeader);
            
            // THEN publish HeaderChanged event so UI knows to update
            _navigationMediator.SetActiveHeader(_testCaseGeneratorHeader);
            
            // Set Test Case Generator notification area
            SetNotificationArea(_viewModelFactory.CreateTestCaseGeneratorNotificationViewModel());
            
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
            
            // Set default notification area
            SetNotificationArea(_viewModelFactory.CreateDefaultNotificationViewModel());
            
            // Don't override the initial content - let SetInitialContent handle the main workspace content
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
        
        /// <summary>
        /// Update the current section in breadcrumb and notify subscribers
        /// </summary>
        private void UpdateCurrentSection(string? sectionName)
        {
            var displayName = sectionName switch
            {
                "testcase" or "test case creator" => "Test Case Generator",
                "requirements" => "Requirements", 
                "project" => "Project",
                "testflow" => "Test Flow",
                _ => sectionName
            };
        }
    }
}