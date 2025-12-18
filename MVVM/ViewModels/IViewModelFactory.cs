using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Factory interface for creating ViewModels with proper dependency injection.
    /// This abstracts ViewModel creation and reduces coupling in MainViewModel.
    /// </summary>
    public interface IViewModelFactory
    {
        // Navigation Infrastructure
        INavigationMediator CreateNavigationMediator();
        IViewAreaCoordinator CreateViewAreaCoordinator();
        
        // Content ViewModels
        object CreateProjectViewModel();
        object CreateRequirementsViewModel();
        PlaceholderViewModel CreatePlaceholderViewModel();
        
        // Legacy ViewModels (for backwards compatibility)
        WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel();
        NavigationViewModel CreateNavigationViewModel();
        ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel();
        NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel();
        TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerator_Navigator navigator);
    }
}