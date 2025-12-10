using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class WorkspaceSelectionDialog : Window
    {
        public enum DialogMode
        {
            CreateNew,
            SelectExisting
        }

        private readonly AnythingLLMService _anythingLLMService;
        private readonly DialogMode _mode;

        public AnythingLLMService.Workspace? SelectedWorkspace { get; private set; }
        public bool WasCreated { get; private set; } = false;

        public WorkspaceSelectionDialog(DialogMode mode = DialogMode.CreateNew)
        {
            InitializeComponent();
            _mode = mode;
            _anythingLLMService = new AnythingLLMService();
            
            // Debug: Check if API key is configured
            var apiKeyStatus = _anythingLLMService.GetConfigurationStatus();
            TestCaseEditorApp.Services.Logging.Log.Info($"[DIALOG] AnythingLLM configuration: {apiKeyStatus}");
            
            // Configure UI based on mode
            if (_mode == DialogMode.CreateNew)
            {
                Title = "Create New AnythingLLM Workspace";
                TitleTextBlock.Text = "Create New Workspace";
                PromptTextBlock.Text = "Enter workspace name:";
                NewWorkspaceNameTextBox.Visibility = Visibility.Visible;
                WorkspaceScrollViewer.Visibility = Visibility.Collapsed;
                NewWorkspaceNameTextBox.Focus();
                CreateButton.Content = "Create";
            }
            else
            {
                Title = "Select Existing AnythingLLM Workspace";
                TitleTextBlock.Text = "Select Existing Workspace";
                PromptTextBlock.Text = "Select a workspace:";
                NewWorkspaceNameTextBox.Visibility = Visibility.Collapsed;
                WorkspaceScrollViewer.Visibility = Visibility.Visible;
                CreateButton.Content = "Select";
                
                // Load workspaces when dialog is shown
                this.Loaded += async (s, e) => await LoadWorkspacesIntoListAsync();
            }
        }

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == DialogMode.CreateNew)
            {
                await HandleCreateNewWorkspaceAsync();
            }
            else
            {
                await HandleSelectExistingWorkspaceAsync();
            }
        }

        private async Task HandleCreateNewWorkspaceAsync()
        {
            var workspaceName = NewWorkspaceNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(workspaceName))
            {
                MessageBox.Show("Please enter a workspace name.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                CreateButton.IsEnabled = false;
                CreateButton.Content = "Creating...";

                // Create workspace via cloud API
                var newWorkspace = await _anythingLLMService.CreateWorkspaceAsync(workspaceName);
                if (newWorkspace != null)
                {
                    SelectedWorkspace = newWorkspace;
                    WasCreated = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show(
                        "Failed to create workspace. Please try again.",
                        "Creation Failed", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating workspace: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CreateButton.IsEnabled = true;
                CreateButton.Content = "Create";
            }
        }

        private async Task HandleSelectExistingWorkspaceAsync()
        {
            var selectedWorkspace = WorkspacesListBox.SelectedItem as AnythingLLMService.Workspace;
            if (selectedWorkspace == null)
            {
                MessageBox.Show("Please select a workspace.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedWorkspace = selectedWorkspace;
            WasCreated = false;
            DialogResult = true;
            Close();
        }

        private async Task LoadWorkspacesIntoListAsync()
        {
            try
            {
                CreateButton.IsEnabled = false;
                CreateButton.Content = "Loading...";

                // Get list of available workspaces via API
                var workspaces = await _anythingLLMService.GetWorkspacesAsync();
                if (workspaces == null || !workspaces.Any())
                {
                    MessageBox.Show(
                        "No workspaces found.\n\n" +
                        "Please create a workspace first using 'Create New Project'.",
                        "No Workspaces",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    DialogResult = false;
                    Close();
                    return;
                }

                // Populate the list box
                WorkspacesListBox.ItemsSource = workspaces;
                if (workspaces.Count == 1)
                {
                    WorkspacesListBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading workspaces: {ex.Message}\n\n" +
                    "Please check your API key configuration.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
            finally
            {
                CreateButton.IsEnabled = true;
                CreateButton.Content = "Select";
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}