using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
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

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Default implementation of ViewModel factory.
    /// Creates ViewModels with proper service dependencies.
    /// </summary>
    public class ViewModelFactory : IViewModelFactory
    {
        private readonly IApplicationServices _applicationServices;
        private readonly IWorkspaceManagementMediator? _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator? _testCaseGenerationMediator;

        public ViewModelFactory(IApplicationServices applicationServices, IWorkspaceManagementMediator? workspaceManagementMediator = null, 
            ITestCaseGenerationMediator? testCaseGenerationMediator = null)
        {
            _applicationServices = applicationServices ?? throw new ArgumentNullException(nameof(applicationServices));
            _workspaceManagementMediator = workspaceManagementMediator;
            _testCaseGenerationMediator = testCaseGenerationMediator;
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
            return new ViewAreaCoordinator(this, navigationMediator, _workspaceManagementMediator!, _testCaseGenerationMediator!);
        }

        public WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel()
        {
            return new WorkspaceHeaderViewModel();
        }

        public NavigationViewModel CreateNavigationViewModel()
        {
            return new NavigationViewModel();
        }

        public ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel()
        {
            var workflow = new ImportRequirementsWorkflowViewModel();
            return workflow;
        }

        public NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel()
        {
            return new NewProjectWorkflowViewModel(
                _applicationServices.AnythingLLMService, 
                _applicationServices.ToastService,
                _workspaceManagementMediator);
        }

        public TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerationMediator mediator)
        {
            // Use null MainViewModel for now - HeaderVM should be migrated to use mediator
            var headerVM = new TestCaseGenerator_HeaderVM(null) 
            { 
                TitleText = "Test Case Creator" 
            };
            
            // Link header VM to mediator for project status updates
            mediator.SetHeaderViewModel(headerVM);
            
            return headerVM;
        }
        
        public PlaceholderViewModel CreatePlaceholderViewModel()
        {
            return new PlaceholderViewModel("Content coming soon...");
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
            // TODO: Create proper mediator when all required services are available
            throw new NotImplementedException("RequirementsViewModel creation needs proper DI setup with all required services");
        }
        
        // Domain ViewModels - proper DI pattern implementation
        public WorkspaceManagementVM CreateWorkspaceManagementViewModel()
        {
            // Note: This factory method should receive the necessary dependencies
            // For now, returning null to indicate this needs proper DI setup
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("WorkspaceManagementVM creation needs proper DI container setup");
        }
        
        public ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel()
        {
            // Note: This factory method should receive the necessary dependencies
            // For now, returning null to indicate this needs proper DI setup
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("ChatGptExportAnalysisViewModel creation needs proper DI container setup");
        }
        
        public RequirementAnalysisViewModel CreateRequirementAnalysisWorkflowViewModel()
        {
            // Note: This factory method should receive the necessary dependencies
            // For now, returning null to indicate this needs proper DI setup
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("RequirementAnalysisViewModel creation needs proper DI container setup");
        }
        
        public RequirementGenerationViewModel CreateRequirementGenerationViewModel()
        {
            // Note: This factory method should receive the necessary dependencies
            // For now, returning null to indicate this needs proper DI setup
            // TODO: Implement with proper service resolution from IApplicationServices
            throw new NotImplementedException("RequirementGenerationViewModel creation needs proper DI container setup");
        }
        
        public object CreateTestCaseGeneratorViewModel()
        {
            return new TestCaseGeneratorViewModel(
                _applicationServices.AnythingLLMService,
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
    }
}