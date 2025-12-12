using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestCaseEditorApp.Controls;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class SideMenuView : UserControl
    {
        public SideMenuView()
        {
            InitializeComponent();
        }

        private void StepsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // close any open DropDownButton popup when selection changes
            DropDownManager.CloseOpenPopup();
        }

        private void MainContent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Get the data context (the step item)
            if (sender is FrameworkElement element && element.DataContext is StepDescriptor step)
            {
                // Check if this item has a file menu and is currently selected
                if (step.HasFileMenu)
                {
                    var listBoxItem = FindVisualParent<ListBoxItem>(element);
                    if (listBoxItem?.IsSelected == true)
                    {
                        // Item is already selected, toggle the dropdown
                        step.IsFileMenuExpanded = !step.IsFileMenuExpanded;
                        e.Handled = true; // Prevent the ListBox from handling the click
                    }
                }
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null)
                return null;

            if (parentObject is T parent)
                return parent;

            return FindVisualParent<T>(parentObject);
        }
    }
}