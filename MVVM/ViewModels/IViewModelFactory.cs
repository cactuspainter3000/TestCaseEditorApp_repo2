using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.ViewModels;
using TestCaseEditorApp.MVVM.Domains.ChatGptExportAnalysis.ViewModels;
using TestCaseEditorApp.MVVM.Domains.RequirementAnalysisWorkflow.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;

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
        RequirementsViewModel CreateRequirementsViewModel();
        PlaceholderViewModel CreatePlaceholderViewModel();
        
        // Legacy ViewModels (for backwards compatibility)
        WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel();
        NavigationViewModel CreateNavigationViewModel();
        ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel();
        NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel();
        TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerationMediator mediator);
        object CreateTestCaseGeneratorViewModel();
        object CreateTestCaseGeneratorSplashScreenViewModel();
        
        // Domain ViewModels (proper DI approach)
        WorkspaceManagementVM CreateWorkspaceManagementViewModel();
        ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel();
        RequirementAnalysisViewModel CreateRequirementAnalysisWorkflowViewModel();
        RequirementGenerationViewModel CreateRequirementGenerationViewModel();
    }
}