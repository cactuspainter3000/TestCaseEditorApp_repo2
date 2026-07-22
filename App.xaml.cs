using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;

using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels;

using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Mediators;
using TestCaseEditorApp.Services.Parsing; // For ResponseParserManager
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services.Templates;
using TestCaseEditorApp.MVVM.Extensions;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.DependencyInjection;
using TestCaseEditorApp.MVVM.Services.Theme;

namespace TestCaseEditorApp
{
    /// <summary>
    /// Application entrypoint that sets up a Generic Host for DI, logging and any app-wide services.
    /// </summary>
    public partial class App : Application
    {
        // Make the host static so the static ServiceProvider accessor can reference it without an instance.
        private static IHost? _host;
        private static string? _activeSessionLogPath;

        /// <summary>
        /// Public accessor for the application's root service provider.
        /// Returns null if the host has not yet been started.
        /// </summary>
        public static IServiceProvider? ServiceProvider => _host?.Services;

        [SupportedOSPlatform("windows")]
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Suppress Visual Studio designer binding errors
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;
            
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging =>
                {
                    // Clear default providers that might cause permission issues
                    logging.ClearProviders();

                    // Keep Output window signal high: warnings/errors globally, plus
                    // extraction pipeline informational logs needed for troubleshooting.
                    logging.SetMinimumLevel(LogLevel.Warning);
                    logging.AddFilter((category, level) =>
                    {
                        if (level >= LogLevel.Warning)
                        {
                            return true;
                        }

                        if (level >= LogLevel.Information)
                        {
                            if (string.Equals(category, "App", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }

                            if (category.Contains("JamaDocumentParserService", StringComparison.OrdinalIgnoreCase) ||
                                category.Contains("DirectRagService", StringComparison.OrdinalIgnoreCase) ||
                                category.Contains("DocumentRequirementExtractionService", StringComparison.OrdinalIgnoreCase) ||
                                category.Contains("SystemCapabilityDerivationService", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }

                        return false;
                    });
                    
                    // Add only basic providers that don't require special permissions
                    logging.AddDebug();
                    logging.AddConsole();
                    
                    // Add file logging with robust error handling
                    try
                    {
                        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        var logs = System.IO.Path.Combine(userProfile, "TestCaseEditorApp", "logs");
                        
                        // Ensure directory exists and is writable
                        if (!System.IO.Directory.Exists(logs))
                        {
                            System.IO.Directory.CreateDirectory(logs);
                        }

                        // Keep bounded session history to avoid unbounded disk growth.
                        const int maxSessionLogFiles = 20;
                        var existingSessionLogs = new System.IO.DirectoryInfo(logs)
                            .GetFiles("app-session-*.log", System.IO.SearchOption.TopDirectoryOnly);
                        if (existingSessionLogs.Length > maxSessionLogFiles)
                        {
                            foreach (var stale in existingSessionLogs
                                .OrderByDescending(f => f.LastWriteTimeUtc)
                                .Skip(maxSessionLogFiles))
                            {
                                try { stale.Delete(); } catch { }
                            }
                        }
                        
                        // Test write permissions
                        var testFile = System.IO.Path.Combine(logs, "test.tmp");
                        System.IO.File.WriteAllText(testFile, "test");
                        System.IO.File.Delete(testFile);
                        
                        // If we get here, directory is writable.
                        // Use a startup-scoped file so each app session has isolated runtime logs.
                        var sessionLogFileName = $"app-session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log";
                        _activeSessionLogPath = System.IO.Path.Combine(logs, sessionLogFileName);
                        logging.AddProvider(new TestCaseEditorApp.Services.Logging.FileLoggerProvider(logs, sessionLogFileName));
                    }
                    catch
                    {
                        // Silently skip file logging if there are permission issues
                        // App will still work with console/debug logging
                    }
                })
                .ConfigureServices((ctx, services) =>
                {
                    // Consolidated DI registration (reliability + consistency refactor).
                    services.AddCoreServices();
                    services.AddLLMServices();
                    services.AddCapabilityDerivationServices();
                    services.AddRequirementAnalysisServices();
                    services.AddTestCaseCreationServices();
                    services.AddTemplateFormServices();
                    services.AddTrainingDataValidationServices();
                    services.AddDomainMediators();
                    services.AddViewModels();
                    services.AddViewCoordinationServices();
                })
                .Build();

            try
            {
                await _host.StartAsync();

                // Load user settings and apply them to process environment before domain services run.
#pragma warning disable CA1416
                var userSettingsService = _host.Services.GetRequiredService<IUserSettingsService>();
                var startupSettings = userSettingsService.LoadSettings();
                userSettingsService.ApplySettingsToEnvironment(startupSettings);

                var themeService = _host.Services.GetRequiredService<ThemeService>();
                if (!string.IsNullOrWhiteSpace(startupSettings.ThemeName))
                {
                    themeService.SetTheme(startupSettings.ThemeName);
                }
#pragma warning restore CA1416
                
                // Ensure Ollama is running and healthy before any LLM operations
                var ollamaProcessManager = _host.Services.GetRequiredService<IOllamaProcessManager>();
                var logger = _host.Services.GetRequiredService<ILogger<App>>();
                
                try
                {
                    logger.LogInformation("Ensuring Ollama service is running...");
                    if (!string.IsNullOrWhiteSpace(_activeSessionLogPath))
                    {
                        logger.LogInformation("Runtime session log file: {SessionLogPath}", _activeSessionLogPath);
                    }
                    await ollamaProcessManager.EnsureOllamaRunningAsync();
                    logger.LogInformation("? Ollama service is ready");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start Ollama - LLM features may not work properly");
                }
                
                // Ollama status monitoring starts lazily when LLM sections (like Test Case Generator) are first accessed.
                
                // Initialize extension system (Phase 7)
                var extensionManager = _host.Services.GetRequiredService<ExtensionManager>();
                
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

            try
            {
#pragma warning disable CA1416
                var userSettingsService = _host.Services.GetRequiredService<IUserSettingsService>();
                if (userSettingsService.HasMissingRequiredSettings())
                {
                    var settingsDialogService = _host.Services.GetRequiredService<ISettingsDialogService>();
                    settingsDialogService.ShowSettingsDialog(mainWindow, isRequired: true);
                }
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                var logger = _host.Services.GetService<ILogger<App>>();
                logger?.LogWarning(ex, "Unable to show first-run settings dialog");
            }
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

