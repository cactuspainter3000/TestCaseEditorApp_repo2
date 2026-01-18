using System.Windows.Controls;
using System.Windows;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using System.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.Views
{
    public partial class TestCaseGenerator_NavigationControl : UserControl
    {
        public TestCaseGenerator_NavigationControl()
        {
            InitializeComponent();
            
            // Subscribe to window location changes to close dropdown when window moves
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged += OnWindowLocationChanged;
            }
        }
        
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged -= OnWindowLocationChanged;
            }
        }
        
        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            // Close dropdown when window moves
            if (DataContext is TestCaseGenerator_NavigationVM vm && vm.RequirementsDropdown != null)
            {
                vm.RequirementsDropdown.IsExpanded = false;
            }
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