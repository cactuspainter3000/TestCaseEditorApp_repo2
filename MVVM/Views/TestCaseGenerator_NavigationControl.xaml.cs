using System.Windows.Controls;
using System.Windows;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class TestCaseGenerator_NavigationControl : UserControl
    {
        public TestCaseGenerator_NavigationControl()
        {
            InitializeComponent();
        }

        private void RequirementItem_Click(object sender, RoutedEventArgs e)
        {
            // Close the popup after item selection
            if (DataContext is TestCaseGenerator_NavigationVM vm && vm.RequirementsDropdown != null)
            {
                vm.RequirementsDropdown.IsExpanded = false;
            }
        }
    }
}