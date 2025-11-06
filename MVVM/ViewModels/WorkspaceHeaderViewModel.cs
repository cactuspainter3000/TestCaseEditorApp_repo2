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

        // Existing (keeps Title / Badge for legacy uses)
        [ObservableProperty] private string title = "Workspace";
        [ObservableProperty] private object? badge;

        // Properties expected by WorkspaceHeaderView.xaml
        [ObservableProperty] private string? workspaceName;
        [ObservableProperty] private string? sourceInfo;

        [ObservableProperty] private string? currentRequirementTitle;
        [ObservableProperty] private string? currentRequirementSummary;
        [ObservableProperty] private string? currentRequirementId;
        [ObservableProperty] private string? currentRequirementStatus;

        [ObservableProperty] private bool hasUnsavedChanges;

        // Commands: main VM may assign its own IRelayCommand instances here
        // (keep these as settable so the MainViewModel can wire them)
        public IRelayCommand? EditRequirementCommand { get; set; }
        public IRelayCommand? OpenRequirementCommand { get; set; }

        // Window controls (generated RelayCommand methods)
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