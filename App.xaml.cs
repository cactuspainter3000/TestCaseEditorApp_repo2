using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.Parsing;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.Extensions;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.Prompts;

namespace TestCaseEditorApp
{
    /// <summary>
    /// Application entrypoint that sets up a Generic Host for DI, logging and any app-wide services.
    /// </summary>
    public partial class App : Application
    {
        // Make the host static so the static ServiceProvider accessor can reference it without an instance.
        private static IHost? _host;

        /// <summary>
        /// Public accessor for the application's root service provider.
        /// Returns null if the host has not yet been started.
        /// </summary>
        public static IServiceProvider? ServiceProvider => _host?.Services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    // Configure default console and debug logging plus a simple file sink
                    logging.AddDebug();
                    logging.AddConsole();
                    try
                    {
                        var logs = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "logs");
                        logging.AddProvider(new TestCaseEditorApp.Services.Logging.FileLoggerProvider(logs));
                    }
                    catch
                    {
                        // best-effort - do not fail startup for logging provider issues
                    }
                })
                .ConfigureServices((ctx, services) =>
                {
                    // Core / persistence services
                    services.AddSingleton<IPersistenceService, JsonPersistenceService>();
                    services.AddSingleton<IWorkspaceValidationService, WorkspaceValidationService>();
                    services.AddSingleton<IWorkspaceContext, WorkspaceContextService>();
                    services.AddSingleton<RecentFilesService>();

                    // Toast notification system
                    services.AddSingleton<ToastNotificationService>(provider => 
                        new ToastNotificationService(Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher));
                    services.AddSingleton<NotificationService>();
                    
                    // Modal service for cross-domain modal display
                    services.AddSingleton<IModalService, StubModalService>(); // Stub implementation to prevent modal issues
                    
                    // Requirement parsing - wrap with notification support
                    services.AddSingleton<RequirementService>(); // Core service
                    services.AddSingleton<IRequirementService, NotifyingRequirementService>(); // Wrapper with notifications
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.SmartRequirementImporter>(); // Smart importer with fallback logic

                    // File dialog helper used by the VM
                    services.AddSingleton<IFileDialogService, FileDialogService>();
                    
                    // Text editing dialog service for architectural compliance
                    services.AddSingleton<ITextEditingDialogService, TextEditingDialogService>();

                    // Domain UI coordination
                    services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();

                    // Navigation service for title management
                    services.AddSingleton<INavigationService, NavigationService>();
                    
                    // Navigation mediator for view coordination
                    services.AddSingleton<INavigationMediator>(provider => 
                    {
                        var logger = provider.GetService<ILogger<NavigationMediator>>();
                        return new NavigationMediator(logger);
                    });

                    // Requirement data scrubber (shared infrastructure)
                    services.AddScoped<IRequirementDataScrubber, RequirementDataScrubber>();

                    // LLM services (shared infrastructure)
                    services.AddSingleton<ITextGenerationService>(_ => LlmFactory.Create());
                    
                    // LLM Health Monitoring - configured to be less aggressive with fallback
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.LlmServiceHealthMonitor>(provider =>
                    {
                        var primaryLlmService = LlmFactory.Create();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.LlmServiceHealthMonitor>>();
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.LlmServiceHealthMonitor(
                            primaryLlmService, 
                            logger, 
                            TimeSpan.FromMinutes(2)); // Less frequent health checks to avoid premature fallback
                    });
                    
                    // LLM Analysis Caching
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisCache>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisCache>>();
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisCache(
                            logger,
                            maxCacheSize: 500, // Cache up to 500 analysis results
                            maxAge: TimeSpan.FromHours(8), // Cache expires after 8 hours
                            cleanupInterval: TimeSpan.FromMinutes(30)); // Cleanup every 30 minutes
                    });
                    
                    // Register dependencies for RequirementAnalysisService
                    services.AddSingleton<RequirementAnalysisPromptBuilder>();
                    services.AddSingleton<ResponseParserManager>();
                    
                    // Enhanced RequirementAnalysisService with proper dependency injection
                    // Register for Requirements domain (new interface location)
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService, TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisService>(provider =>
                    {
                        var primaryLlmService = LlmFactory.Create();
                        var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                        var promptBuilder = provider.GetRequiredService<RequirementAnalysisPromptBuilder>();
                        var parserManager = provider.GetRequiredService<ResponseParserManager>();
                        var cache = provider.GetService<RequirementAnalysisCache>(); // Optional
                        
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisService(
                            primaryLlmService, 
                            promptBuilder, 
                            parserManager,
                            healthMonitor: null, // No health monitor for performance
                            cache: cache,
                            anythingLLMService: anythingLLMService);
                    });
                    
                    // Also register for TestCaseGeneration domain (legacy interface location) during migration
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.IRequirementAnalysisService>(provider =>
                        provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>() as TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisService);

                    // ===== REQUIREMENTS DOMAIN SERVICES (Refactored Architecture) =====
                    
                    // Register the new analysis engine that consolidates analysis functionality
                    services.AddScoped<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisEngine, 
                                      TestCaseEditorApp.MVVM.Domains.Requirements.Services.RequirementAnalysisEngine>();
                    
                    // Register the focused RequirementAnalysisViewModel for Requirements domain
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.RequirementAnalysisViewModel>();
                    
                    // Register the shared analysis ViewModel for cross-domain usage (DRY principle)
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Shared.ViewModels.SharedAnalysisViewModel>();
                    
                    services.AddSingleton<AnythingLLMService>(provider =>
                        new AnythingLLMService()); // Let it get baseUrl and apiKey from defaults/user config
                    services.AddSingleton<TestCaseAnythingLLMService>();
                    
                    // LLM Learning Feedback Services
                    services.AddSingleton<ITextSimilarityService, TextSimilarityService>();
                    services.AddSingleton<ILLMLearningService, LLMLearningService>();
                    services.AddSingleton<IEditDetectionService, EditDetectionService>();
                    
                    // Jama Connect integration service - Following Architectural Guide AI patterns
                    services.AddSingleton<JamaConnectService>(provider =>
                    {
                        try
                        {
                            return JamaConnectService.FromConfiguration();
                        }
                        catch (Exception ex)
                        {
                            // Create a non-configured service that will report proper errors
                            var logger = provider.GetService<ILogger<JamaConnectService>>();
                            logger?.LogWarning("Jama Connect not configured: {Error}", ex.Message);
                            return new JamaConnectService("", ""); // This will properly report "not configured" in IsConfigured
                        }
                    });
                    
                    // Register interface for testable architecture
                    services.AddSingleton<IJamaConnectService>(provider => provider.GetRequiredService<JamaConnectService>());
                    
                    // Generic service monitoring
                    services.AddSingleton<GenericServiceMonitor>();

                    // ViewModels that need DI
                    services.AddSingleton<SideMenuViewModel>(provider =>
                    {
                        var newProjectMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator>();
                        var openProjectMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.IOpenProjectMediator>();
                        var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var requirementsMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                        var testCaseAnythingLLMService = provider.GetRequiredService<TestCaseAnythingLLMService>();
                        var jamaConnectService = provider.GetRequiredService<JamaConnectService>();
                        var logger = provider.GetRequiredService<ILogger<SideMenuViewModel>>();
                        
                        return new SideMenuViewModel(newProjectMediator, openProjectMediator, navigationMediator, 
                            testCaseGenerationMediator, requirementsMediator, testCaseAnythingLLMService, jamaConnectService, logger);
                    });

                    // Domain coordination
                    services.AddSingleton<IDomainCoordinator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<DomainCoordinator>>();
                        
                        return new DomainCoordinator(logger);
                    });

                    // Extensibility infrastructure (Phase 7)
                    services.AddSingleton<IServiceDiscovery, ServiceDiscovery>();
                    services.AddSingleton<ExtensionManager>();
                    services.AddSingleton<PerformanceMonitoringService>(); // From Phase 6
                    services.AddSingleton<EventReplayService>(); // From Phase 6

                    // Domain mediators with advanced services integration
                    services.AddSingleton<ITestCaseGenerationMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators.TestCaseGenerationMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var requirementService = provider.GetRequiredService<IRequirementService>();
                        var analysisService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>();
                        var llmService = provider.GetRequiredService<ITextGenerationService>();
                        var scrubber = provider.GetRequiredService<IRequirementDataScrubber>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators.TestCaseGenerationMediator(logger, uiCoordinator, requirementService, 
                            analysisService, llmService, scrubber, performanceMonitor, eventReplay);
                    });

                    services.AddSingleton<ITestFlowMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestFlowMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var llmService = provider.GetRequiredService<ITextGenerationService>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new TestFlowMediator(logger, uiCoordinator, llmService,
                            performanceMonitor, eventReplay);
                    });

                    // === DUMMY DOMAIN REGISTRATION (FOR TESTING WORKSPACE COORDINATION) ===
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Dummy.Mediators.IDummyMediator, TestCaseEditorApp.MVVM.Domains.Dummy.Mediators.DummyMediator>();
                    
                    // === STARTUP DOMAIN REGISTRATION ===
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Startup.Mediators.IStartupMediator, TestCaseEditorApp.MVVM.Domains.Startup.Mediators.StartupMediator>();
                    
                    // === REQUIREMENTS DOMAIN REGISTRATION ===
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.RequirementsMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var requirementService = provider.GetRequiredService<IRequirementService>();
                        var analysisService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>();
                        var scrubber = provider.GetRequiredService<IRequirementDataScrubber>();
                        var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                        var newProjectMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<TestCaseEditorApp.MVVM.Utils.EventReplayService>();
                        
                        // NEW: Get the Requirements domain analysis engine
                        var analysisEngine = provider.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisEngine>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.RequirementsMediator(
                            logger, uiCoordinator, requirementService, analysisService, scrubber, 
                            workspaceContext, newProjectMediator, analysisEngine, performanceMonitor, eventReplay);
                    });
                    
                    // Requirements domain ViewModels - Navigation as Singleton to maintain state
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel>(provider =>
                    {
                        var reqMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                        var testCaseGenMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators.ITestCaseGenerationMediator>();
                        var persistence = provider.GetRequiredService<IPersistenceService>();
                        var textEditingService = provider.GetRequiredService<ITextEditingDialogService>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel>>();
                        var analysisService = provider.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel(
                            reqMediator, persistence, textEditingService, logger, analysisService);
                    });
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>(provider =>
                    {
                        // Use RequirementsMediator as independent data source for header
                        var reqMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                        var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>>();
                        return new TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel(reqMediator, workspaceContext, logger);
                    });
                    
                    // Jama-optimized Requirements ViewModel for rich structured content display
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.JamaRequirementsMainViewModel>(provider =>
                    {
                        var reqMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.JamaRequirementsMainViewModel>>();
                        var requirementAnalysisVM = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.RequirementAnalysisViewModel>();
                        return new TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.JamaRequirementsMainViewModel(reqMediator, logger, requirementAnalysisVM);
                    });
                    
                    // Requirements uses shared NavigationViewModel and NotificationWorkspaceViewModel
                    
                    // Project domain ViewModels
                    
                    // Dummy domain ViewModels - updated naming convention
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_MainViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_HeaderViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_NavigationViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_TitleViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_NotificationViewModel>();
                    
                    // === NOTIFICATION DOMAIN REGISTRATION ===
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator, TestCaseEditorApp.MVVM.Domains.Notification.Mediators.NotificationMediator>();
                    
                    // Notification domain ViewModels - singleton to maintain event subscriptions
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                    
                    // NewProject domain ViewModels - using proper domain ViewModels
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>(provider =>
                    {
                        var newProjectMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>>();
                        var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel(
                            newProjectMediator, logger, anythingLLMService);
                    });
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectHeaderViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.DummyNewProjectTitleViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.DummyNewProjectNavigationViewModel>();
                    
                    // === STARTUP DOMAIN REGISTRATION (FOR INITIAL APP STATE) ===
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_MainViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_HeaderViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NavigationViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_TitleViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NotificationViewModel>();

                    // === TEST CASE GENERATION DOMAIN WORKSPACE VIEWMODELS ===
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels.TestCaseGeneratorMode_MainVM>();
                    // TestCase domains use shared NavigationViewModel for consistent navigation
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_HeaderVM>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                    // DEPRECATED: TestCaseGeneratorNotificationViewModel - use NotificationWorkspaceViewModel instead

                    services.AddSingleton<INewProjectMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<NewProjectMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var persistenceService = provider.GetRequiredService<IPersistenceService>();
                        var fileDialogService = provider.GetRequiredService<IFileDialogService>();
                        var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                        var notificationService = provider.GetRequiredService<NotificationService>();
                        var requirementService = provider.GetRequiredService<IRequirementService>();
                        var smartImporter = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.SmartRequirementImporter>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var workspaceValidationService = provider.GetRequiredService<IWorkspaceValidationService>();
                        var jamaConnectService = provider.GetRequiredService<JamaConnectService>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new NewProjectMediator(logger, uiCoordinator, persistenceService, 
                            fileDialogService, anythingLLMService, notificationService, requirementService,
                            smartImporter, testCaseGenerationMediator, workspaceValidationService, jamaConnectService, performanceMonitor, eventReplay);
                    });

                    // === OPEN PROJECT DOMAIN ===
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.IOpenProjectMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.OpenProjectMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var persistenceService = provider.GetRequiredService<IPersistenceService>();
                        var fileDialogService = provider.GetRequiredService<IFileDialogService>();
                        var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                        var notificationService = provider.GetRequiredService<NotificationService>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var workspaceValidationService = provider.GetRequiredService<IWorkspaceValidationService>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.OpenProjectMediator(logger, uiCoordinator, persistenceService, 
                            fileDialogService, anythingLLMService, notificationService, testCaseGenerationMediator, workspaceValidationService, performanceMonitor, eventReplay);
                    });

                    // OpenProject ViewModels
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>(provider =>
                    {
                        var mediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.IOpenProjectMediator>();
                        var persistenceService = provider.GetRequiredService<IPersistenceService>();
                        var recentFilesService = provider.GetRequiredService<RecentFilesService>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>>();
                        return new TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel(mediator, persistenceService, recentFilesService, logger);
                    });
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProject_TitleViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProject_HeaderViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProject_NavigationViewModel>();

                    // ViewModels and header VM
                    // REMOVED: RequirementGenerationViewModel - dead code, never resolved
                    // REMOVED: RequirementImportExportViewModel - dead code, never resolved (functionality in services/mediators)
                    // REMOVED: TestCaseGeneratorSplashViewModel - dead code, never used
                    // REMOVED: TestCaseGeneratorSplashScreenViewModel - dead code, never used
                    // REMOVED: RequirementAnalysisViewModel (TestCaseGeneration namespace) - duplicate of Requirements domain version, deleted
                    // REMOVED: ChatGptExportAnalysisViewModel - dead code domain, never used
                    services.AddSingleton<WorkspaceHeaderViewModel>(); // workspace header shared instance
                    // Old NotificationAreaViewModel deleted - now using shared NotificationWorkspaceViewModel
                    
                    services.AddTransient<MainViewModel>(provider =>
                    {
                        var viewAreaCoordinator = provider.GetRequiredService<IViewAreaCoordinator>();
                        var navigationService = provider.GetRequiredService<INavigationService>();
                        var logger = provider.GetService<ILogger<MainViewModel>>();
                        
                        return new MainViewModel(viewAreaCoordinator, navigationService, logger);
                    });
                    // Shared NavigationViewModel - SINGLETON to maintain state across domains
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>(provider =>
                    {
                        var requirementsMediator = provider.GetRequiredService<IRequirementsMediator>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>>();
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel(requirementsMediator, logger);
                    });

                    // New domain ViewModels for consolidation
                    // UIModalManagementViewModel REMOVED - Cross-cutting infrastructure violation, use domain mediators
                    // LLMServiceManagementViewModel REMOVED - Duplicate of TestCaseGeneration domain LLM infrastructure
                    // REMOVED: RequirementProcessingViewModel - dead code, never resolved
                    // RequirementAnalysisManagementViewModel REMOVED - duplicate functionality, use RequirementAnalysisViewModel in TestCaseGeneration domain
                    // WorkspaceManagementViewModel REMOVED - duplicate functionality, use WorkspaceProjectViewModel
                    // NavigationHeaderManagementViewModel REMOVED - Cross-cutting infrastructure violation
                    // RequirementImportExportViewModel (legacy root version) REMOVED - duplicate of domain version
                    // Use TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementImportExportViewModel
                    // ChatGptExportAnalysisViewModel is registered in its domain
                    
                    // Test Case Generation domain ViewModels - proper DI registration
                    services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_VM>(provider =>
                    {
                        var mediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators.ITestCaseGenerationMediator>();
                        var persistence = provider.GetRequiredService<IPersistenceService>();
                        var textEditingService = provider.GetRequiredService<ITextEditingDialogService>();
                        var analysisService = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.IRequirementAnalysisService>();
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_VM>>();
                        
                        var vm = new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_VM(
                            mediator, persistence, textEditingService, analysisService, logger);
                        
                        // Initialize the CoreVM for table and paragraph data (like ViewModelFactory does)
                        vm.TestCaseGenerator = new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_CoreVM();
                        
                        return vm;
                    });
                    // REMOVED: RequirementsWorkspaceViewModel - dead code, use Requirements_MainViewModel from Requirements domain
                    // REMOVED: TestCaseGeneratorNotificationViewModel - use NotificationWorkspaceViewModel from Notification domain instead

                    // Core application services
                    services.AddSingleton<ChatGptExportService>();
                    
                    // View configuration service for new navigation pattern
                    services.AddSingleton<IViewConfigurationService>(provider =>
                    {
                        var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                        var openProjectMediator = provider.GetRequiredService<IOpenProjectMediator>();
                        var requirementsMediator = provider.GetRequiredService<IRequirementsMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();

                        return new ViewConfigurationService(newProjectMediator, openProjectMediator, requirementsMediator, testCaseGenerationMediator);
                    });
                    
                    // ViewAreaCoordinator registration - required for workspace coordination
                    services.AddSingleton<IViewAreaCoordinator>(provider =>
                    {
                        var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                        var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var viewConfigurationService = provider.GetRequiredService<IViewConfigurationService>();
                        var sideMenuViewModel = provider.GetRequiredService<SideMenuViewModel>();
                        return new ViewAreaCoordinator(navigationMediator, newProjectMediator, testCaseGenerationMediator, viewConfigurationService, sideMenuViewModel);
                    });
                    
                    services.AddSingleton<IApplicationServices, ApplicationServices>();

                    // Views / Windows
                    services.AddTransient<MainWindow>();

                    // Add any other services your app needs here...
                })
                .Build();

            try
            {
                await _host.StartAsync();
                
                // Initialize extension system (Phase 7)
                var extensionManager = _host.Services.GetRequiredService<ExtensionManager>();
                var logger = _host.Services.GetRequiredService<ILogger<App>>();
                
                try
                {
                    logger.LogInformation("Initializing extension system...");
                    var discoveryResult = await extensionManager.DiscoverAndLoadExtensionsAsync();
                    
                    logger.LogInformation("Extension discovery completed. Loaded: {LoadedCount}, Errors: {ErrorCount}", 
                        discoveryResult.LoadedExtensions.Count, discoveryResult.Errors.Count);
                        
                    foreach (var error in discoveryResult.Errors)
                    {
                        logger.LogWarning("Extension error: {Error}", error);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to initialize extension system - continuing with core functionality");
                }
                
                // Mark domain mediators as registered for fail-fast validation
                var testCaseGenMediator = _host.Services.GetRequiredService<ITestCaseGenerationMediator>();
                testCaseGenMediator.MarkAsRegistered();
                
                var testFlowMediator = _host.Services.GetRequiredService<ITestFlowMediator>();
                testFlowMediator.MarkAsRegistered();
                
                var newProjectMediator = _host.Services.GetRequiredService<INewProjectMediator>();
                newProjectMediator.MarkAsRegistered();
                
                var openProjectMediator = _host.Services.GetRequiredService<TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators.IOpenProjectMediator>();
                openProjectMediator.MarkAsRegistered();
                
                // Mark Dummy mediator as registered for testing
                var dummyMediator = _host.Services.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Dummy.Mediators.IDummyMediator>();
                dummyMediator.MarkAsRegistered();
                
                // Mark Requirements mediator as registered for requirements domain
                var requirementsMediator = _host.Services.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                requirementsMediator.MarkAsRegistered();
                
                // Mark Startup mediator as registered for startup domain
                var startupMediator = _host.Services.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Startup.Mediators.IStartupMediator>();
                startupMediator.MarkAsRegistered();
                
                // Mark Notification mediator as registered for notification domain
                var notificationMediatorService = _host.Services.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator>();
                if (notificationMediatorService is TestCaseEditorApp.MVVM.Domains.Notification.Mediators.NotificationMediator notificationMediator)
                {
                    notificationMediator.MarkAsRegistered();
                }
                
                // Wire cross-domain commands - enable workspace commands in header
                if (testCaseGenMediator is MVVM.Domains.TestCaseGeneration.Mediators.TestCaseGenerationMediator tcgMediator)
                {
                    tcgMediator.WireWorkspaceCommands(newProjectMediator);
                }
                
                // Set up domain coordinator and register mediators
                var domainCoordinator = _host.Services.GetRequiredService<IDomainCoordinator>();
                TestCaseEditorApp.MVVM.Utils.BaseDomainMediatorBase.SetDomainCoordinator(domainCoordinator);
                
                var navigationMediator = _host.Services.GetRequiredService<INavigationMediator>();
                domainCoordinator.RegisterDomainMediator("Navigation", navigationMediator);
                domainCoordinator.RegisterDomainMediator("TestCaseGeneration", testCaseGenMediator);
                domainCoordinator.RegisterDomainMediator("TestFlow", testFlowMediator);
                domainCoordinator.RegisterDomainMediator("NewProject", newProjectMediator);
                domainCoordinator.RegisterDomainMediator("OpenProject", openProjectMediator);
                domainCoordinator.RegisterDomainMediator("Requirements", requirementsMediator);
                domainCoordinator.RegisterDomainMediator("Notification", notificationMediatorService);
                
                // Register any extension-provided domain mediators
                foreach (var domainExtension in extensionManager.DomainExtensions.Values)
                {
                    try
                    {
                        var domainMediator = domainExtension.CreateDomainMediator(_host.Services);
                        domainCoordinator.RegisterDomainMediator(domainExtension.DomainName, domainMediator);
                        logger.LogInformation("Registered domain mediator for extension: {DomainName}", domainExtension.DomainName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to register domain mediator for extension: {DomainName}", domainExtension.DomainName);
                    }
                }
                
                // Set up service monitoring
                var serviceMonitor = _host.Services.GetRequiredService<GenericServiceMonitor>();
                
                // Configure AnythingLLM monitoring
                serviceMonitor.AddService(new ServiceMonitorConfig
                {
                    Name = "AnythingLLM",
                    Endpoint = "http://localhost:3001/api/v1/workspaces",
                    CheckInterval = TimeSpan.FromSeconds(10),
                    Type = ServiceType.AnythingLLM
                });
                
                // Start monitoring all configured services
                serviceMonitor.StartAll();
                logger.LogInformation("Service monitoring started for AnythingLLM");
                
                // Auto-start AnythingLLM service if not running
                try
                {
                    var anythingLLMService = _host.Services.GetRequiredService<AnythingLLMService>();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            logger.LogInformation("Attempting to auto-start AnythingLLM service...");
                            var result = await anythingLLMService.EnsureServiceRunningAsync();
                            if (result.Success)
                            {
                                logger.LogInformation("AnythingLLM auto-start completed successfully");
                            }
                            else
                            {
                                logger.LogWarning("AnythingLLM auto-start failed: {Message}", result.Message);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error during AnythingLLM auto-start");
                        }
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to initiate AnythingLLM auto-start");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start host: {ex.Message}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                _host?.Dispose();
                _host = null;
                Shutdown(-1);
                return;
            }

            // Load merged dictionary BEFORE creating MainWindow so StaticResource resolves at parse time
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var asmName = asm.GetName().Name ?? "TestCaseEditorApp";
            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri($"/{asmName};component/Resources/MainWindowResources.xaml", UriKind.Relative)
                };
                Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load resources: {ex.Message}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                try { await _host.StopAsync(); } catch { }
                _host.Dispose();
                _host = null;
                Shutdown(-1);
                return;
            }

            // Resolve MainWindow from DI so its constructor can receive injected dependencies (MainViewModel, etc.)
            MainWindow mainWindow;
            try
            {
                mainWindow = _host.Services.GetRequiredService<MainWindow>();
            }
            catch (Exception ex)
            {
                // Fallback: if MainWindow ctor expects a MainViewModel, resolve the VM and construct manually.
                try
                {
                    var vm = _host.Services.GetService<MainViewModel>();
                    
                    if (vm != null)
                    {
                        mainWindow = new MainWindow(vm);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unable to resolve MainViewModel or its dependencies");
                    }
                }
                catch (Exception fallbackEx)
                {
                    MessageBox.Show($"Failed to create main window: {ex.Message}\nFallback error: {fallbackEx.Message}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
                    try { await _host.StopAsync(); } catch { }
                    _host.Dispose();
                    _host = null;
                    Shutdown(-1);
                    return;
                }
            }

            // Show window
            Current.MainWindow = mainWindow;
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            mainWindow.ShowInTaskbar = true;
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Show();
            mainWindow.Activate();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            if (_host != null)
            {
                try
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(3));
                }
                catch { /* best-effort */ }
                _host.Dispose();
                _host = null;
            }
        }
    }
}