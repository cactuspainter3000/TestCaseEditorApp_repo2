using System;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Coordinates navigation between different UI areas.
    /// This replaces the complex navigation logic scattered throughout MainViewModel.
    /// </summary>
    public interface IViewAreaCoordinator
    {
        // UI Area ViewModels
        SideMenuViewModel SideMenu { get; }
        HeaderAreaViewModel HeaderArea { get; }
        WorkspaceContentViewModel WorkspaceContent { get; }

        // Navigation Methods
        void NavigateToProject();
        void NavigateToRequirements();
        void NavigateToTestCaseGenerator();
        void NavigateToTestFlow();
        void NavigateToImport();
        void NavigateToNewProject();

        // State Management
        string CurrentSection { get; }
        event Action<string>? SectionChanged;
    }
}