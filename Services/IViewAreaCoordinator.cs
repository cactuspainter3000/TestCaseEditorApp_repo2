using System;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Coordinates navigation between different UI areas using mediator pattern.
    /// This replaces the complex navigation logic scattered throughout MainViewModel.
    /// Eliminates circular dependencies through message-based communication.
    /// </summary>
    public interface IViewAreaCoordinator
    {
        // UI Area ViewModels
        SideMenuViewModel SideMenu { get; }
        HeaderAreaViewModel HeaderArea { get; }
        WorkspaceContentViewModel WorkspaceContent { get; }
        INavigationMediator NavigationMediator { get; }

        // Navigation Methods
        void NavigateToProject();
        void NavigateToRequirements();
        void NavigateToTestCaseGenerator();
        void NavigateToTestFlow();
        void NavigateToImport();
        void NavigateToNewProject();
    }
}