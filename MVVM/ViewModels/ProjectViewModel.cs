using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Dedicated ViewModel for project and workspace management.
    /// Enhanced from basic implementation to handle full project lifecycle.
    /// </summary>
    public partial class ProjectViewModel : ObservableObject
    {
        // Dependencies  
        private readonly IPersistenceService _persistence;
        private readonly IFileDialogService _fileDialog;
        private readonly NotificationService _notificationService;
        private readonly INavigationMediator _navigationMediator;
        private readonly AnythingLLMService _anythingLLMService;
        private readonly INewProjectMediator _workspaceMediator;
        private readonly ILogger<ProjectViewModel>? _logger;
        
        // Current workspace state
        [ObservableProperty]
        private Workspace? _currentWorkspace;
        
        [ObservableProperty]
        private string? _workspacePath;
        
        [ObservableProperty]
        private string? _currentSourcePath;
        
        [ObservableProperty]
        private string? _currentAnythingLLMWorkspaceSlug;
        
        [ObservableProperty]
        private bool _hasUnsavedChanges;
        
        [ObservableProperty]
        private bool _isDirty;
        
        [ObservableProperty]
        private string? _statusMessage;

        // UI Properties
        [ObservableProperty]
        private string title = "Project Management";
        
        [ObservableProperty]
        private string description = "Configure your test case generation projects and workspace settings.";
        
        // AnythingLLM status properties
        [ObservableProperty] private bool isAnythingLLMAvailable;
        [ObservableProperty] private bool isAnythingLLMStarting;
        [ObservableProperty] private string anythingLLMStatusMessage = "Initializing AnythingLLM...";
        
        // Requirements collection (shared with other ViewModels)
        public ObservableCollection<Requirement> Requirements { get; }
        
        // Commands
        public ICommand SaveWorkspaceCommand { get; }
        public ICommand SaveWorkspaceAsCommand { get; }
        public ICommand LoadWorkspaceCommand { get; }
        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }

        public ProjectViewModel(
            IPersistenceService persistence,
            IFileDialogService fileDialog,
            NotificationService notificationService,
            INavigationMediator navigationMediator,
            ObservableCollection<Requirement> requirements,
            AnythingLLMService anythingLLMService,
            INewProjectMediator workspaceMediator,
            ILogger<ProjectViewModel>? logger = null)
        {
            // Store dependencies
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _workspaceMediator = workspaceMediator ?? throw new ArgumentNullException(nameof(workspaceMediator));
            _logger = logger;
            
            TestCaseEditorApp.Services.Logging.Log.Info("[ProjectViewModel] Constructor called with full dependencies including AnythingLLMService and WorkspaceManagementMediator");
            
            // Initialize commands
            SaveWorkspaceCommand = new RelayCommand(SaveWorkspace, () => HasUnsavedChanges);
            SaveWorkspaceAsCommand = new AsyncRelayCommand(SaveWorkspaceAsync);
            LoadWorkspaceCommand = new RelayCommand(LoadWorkspace);
            NewProjectCommand = new RelayCommand(() => _workspaceMediator.CreateNewProjectAsync());
            OpenProjectCommand = new RelayCommand(() => _workspaceMediator.OpenProjectAsync());
            
            // Setup property change monitoring
            Requirements.CollectionChanged += (s, e) => MarkDirty();
            
            // Subscribe to AnythingLLM status updates via mediator (but don't auto-request status to avoid triggering startup checks)
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Use current status if already available without triggering new health checks
            var currentStatus = AnythingLLMMediator.GetLastKnownStatus();
            if (currentStatus != null)
            {
                OnAnythingLLMStatusUpdated(currentStatus);
            }
        }
        
        // Legacy constructor for compatibility (minimal functionality)
        public ProjectViewModel() : this(
            new StubPersistenceService(),
            new StubFileDialogService(), 
            new StubNotificationService(),
            new StubNavigationMediator(),
            new ObservableCollection<Requirement>(),
            new AnythingLLMService(), // Use real service even in legacy constructor
            new StubWorkspaceManagementMediator(),
            null)
        {
            TestCaseEditorApp.Services.Logging.Log.Info("[ProjectViewModel] Legacy constructor called - limited functionality");
        }
        
        /// <summary>
        /// Quick save to existing workspace path
        /// </summary>
        public void SaveWorkspace()
        {
            _logger?.LogInformation("Saving workspace to existing path: {WorkspacePath}", WorkspacePath);
            
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                _logger?.LogInformation("No existing path, delegating to SaveAs");
                _ = SaveWorkspaceAsync();
                return;
            }

            if (Requirements.Count == 0)
            {
                SetStatusMessage("Nothing to save.", 2);
                return;
            }

            var workspace = CreateWorkspaceFromCurrentState();

            try
            {
                TestCaseEditorApp.Services.WorkspaceFileManager.Save(WorkspacePath!, workspace);
                
                CurrentWorkspace = workspace;
                IsDirty = false;
                HasUnsavedChanges = false;
                
                SetStatusMessage($"Saved: {Path.GetFileName(WorkspacePath)}", 3);
                _logger?.LogInformation("Workspace saved successfully to {WorkspacePath}", WorkspacePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save workspace to {WorkspacePath}", WorkspacePath);
                _notificationService.ShowError($"Failed to save workspace: {ex.Message}", 8);
            }
        }

        /// <summary>
        /// Save As - prompts for location
        /// </summary>
        public async Task SaveWorkspaceAsync()
        {
            await Task.CompletedTask;
            
            if (Requirements.Count == 0)
            {
                SetStatusMessage("Nothing to save.", 2);
                return;
            }

            var suggested = GetSuggestedFileName();
            var defaultFolder = GetDefaultSaveFolder();
            
            var chosenPath = _fileDialog.ShowSaveFile(
                title: "Save Workspace",
                suggestedFileName: suggested,
                filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                defaultExt: ".tcex.json",
                initialDirectory: defaultFolder);

            if (string.IsNullOrWhiteSpace(chosenPath))
            {
                SetStatusMessage("Save cancelled.", 2);
                return;
            }

            WorkspacePath = EnsureValidFilePath(chosenPath, suggested);
            
            var workspace = CreateWorkspaceFromCurrentState();
            
            try
            {
                TestCaseEditorApp.Services.WorkspaceFileManager.Save(WorkspacePath, workspace);
                
                CurrentWorkspace = workspace;
                IsDirty = false;
                HasUnsavedChanges = false;
                
                SetStatusMessage($"Saved: {Path.GetFileName(WorkspacePath)}", 4);
                _logger?.LogInformation("Workspace saved to new path: {WorkspacePath}", WorkspacePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save workspace to {WorkspacePath}", WorkspacePath);
                _notificationService.ShowError($"Failed to save workspace: {ex.Message}", 8);
            }
        }

        /// <summary>
        /// Load workspace from file dialog
        /// </summary>
        public void LoadWorkspace()
        {
            var chosenPath = _fileDialog.ShowOpenFile(
                title: "Open Workspace",
                filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                initialDirectory: GetDefaultOpenFolder());

            if (string.IsNullOrWhiteSpace(chosenPath))
            {
                return;
            }

            LoadWorkspaceFromPath(chosenPath);
        }

        /// <summary>
        /// Load workspace from specific path
        /// </summary>
        public void LoadWorkspaceFromPath(string filePath)
        {
            _logger?.LogInformation("Loading workspace from: {FilePath}", filePath);
            
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _logger?.LogWarning("Invalid file path or file doesn't exist: {FilePath}", filePath);
                SetStatusMessage("Invalid workspace file path.", 0, true);
                return;
            }

            try
            {
                var workspace = TestCaseEditorApp.Services.WorkspaceFileManager.Load(filePath);
                if (workspace == null)
                {
                    _logger?.LogWarning("Workspace file loaded but returned null: {FilePath}", filePath);
                    SetStatusMessage("Failed to load workspace (file empty or invalid).", 0, true);
                    return;
                }

                WorkspacePath = filePath;
                CurrentWorkspace = workspace;
                CurrentSourcePath = workspace.SourceDocPath;
                
                // Clear and reload requirements
                Requirements.Clear();
                foreach (var requirement in workspace.Requirements ?? Enumerable.Empty<Requirement>())
                {
                    Requirements.Add(requirement);
                }
                
                HasUnsavedChanges = false;
                IsDirty = false;
                
                SetStatusMessage($"Opened workspace: {Path.GetFileName(WorkspacePath)} - {Requirements.Count} requirements", 4);
                _logger?.LogInformation("Workspace loaded successfully: {RequirementCount} requirements", Requirements.Count);
                
                // Notify navigation that workspace is loaded
                _navigationMediator.Publish(new WorkspaceEvents.WorkspaceLoaded(workspace, filePath));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load workspace from {FilePath}", filePath);
                SetStatusMessage($"Failed to load workspace: {ex.Message}", 0, true);
            }
        }
        
        /// <summary>
        /// Create new project workflow
        /// </summary>
        public void CreateNewProject()
        {
            _logger?.LogInformation("Starting new project creation workflow");
            _navigationMediator.NavigateToSection("newproject");
        }
        
        /// <summary>
        /// Open existing project workflow  
        /// </summary>
        public void OpenExistingProject()
        {
            _logger?.LogInformation("Starting open existing project workflow");
            LoadWorkspace();
        }
        
        // Private helper methods
        private Workspace CreateWorkspaceFromCurrentState()
        {
            return new Workspace
            {
                SourceDocPath = CurrentSourcePath,
                Requirements = Requirements.ToList(),
                Name = Path.GetFileNameWithoutExtension(WorkspacePath),
                LastSavedUtc = DateTime.UtcNow
            };
        }
        
        private string GetSuggestedFileName()
        {
            var baseName = !string.IsNullOrWhiteSpace(CurrentSourcePath) 
                ? Path.GetFileNameWithoutExtension(CurrentSourcePath)
                : "Workspace";
            return $"{baseName}.tcex.json";
        }
        
        private string GetDefaultSaveFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "TestCaseEditorApp", "Workspaces");
            Directory.CreateDirectory(folder);
            return folder;
        }
        
        private string GetDefaultOpenFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "TestCaseEditorApp", "Workspaces");
            return Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
        
        private string EnsureValidFilePath(string chosenPath, string suggested)
        {
            try
            {
                // If dialog returned a directory, append suggested filename
                if (Directory.Exists(chosenPath) || string.IsNullOrWhiteSpace(Path.GetFileName(chosenPath)))
                {
                    chosenPath = Path.Combine(chosenPath, suggested);
                }

                // Ensure file extension
                if (string.IsNullOrWhiteSpace(Path.GetExtension(chosenPath)))
                {
                    chosenPath = Path.ChangeExtension(chosenPath, ".tcex.json");
                }
                
                return chosenPath;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to normalize file path: {ChosenPath}", chosenPath);
                throw new InvalidOperationException($"Failed to determine save file path: {ex.Message}", ex);
            }
        }
        
        private void MarkDirty()
        {
            if (!IsDirty)
            {
                IsDirty = true;
                HasUnsavedChanges = true;
                // ObservableProperty automatically notifies command CanExecute changes
            }
        }
        
        private void SetStatusMessage(string message, int durationSeconds = 0, bool isError = false)
        {
            StatusMessage = message;
            
            if (isError)
            {
                _notificationService.ShowError(message, durationSeconds > 0 ? durationSeconds : 5);
            }
            else if (durationSeconds > 0)
            {
                // Auto-clear status message after duration
                Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ => 
                {
                    if (StatusMessage == message) // Only clear if message hasn't changed
                    {
                        StatusMessage = null;
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
        
        /// <summary>
        /// Handles AnythingLLM status updates from the mediator
        /// </summary>
        private void OnAnythingLLMStatusUpdated(AnythingLLMStatus status)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[ProjectViewModel] Received status update: Available={status.IsAvailable}, Starting={status.IsStarting}, Message={status.StatusMessage}");
            
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsAnythingLLMAvailable = status.IsAvailable;
                IsAnythingLLMStarting = status.IsStarting;
                AnythingLLMStatusMessage = status.StatusMessage;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[ProjectViewModel] Properties updated: Available={IsAnythingLLMAvailable}, Starting={IsAnythingLLMStarting}, Message={AnythingLLMStatusMessage}");
            });
        }
        
        #region Stub Services (for legacy constructor compatibility)
        
        private class StubPersistenceService : IPersistenceService  
        {
            public void SaveWorkspace(string path, Workspace workspace) => TestCaseEditorApp.Services.WorkspaceFileManager.Save(path, workspace);
            public Workspace? LoadWorkspace(string path) => TestCaseEditorApp.Services.WorkspaceFileManager.Load(path);
            public void Save<T>(string key, T value) { }
            public T? Load<T>(string key) => default;
            public bool Exists(string key) => false;
            public string[] GetAvailableBackups(string filePath) => Array.Empty<string>();
            public void RestoreFromBackup(string filePath, string backupPath) { }
            public bool CanUndo(string filePath) => false;
            public void UndoLastSave(string filePath) { }
        }
        
        private class StubFileDialogService : IFileDialogService
        {
            public string? ShowOpenFile(string title, string filter, string? initialDirectory = null) => null;
            public string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null) => null;
            public string? ShowFolderDialog(string title, string? initialDirectory = null) => null;
        }
        
        private class StubNotificationService : NotificationService
        {
            private static ToastNotificationService CreateStubToastService()
            {
                try
                {
                    return new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                }
                catch
                {
                    // If no dispatcher available, create a no-op service
                    return null!; // Will be handled by the null-safe ShowError methods below  
                }
            }
            
            public StubNotificationService() : base(CreateStubToastService()!)
            {
            }
            
            // Override with no-op implementations for design-time safety
            public new void ShowSuccess(string message, int durationSeconds = 4) { /* no-op */ }
            public new void ShowError(string message, int durationSeconds = 8) { /* no-op */ }
            public new void ShowWarning(string message, int durationSeconds = 6) { /* no-op */ }
            public new void ShowInfo(string message, int durationSeconds = 4) { /* no-op */ }
        }
        
        private class StubNavigationMediator : INavigationMediator
        {
            public string? CurrentSection => null;
            public object? CurrentHeader => null; 
            public object? CurrentContent => null;
            public void NavigateToSection(string sectionName, object? context = null) { }
            public void NavigateToStep(string stepId, object? context = null) { }
            public void SetActiveHeader(object? headerViewModel) { }
            public void SetMainContent(object? contentViewModel) { }
            public void ClearNavigationState() { }
            public void Subscribe<T>(Action<T> handler) where T : class { }
            public void Unsubscribe<T>(Action<T> handler) where T : class { }
            public void Publish<T>(T navigationEvent) where T : class { }
        }
        
        private class StubWorkspaceManagementMediator : INewProjectMediator
        {
            public string CurrentStep => "ProjectSelection";
            public object? CurrentViewModel => null;
            public bool IsRegistered => true;
            public string DomainName => "Workspace Management";
            public void MarkAsRegistered() { }
            public void RequestCrossDomainAction<T>(T request) where T : class { }
            public void BroadcastToAllDomains<T>(T notification) where T : class { }
            public void NavigateToInitialStep() { }
            public void NavigateToFinalStep() { }
            public void NavigateToStep(string stepName, object? context = null) { }
            public bool CanNavigateBack() => false;
            public bool CanNavigateForward() => false;
            public async Task CreateNewProjectAsync() => await Task.CompletedTask;
            public async Task OpenProjectAsync() => await Task.CompletedTask;
            public async Task SaveProjectAsync() => await Task.CompletedTask;
            public async Task CloseProjectAsync() => await Task.CompletedTask;
            public async Task UndoLastSaveAsync() => await Task.CompletedTask;
            public bool CanUndoLastSave() => false;
            public bool HasRequirements() => false;
            public async Task ImportAdditionalRequirementsAsync() => await Task.CompletedTask;

            public void ShowWorkspaceSelectionForOpen() { }
            public void ShowWorkspaceSelectionForNew() { }
            public async Task OnWorkspaceSelectedAsync(string workspaceSlug, string workspaceName, bool isNewProject) => await Task.CompletedTask;
            public WorkspaceInfo? GetCurrentWorkspaceInfo() => null;
            public bool HasUnsavedChanges() => false;
            public void ShowProgress(string message, double percentage = 0) { /* no-op */ }
            public void UpdateProgress(string message, double percentage) { /* no-op */ }
            public void HideProgress() { /* no-op */ }
            public void ShowNotification(string message, DomainNotificationType type = DomainNotificationType.Info) { /* no-op */ }
            public void Subscribe<T>(Action<T> handler) where T : class { /* no-op */ }
            public void Unsubscribe<T>(Action<T> handler) where T : class { /* no-op */ }
            public void PublishEvent<T>(T eventData) where T : class { /* no-op */ }
            public Task<bool> CompleteProjectCreationAsync(string workspaceName, string projectName, string projectSavePath, string documentPath) => Task.FromResult(true);
            public Task<bool> CreateNewProjectWithWarningAsync(string workspaceName, string projectName, string projectSavePath, string documentPath) => Task.FromResult(true);
            public (bool Success, string FilePath, string ProjectName) ShowSaveProjectDialog(string currentProjectName) => (false, string.Empty, string.Empty);
            
            // Form persistence methods (no-op for stub)
            public void SaveDraftProjectInfo(string? projectName, string? projectPath, string? requirementsPath) { /* no-op */ }
            public (string? projectName, string? projectPath, string? requirementsPath) GetDraftProjectInfo() => (null, null, null);
            public void ClearDraftProjectInfo() { /* no-op */ }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Workspace-related events for the navigation mediator
    /// </summary>
    public static class WorkspaceEvents
    {
        public class WorkspaceLoaded
        {
            public Workspace Workspace { get; }
            public string FilePath { get; }
            
            public WorkspaceLoaded(Workspace workspace, string filePath)
            {
                Workspace = workspace;
                FilePath = filePath;
            }
        }
        
        public class WorkspaceSaved
        {
            public Workspace Workspace { get; }
            public string FilePath { get; }
            
            public WorkspaceSaved(Workspace workspace, string filePath)
            {
                Workspace = workspace;
                FilePath = filePath;
            }
        }
    }
}
