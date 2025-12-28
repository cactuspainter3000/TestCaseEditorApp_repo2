using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Views
{
    /// <summary>
    /// Interaction logic for NewSideMenuView.xaml
    /// </summary>
    public partial class NewSideMenuView : UserControl
    {
        public NewSideMenuView()
        {
            InitializeComponent();
        }
        
        private async void TestAnythingLLM_Click(object sender, RoutedEventArgs e)
        {
            var service = App.ServiceProvider?.GetService<TestCaseAnythingLLMService>();
            if (service != null)
            {
                await service.ConnectAsync();
            }
            else
            {
                MessageBox.Show("Service not available", "Error");
            }
        }
    }
}