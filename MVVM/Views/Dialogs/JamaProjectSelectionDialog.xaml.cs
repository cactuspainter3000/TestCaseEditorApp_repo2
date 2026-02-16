using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Views.Dialogs
{
    /// <summary>
    /// Dialog for selecting target Jama project for test case import
    /// </summary>
    public partial class JamaProjectSelectionDialog : Window
    {
        public JamaProject? SelectedProject { get; private set; }

        public JamaProjectSelectionDialog(List<JamaProject> projects)
        {
            InitializeComponent();
            
            // Populate the project list
            ProjectListBox.ItemsSource = projects?.OrderBy(p => p.ProjectKey).ToList() ?? new List<JamaProject>();
            
            // Auto-select if only one project
            if (projects?.Count == 1)
            {
                ProjectListBox.SelectedIndex = 0;
                SelectButton.IsEnabled = true;
            }
        }

        private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = ProjectListBox.SelectedItem != null;
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectListBox.SelectedItem is JamaProject selectedProject)
            {
                SelectedProject = selectedProject;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a project to continue.", "No Project Selected", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedProject = null;
            DialogResult = false;
            Close();
        }
    }
}