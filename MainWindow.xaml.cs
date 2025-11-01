using System;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.ViewModels;

namespace TestCaseEditorApp
{
    public partial class MainWindow : Window, IDisposable
    {
        private readonly AppViewModel? _vm;

        // Parameterless ctor keeps the XAML designer happy. DataContext will be set at runtime via DI ctor.
        public MainWindow()
        {
            InitializeComponent();
            // Designer can render; don't set DataContext here for runtime.
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
        }

        // Main DI constructor
        public MainWindow(AppViewModel vm)
        {
            InitializeComponent();

            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            DataContext = _vm;
        }

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