using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TestCaseEditorApp.Controls;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;

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

        // ==================== Simple Accordion Handlers ====================

        private void AnalysisHeader_Click(object sender, RoutedEventArgs e)
        {
            // Get the data context (the step item) and toggle its analysis menu
            if (sender is FrameworkElement element && element.DataContext is StepDescriptor step)
            {
                bool wasExpanded = step.IsAnalysisMenuExpanded;
                
                // Close only the other accordion menus within this same step
                step.IsQuestionsMenuExpanded = false;
                step.IsAssumptionsMenuExpanded = false;
                
                // Then expand/collapse this menu
                step.IsAnalysisMenuExpanded = !wasExpanded;
            }
        }

        private void QuestionsHeader_Click(object sender, RoutedEventArgs e)
        {
            // Get the data context (the step item) and toggle its questions menu
            if (sender is FrameworkElement element && element.DataContext is StepDescriptor step)
            {
                bool wasExpanded = step.IsQuestionsMenuExpanded;
                
                // Close only the other accordion menus within this same step
                step.IsAnalysisMenuExpanded = false;
                step.IsAssumptionsMenuExpanded = false;
                
                // Then expand/collapse this menu
                step.IsQuestionsMenuExpanded = !wasExpanded;
            }
        }

        private void AssumptionsHeader_Click(object sender, RoutedEventArgs e)
        {
            // Get the data context (the step item) and toggle its assumptions menu
            if (sender is FrameworkElement element && element.DataContext is StepDescriptor step)
            {
                bool wasExpanded = step.IsAssumptionsMenuExpanded;
                
                // Close only the other accordion menus within this same step
                step.IsAnalysisMenuExpanded = false;
                step.IsQuestionsMenuExpanded = false;
                
                // Then expand/collapse this menu
                step.IsAssumptionsMenuExpanded = !wasExpanded;
            }
        }

        private void ToggleAccordionSection(string menuName, string chevronName)
        {
            try
            {
                // Find the container (Border) instead of the StackPanel
                var containerName = menuName.Replace("Menu", "MenuContainer");
                var targetContainer = FindElementByName(this, containerName) as Border;
                var targetChevron = FindElementByName(this, chevronName) as TextBlock;

                if (targetContainer == null || targetChevron == null) return;

                // First, close all other menus
                CloseAllMenusExcept(menuName);

                // Toggle the target menu using Height instead of MaxHeight
                bool isCurrentlyVisible = targetContainer.Height > 0 && !double.IsNaN(targetContainer.Height);
                targetContainer.Height = isCurrentlyVisible ? 0 : 120;
                targetContainer.MaxHeight = double.PositiveInfinity; // Remove MaxHeight constraint
                
                // Force layout update
                targetContainer.UpdateLayout();
                this.UpdateLayout();
                
                System.Windows.MessageBox.Show($"Set Height to: {targetContainer.Height}, Actual size: {targetContainer.ActualWidth}x{targetContainer.ActualHeight}", "Debug");
                
                // Rotate the chevron
                double newAngle = isCurrentlyVisible ? 0 : 180;
                targetChevron.RenderTransform = new RotateTransform(newAngle, 0.5, 0.5);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Debug Error");
            }
        }
        
        private FrameworkElement FindElementByName(DependencyObject parent, string name)
        {
            if (parent == null) return null;
            
            // Check if current element has the name we're looking for
            if (parent is FrameworkElement element && element.Name == name)
            {
                return element;
            }
            
            // Recursively search children
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var found = FindElementByName(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            
            return null;
        }

        private void CloseAllMenusExcept(string exceptMenuName)
        {
            var menuNames = new[] { "AnalysisMenu", "QuestionsMenu", "AssumptionsMenu" };
            var chevronNames = new[] { "AnalysisChevron", "QuestionsChevron", "AssumptionsChevron" };

            for (int i = 0; i < menuNames.Length; i++)
            {
                if (menuNames[i] != exceptMenuName)
                {
                    var containerName = menuNames[i].Replace("Menu", "MenuContainer");
                    var container = FindElementByName(this, containerName) as Border;
                    var chevron = FindElementByName(this, chevronNames[i]) as TextBlock;
                    
                    if (container != null) container.Height = 0;
                    if (chevron != null)
                        chevron.RenderTransform = new RotateTransform(0, 0.5, 0.5);
                }
            }
        }

        // ==================== Clarifying Questions Command Handlers ====================

        private void AskQuestions_Click(object sender, RoutedEventArgs e)
        {
            NavigateToQuestionsStepAndExecute(vm => 
            {
                if (vm.ClarifyCommand != null)
                    _ = vm.ClarifyCommand.ExecuteAsync(null);
            });
        }

        private void PasteFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            NavigateToQuestionsStepAndExecute(vm => vm.PasteQuestionsFromClipboardCommand?.Execute(null));
        }

        private void RegenerateQuestions_Click(object sender, RoutedEventArgs e)
        {
            NavigateToQuestionsStepAndExecute(vm => 
            {
                if (vm.RegenerateCommand is System.Windows.Input.ICommand cmd)
                    cmd.Execute(null);
            });
        }

        private void SkipQuestions_Click(object sender, RoutedEventArgs e)
        {
            NavigateToQuestionsStepAndExecute(vm => 
            {
                if (vm.SkipQuestionsCommand is System.Windows.Input.ICommand cmd)
                    cmd.Execute(null);
            });
        }

        // ==================== Verification Method Assumptions Command Handlers ====================

        private void ResetAssumptions_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAssumptionsStepAndExecute(vm => vm.ResetAssumptionsCommand?.Execute(null));
        }

        private void ClearPresetFilter_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAssumptionsStepAndExecute(vm => vm.ClearPresetFilterCommand?.Execute(null));
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            NavigateToAssumptionsStepAndExecute(vm => vm.SavePresetCommand?.Execute(null));
        }

        // ==================== Helper Methods ====================

        private void NavigateToQuestionsStepAndExecute(System.Action<TestCaseGenerator_QuestionsVM> action)
        {
            if (DataContext is MainViewModel mainVm && mainVm.QuestionsViewModel != null)
            {
                action(mainVm.QuestionsViewModel);
            }
        }

        private void NavigateToAssumptionsStepAndExecute(System.Action<TestCaseGenerator_AssumptionsVM> action)
        {
            if (DataContext is MainViewModel mainVm && mainVm.AssumptionsViewModel != null)
            {
                action(mainVm.AssumptionsViewModel);
            }
        }
    }
}