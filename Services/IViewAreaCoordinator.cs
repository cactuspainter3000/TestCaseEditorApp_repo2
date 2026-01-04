using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Clean coordinator using configuration pattern.
    /// Delegates to ViewConfigurationService for all view management.
    /// </summary>
    public interface IViewAreaCoordinator
    {
        // UI Area ViewModels (now configurable)
        SideMenuViewModel SideMenu { get; }
        ConfigurableHeaderAreaViewModel HeaderArea { get; }
        ConfigurableContentAreaViewModel WorkspaceContent { get; }
        ConfigurableNotificationAreaViewModel NotificationArea { get; }
        INavigationMediator NavigationMediator { get; }
        IWorkspaceManagementMediator WorkspaceManagement { get; }
    }
}