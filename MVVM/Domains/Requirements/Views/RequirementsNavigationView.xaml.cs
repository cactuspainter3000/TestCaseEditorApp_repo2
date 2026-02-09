using System.Windows.Controls;
using System.Windows;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using System.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    public partial class RequirementsNavigationView : UserControl
    {
        public RequirementsNavigationView()
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
            // DEPRECATED: NavigationViewModel dropdown functionality disabled after domain architecture refactor
            // Requirements domain now handles navigation internally
            // Close dropdown when window moves
            // if (DataContext is TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel vm && vm.RequirementsDropdown != null)
            // {
            //     vm.RequirementsDropdown.IsExpanded = false;
            // }
        }

        private void RequirementItem_Click(object sender, RoutedEventArgs e)
        {
            // DEPRECATED: NavigationViewModel dropdown functionality disabled after domain architecture refactor
            // Requirements domain now handles navigation internally
            // Close the popup after item selection
            // if (DataContext is TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel vm && vm.RequirementsDropdown != null)
            // {
            //     vm.RequirementsDropdown.IsExpanded = false;
            // }
        }
    }
}