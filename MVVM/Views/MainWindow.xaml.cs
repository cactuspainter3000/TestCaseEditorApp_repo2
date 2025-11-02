using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class MainWindow : Window, IWindow
    {
        private readonly AppViewModel? _vm;
        private bool _disposed;

        // Parameterless ctor keeps the XAML designer happy. DataContext will be set at runtime via DI ctor.
        public MainWindow()
        {
            InitializeComponent();
            // Designer can render; don't set DataContext here for runtime.
        }

        // Main DI constructor
        public MainWindow(AppViewModel vm) : this()
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_vm is IDisposable disposableVm)
                {
                    disposableVm.Dispose();
                }
            }

            _disposed = true;
        }

        ~MainWindow() => Dispose(disposing: false);

        // Allow window dragging by clicking the outer border
        public void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // ignore drag exceptions (e.g., during startup)
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext; System.Diagnostics.Debug.WriteLine($"MainWindow DataContext = {vm?.GetType().FullName ?? "<null>"}");
            var props = vm?.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (props == null)
            {
                System.Diagnostics.Debug.WriteLine("No public properties on DataContext.");
                return;
            }

            foreach (var p in props)
            {
                var val = p.GetValue(vm);
                var typeName = p.PropertyType.FullName;
                string info = $"{p.Name} : {typeName}";
                // if it's enumerable, print a count
                if (val is System.Collections.IEnumerable enm && !(val is string))
                {
                    int c = 0;
                    foreach (var _ in enm) { c++; if (c > 100) break; } // avoid huge enumeration
                    info += $" (enumerable, count ~{c})";
                }
                else
                {
                    info += $" (value={(val == null ? "null" : val.GetType().FullName)})";
                }
                System.Diagnostics.Debug.WriteLine(info);
            }

            try
            {
                var headerVm = new WorkspaceHeaderViewModel(new WindowWrapper(this));

                // If DataContext (AppViewModel) exposes a property named "WorkspaceHeaderViewModel", set it.
                if (DataContext != null)
                {
                    var prop = DataContext.GetType().GetProperty("WorkspaceHeaderViewModel",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    if (prop != null && prop.CanWrite && prop.PropertyType.IsAssignableFrom(headerVm.GetType()))
                    {
                        prop.SetValue(DataContext, headerVm);
                        System.Diagnostics.Debug.WriteLine("Assigned WorkspaceHeaderViewModel into AppViewModel.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("AppViewModel has no writable WorkspaceHeaderViewModel property; will try view lookup.");
                        // fallthrough to view lookup below
                        var headerElement = this.FindName("WorkspaceHeaderView") as System.Windows.FrameworkElement;
                        if (headerElement != null)
                        {
                            headerElement.DataContext = headerVm;
                            System.Diagnostics.Debug.WriteLine("Set DataContext on named WorkspaceHeaderView element.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create/set WorkspaceHeaderViewModel: {ex}");
            }
        }

        public void ButtonMinimize_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.WindowState = WindowState.Minimized;
        }

        public void WindowStateButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.MainWindow.WindowState =
                Application.Current.MainWindow.WindowState != WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        public void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            // Dispose of managed resources here if you later add them.
        }
    }
}