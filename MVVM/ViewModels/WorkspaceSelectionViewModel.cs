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
            await EnsureServiceReadyAsync();
            if (IsServiceReady)
            {
                await LoadWorkspacesAsync();
            }
        }

        private async Task EnsureServiceReadyAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Checking AnythingLLM installation...";

                // Check if AnythingLLM is installed
                var (isInstalled, installPath, shortcutPath, installMessage) = AnythingLLMService.DetectInstallation();
                if (!isInstalled)
                {
                    _notificationService.ShowInfo(installMessage);
                    StatusMessage = "AnythingLLM not installed";
                    return;
                }

                // Check if API key is configured
                StatusMessage = "Checking API key...";
                var apiKey = AnythingLLMService.GetUserApiKey();
                if (string.IsNullOrEmpty(apiKey))
                {
                    StatusMessage = "API key required";
                    _notificationService.ShowWarning("API key is required to use AnythingLLM features.");
                    return;
                }

                // Start the service
                StatusMessage = "Starting AnythingLLM service...";
                await _anythingLLMService.EnsureServiceRunningAsync();

                IsServiceReady = true;
                StatusMessage = "Service ready";
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to initialize AnythingLLM service: {ex.Message}");
                StatusMessage = $"Initialization failed: {ex.Message}";
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

                var workspaces = await _anythingLLMService.GetWorkspacesAsync();
                if (workspaces != null)
                {
                    // Filter workspaces based on mode
                    var filteredWorkspaces = _mode == SelectionMode.SelectExisting 
                        ? workspaces.Where(w => w.HasLocalFile).ToList()  // Only show workspaces with local files
                        : workspaces;  // Show all workspaces for create mode
                    
                    foreach (var workspace in filteredWorkspaces)
                    {
                        Workspaces.Add(workspace);
                    }
                }

                var workspaceCount = workspaces?.Count ?? 0;
                var availableCount = Workspaces.Count;
                
                StatusMessage = _mode == SelectionMode.SelectExisting 
                    ? $"Found {availableCount} project(s) with local files (out of {workspaceCount} total)"
                    : "Enter a name for the new workspace:";
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to load workspaces: {ex.Message}");
                StatusMessage = "Failed to load workspaces";
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
            ((AsyncRelayCommand)SelectWorkspaceCommand).NotifyCanExecuteChanged();
        }

        partial void OnNewWorkspaceNameChanged(string value)
        {
            ((AsyncRelayCommand)CreateWorkspaceCommand).NotifyCanExecuteChanged();
            
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
            ((AsyncRelayCommand)SelectWorkspaceCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CreateWorkspaceCommand).NotifyCanExecuteChanged();
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