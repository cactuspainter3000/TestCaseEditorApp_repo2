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

        /// <summary>
        /// Whether to show workspace count in status message
        /// </summary>
        public bool ShouldShowWorkspaceCount => _mode == SelectionMode.SelectExisting && IsServiceReady;

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