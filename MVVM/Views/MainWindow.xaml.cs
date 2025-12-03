using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Views
{
    /// <summary>
    /// Code-behind for MainWindow. Supports both designer-friendly parameterless construction
    /// and a DI constructor that accepts a fully-wired MainViewModel. Implements IWindow so
    /// header viewmodels can interact with the window via a lightweight abstraction.
    /// </summary>
    public partial class MainWindow : Window, IWindow, IDisposable
    {
        private readonly MainViewModel? _vm;
        private bool _disposed;

        // Parameterless ctor keeps the XAML designer happy. DataContext will be set at runtime via DI ctor.
        public MainWindow()
        {
            InitializeComponent();
            // Designer can render; don't set DataContext here for runtime.
        }

        // DI constructor that accepts the MainViewModel (ensures the runtime VM has its services wired)
        public MainWindow(MainViewModel vm) : this()
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
        }

        // Standard Dispose pattern
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

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~MainWindow() => Dispose(disposing: false);

        // Allow window dragging by clicking the outer border
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
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
            // Prefer DI-provided DataContext. Fall back to App.ServiceProvider if available, then to a parameterless VM.
            DataContext ??= App.ServiceProvider?.GetService(typeof(MainViewModel)) as MainViewModel
               ?? new MainViewModel();

            var vm = DataContext;
            TestCaseEditorApp.Services.Logging.Log.Debug($"MainWindow DataContext = {vm?.GetType().FullName ?? "<null>"}");
            if (vm == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("No DataContext for MainWindow.");
                return;
            }

            var props = vm.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var nonNullVm = vm; // local non-null alias for analyzer
            foreach (var p in props)
            {
                var val = p.GetValue(nonNullVm);
                var typeName = p.PropertyType.FullName;
                string info = $"{p.Name} : {typeName}";
                // if it's enumerable, print a count (best-effort)
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
                TestCaseEditorApp.Services.Logging.Log.Debug(info);
            }

            // Try to provide a Window wrapper to any header VM that expects it.
            try
            {
                var wrapper = new WindowWrapper(this);

                // If DataContext exposes a property named "WorkspaceHeaderViewModel", prefer to set its window
                if (DataContext != null)
                {
                    var prop = DataContext.GetType().GetProperty("WorkspaceHeaderViewModel",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (prop != null)
                    {
                        var headerVm = prop.GetValue(DataContext);
                        if (headerVm != null)
                        {
                            // Try a strongly-typed SetWindow method first
                            var setWin = headerVm.GetType().GetMethod("SetWindow",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (setWin != null)
                            {
                                // Call SetWindow with our wrapper
                                setWin.Invoke(headerVm, new object[] { wrapper });
                                TestCaseEditorApp.Services.Logging.Log.Debug("Called SetWindow on injected WorkspaceHeaderViewModel.");
                            }
                            else
                            {
                                // Fallback: if the headerVm exposes a property "Window" that is settable, assign it
                                var winProp = headerVm.GetType().GetProperty("Window",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (winProp != null && winProp.CanWrite && winProp.PropertyType.IsAssignableFrom(typeof(IWindow)))
                                {
                                    winProp.SetValue(headerVm, wrapper);
                                    TestCaseEditorApp.Services.Logging.Log.Debug("Assigned Window property on injected WorkspaceHeaderViewModel.");
                                }
                                else
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Debug("Injected WorkspaceHeaderViewModel has no SetWindow/Window setter; creating and assigning a new header VM.");
                                    // Create and set a new header VM with window
                                    var newHeaderVm = new WorkspaceHeaderViewModel();
                                    try { newHeaderVm.SetWindow(wrapper); } catch { /* best-effort */ }

                                    // Try to assign into DataContext property if possible
                                    if (prop.CanWrite && prop.PropertyType.IsAssignableFrom(newHeaderVm.GetType()))
                                    {
                                        prop.SetValue(DataContext, newHeaderVm);
                                        TestCaseEditorApp.Services.Logging.Log.Debug("Assigned new WorkspaceHeaderViewModel into DataContext.");
                                    }
                                    else
                                    {
                                        var headerElement = this.FindName("WorkspaceHeaderView") as FrameworkElement;
                                        if (headerElement != null)
                                        {
                                            headerElement.DataContext = newHeaderVm;
                                            TestCaseEditorApp.Services.Logging.Log.Debug("Set DataContext on named WorkspaceHeaderView element with newly created WorkspaceHeaderViewModel.");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // No injected header VM present — create one, set its window and assign to DataContext if possible
                            var newHeaderVm = new WorkspaceHeaderViewModel();
                            try { newHeaderVm.SetWindow(wrapper); } catch { /* best-effort */ }
                            if (prop.CanWrite && prop.PropertyType.IsAssignableFrom(newHeaderVm.GetType()))
                            {
                                prop.SetValue(DataContext, newHeaderVm);
                                    TestCaseEditorApp.Services.Logging.Log.Debug("Assigned new WorkspaceHeaderViewModel into DataContext.");
                            }
                            else
                            {
                                var headerElement = this.FindName("WorkspaceHeaderView") as FrameworkElement;
                                if (headerElement != null)
                                {
                                    headerElement.DataContext = newHeaderVm;
                                    TestCaseEditorApp.Services.Logging.Log.Debug("Set DataContext on named WorkspaceHeaderView element.");
                                }
                            }
                        }
                    }
                    else
                    {
                        // No WorkspaceHeaderViewModel property on DataContext: fallback as before
                        var headerElement = this.FindName("WorkspaceHeaderView") as FrameworkElement;
                        if (headerElement != null)
                        {
                            var newHeaderVm = new WorkspaceHeaderViewModel();
                            try { newHeaderVm.SetWindow(wrapper); } catch { /* best-effort */ }
                            headerElement.DataContext = newHeaderVm;
                            TestCaseEditorApp.Services.Logging.Log.Debug("No WorkspaceHeaderViewModel on DataContext — set DataContext on named WorkspaceHeaderView element.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"Failed to create/set WorkspaceHeaderViewModel: {ex}");
            }

            // Command presence probe (keeps previous diagnostic)
            try
            {
                string[] commandNames = new[] { "ImportWordCommand", "ImportRequirementsCommand", "ImportWordAsyncCommand", "ImportCommand", "Import" };
                TestCaseEditorApp.Services.CommandInspector.LogCommandPresence(vm, commandNames);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[CMD CHECK] Exception: " + ex);
            }
        }

        public void ButtonMinimize_Click(object sender, RoutedEventArgs e)
        {
            var app = Application.Current;
            if (app?.MainWindow != null)
                app.MainWindow.WindowState = WindowState.Minimized;
        }

        public void WindowStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current?.MainWindow != null)
                Application.Current.MainWindow.WindowState =
                    Application.Current.MainWindow.WindowState != WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        public void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #region IWindow implementation (wrapper for header VMs that expect an IWindow)

        // These members allow header VMs to interact with the window without a direct Window dependency.
        WindowState IWindow.WindowState
        {
            get => this.WindowState;
            set => this.WindowState = value;
        }

        void IWindow.Close()
        {
            this.Close();
        }

        #endregion

        /// <summary>
        /// A lightweight wrapper that implements IWindow around a WPF Window instance.
        /// Used to pass a stable abstraction into ViewModels that require window operations.
        /// </summary>
        private sealed class WindowWrapper : IWindow
        {
            private readonly Window _w;
            public WindowWrapper(Window w) => _w = w ?? throw new ArgumentNullException(nameof(w));
            public WindowState WindowState
            {
                get => _w.WindowState;
                set => _w.WindowState = value;
            }
            public void Close() => _w.Close();
        }
    }
}