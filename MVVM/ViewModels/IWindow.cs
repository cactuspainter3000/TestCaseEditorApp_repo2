using System.Windows;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Small testable abstraction for window operations used by WorkspaceHeaderViewModel.
    // Keep it intentionally tiny so unit tests can inject a fake implementation.
    public interface IWindow
    {
        void Close();
        WindowState WindowState { get; set; }   // Use WPF's WindowState enum so System.Windows.Window implements this directly
    }
}