using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

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

                    // Domain mediators
                    services.AddSingleton<ITestCaseGenerationMediator, TestCaseGenerationMediator>();

                    // ViewModels and header VM
                    services.AddTransient<TestCaseGenerator_VM>();
                    services.AddSingleton<WorkspaceHeaderViewModel>(); // workspace header shared instance
                    services.AddTransient<MainViewModel>();
                    services.AddTransient<NavigationViewModel>();

                    // Views / Windows
                    services.AddTransient<MainWindow>();

                    // Add any other services your app needs here...
                })
                .Build();

            try
            {
                await _host.StartAsync();
                
                // Mark domain mediators as registered for fail-fast validation
                var testCaseGenMediator = _host.Services.GetRequiredService<ITestCaseGenerationMediator>();
                testCaseGenMediator.MarkAsRegistered();
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