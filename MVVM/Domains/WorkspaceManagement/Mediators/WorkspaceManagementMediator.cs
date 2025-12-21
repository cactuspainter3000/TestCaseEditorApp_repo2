using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Events;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators
{
    /// <summary>
    /// Mediator for the Workspace Management domain.
    /// Coordinates project lifecycle operations: create, open, save, close, and workspace management.
    /// Provides domain-specific UI coordination and cross-domain communication.
    /// </summary>
    public class WorkspaceManagementMediator : BaseDomainMediator<WorkspaceManagementEvents>, IWorkspaceManagementMediator
    {
        private readonly IPersistenceService _persistenceService;
        private readonly IFileDialogService _fileDialogService;
        private readonly AnythingLLMService _anythingLLMService;
        private readonly NotificationService _notificationService;
        private WorkspaceInfo? _currentWorkspaceInfo;

        public WorkspaceManagementMediator(
            ILogger<WorkspaceManagementMediator> logger,
            IDomainUICoordinator uiCoordinator,
            IPersistenceService persistenceService,
            IFileDialogService fileDialogService,
            AnythingLLMService anythingLLMService,
            NotificationService notificationService,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Workspace Management", performanceMonitor, eventReplay)
        {
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        public override void NavigateToInitialStep()
        {
            NavigateToStep("ProjectSelection");
        }

        public override void NavigateToFinalStep()
        {
            NavigateToStep("ProjectActive");
        }

        public override bool CanNavigateBack()
        {
            return _currentStep != "ProjectSelection";
        }

        public override bool CanNavigateForward()
        {
            return _currentStep != "ProjectActive" && _currentWorkspaceInfo != null;
        }

        public new void NavigateToStep(string stepName, object? context = null)
        {
            var previousStep = _currentStep;
            _currentStep = stepName;
            
            _logger.LogDebug("Navigating from {PreviousStep} to {CurrentStep} in {Domain}", 
                previousStep, _currentStep, _domainName);
            
            PublishEvent(new WorkspaceManagementEvents.StepChanged 
            { 
                Step = stepName, 
                ViewModel = context 
            });
        }

        public async Task CreateNewProjectAsync()
        {
            try
            {
                ShowProgress("Initiating new project creation...", 0);
                
                _logger.LogInformation("Starting new project creation workflow");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectCreationStarted());
                
                // Show workspace selection modal for new project
                ShowWorkspaceSelectionForNew();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start new project creation");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    Operation = "CreateNewProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error creating new project: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        public async Task OpenProjectAsync()
        {
            try
            {
                ShowProgress("Initiating project opening...", 0);
                
                _logger.LogInformation("Starting project opening workflow");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOpenStarted());
                
                // Show workspace selection modal for existing project
                ShowWorkspaceSelectionForOpen();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start project opening");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    Operation = "OpenProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error opening project: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        public async Task SaveProjectAsync()
        {
            try
            {
                if (_currentWorkspaceInfo == null)
                {
                    ShowNotification("No active workspace to save", DomainNotificationType.Warning);
                    return;
                }

                ShowProgress("Saving project...", 50);
                
                _logger.LogInformation("Saving project: {WorkspacePath}", _currentWorkspaceInfo.Path);
                
                PublishEvent(new WorkspaceManagementEvents.ProjectSaveStarted 
                { 
                    WorkspacePath = _currentWorkspaceInfo.Path 
                });
                
                // TODO: Implement actual save logic through persistence service
                await Task.Delay(100); // Placeholder for save operation
                
                _currentWorkspaceInfo.HasUnsavedChanges = false;
                _currentWorkspaceInfo.LastModified = DateTime.Now;
                
                PublishEvent(new WorkspaceManagementEvents.ProjectSaved 
                { 
                    WorkspacePath = _currentWorkspaceInfo.Path 
                });
                
                ShowNotification("Project saved successfully", DomainNotificationType.Success);
                HideProgress();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save project");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    Operation = "SaveProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error saving project: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        public async Task CloseProjectAsync()
        {
            try
            {
                if (_currentWorkspaceInfo == null)
                {
                    return;
                }

                ShowProgress("Closing project...", 50);
                
                _logger.LogInformation("Closing project: {WorkspacePath}", _currentWorkspaceInfo.Path);
                
                PublishEvent(new WorkspaceManagementEvents.ProjectCloseStarted 
                { 
                    WorkspacePath = _currentWorkspaceInfo.Path 
                });
                
                var workspacePath = _currentWorkspaceInfo.Path;
                _currentWorkspaceInfo = null;
                
                PublishEvent(new WorkspaceManagementEvents.ProjectClosed 
                { 
                    WorkspacePath = workspacePath 
                });
                
                ShowNotification("Project closed", DomainNotificationType.Info);
                HideProgress();
                
                NavigateToInitialStep();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close project");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    Operation = "CloseProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error closing project: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        public void ShowWorkspaceSelectionForOpen()
        {
            _logger.LogInformation("Showing workspace selection for opening existing project");
            
            PublishEvent(new WorkspaceManagementEvents.WorkspaceSelectionRequested 
            { 
                IsOpenExisting = true 
            });
            
            // Request UI modal through cross-domain communication
            RequestCrossDomainAction(new ShowWorkspaceSelectionModalRequest 
            { 
                ForOpenExisting = true, 
                DomainContext = "Workspace Management" 
            });
        }

        public void ShowWorkspaceSelectionForNew()
        {
            _logger.LogInformation("Showing workspace selection for creating new project");
            
            PublishEvent(new WorkspaceManagementEvents.WorkspaceSelectionRequested 
            { 
                IsOpenExisting = false 
            });
            
            // Request UI modal through cross-domain communication
            RequestCrossDomainAction(new ShowWorkspaceSelectionModalRequest 
            { 
                ForOpenExisting = false, 
                DomainContext = "Workspace Management" 
            });
        }

        public async Task OnWorkspaceSelectedAsync(string workspaceSlug, string workspaceName, bool isNewProject)
        {
            try
            {
                _logger.LogInformation("Workspace selected: {WorkspaceName} ({WorkspaceSlug}), IsNew: {IsNew}", 
                    workspaceName, workspaceSlug, isNewProject);
                
                PublishEvent(new WorkspaceManagementEvents.WorkspaceSelected 
                { 
                    WorkspaceSlug = workspaceSlug, 
                    WorkspaceName = workspaceName 
                });
                
                if (isNewProject)
                {
                    await CompleteProjectCreationAsync(workspaceSlug, workspaceName);
                }
                else
                {
                    await CompleteProjectOpeningAsync(workspaceSlug, workspaceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle workspace selection");
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    Operation = "WorkspaceSelection", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error processing workspace selection: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        private async Task CompleteProjectCreationAsync(string workspaceSlug, string workspaceName)
        {
            UpdateProgress("Creating new project workspace...", 75);
            
            // TODO: Implement actual workspace creation logic
            await Task.Delay(500); // Placeholder
            
            _currentWorkspaceInfo = new WorkspaceInfo
            {
                Name = workspaceName,
                Path = $"path/to/workspace/{workspaceName}", // TODO: Get actual path
                AnythingLLMSlug = workspaceSlug,
                HasUnsavedChanges = false,
                LastModified = DateTime.Now
            };
            
            PublishEvent(new WorkspaceManagementEvents.ProjectCreated 
            { 
                WorkspacePath = _currentWorkspaceInfo.Path,
                WorkspaceName = workspaceName,
                AnythingLLMWorkspaceSlug = workspaceSlug
            });
            
            ShowNotification($"New project '{workspaceName}' created successfully", DomainNotificationType.Success);
            HideProgress();
            
            NavigateToStep("ProjectActive", _currentWorkspaceInfo);
        }

        private async Task CompleteProjectOpeningAsync(string workspaceSlug, string workspaceName)
        {
            UpdateProgress("Opening existing project workspace...", 75);
            
            // TODO: Implement actual workspace loading logic
            await Task.Delay(500); // Placeholder
            
            _currentWorkspaceInfo = new WorkspaceInfo
            {
                Name = workspaceName,
                Path = $"path/to/workspace/{workspaceName}", // TODO: Get actual path
                AnythingLLMSlug = workspaceSlug,
                HasUnsavedChanges = false,
                LastModified = DateTime.Now
            };
            
            // TODO: Load actual workspace data
            var workspace = new Workspace { Name = workspaceName };
            
            PublishEvent(new WorkspaceManagementEvents.ProjectOpened 
            { 
                WorkspacePath = _currentWorkspaceInfo.Path,
                WorkspaceName = workspaceName,
                AnythingLLMWorkspaceSlug = workspaceSlug,
                Workspace = workspace
            });
            
            ShowNotification($"Project '{workspaceName}' opened successfully", DomainNotificationType.Success);
            HideProgress();
            
            NavigateToStep("ProjectActive", _currentWorkspaceInfo);
        }

        public WorkspaceInfo? GetCurrentWorkspaceInfo()
        {
            return _currentWorkspaceInfo;
        }

        public bool HasUnsavedChanges()
        {
            return _currentWorkspaceInfo?.HasUnsavedChanges ?? false;
        }

        public new void ShowProgress(string message, double percentage = 0)
        {
            base.ShowProgress(message, percentage);
        }

        public new void UpdateProgress(string message, double percentage)
        {
            base.UpdateProgress(message, percentage);
        }

        public new void HideProgress()
        {
            base.HideProgress();
        }

        public void ShowNotification(string message, DomainNotificationType type = DomainNotificationType.Info)
        {
            base.ShowNotification(message, type);
        }
        
        // Implement interface requirement for public PublishEvent
        public new void PublishEvent<T>(T eventData) where T : class
        {
            base.PublishEvent(eventData);
        }
        
        // Implement interface requirement for MarkAsRegistered
        public new void MarkAsRegistered()
        {
            base.MarkAsRegistered();
        }
    }

    /// <summary>
    /// Cross-domain request for showing workspace selection modal
    /// </summary>
    public class ShowWorkspaceSelectionModalRequest
    {
        public bool ForOpenExisting { get; set; }
        public string DomainContext { get; set; } = string.Empty;
    }
}