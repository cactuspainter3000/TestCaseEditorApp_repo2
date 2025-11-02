using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class WorkspaceHeaderViewModel : ObservableObject
    {
        private readonly IWindow? _window;

        public WorkspaceHeaderViewModel(IWindow? window = null)
        {
            _window = window;
        }

        [ObservableProperty]
        private string title = "Workspace";

        [ObservableProperty]
        private object? badge;

        [RelayCommand]
        private void Close()
        {
            if (_window != null) { _window.Close(); return; }
            var appWin = System.Windows.Application.Current?.MainWindow;
            appWin?.Close();
        }

        [RelayCommand]
        private void Minimize()
        {
            if (_window != null) { _window.WindowState = WindowState.Minimized; return; }
            var appWin = System.Windows.Application.Current?.MainWindow;
            if (appWin != null) appWin.WindowState = System.Windows.WindowState.Minimized;
        }

        [RelayCommand]
        private void MaximizeRestore()
        {
            if (_window != null)
            {
                _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            var appWin = System.Windows.Application.Current?.MainWindow;
            if (appWin != null)
            {
                appWin.WindowState = appWin.WindowState == System.Windows.WindowState.Maximized
                    ? System.Windows.WindowState.Normal
                    : System.Windows.WindowState.Maximized;
            }
        }
    }
}