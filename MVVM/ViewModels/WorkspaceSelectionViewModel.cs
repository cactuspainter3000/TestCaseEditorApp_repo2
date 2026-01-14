using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for AnythingLLM workspace selection and creation.
    /// Designed to work within the modal overlay system.
    /// Part of shared infrastructure - used by both project creation and project opening workflows.
    /// </summary>
    public partial class WorkspaceSelectionViewModel : ObservableObject
    {
        public enum SelectionMode
        {
            CreateNew,
            SelectExisting
        }

        private readonly AnythingLLMService _anythingLLMService;
        private readonly NotificationService _notificationService;
        private readonly SelectionMode _mode;

        [ObservableProperty]
        private ObservableCollection<AnythingLLMService.Workspace> _workspaces = new();

        [ObservableProperty]
        private AnythingLLMService.Workspace? _selectedWorkspace;

        [ObservableProperty]
        private string _newWorkspaceName = string.Empty;

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private bool _isServiceReady = false;

        [ObservableProperty]
        private bool _isCreateMode = false;

        [ObservableProperty]
        private bool _showDuplicateNameOptions = false;

        [ObservableProperty]
        private string _duplicateWorkspaceName = string.Empty;

        /// <summary>
        /// Whether to show workspace count in status message
        /// </summary>
        public bool ShouldShowWorkspaceCount => _mode == SelectionMode.SelectExisting && IsServiceReady;

        /// <summary>
        /// Whether to show the refresh button (only useful when selecting existing workspaces)
        /// </summary>
        public bool ShouldShowRefreshButton => !IsCreateMode && !ShowDuplicateNameOptions;

        /// <summary>
        /// Called when IsCreateMode changes to update dependent properties
        /// </summary>
        partial void OnIsCreateModeChanged(bool value)
        {
            OnPropertyChanged(nameof(ShouldShowRefreshButton));
        }

        /// <summary>
        /// Called when ShowDuplicateNameOptions changes to update dependent properties
        /// </summary>
        partial void OnShowDuplicateNameOptionsChanged(bool value)
        {
            OnPropertyChanged(nameof(ShouldShowRefreshButton));
        }

        /// <summary>
        /// Event raised when a workspace is selected or created successfully
        /// </summary>
        public event EventHandler<WorkspaceSelectedEventArgs>? WorkspaceSelected;

        /// <summary>
        /// Event raised when the user cancels the operation
        /// </summary>
        public event EventHandler? Cancelled;

        public ICommand SelectWorkspaceCommand { get; }
        public ICommand CreateWorkspaceCommand { get; }
        public ICommand ToggleCreateModeCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenExistingWorkspaceCommand { get; }
        public ICommand CreateWithNewNameCommand { get; }

        public WorkspaceSelectionViewModel(AnythingLLMService anythingLLMService, NotificationService notificationService, SelectionMode mode = SelectionMode.CreateNew)
        {
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _mode = mode;

            // Set initial state based on mode
            IsCreateMode = (_mode == SelectionMode.CreateNew);
            StatusMessage = (_mode == SelectionMode.CreateNew) 
                ? "Enter a name for the new workspace:" 
                : "Loading workspaces...";

            // Initialize commands
            SelectWorkspaceCommand = new AsyncRelayCommand(SelectWorkspaceAsync, () => SelectedWorkspace != null && !IsLoading);
            CreateWorkspaceCommand = new AsyncRelayCommand(CreateWorkspaceAsync, () => !string.IsNullOrEmpty(NewWorkspaceName) && !IsLoading);
            ToggleCreateModeCommand = new RelayCommand(() => IsCreateMode = !IsCreateMode, () => _mode == SelectionMode.CreateNew);
            CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
            RefreshCommand = new AsyncRelayCommand(LoadWorkspacesAsync);
            OpenExistingWorkspaceCommand = new AsyncRelayCommand(OpenExistingDuplicateWorkspaceAsync);
            CreateWithNewNameCommand = new RelayCommand(CreateWithNewName);

            // Start initialization
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            TestCaseEditorApp.Services.Logging.Log.Info("[WorkspaceSelection] InitializeAsync started");
            await EnsureServiceReadyAsync();
            TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceSelection] Service ready: {IsServiceReady}");
            if (IsServiceReady)
            {
                await LoadWorkspacesAsync();
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Warn("[WorkspaceSelection] Service not ready, skipping LoadWorkspacesAsync");
            }
        }

        private async Task EnsureServiceReadyAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Checking AnythingLLM installation...";
                TestCaseEditorApp.Services.Logging.Log.Info("[WorkspaceSelection] EnsureServiceReadyAsync started");

                // Check if AnythingLLM is installed
                var (isInstalled, installPath, shortcutPath, installMessage) = AnythingLLMService.DetectInstallation();
                TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceSelection] AnythingLLM installed: {isInstalled}");
                if (!isInstalled)
                {
                    _notificationService.ShowInfo(installMessage);
                    StatusMessage = "AnythingLLM not installed";
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[WorkspaceSelection] {installMessage}");
                    return;
                }

                // Check if API key is configured
                StatusMessage = "Checking API key...";
                var apiKey = AnythingLLMService.GetUserApiKey();
                TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceSelection] API key configured: {!string.IsNullOrEmpty(apiKey)}");
                if (string.IsNullOrEmpty(apiKey))
                {
                    StatusMessage = "API key required - attempting to load workspaces anyway";
                    _notificationService.ShowWarning("API key is required for full functionality, but attempting to load workspaces.");
                    TestCaseEditorApp.Services.Logging.Log.Warn("[WorkspaceSelection] API key missing, but continuing");
                }

                // Try to start the service - but don't fail if it doesn't work
                StatusMessage = "Starting AnythingLLM service...";
                TestCaseEditorApp.Services.Logging.Log.Info("[WorkspaceSelection] Starting AnythingLLM service");
                try
                {
                    await _anythingLLMService.EnsureServiceRunningAsync();
                }
                catch (Exception serviceEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[WorkspaceSelection] Service startup failed but continuing: {serviceEx.Message}");
                }

                IsServiceReady = true;
                StatusMessage = "Service ready";
                TestCaseEditorApp.Services.Logging.Log.Info("[WorkspaceSelection] Service ready");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to initialize AnythingLLM service: {ex.Message}");
                StatusMessage = $"Initialization failed: {ex.Message}";
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[WorkspaceSelection] Failed to initialize AnythingLLM service");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadWorkspacesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading workspaces...";
                Workspaces.Clear();

                TestCaseEditorApp.Services.Logging.Log.Info("[WorkspaceSelection] Starting LoadWorkspacesAsync");
                
                var workspaces = await _anythingLLMService.GetWorkspacesAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceSelection] GetWorkspacesAsync returned {workspaces?.Count ?? 0} workspaces");
                
                if (workspaces != null)
                {
                    // Show all workspaces regardless of mode
                    // Users should be able to select any AnythingLLM workspace
                    foreach (var workspace in workspaces)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceSelection] Adding workspace: {workspace.Name} (Slug: {workspace.Slug})");
                        Workspaces.Add(workspace);
                    }
                }

                var workspaceCount = workspaces?.Count ?? 0;
                var availableCount = Workspaces.Count;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceSelection] Final workspace count: {availableCount}");
                
                StatusMessage = _mode == SelectionMode.SelectExisting 
                    ? ""  // No status message for existing workspace selection
                    : "Enter a name for the new workspace:";
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[WorkspaceSelection] LoadWorkspacesAsync failed");
                var errorMessage = ex.Message;
                
                // Provide more helpful error messages based on exception type
                if (ex.Message.Contains("All API endpoints failed"))
                {
                    errorMessage = "Cannot connect to AnythingLLM. Please ensure it's running on http://localhost:3001";
                }
                else if (ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
                {
                    errorMessage = "Authentication failed. Please check your API key configuration.";
                }
                
                _notificationService.ShowError($"Failed to load workspaces: {errorMessage}");
                StatusMessage = $"Failed to load workspaces: {errorMessage}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SelectWorkspaceAsync()
        {
            if (SelectedWorkspace == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Opening project: {SelectedWorkspace.Name}...";

                // Add a small delay to show the loading state
                await Task.Delay(500);
                
                StatusMessage = "Loading project data...";
                await Task.Delay(300);

                // Notify success
                WorkspaceSelected?.Invoke(this, new WorkspaceSelectedEventArgs(SelectedWorkspace.Name, false));
                
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to select workspace: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CreateWorkspaceAsync()
        {
            if (string.IsNullOrEmpty(NewWorkspaceName))
                return;

            try
            {
                IsLoading = true;
                
                // First check if workspace name already exists
                StatusMessage = $"Checking workspace name: {NewWorkspaceName}";
                
                var nameExists = await _anythingLLMService.WorkspaceNameExistsAsync(NewWorkspaceName);
                if (nameExists)
                {
                    // Show duplicate name options instead of just an error
                    ShowDuplicateNameOptions = true;
                    DuplicateWorkspaceName = NewWorkspaceName;
                    
                    // Find and pre-select the existing workspace
                    var existingWorkspace = Workspaces.FirstOrDefault(w => string.Equals(w.Name, NewWorkspaceName, StringComparison.OrdinalIgnoreCase));
                    if (existingWorkspace != null)
                    {
                        SelectedWorkspace = existingWorkspace;
                    }
                    
                    StatusMessage = $"⚠️ A workspace named '{NewWorkspaceName}' already exists. What would you like to do?";
                    return;
                }
                
                StatusMessage = $"Creating workspace: {NewWorkspaceName}";

                // Create the workspace
                var createdWorkspace = await _anythingLLMService.CreateWorkspaceAsync(NewWorkspaceName);
                if (createdWorkspace == null)
                {
                    _notificationService.ShowError("Failed to create workspace");
                    return;
                }

                // Refresh the list
                await LoadWorkspacesAsync();

                // Select the new workspace
                SelectedWorkspace = createdWorkspace;

                // Notify success
                WorkspaceSelected?.Invoke(this, new WorkspaceSelectedEventArgs(NewWorkspaceName, true));

            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to create workspace: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                NewWorkspaceName = string.Empty;
                IsCreateMode = false;
            }
        }

        partial void OnSelectedWorkspaceChanged(AnythingLLMService.Workspace? value)
        {
            // ObservableProperty automatically notifies command CanExecute changes
        }

        partial void OnNewWorkspaceNameChanged(string value)
        {
            // ObservableProperty automatically notifies command CanExecute changes
            
            // Clear any previous error messages when user starts typing a new name
            if (!string.IsNullOrEmpty(value) && (StatusMessage.Contains("already exists") || ShowDuplicateNameOptions))
            {
                StatusMessage = "Enter a name for the new workspace:";
                ShowDuplicateNameOptions = false;
                DuplicateWorkspaceName = string.Empty;
            }
        }

        /// <summary>
        /// Opens the existing workspace that has the same name
        /// </summary>
        private async Task OpenExistingDuplicateWorkspaceAsync()
        {
            if (SelectedWorkspace == null) return;
            
            try
            {
                IsLoading = true;
                StatusMessage = $"Opening existing workspace: {SelectedWorkspace.Name}";
                
                await Task.Delay(500); // Brief delay for UX
                
                // Notify that we're selecting the existing workspace
                WorkspaceSelected?.Invoke(this, new WorkspaceSelectedEventArgs(SelectedWorkspace.Name, false));
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to open workspace: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        /// <summary>
        /// Clears the duplicate name scenario and lets user enter a new name
        /// </summary>
        private void CreateWithNewName()
        {
            ShowDuplicateNameOptions = false;
            DuplicateWorkspaceName = string.Empty;
            NewWorkspaceName = string.Empty;
            SelectedWorkspace = null;
            StatusMessage = "Enter a name for the new workspace:";
        }

        partial void OnIsLoadingChanged(bool value)
        {
            // ObservableProperty automatically notifies command CanExecute changes
        }
    }

    public class WorkspaceSelectedEventArgs : EventArgs
    {
        public string WorkspaceName { get; }
        public bool WasCreated { get; }

        public WorkspaceSelectedEventArgs(string workspaceName, bool wasCreated)
        {
            WorkspaceName = workspaceName;
            WasCreated = wasCreated;
        }
    }
}