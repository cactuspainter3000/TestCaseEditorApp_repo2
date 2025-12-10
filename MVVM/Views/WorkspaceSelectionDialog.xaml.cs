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

        private AnythingLLMService _anythingLLMService;
        private readonly DialogMode _mode;
        private bool _isServiceReady = false;

        public AnythingLLMService.Workspace? SelectedWorkspace { get; private set; }
        public bool WasCreated { get; private set; } = false;

        public WorkspaceSelectionDialog(DialogMode mode = DialogMode.CreateNew)
        {
            InitializeComponent();
            _mode = mode;
            _anythingLLMService = new AnythingLLMService();
            
            // Subscribe to status updates
            _anythingLLMService.StatusUpdated += OnServiceStatusUpdated;
            
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
            
            // Ensure AnythingLLM service is running when dialog loads
            this.Loaded += async (s, e) => await EnsureServiceReadyAsync();
        }

        private void OnServiceStatusUpdated(string status)
        {
            // Update UI on main thread
            Dispatcher.Invoke(() =>
            {
                // Update the prompt text to show status while service is starting
                if (!_isServiceReady)
                {
                    PromptTextBlock.Text = $"🔄 {status}";
                }
                TestCaseEditorApp.Services.Logging.Log.Info($"[DIALOG] Service status: {status}");
            });
        }
        
        private async Task EnsureServiceReadyAsync()
        {
            try
            {
                // Disable buttons while checking/starting service
                CreateButton.IsEnabled = false;
                CancelButton.IsEnabled = false;
                
                // First check if AnythingLLM is installed
                var (isInstalled, installPath, shortcutPath, installMessage) = AnythingLLMService.DetectInstallation();
                if (!isInstalled)
                {
                    MessageBox.Show(
                        installMessage,
                        "AnythingLLM Not Installed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    DialogResult = false;
                    Close();
                    return;
                }
                
                // Check if API key is configured
                var apiKey = AnythingLLMService.GetUserApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    OnServiceStatusUpdated("API key configuration required...");
                    
                    var apiKeyDialog = new ApiKeyConfigDialog()
                    {
                        Owner = this
                    };
                    
                    var result = apiKeyDialog.ShowDialog();
                    if (result != true || string.IsNullOrEmpty(apiKeyDialog.ApiKey))
                    {
                        MessageBox.Show(
                            "API key is required to use AnythingLLM features.",
                            "API Key Required",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        
                        DialogResult = false;
                        Close();
                        return;
                    }
                    
                    // Create new service instance with the configured API key
                    _anythingLLMService = new AnythingLLMService(apiKey: apiKeyDialog.ApiKey);
                    _anythingLLMService.StatusUpdated += OnServiceStatusUpdated;
                }
                
                // Check if service is already running
                if (await _anythingLLMService.IsServiceAvailableAsync())
                {
                    _isServiceReady = true;
                    OnServiceStatusUpdated("AnythingLLM service is ready!");
                    RestoreOriginalPromptText();
                    return;
                }
                
                // Try to start the service
                OnServiceStatusUpdated("Starting AnythingLLM service...");
                var (success, message) = await _anythingLLMService.EnsureServiceRunningAsync();
                
                if (success)
                {
                    _isServiceReady = true;
                    OnServiceStatusUpdated("AnythingLLM service is ready!");
                    RestoreOriginalPromptText();
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to start AnythingLLM service:\n\n{message}\n\n" +
                        "Please ensure AnythingLLM is installed and try again.",
                        "Service Startup Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    
                    // Allow user to continue anyway in case they want to start manually
                    RestoreOriginalPromptText();
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[DIALOG] Error ensuring service ready");
                MessageBox.Show(
                    $"Error checking AnythingLLM service: {ex.Message}",
                    "Service Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                RestoreOriginalPromptText();
            }
            finally
            {
                CreateButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }
        
        private void RestoreOriginalPromptText()
        {
            Dispatcher.Invoke(() =>
            {
                if (_mode == DialogMode.CreateNew)
                {
                    PromptTextBlock.Text = "Enter workspace name:";
                }
                else
                {
                    PromptTextBlock.Text = "Select a workspace:";
                }
            });
        }

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            // Ensure service is ready before proceeding
            if (!_isServiceReady)
            {
                MessageBox.Show(
                    "AnythingLLM service is not ready yet. Please wait for startup to complete.",
                    "Service Not Ready",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            
            if (_mode == DialogMode.CreateNew)
            {
                await HandleCreateNewWorkspaceAsync();
            }
            else
            {
                HandleSelectExistingWorkspaceAsync();
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

        private void HandleSelectExistingWorkspaceAsync()
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
                // Wait for service to be ready before loading workspaces
                if (!_isServiceReady)
                {
                    // Service might still be starting up, wait a bit
                    await Task.Delay(1000);
                    if (!_isServiceReady)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[DIALOG] Service not ready, skipping workspace load");
                        return;
                    }
                }
                
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