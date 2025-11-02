using System.Windows;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Small adapter that wraps a WPF Window so it can be consumed as IWindow
    public class WindowWrapper : IWindow
    {
        private readonly Window _window;

        public WindowWrapper(Window window)
        {
            _window = window;
        }

        public void Close() => _window.Close();

        // Use WPF's WindowState so the signature matches IWindow
        public WindowState WindowState
        {
            get => _window.WindowState;
            set => _window.WindowState = value;
        }
    }
}