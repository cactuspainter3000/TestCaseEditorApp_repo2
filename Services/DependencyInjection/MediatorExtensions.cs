using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using TestCaseEditorApp.MVVM.Domains.Notification.Mediators;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services.DependencyInjection
{
    /// <summary>
    /// Domain mediators: cross-domain communication orchestrators.
    /// All SINGLETON - maintains domain state and mediates communication between UI and services.
    /// Mediators should NOT create circular dependencies - carefully validate resolution order.
    /// </summary>
    public static class MediatorExtensions
    {
        public static IServiceCollection AddDomainMediators(this IServiceCollection services)
        {
            // Dummy Mediator - Testing workspace coordination (SINGLETON)
            services.AddSingleton<IDummyMediator, DummyMediator>();

            // Startup Mediator - Initial app state (SINGLETON)
            services.AddSingleton<IStartupMediator, StartupMediator>();

            // Notification Mediator - Event notifications (SINGLETON)
            services.AddSingleton<INotificationMediator, NotificationMediator>();

            // Test Case Generation Mediator (SINGLETON - orchestrates analysis and generation)
            services.AddSingleton<ITestCaseGenerationMediator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TestCaseGenerationMediator>>();
                var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                var analysisService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>();
                var llmService = provider.GetRequiredService<ITextGenerationService>();
                var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();

                return new TestCaseGenerationMediator(logger, uiCoordinator,
                    analysisService, llmService, performanceMonitor, eventReplay);
            });

            // Test Flow Mediator (SINGLETON)
            services.AddSingleton<ITestFlowMediator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TestFlowMediator>>();
                var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                var llmService = provider.GetRequiredService<ITextGenerationService>();
                var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();

                return new TestFlowMediator(logger, uiCoordinator, llmService,
                    performanceMonitor, eventReplay);
            });

            // Requirements Mediator - Requirement management and analysis (SINGLETON - primary data orchestrator)
            services.AddSingleton<IRequirementsMediator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RequirementsMediator>>();
                var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                var analysisService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>();
                var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var jamaConnectService = provider.GetRequiredService<IJamaConnectService>();
                var jamaDocumentParserService = provider.GetRequiredService<IJamaDocumentParserService>();
                var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();

                // Optional analysis engine from Requirements domain
                var analysisEngine = provider.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisEngine>();

                return new RequirementsMediator(
                    logger, uiCoordinator, analysisService,
                    workspaceContext, newProjectMediator, jamaConnectService, jamaDocumentParserService,
                    provider.GetRequiredService<SmartRequirementImporter>(),
                    analysisEngine, performanceMonitor, eventReplay);
            });

            // Test Case Creation Mediator (SINGLETON)
            services.AddSingleton<ITestCaseCreationMediator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<TestCaseCreationMediator>>();
                var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();
                return new TestCaseCreationMediator(
                    logger, uiCoordinator, performanceMonitor, eventReplay);
            });

            // New Project Mediator - Project creation workflow (SINGLETON)
            services.AddSingleton<INewProjectMediator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<NewProjectMediator>>();
                var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                var persistenceService = provider.GetRequiredService<IPersistenceService>();
                var fileDialogService = provider.GetRequiredService<IFileDialogService>();
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                var smartImporter = provider.GetRequiredService<SmartRequirementImporter>();
                var jamaConnectService = provider.GetRequiredService<JamaConnectService>();
                var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();
                var userSettingsService = provider.GetService<IUserSettingsService>();

                return new NewProjectMediator(logger, uiCoordinator, persistenceService,
                    fileDialogService, anythingLLMService, smartImporter, jamaConnectService,
                    userSettingsService, performanceMonitor, eventReplay);
            });

            // Open Project Mediator - Project loading and switching (SINGLETON)
            services.AddSingleton<IOpenProjectMediator>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<OpenProjectMediator>>();
                var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                var jamaConnectService = provider.GetRequiredService<IJamaConnectService>();
                var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();

                return new OpenProjectMediator(logger, uiCoordinator,
                    anythingLLMService, jamaConnectService, newProjectMediator, performanceMonitor, eventReplay);
            });

            // Training Data Validation Mediator (SINGLETON)
            services.AddSingleton<ITrainingDataValidationMediator, TrainingDataValidationMediator>();

            return services;
        }
    }
}
