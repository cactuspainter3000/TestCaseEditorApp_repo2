using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using TestCaseEditorApp.MVVM.Extensions;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.Services.Theme;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// Core application services: persistence, validation, notifications, settings.
    /// These services are required for basic app functionality and are always Singleton.
    /// </summary>
    public static class CoreServiceExtensions
    {
        [SupportedOSPlatform("windows")]
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            // Persistence and workspace management (SINGLETON - app-wide state)
            services.AddSingleton<IPersistenceService, JsonPersistenceService>();
            services.AddSingleton<IWorkspaceValidationService, WorkspaceValidationService>();
            services.AddSingleton<IWorkspaceContext, WorkspaceContextService>();
            services.AddSingleton<RecentFilesService>();

            // Notification system (SINGLETON - Dispatcher-tied, must be single instance)
            services.AddSingleton<ToastNotificationService>(provider =>
                new ToastNotificationService(System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher));
            services.AddSingleton<NotificationService>();

            // User configuration (SINGLETON - loaded once at startup)
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<ISettingsDialogService, SettingsDialogService>();

            // Modal service (SINGLETON - cross-domain modal state)
            services.AddSingleton<IModalService, StubModalService>();

            // File and text dialog services (SINGLETON - stateless helpers)
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<ITextEditingDialogService, TextEditingDialogService>();

            // OCR service (SINGLETON - resource-intensive, shared across app)
            services.AddSingleton<IOCRService, TesseractOCRService>();

            // Navigation services (SINGLETON - maintains view state)
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<INavigationMediator>(provider =>
            {
                var logger = provider.GetService<Microsoft.Extensions.Logging.ILogger<NavigationMediator>>();
                return new NavigationMediator(logger);
            });

            // Domain UI coordination (SINGLETON - app-wide state)
            services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();

            // Extensibility infrastructure (SINGLETON - system-wide)
            services.AddSingleton<IServiceDiscovery, ServiceDiscovery>();
            services.AddSingleton<ExtensionManager>();
            services.AddSingleton<PerformanceMonitoringService>();
            services.AddSingleton<EventReplayService>();

            // Theme management (SINGLETON - app-wide theme state)
            services.AddSingleton<ThemeService>();

            // Domain coordinator (SINGLETON - cross-domain event routing)
            services.AddSingleton<IDomainCoordinator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<DomainCoordinator>>();
                return new DomainCoordinator(logger);
            });

            // Generic monitoring (SINGLETON - system-wide metrics)
            services.AddSingleton<GenericServiceMonitor>();

            // Data scrubbing (SCOPED - per-request lifecycle)
            services.AddScoped<IRequirementDataScrubber, RequirementDataScrubber>();

            // Core application services (SINGLETON - app state)
            services.AddSingleton<ChatGptExportService>();
            services.AddSingleton<IWorkspaceDiagnosticsService, WorkspaceDiagnosticsService>();

            return services;
        }
    }
}
