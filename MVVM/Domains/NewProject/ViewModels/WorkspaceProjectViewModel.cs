using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    /// <summary>
    /// Domain ViewModel for workspace and project management operations.
    /// Follows the architectural pattern with mediator injection and domain-specific functionality.
    /// Handles project lifecycle: create, open, save, close operations.
    /// </summary>
    public partial class WorkspaceProjectViewModel : BaseDomainViewModel
    {
        private readonly AnythingLLMService _anythingLLMService;
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private string? _currentWorkspacePath;
        
        [ObservableProperty]
        private string? _currentWorkspaceName;
        
        [ObservableProperty]
        private string? _currentAnythingLLMWorkspaceSlug;
        
        [ObservableProperty]
        private bool _hasUnsavedChanges;
        
        [ObservableProperty]
        private bool _isProjectOpen;

        // Commands exposed to UI
        public IRelayCommand CreateNewProjectCommand { get; private set; } = null!;
        public IRelayCommand OpenProjectCommand { get; private set; } = null!;
        public IAsyncRelayCommand SaveProjectCommand { get; private set; } = null!;
        public IAsyncRelayCommand CloseProjectCommand { get; private set; } = null!;

        public WorkspaceProjectViewModel(
            INewProjectMediator mediator,
            ILogger<WorkspaceProjectViewModel> logger,
            AnythingLLMService anythingLLMService,
            NotificationService notificationService)
            : base(mediator, logger)
        {
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            Title = "Workspace & Project Management";
            
            // Subscribe to domain events
            SubscribeToEvents();
        }

        protected override void InitializeCommands()
        {
            base.InitializeCommands(); // Initialize common commands
            
            // Domain-specific commands
            var workspaceMediator = (INewProjectMediator)_mediator;
            CreateNewProjectCommand = new RelayCommand(
                async () => await workspaceMediator.CreateNewProjectAsync(),
                () => !IsBusy);
            
            OpenProjectCommand = new RelayCommand(
                async () => await workspaceMediator.OpenProjectAsync(),
                () => !IsBusy);
            
            SaveProjectCommand = new AsyncRelayCommand(
                async () => await workspaceMediator.SaveProjectAsync(),
                () => IsProjectOpen && HasUnsavedChanges && !IsBusy);
            
            CloseProjectCommand = new AsyncRelayCommand(
                async () => await workspaceMediator.CloseProjectAsync(),
                () => IsProjectOpen && !IsBusy);
        }

        private void SubscribeToEvents()
        {
            // Subscribe to workspace management events
            Subscribe<NewProjectEvents.ProjectCreated>(OnProjectCreated);
            Subscribe<NewProjectEvents.ProjectOpened>(OnProjectOpened);
            Subscribe<NewProjectEvents.ProjectSaved>(OnProjectSaved);
            Subscribe<NewProjectEvents.ProjectClosed>(OnProjectClosed);
            Subscribe<NewProjectEvents.ProjectOperationError>(OnProjectOperationError);
            Subscribe<NewProjectEvents.StepChanged>(OnStepChanged);
        }

        private void OnProjectCreated(NewProjectEvents.ProjectCreated e)
        {
            CurrentWorkspacePath = e.WorkspacePath;
            CurrentWorkspaceName = e.WorkspaceName;
            CurrentAnythingLLMWorkspaceSlug = e.AnythingLLMWorkspaceSlug;
            IsProjectOpen = true;
            HasUnsavedChanges = false;
            
            StatusMessage = $"New project '{e.WorkspaceName}' created successfully";
            
            _logger.LogInformation("Project created: {WorkspaceName} at {WorkspacePath}", 
                e.WorkspaceName, e.WorkspacePath);
        }

        private void OnProjectOpened(NewProjectEvents.ProjectOpened e)
        {
            CurrentWorkspacePath = e.WorkspacePath;
            CurrentWorkspaceName = e.WorkspaceName;
            CurrentAnythingLLMWorkspaceSlug = e.AnythingLLMWorkspaceSlug;
            IsProjectOpen = true;
            HasUnsavedChanges = false;
            
            StatusMessage = $"Project '{e.WorkspaceName}' opened successfully";
            
            // Refresh command states

            
            _logger.LogInformation("Project opened: {WorkspaceName} at {WorkspacePath}", 
                e.WorkspaceName, e.WorkspacePath);
        }

        private void OnProjectSaved(NewProjectEvents.ProjectSaved e)
        {
            HasUnsavedChanges = false;
            StatusMessage = "Project saved successfully";
            
            // Refresh command states

            
            _logger.LogInformation("Project saved: {WorkspacePath}", e.WorkspacePath);
        }

        private void OnProjectClosed(NewProjectEvents.ProjectClosed e)
        {
            CurrentWorkspacePath = null;
            CurrentWorkspaceName = null;
            CurrentAnythingLLMWorkspaceSlug = null;
            IsProjectOpen = false;
            HasUnsavedChanges = false;
            
            StatusMessage = "Project closed";
            
            // Refresh command states

            
            _logger.LogInformation("Project closed: {WorkspacePath}", e.WorkspacePath);
        }

        private void OnProjectOperationError(NewProjectEvents.ProjectOperationError e)
        {
            SetError($"Error during {e.Operation}: {e.ErrorMessage}");
            StatusMessage = $"Error: {e.ErrorMessage}";
            
            _logger.LogError(e.Exception, "Project operation error: {Operation} - {ErrorMessage}", 
                e.Operation, e.ErrorMessage);
        }

        private void OnStepChanged(NewProjectEvents.StepChanged e)
        {
            _logger.LogDebug("Workspace management step changed to: {Step}", e.Step);
            
            // Update UI based on step changes if needed
            OnPropertyChanged(nameof(Title));
        }



        // Implementation of required abstract methods from BaseDomainViewModel
        protected override async Task SaveAsync()
        {
            var workspaceMediator = (INewProjectMediator)_mediator;
            await workspaceMediator.SaveProjectAsync();
        }

        protected override void Cancel()
        {
            // Cancel any ongoing operations
            if (IsBusy)
            {
                _logger.LogInformation("Cancelling workspace operation");
                // TODO: Implement cancellation logic if needed
            }
        }

        protected override async Task RefreshAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Refreshing workspace information...";
                
                // Get current workspace info from mediator
                var workspaceMediator = (INewProjectMediator)_mediator;
                var workspaceInfo = workspaceMediator.GetCurrentWorkspaceInfo();
                if (workspaceInfo != null)
                {
                    CurrentWorkspacePath = workspaceInfo.Path;
                    CurrentWorkspaceName = workspaceInfo.Name;
                    CurrentAnythingLLMWorkspaceSlug = workspaceInfo.AnythingLLMSlug;
                    HasUnsavedChanges = workspaceInfo.HasUnsavedChanges;
                    IsProjectOpen = true;
                }
                else
                {
                    CurrentWorkspacePath = null;
                    CurrentWorkspaceName = null;
                    CurrentAnythingLLMWorkspaceSlug = null;
                    HasUnsavedChanges = false;
                    IsProjectOpen = false;
                }
                
                StatusMessage = "Workspace information refreshed";

                
                await Task.Delay(100); // Simulate refresh delay
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh workspace information");
                SetError($"Failed to refresh: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected override bool CanSave()
        {
            return IsProjectOpen && HasUnsavedChanges && !IsBusy;
        }

        protected override bool CanCancel()
        {
            return IsBusy;
        }

        protected override bool CanRefresh()
        {
            return !IsBusy;
        }

        // Public methods for external coordination
        public void MarkDirty()
        {
            HasUnsavedChanges = true;

        }

        public bool HasActiveProject => IsProjectOpen && !string.IsNullOrEmpty(CurrentWorkspaceName);

        public override void Dispose()
        {
            // Unsubscribe from events to prevent memory leaks
            // BaseDomainViewModel handles the actual disposal of mediator
            base.Dispose();
        }
    }
}
