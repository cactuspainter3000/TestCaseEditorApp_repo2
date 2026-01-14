using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

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
        ConfigurableTitleAreaViewModel TitleArea { get; }
        ConfigurableHeaderAreaViewModel HeaderArea { get; }
        ConfigurableContentAreaViewModel WorkspaceContent { get; }
        ConfigurableNavigationAreaViewModel NavigationArea { get; }
        ConfigurableNotificationAreaViewModel NotificationArea { get; }
        INavigationMediator NavigationMediator { get; }
        INewProjectMediator WorkspaceManagement { get; }
    }
}