using System;
using TestCaseEditorApp.Services;

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

        public IViewAreaCoordinator CreateViewAreaCoordinator()
        {
            return new ViewAreaCoordinator(this);
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
    }
}