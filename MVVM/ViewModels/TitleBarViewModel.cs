using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class TitleBarViewModel : ObservableObject
    {
        private IWindow? _window;

        public void SetWindow(IWindow? window) => _window = window;

        // Close command
        [RelayCommand]
        private void Close()
        {
            try
            {
                if (_window != null) { _window.Close(); return; }
                Application.Current?.MainWindow?.Close();
            }
            catch { /* best-effort */ }
        }

        // Minimize command
        [RelayCommand]
        private void Minimize()
        {
            try
            {
                if (_window != null) { _window.WindowState = WindowState.Minimized; return; }
                var win = Application.Current?.MainWindow;
                if (win != null) win.WindowState = WindowState.Minimized;
            }
            catch { /* best-effort */ }
        }

        // Maximize / Restore command
        [RelayCommand]
        private void MaximizeRestore()
        {
            try
            {
                if (_window != null)
                {
                    _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return;
                }
                var win = Application.Current?.MainWindow;
                if (win != null) win.WindowState = win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            catch { /* best-effort */ }
        }
    }
}