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
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.Extensions;
using TestCaseEditorApp.MVVM.Utils;

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

                    // Toast notification system
                    services.AddSingleton<ToastNotificationService>(provider => 
                        new ToastNotificationService(Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher));
                    services.AddSingleton<NotificationService>();
                    
                    // Requirement parsing - wrap with notification support
                    services.AddSingleton<RequirementService>(); // Core service
                    services.AddSingleton<IRequirementService, NotifyingRequirementService>(); // Wrapper with notifications

                    // File dialog helper used by the VM
                    services.AddSingleton<IFileDialogService, FileDialogService>();

                    // Domain UI coordination
                    services.AddSingleton<IDomainUICoordinator, DomainUICoordinator>();

                    // LLM services (shared infrastructure)
                    services.AddSingleton<ITextGenerationService>(_ => LlmFactory.Create());
                    services.AddSingleton<RequirementAnalysisService>();
                    services.AddSingleton<AnythingLLMService>(provider =>
                        new AnythingLLMService()); // Let it get baseUrl and apiKey from defaults/user config

                    // Domain coordination
                    services.AddSingleton<IDomainCoordinator, DomainCoordinator>();

                    // Extensibility infrastructure (Phase 7)
                    services.AddSingleton<IServiceDiscovery, ServiceDiscovery>();
                    services.AddSingleton<ExtensionManager>();
                    services.AddSingleton<PerformanceMonitoringService>(); // From Phase 6
                    services.AddSingleton<EventReplayService>(); // From Phase 6

                    // Domain mediators with advanced services integration
                    services.AddSingleton<ITestCaseGenerationMediator>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<TestCaseGenerationMediator>>();
                        var uiCoordinator = provider.GetRequiredService<IDomainUICoordinator>();
                        var requirementService = provider.GetRequiredService<IRequirementService>();
                        var analysisService = provider.GetRequiredService<RequirementAnalysisService>();
                        var llmService = provider.GetRequiredService<ITextGenerationService>();
                        var performanceMonitor = provider.GetService<PerformanceMonitoringService>();
                        var eventReplay = provider.GetService<EventReplayService>();
                        
                        return new TestCaseGenerationMediator(logger, uiCoordinator, requirementService, 
                            analysisService, llmService, performanceMonitor, eventReplay);
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

                    // ViewModels and header VM
                    services.AddTransient<TestCaseGenerator_VM>();
                    services.AddSingleton<WorkspaceHeaderViewModel>(); // workspace header shared instance
                    services.AddTransient<MainViewModel>(provider =>
                    {
                        var applicationServices = provider.GetRequiredService<IApplicationServices>();
                        var viewModelFactory = provider.GetRequiredService<IViewModelFactory>();
                        var projectManagement = provider.GetRequiredService<ProjectManagementViewModel>();
                        var llmServiceManagement = provider.GetRequiredService<LLMServiceManagementViewModel>();
                        var requirementProcessing = provider.GetRequiredService<RequirementProcessingViewModel>();
                        var uiModalManagement = provider.GetRequiredService<UIModalManagementViewModel>();
                        var workspaceManagement = provider.GetRequiredService<WorkspaceManagementViewModel>();
                        var navigationHeaderManagement = provider.GetRequiredService<NavigationHeaderManagementViewModel>();
                        var requirementAnalysisManagement = provider.GetRequiredService<RequirementAnalysisManagementViewModel>();
                        
                        return new MainViewModel(applicationServices, viewModelFactory, projectManagement, llmServiceManagement, requirementProcessing, uiModalManagement, workspaceManagement, navigationHeaderManagement, requirementAnalysisManagement, provider);
                    });
                    services.AddTransient<NavigationViewModel>();

                    // New domain ViewModels for consolidation
                    services.AddTransient<ProjectManagementViewModel>();
                    services.AddTransient<UIModalManagementViewModel>();
                    services.AddTransient<LLMServiceManagementViewModel>();
                    services.AddTransient<RequirementProcessingViewModel>();
                    services.AddTransient<RequirementAnalysisManagementViewModel>();
                    services.AddTransient<WorkspaceManagementViewModel>();
                    services.AddTransient<NavigationHeaderManagementViewModel>();
                    // NavigationHeaderManagementViewModel and RequirementImportExportViewModel already exist
                    // ChatGptExportAnalysisViewModel is registered in its domain

                    // Core application services
                    services.AddSingleton<ChatGptExportService>();
                    services.AddSingleton<IViewModelFactory, ViewModelFactory>();
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
                
                // Set up domain coordinator and register mediators
                var domainCoordinator = _host.Services.GetRequiredService<IDomainCoordinator>();
                TestCaseEditorApp.MVVM.Utils.BaseDomainMediatorBase.SetDomainCoordinator(domainCoordinator);
                
                domainCoordinator.RegisterDomainMediator("TestCaseGeneration", testCaseGenMediator);
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
                    var vm = _host.Services.GetService<MainViewModel>() ?? new MainViewModel();
                    mainWindow = new MainWindow(vm);
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