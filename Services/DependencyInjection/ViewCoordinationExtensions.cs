using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// View configuration and coordination services: layout, navigation, workspace management.
    /// These services orchestrate multi-workspace UI composition and are SINGLETON.
    /// </summary>
    public static class ViewCoordinationExtensions
    {
        public static IServiceCollection AddViewCoordinationServices(this IServiceCollection services)
        {
            // View Configuration Service - Maps domain ViewModels to workspace configurations (SINGLETON)
            services.AddSingleton<IViewConfigurationService>(provider =>
            {
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var openProjectMediator = provider.GetRequiredService<IOpenProjectMediator>();
                var requirementsMediator = provider.GetRequiredService<IRequirementsMediator>();

                return new ViewConfigurationService(newProjectMediator, openProjectMediator, requirementsMediator);
            });

            // View Area Coordinator - Orchestrates 5-workspace composition (SINGLETON)
            services.AddSingleton<IViewAreaCoordinator>(provider =>
            {
                var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                var viewConfigurationService = provider.GetRequiredService<IViewConfigurationService>();
                var sideMenuViewModel = provider.GetRequiredService<SideMenuViewModel>();
                return new ViewAreaCoordinator(navigationMediator, newProjectMediator, testCaseGenerationMediator, viewConfigurationService, sideMenuViewModel);
            });

            // Application Services aggregator (SINGLETON)
            services.AddSingleton<IApplicationServices, ApplicationServices>();

            // Main Window View (TRANSIENT - per-instance)
            services.AddTransient<MainWindow>();

            return services;
        }
    }
}
