using System.Windows.Controls;
using System.Windows;

namespace TestCaseEditorApp.MVVM.Domains.Startup.Views
{
    public partial class StartUp_MainView : UserControl
    {
        public StartUp_MainView()
        {
            InitializeComponent();
        }
        
        private void ToggleOriginalContent_Click(object sender, RoutedEventArgs e)
        {
            JamaTroubleshootingGrid.Visibility = Visibility.Collapsed;
            OriginalContentGrid.Visibility = Visibility.Visible;
        }
        
        private void ToggleTroubleshooting_Click(object sender, RoutedEventArgs e)
        {
            OriginalContentGrid.Visibility = Visibility.Collapsed;
            JamaTroubleshootingGrid.Visibility = Visibility.Visible;
        }
        
        private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Auto-scroll to bottom when new log entries are added
            LogScrollViewer.ScrollToEnd();
        }
    }
}