using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Factory interface for creating ViewModels with proper dependency injection.
    /// This abstracts ViewModel creation and reduces coupling in MainViewModel.
    /// </summary>
    public interface IViewModelFactory
    {
        // UI Area ViewModels
        IViewAreaCoordinator CreateViewAreaCoordinator();
        
        // Legacy ViewModels (for backwards compatibility)
        WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel();
        NavigationViewModel CreateNavigationViewModel();
        ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel();
        NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel();
        TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerator_Navigator navigator);
    }
}