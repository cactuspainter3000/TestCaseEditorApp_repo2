using System;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.ViewModels;

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
        InitialStateViewModel CreateInitialStateViewModel();
        
        // Navigation ViewModels
        TestCaseGenerator_NavigationVM CreateRequirementsNavigationViewModel();
        
        // Legacy ViewModels (for backwards compatibility)
        WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel();
        NavigationViewModel CreateNavigationViewModel();
        ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel();
        NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel();
        // REMOVED: TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel - now handled by mediator

        NotificationAreaViewModel CreateNotificationAreaViewModel();
        DefaultNotificationViewModel CreateDefaultNotificationViewModel();
        
        // Domain ViewModels (proper DI approach)
        // WorkspaceManagementViewModel removed - use WorkspaceProjectViewModel instead
        ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel();

    }
}