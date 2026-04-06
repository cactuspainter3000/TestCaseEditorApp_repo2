using System;
using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class RequirementsView : UserControl
    {
        public RequirementsView()
        {
            InitializeComponent();
            Unloaded += RequirementsView_Unloaded;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Attach to window resize if any future sizing logic is needed
            var w = Window.GetWindow(this);
            if (w != null)
                w.SizeChanged += HostWindow_SizeChanged;
        }

        private void RequirementsView_Unloaded(object? sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null)
                w.SizeChanged -= HostWindow_SizeChanged;
        }

        private void HostWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // no-op: popup/description sizing moved to the workspace header
        }
    }
}