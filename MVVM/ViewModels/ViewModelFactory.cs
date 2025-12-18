using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Models;
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

        public ViewModelFactory(IApplicationServices applicationServices)
        {
            _applicationServices = applicationServices ?? throw new ArgumentNullException(nameof(applicationServices));
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
            return new ViewAreaCoordinator(this, navigationMediator);
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
                _applicationServices.ToastService);
        }

        public TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerator_Navigator navigator)
        {
            // The TestCaseGenerator_HeaderVM constructor expects MainViewModel, not ITestCaseGenerator_Navigator
            // Cast the navigator back to MainViewModel for now (this will be improved in further refactoring)
            var mainVm = navigator as MainViewModel;
            return new TestCaseGenerator_HeaderVM(mainVm) 
            { 
                TitleText = "Test Case Creator" 
            };
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
                _applicationServices.LoggerFactory?.CreateLogger(typeof(ProjectViewModel).FullName ?? "ProjectViewModel") as ILogger<ProjectViewModel>);
        }
        
        public object CreateRequirementsViewModel()
        {
            return new RequirementsViewModel(
                _applicationServices.RequirementService,
                _applicationServices.FileDialogService,
                _applicationServices.ChatGptExportService,
                _applicationServices.NotificationService,
                CreateNavigationMediator(),
                new System.Collections.ObjectModel.ObservableCollection<Requirement>(), // Shared requirements will be injected later
                _applicationServices.PersistenceService,
                // Legacy navigator support - will be handled by the mediator eventually
                new StubTestCaseGeneratorNavigator(),
                null, // TestCaseGenerator_CoreVM will be set separately
                _applicationServices.LoggerFactory?.CreateLogger(typeof(RequirementsViewModel).FullName ?? "RequirementsViewModel") as ILogger<RequirementsViewModel>);
        }
        
        // Stub navigator for RequirementsViewModel legacy constructor compatibility
        private class StubTestCaseGeneratorNavigator : ITestCaseGenerator_Navigator, INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            public ObservableCollection<Requirement> Requirements { get; } = new ObservableCollection<Requirement>();
            public Requirement? CurrentRequirement { get; set; }
            public ICommand NextRequirementCommand { get; } = new RelayCommand(() => { });
            public ICommand PreviousRequirementCommand { get; } = new RelayCommand(() => { });
            public ICommand NextWithoutTestCaseCommand { get; } = new RelayCommand(() => { });
            public string RequirementPositionDisplay => "0/0";
            public bool WrapOnNextWithoutTestCase { get; set; }
            public bool IsLlmBusy { get; set; }
            public bool IsBatchAnalyzing { get; set; }
            
            public void NavigateToRequirement(Requirement requirement) { }
            public void NavigateToTestCaseGeneration() { }
            public void NavigateBack() { }
            public void ShowRequirementEditor(Requirement requirement) { }
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
    }
}