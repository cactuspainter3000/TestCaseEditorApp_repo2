using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Windows;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.Services;           // for RequirementService, IFileDialogService, etc.

namespace TestCaseEditorApp
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            // Core services
            services.AddSingleton<IPersistenceService, JsonPersistenceService>();

            // Requirement parsing
            services.AddSingleton<IRequirementService, RequirementService>();

            // File dialog helper used by the VM
            services.AddSingleton<IFileDialogService, FileDialogService>();

            // ViewModels
            services.AddTransient<RequirementsViewModel>();

            // Register the WorkspaceHeaderViewModel so DI can construct it and inject its dependencies
            services.AddTransient<WorkspaceHeaderViewModel>();

            // MainViewModel remains transient (or change lifetime as needed)
            services.AddTransient<MainViewModel>();

            services.AddTransient<NavigationViewModel>();

            // Register the window and expose it via IWindow if WorkspaceHeaderViewModel (or others) need it.
            // This registers MainWindow as the implementation for IWindow and also registers MainWindow itself for direct resolution.
            services.AddSingleton<IWindow, MainWindow>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

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
                Shutdown(-1);
                return;
            }

            // Resolve and show window
            var main = _serviceProvider.GetRequiredService<MainWindow>();

            // Provide the MainViewModel from DI as DataContext for the window
            // In App.xaml.cs, when resolving MainViewModel from DI
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            main.DataContext = mainVm;

            Current.MainWindow = main;
            main.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            main.ShowInTaskbar = true;
            main.WindowState = WindowState.Normal;
            main.Show();
            main.Activate();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }
}