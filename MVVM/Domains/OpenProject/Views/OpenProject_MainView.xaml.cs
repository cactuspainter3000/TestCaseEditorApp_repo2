using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.Views
{
    /// <summary>
    /// Interaction logic for OpenProject_MainView.xaml
    /// </summary>
    public partial class OpenProject_MainView : UserControl
    {
        public OpenProject_MainView()
        {
            InitializeComponent();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Navigate back - this could be handled through command binding if needed
            // For now, just a placeholder for potential navigation logic
        }
    }
}