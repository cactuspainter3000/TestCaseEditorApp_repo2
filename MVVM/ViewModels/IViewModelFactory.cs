using System;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Startup.ViewModels;

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
        StartUp_MainViewModel CreateInitialStateViewModel();
        
        // Navigation ViewModels
        
        // Legacy ViewModels (for backwards compatibility)
        WorkspaceHeaderViewModel CreateWorkspaceHeaderViewModel();
        TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel CreateNavigationViewModel();
        ImportRequirementsWorkflowViewModel CreateImportWorkflowViewModel();
        // REMOVED: TestCaseGenerator_HeaderVM CreateTestCaseGeneratorHeaderViewModel - now handled by mediator
        // REMOVED: NewProjectWorkflowViewModel CreateNewProjectWorkflowViewModel - use DI container directly

        NotificationAreaViewModel CreateNotificationAreaViewModel();
        DefaultNotificationViewModel CreateDefaultNotificationViewModel();
        
        // Domain-specific notification VM - replaces legacy TestCaseGeneratorNotificationViewModel
        TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel CreateNotificationWorkspaceViewModel();
        
        // Domain ViewModels (proper DI approach)
        // WorkspaceManagementViewModel removed - use WorkspaceProjectViewModel instead
        ChatGptExportAnalysisViewModel CreateChatGptExportAnalysisViewModel();

    }
}
