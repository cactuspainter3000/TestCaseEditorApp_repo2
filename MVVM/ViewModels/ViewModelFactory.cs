using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.ViewModels;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Domains.ChatGptExportAnalysis.ViewModels;
using TestCaseEditorApp.MVVM.Domains.RequirementAnalysisWorkflow.ViewModels;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Mediators;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Default implementation of ViewModel factory.
    /// Creates ViewModels with proper service dependencies.
    /// </summary>
    public class ViewModelFactory : IViewModelFactory
    {
        private readonly IApplicationServices _applicationServices;
        private readonly IWorkspaceManagementMediator _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;

        public ViewModelFactory(IApplicationServices applicationServices, IWorkspaceManagementMediator workspaceManagementMediator, 
            ITestCaseGenerationMediator testCaseGenerationMediator)
        {
            _applicationServices = applicationServices ?? throw new ArgumentNullException(nameof(applicationServices));
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
        }

        public INavigationMediator CreateNavigationMediator()
        {
            var loggerFactory = _applicationServices.LoggerFactory;
            var logger = loggerFactory?.CreateLogger(typeof(NavigationMediator).FullName ?? "NavigationMediator");
            return new NavigationMediator(logger);
        }

        public IViewAreaCoordinator CreateViewAreaCoordinator()
        {
            var navigationMediator = CreateNavigationMediator();
            // Don't resolve ViewConfigurationService here to avoid circular dependency
            // It will be resolved later when actually needed
            var sideMenuViewModel = App.ServiceProvider?.GetService<SideMenuViewModel>() 
                ?? throw new InvalidOperationException("SideMenuViewModel not registered in DI container");
            
            return new ViewAreaCoordinator(this, navigationMediator, _workspaceManagementMediator!, _testCaseGenerationMediator!,
                null, sideMenuViewModel);
        }
        
        public IViewConfigurationService CreateViewConfigurationService()
        {
            return new ViewConfigurationService(this, _workspaceManagementMediator!, _testCaseGenerationMediator!);
        }
        public WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel()
        {
            var headerViewModel = new WorkspaceHeaderViewModel();
            
            // Wire up save commands (proper domain location per architecture)
            if (_workspaceManagementMediator != null)
            {
                headerViewModel.SaveWorkspaceCommand = new AsyncRelayCommand(
                    async () => 
                    {
                        await _workspaceManagementMediator.SaveProjectAsync();
                        headerViewModel.UpdateSaveStatus(_workspaceManagementMediator);
                    });
                headerViewModel.UndoLastSaveCommand = new AsyncRelayCommand(
                    async () => 
                    {
                        await _workspaceManagementMediator.UndoLastSaveAsync();
                        headerViewModel.UpdateSaveStatus(_workspaceManagementMediator);
                    }, 
                    () => _workspaceManagementMediator.CanUndoLastSave());
                
                // Initial state update
                headerViewModel.UpdateSaveStatus(_workspaceManagementMediator);
            }
            
            return headerViewModel;
        }

        public NavigationViewModel CreateNavigationViewModel()
        {
            if (_testCaseGenerationMediator == null)
                throw new InvalidOperationException("TestCaseGenerationMediator is required for NavigationViewModel");
                
            var logger = _applicationServices.LoggerFactory?.CreateLogger<NavigationViewModel>();
            return new NavigationViewModel(_testCaseGenerationMediator, logger!);
        }

        public ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel()
        {
            var workflow = new ImportRequirementsWorkflowViewModel();
            return workflow;
        }

        // Singleton for in-progress workflows to maintain form data during navigation
        public NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel()
        {
            // Create new workflow instance with proper dependency injection
            TestCaseEditorApp.Services.Logging.Log.Debug("[NewProject] Creating new workflow instance");
            var logger = _applicationServices.LoggerFactory?.CreateLogger<NewProjectWorkflowViewModel>() 
                ?? throw new InvalidOperationException("Logger is required for NewProjectWorkflowViewModel");
                
            var workflowViewModel = new NewProjectWorkflowViewModel(
                _workspaceManagementMediator,
                logger,
                _applicationServices.AnythingLLMService, 
                _applicationServices.ToastService);
            
            return workflowViewModel;
        }
        
        /// <summary>
        /// Creates a new project workflow. All instances are fresh to maintain architectural compliance.
        /// Form persistence is handled within the ViewModel itself via mediator state.
        /// </summary>
        public NewProjectWorkflowViewModel CreateFreshNewProjectWorkflow()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[NewProject] Creating fresh workflow instance");
            return CreateNewProjectWorkflowViewModel();
        }

        /// <summary>
        /// LEGACY METHOD - REMOVED
        /// HeaderVM is now created directly by TestCaseGenerationMediator via proper DI
        /// ViewModelFactory pattern conflicts with mediator architecture
        /// </summary>
        [Obsolete("HeaderVM creation moved to TestCaseGenerationMediator - use mediator pattern instead of factory")]
        public TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerationMediator mediator)
        {
            throw new InvalidOperationException("HeaderVM creation moved to TestCaseGenerationMediator for proper domain architecture");
        }
        
        public PlaceholderViewModel CreatePlaceholderViewModel()
        {
            return new PlaceholderViewModel("Content coming soon...");
        }
        
        public InitialStateViewModel CreateInitialStateViewModel()
        {
            return new InitialStateViewModel();
        }

        public TestCaseGenerator_NavigationVM CreateRequirementsNavigationViewModel()
        {
            if (_testCaseGenerationMediator == null)
                throw new InvalidOperationException("TestCaseGenerationMediator is required for navigation ViewModel");
            
            return new TestCaseGenerator_NavigationVM(
                _testCaseGenerationMediator,
                _applicationServices.LoggerFactory?.CreateLogger<TestCaseGenerator_NavigationVM>());
        }
        
        public object CreateProjectViewModel()
        {
            return new ProjectViewModel(
                _applicationServices.PersistenceService,
                _applicationServices.FileDialogService,
                _applicationServices.NotificationService,
                CreateNavigationMediator(),
                new System.Collections.ObjectModel.ObservableCollection<Requirement>(), // Shared requirements will be injected later
                _applicationServices.AnythingLLMService,
                _workspaceManagementMediator ?? throw new InvalidOperationException("WorkspaceManagementMediator is required for ProjectViewModel"),
                _applicationServices.LoggerFactory?.CreateLogger(typeof(ProjectViewModel).FullName ?? "ProjectViewModel") as ILogger<ProjectViewModel>);
        }
        
        public RequirementsViewModel CreateRequirementsViewModel()
        {
            if (_testCaseGenerationMediator == null)
                throw new InvalidOperationException("TestCaseGenerationMediator is required for RequirementsViewModel");

            var logger = _applicationServices.LoggerFactory?.CreateLogger<RequirementsViewModel>() ??
                        throw new InvalidOperationException("Logger is required for RequirementsViewModel");

            return new RequirementsViewModel(
                _testCaseGenerationMediator,
                logger,
                _applicationServices.RequirementService,
                _applicationServices.FileDialogService,
                _applicationServices.ChatGptExportService,
                _applicationServices.NotificationService,
                null, // Navigation will be handled differently
                new System.Collections.ObjectModel.ObservableCollection<Requirement>(), // Will be bound to shared collection
                _applicationServices.PersistenceService,
                _applicationServices.RequirementService as IRequirementDataScrubber ?? throw new InvalidOperationException("RequirementDataScrubber is required"),
                _applicationServices.RequirementAnalysisService);
        }
        
        public RequirementsWorkspaceViewModel CreateRequirementsWorkspaceViewModel()
        {
            if (_testCaseGenerationMediator == null)
                throw new InvalidOperationException("TestCaseGenerationMediator is required for RequirementsWorkspaceViewModel");
            
            // Create the TestCaseGenerator_VM that contains the requirements UI logic
            var testCaseGeneratorVMLogger = _applicationServices.LoggerFactory?.CreateLogger<TestCaseGenerator_VM>();
            var analysisService = _applicationServices.RequirementAnalysisService;
            var testCaseGeneratorVM = new TestCaseGenerator_VM(
                _testCaseGenerationMediator, 
                _applicationServices.PersistenceService, 
                _applicationServices.TextEditingDialogService,
                analysisService,
                testCaseGeneratorVMLogger!);
            
            // Set up the CoreVM for handling tables and paragraphs
            testCaseGeneratorVM.TestCaseGenerator = new TestCaseGenerator_CoreVM();
            
            var logger = _applicationServices.LoggerFactory?.CreateLogger<RequirementsWorkspaceViewModel>();
            return new RequirementsWorkspaceViewModel(_testCaseGenerationMediator, testCaseGeneratorVM, logger!);
        }
        
        // Domain ViewModels - proper DI pattern implementation
        public WorkspaceManagementVM CreateWorkspaceManagementViewModel()
        {
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("WorkspaceManagementVM creation needs proper DI container setup");
        }
        
        public ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel()
        {
            // TODO: Implement with proper service resolution from IApplicationServices  
            throw new NotImplementedException("ChatGptExportAnalysisViewModel creation needs proper DI container setup");
        }
        
        public RequirementAnalysisViewModel CreateRequirementAnalysisWorkflowViewModel()
        {
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("RequirementAnalysisViewModel creation needs proper DI container setup");
        }
        
        public RequirementGenerationViewModel CreateRequirementGenerationViewModel()
        {
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("RequirementGenerationViewModel creation needs proper DI container setup");
        }

        public object CreateTestCaseGeneratorViewModel()
        {
            return new TestCaseGeneratorViewModel(
                _applicationServices.ChatGptExportService,
                _applicationServices.NotificationService,
                CreateNavigationMediator(),
                new System.Collections.ObjectModel.ObservableCollection<Requirement>(), // Shared requirements will be injected later
                _applicationServices.LoggerFactory?.CreateLogger(typeof(TestCaseGeneratorViewModel).FullName ?? "TestCaseGeneratorViewModel") as ILogger<TestCaseGeneratorViewModel>);
        }

        public object CreateTestCaseGeneratorSplashScreenViewModel()
        {
            return new TestCaseGeneratorSplashScreenViewModel();
        }

        public NotificationAreaViewModel CreateNotificationAreaViewModel()
        {
            var loggerFactory = _applicationServices.LoggerFactory;
            var logger = loggerFactory?.CreateLogger<NotificationAreaViewModel>();
            return new NotificationAreaViewModel(logger);
        }

        public DefaultNotificationViewModel CreateDefaultNotificationViewModel()
        {
            var loggerFactory = _applicationServices.LoggerFactory;
            var logger = loggerFactory?.CreateLogger<DefaultNotificationViewModel>();
            return new DefaultNotificationViewModel(logger);
        }

        public TestCaseGeneratorNotificationViewModel CreateTestCaseGeneratorNotificationViewModel()
        {
            var loggerFactory = _applicationServices.LoggerFactory;
            var logger = loggerFactory?.CreateLogger<TestCaseGeneratorNotificationViewModel>();
            return new TestCaseGeneratorNotificationViewModel(logger, _testCaseGenerationMediator);
        }
    }
}