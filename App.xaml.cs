using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.Parsing;
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

                    // Toast notification system
                    services.AddSingleton<ToastNotificationService>(provider => 
                        new ToastNotificationService(Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher));
                    services.AddSingleton<NotificationService>();
                    
                    // Modal service for cross-domain modal display
                    services.AddSingleton<IModalService, StubModalService>(); // Stub implementation to prevent modal issues
                    
                    // Requirement parsing - wrap with notification support
                    services.AddSingleton<RequirementService>(); // Core service
                    services.AddSingleton<IRequirementService, NotifyingRequirementService>(); // Wrapper with notifications

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
                    services.AddSingleton<IRequirementAnalysisService, TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.RequirementAnalysisService>(provider =>
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
                    services.AddSingleton<AnythingLLMService>(provider =>
                        new AnythingLLMService()); // Let it get baseUrl and apiKey from defaults/user config
                    services.AddSingleton<TestCaseAnythingLLMService>();
                    
                    // LLM Learning Feedback Services
                    services.AddSingleton<ITextSimilarityService, TextSimilarityService>();
                    services.AddSingleton<ILLMLearningService, LLMLearningService>();
                    services.AddSingleton<IEditDetectionService, EditDetectionService>();
                    
                    // Jama Connect integration service
                    services.AddSingleton<JamaConnectService>(provider =>
                    {
                        try
                        {
                            var baseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL");
                            var clientId = Environment.GetEnvironmentVariable("JAMA_CLIENT_ID");
                            var clientSecret = Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET");
                            
                            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
                            {
                                // Handle common Jama path variations
                                if (baseUrl.Contains("rockwellcollins.com") && !baseUrl.Contains("/contour") && !baseUrl.EndsWith("/contour"))
                                {
                                    baseUrl = baseUrl.TrimEnd('/') + "/contour";
                                }
                                
                                return new JamaConnectService(baseUrl, clientId, clientSecret, true);
                            }
                            else
                            {
                                return new JamaConnectService("", "");
                            }
                        }
                        catch (Exception)
                        {
                            return new JamaConnectService("", "");
                        }
                    });
                    
                    // Generic service monitoring
                    services.AddSingleton<GenericServiceMonitor>();

                    // ViewModels that need DI
                    services.AddSingleton<SideMenuViewModel>(provider =>
                    {
                        var newProjectMediator = provider.GetRequiredService<TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator>();
                        var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var testCaseAnythingLLMService = provider.GetRequiredService<TestCaseAnythingLLMService>();
                        var jamaConnectService = provider.GetRequiredService<JamaConnectService>();
                        var logger = provider.GetRequiredService<ILogger<SideMenuViewModel>>();
                        
                        return new SideMenuViewModel(newProjectMediator, navigationMediator, 
                            testCaseGenerationMediator, testCaseAnythingLLMService, jamaConnectService, logger);
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
                        var analysisService = provider.GetRequiredService<IRequirementAnalysisService>();
                        var llmService = provider.GetRequiredService<ITextGenerationService>();
                        var scrubber = provider.GetRequiredService<IRequirementDataScrubber>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators.TestCaseGenerationMediator(logger, uiCoordinator, requirementService, 
                            analysisService, llmService, scrubber, performanceMonitor, eventReplay);
                    });
                    
                    services.AddSingleton<ITestCaseCreationMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators.TestCaseCreationMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators.TestCaseCreationMediator(
                            logger, uiCoordinator, performanceMonitor, eventReplay);
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
                    
                    // Dummy domain ViewModels - updated naming convention
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_MainViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_HeaderViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_NavigationViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_TitleViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_NotificationViewModel>();
                    
                    // === STARTUP DOMAIN REGISTRATION (FOR INITIAL APP STATE) ===
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_MainViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_HeaderViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NavigationViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_TitleViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NotificationViewModel>();

                    // === TEST CASE GENERATION DOMAIN WORKSPACE VIEWMODELS ===
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorMainVM>();

                    services.AddSingleton<INewProjectMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<NewProjectMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var persistenceService = provider.GetRequiredService<IPersistenceService>();
                        var fileDialogService = provider.GetRequiredService<IFileDialogService>();
                        var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
                        var notificationService = provider.GetRequiredService<NotificationService>();
                        var requirementService = provider.GetRequiredService<IRequirementService>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var workspaceValidationService = provider.GetRequiredService<IWorkspaceValidationService>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new NewProjectMediator(logger, uiCoordinator, persistenceService, 
                            fileDialogService, anythingLLMService, notificationService, requirementService,
                            testCaseGenerationMediator, workspaceValidationService, performanceMonitor, eventReplay);
                    });

                    // ViewModels and header VM
                    services.AddTransient<TestCaseGenerator_VM>();
                    services.AddTransient<TestCaseGeneratorViewModel>();
                    services.AddTransient<RequirementGenerationViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementImportExportViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorSplashViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorSplashScreenViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementAnalysisViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.ChatGptExportAnalysisViewModel>();
                    services.AddSingleton<WorkspaceHeaderViewModel>(); // workspace header shared instance
                    services.AddTransient<NotificationAreaViewModel>(); // notification area for status indicators
                    
                    services.AddTransient<MainViewModel>(provider =>
                    {
                        var viewModelFactory = provider.GetRequiredService<IViewModelFactory>();
                        var navigationService = provider.GetRequiredService<INavigationService>();
                        var logger = provider.GetService<ILogger<MainViewModel>>();
                        
                        return new MainViewModel(viewModelFactory, navigationService, logger);
                    });
                    services.AddTransient<NavigationViewModel>();

                    // New domain ViewModels for consolidation
                    // UIModalManagementViewModel REMOVED - Cross-cutting infrastructure violation, use domain mediators
                    // LLMServiceManagementViewModel REMOVED - Duplicate of TestCaseGeneration domain LLM infrastructure
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementProcessingViewModel>();
                    // RequirementAnalysisManagementViewModel REMOVED - duplicate functionality, use RequirementAnalysisViewModel in TestCaseGeneration domain
                    // WorkspaceManagementViewModel REMOVED - duplicate functionality, use WorkspaceProjectViewModel
                    // NavigationHeaderManagementViewModel REMOVED - Cross-cutting infrastructure violation
                    // RequirementImportExportViewModel (legacy root version) REMOVED - duplicate of domain version
                    // Use TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementImportExportViewModel
                    // ChatGptExportAnalysisViewModel is registered in its domain

                    // Test Case Creation domain ViewModels
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels.TestCaseCreationMainVM>();
                    
                    // Test Case Generation domain ViewModels - proper DI registration
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_VM>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementsWorkspaceViewModel>();
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel>();
                    
                    // NewProject domain ViewModels - proper DI registration
                    services.AddTransient<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>();

                    // Core application services
                    services.AddSingleton<ChatGptExportService>();
                    
                    // View configuration service for new navigation pattern
                    services.AddSingleton<IViewConfigurationService>(provider =>
                    {
                        var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var testCaseCreationMediator = provider.GetRequiredService<ITestCaseCreationMediator>();
                        
                        return new ViewConfigurationService(newProjectMediator, testCaseGenerationMediator, testCaseCreationMediator);
                    });
                    
                    services.AddSingleton<IViewModelFactory>(provider =>
                    {
                        var applicationServices = provider.GetRequiredService<IApplicationServices>();
                        var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        return new ViewModelFactory(applicationServices, newProjectMediator, testCaseGenerationMediator);
                    });
                    
                    // ViewAreaCoordinator registration - required for workspace coordination
                    services.AddSingleton<IViewAreaCoordinator>(provider =>
                    {
                        var viewModelFactory = provider.GetRequiredService<IViewModelFactory>();
                        var navigationMediator = provider.GetRequiredService<INavigationMediator>();
                        var newProjectMediator = provider.GetRequiredService<INewProjectMediator>();
                        var testCaseGenerationMediator = provider.GetRequiredService<ITestCaseGenerationMediator>();
                        var viewConfigurationService = provider.GetRequiredService<IViewConfigurationService>();
                        var sideMenuViewModel = provider.GetRequiredService<SideMenuViewModel>();
                        return new ViewAreaCoordinator(viewModelFactory, navigationMediator, newProjectMediator, testCaseGenerationMediator, viewConfigurationService, sideMenuViewModel);
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
                
                var testCaseCreationMediator = _host.Services.GetRequiredService<ITestCaseCreationMediator>();
                testCaseCreationMediator.MarkAsRegistered();
                
                var testFlowMediator = _host.Services.GetRequiredService<ITestFlowMediator>();
                testFlowMediator.MarkAsRegistered();
                
                var newProjectMediator = _host.Services.GetRequiredService<INewProjectMediator>();
                newProjectMediator.MarkAsRegistered();
                
                // Mark Dummy mediator as registered for testing
                var dummyMediator = _host.Services.GetRequiredService<TestCaseEditorApp.MVVM.Domains.Dummy.Mediators.IDummyMediator>();
                dummyMediator.MarkAsRegistered();
                
                // Wire cross-domain commands - enable workspace commands in header
                if (testCaseGenMediator is MVVM.Domains.TestCaseGeneration.Mediators.TestCaseGenerationMediator tcgMediator)
                {
                    tcgMediator.WireWorkspaceCommands(newProjectMediator);
                }
                
                // Set up domain coordinator and register mediators
                var domainCoordinator = _host.Services.GetRequiredService<IDomainCoordinator>();
                TestCaseEditorApp.MVVM.Utils.BaseDomainMediatorBase.SetDomainCoordinator(domainCoordinator);
                
                domainCoordinator.RegisterDomainMediator("TestCaseGeneration", testCaseGenMediator);
                domainCoordinator.RegisterDomainMediator("TestCaseCreation", testCaseCreationMediator);
                domainCoordinator.RegisterDomainMediator("TestFlow", testFlowMediator);
                
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
                    Endpoint = "http://localhost:3001/api/system/status",
                    CheckInterval = TimeSpan.FromSeconds(10),
                    Type = ServiceType.AnythingLLM
                });
                
                // Start monitoring all configured services
                serviceMonitor.StartAll();
                logger.LogInformation("Service monitoring started for AnythingLLM");
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