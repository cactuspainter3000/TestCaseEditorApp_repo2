using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Startup.ViewModels;

using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Default implementation of ViewModel factory.
    /// Creates ViewModels with proper service dependencies.
    /// </summary>
    public class ViewModelFactory : IViewModelFactory
    {
        private readonly IApplicationServices _applicationServices;
        private readonly INewProjectMediator _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;

        public ViewModelFactory(IApplicationServices applicationServices, INewProjectMediator newProjectMediator, 
            ITestCaseGenerationMediator testCaseGenerationMediator)
        {
            _applicationServices = applicationServices ?? throw new ArgumentNullException(nameof(applicationServices));
            _workspaceManagementMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
        }

        public INavigationMediator CreateNavigationMediator()
        {
            // Use singleton NavigationMediator from DI container to ensure all components share the same instance
            return App.ServiceProvider?.GetRequiredService<INavigationMediator>()
                ?? throw new InvalidOperationException("NavigationMediator not found in DI container");
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
            // Get mediators from DI container since we removed them from this factory
            var testCaseCreationMediator = App.ServiceProvider?.GetRequiredService<ITestCaseCreationMediator>()
                ?? throw new InvalidOperationException("TestCaseCreationMediator not found in DI container");
            var openProjectMediator = App.ServiceProvider?.GetRequiredService<TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.IOpenProjectMediator>()
                ?? throw new InvalidOperationException("OpenProjectMediator not found in DI container");
            return new ViewConfigurationService(_workspaceManagementMediator!, openProjectMediator, _testCaseGenerationMediator!, testCaseCreationMediator);
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

        public TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel CreateNavigationViewModel()
        {
            if (_testCaseGenerationMediator == null)
                throw new InvalidOperationException("TestCaseGenerationMediator is required for NavigationViewModel");
                
            var logger = _applicationServices.LoggerFactory?.CreateLogger<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
            return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel(_testCaseGenerationMediator, logger!);
        }

        public ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel()
        {
            var workflow = new ImportRequirementsWorkflowViewModel();
            return workflow;
        }

        // Singleton for in-progress workflows to maintain form data during navigation
        public NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel()
        {
            // Create new workflow instance with proper dependency injection via factory
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
        /// REMOVED: Factory method - use DI container directly per AI Guide compliance
        /// </summary>
        public NewProjectWorkflowViewModel CreateFreshNewProjectWorkflow()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[NewProject] Creating fresh workflow instance via DI");
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
        
        public StartUp_MainViewModel CreateInitialStateViewModel()
        {
            return App.ServiceProvider?.GetService<StartUp_MainViewModel>() ??
                   throw new InvalidOperationException("StartUp_MainViewModel not registered in DI container");
        }


        public object CreateProjectViewModel()
        {
            // Redirect to new Project domain - get Project_MainViewModel from DI container
            var projectMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Project.ViewModels.Project_MainViewModel>();
            if (projectMainVM != null)
            {
                return projectMainVM;
            }
            
            throw new InvalidOperationException("Project_MainViewModel not found in DI container. Ensure Project domain is properly registered.");
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
                null!, // Navigation will be handled differently
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
        
        // WorkspaceManagementViewModel removed - use WorkspaceProjectViewModel instead
        
        public ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel()
        {
            // TODO: Implement with proper service resolution from IApplicationServices  
            throw new NotImplementedException("ChatGptExportAnalysisViewModel creation needs proper DI container setup");
        }
        
        // REMOVED: RequirementAnalysisViewModel now uses DI



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

        public TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel CreateNotificationWorkspaceViewModel()
        {
            return _applicationServices.ServiceProvider?.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>() 
                ?? throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
        }
    }
}
