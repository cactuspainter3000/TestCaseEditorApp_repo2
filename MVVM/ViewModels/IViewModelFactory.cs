using System;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.ViewModels;
using TestCaseEditorApp.MVVM.Domains.ChatGptExportAnalysis.ViewModels;
using TestCaseEditorApp.MVVM.Domains.RequirementAnalysisWorkflow.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.Services;

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
        
        // New Configuration-Based Infrastructure  
        IViewConfigurationService CreateViewConfigurationService();

        
        // Content ViewModels
        object CreateProjectViewModel();
        RequirementsViewModel CreateRequirementsViewModel();
        RequirementsWorkspaceViewModel CreateRequirementsWorkspaceViewModel();
        PlaceholderViewModel CreatePlaceholderViewModel();
        
        // Navigation ViewModels
        TestCaseGenerator_NavigationVM CreateRequirementsNavigationViewModel();
        
        // Legacy ViewModels (for backwards compatibility)
        WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel();
        NavigationViewModel CreateNavigationViewModel();
        ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel();
        NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel();
        TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel(ITestCaseGenerationMediator mediator);
        object CreateTestCaseGeneratorViewModel();
        object CreateTestCaseGeneratorSplashScreenViewModel();
        NotificationAreaViewModel CreateNotificationAreaViewModel();
        DefaultNotificationViewModel CreateDefaultNotificationViewModel();
        TestCaseGeneratorNotificationViewModel CreateTestCaseGeneratorNotificationViewModel();
        
        // Domain ViewModels (proper DI approach)
        WorkspaceManagementVM CreateWorkspaceManagementViewModel();
        ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel();
        RequirementAnalysisViewModel CreateRequirementAnalysisWorkflowViewModel();
        RequirementGenerationViewModel CreateRequirementGenerationViewModel();
    }
}