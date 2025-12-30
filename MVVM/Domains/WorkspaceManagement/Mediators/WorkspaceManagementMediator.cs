using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Events;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;

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
            await Task.CompletedTask;
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
                ShowProgress("Opening project...", 0);
                
                _logger.LogInformation("Starting project opening workflow");
                
                // Show file dialog to select .tcex.json file
                var selectedPath = _fileDialogService.ShowOpenFile(
                    title: "Open Test Case Editor Project",
                    filter: "Test Case Editor Session|*.tcex.json|JSON Files|*.json|All Files|*.*"
                );
                
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    _logger.LogInformation("User cancelled project opening dialog");
                    HideProgress();
                    return;
                }
                
                ShowProgress("Loading project file...", 25);
                
                // Validate file exists
                if (!File.Exists(selectedPath))
                {
                    ShowNotification("Selected project file does not exist.", DomainNotificationType.Error);
                    HideProgress();
                    return;
                }
                
                ShowProgress("Reading workspace data...", 50);
                
                // Load workspace using existing service
                var workspace = TestCaseEditorApp.Services.WorkspaceFileManager.Load(selectedPath);
                if (workspace == null)
                {
                    ShowNotification("Failed to load project file. The file may be corrupted or invalid.", DomainNotificationType.Error);
                    HideProgress();
                    return;
                }
                
                ShowProgress("Setting up project...", 75);
                
                // Extract project name from file path
                var projectName = System.IO.Path.GetFileNameWithoutExtension(selectedPath);
                // Remove .tcex extension if present
                if (projectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    projectName = System.IO.Path.GetFileNameWithoutExtension(projectName);
                }
                
                // Project status will be communicated via cross-domain events below
                
                ShowProgress("Finalizing project setup...", 90);
                
                // Publish domain event
                var projectOpenedEvent = new WorkspaceManagementEvents.ProjectOpened 
                { 
                    WorkspacePath = selectedPath,
                    WorkspaceName = projectName,
                    Workspace = workspace
                };
                
                PublishEvent(projectOpenedEvent);
                
                // Broadcast to other domains for cross-domain coordination
                _logger.LogInformation("📡 Broadcasting ProjectOpened event to other domains: {ProjectName}", projectName);
                BroadcastToAllDomains(projectOpenedEvent);
                
                // Store workspace info for future operations
                _currentWorkspaceInfo = new WorkspaceInfo
                {
                    Name = projectName,
                    Path = selectedPath,
                    LastModified = DateTime.Now
                };
                
                ShowProgress("Project opened successfully!", 100);
                
                // Show success notification
                ShowNotification(
                    $"Project '{projectName}' opened successfully! {workspace.Requirements?.Count ?? 0} requirements loaded.", 
                    DomainNotificationType.Success);
                
                // Navigate to the project workspace
                NavigateToStep("ProjectActive", _currentWorkspaceInfo);
                
                await Task.Delay(500); // Brief delay to show completion
                HideProgress();
                
                _logger.LogInformation("Project opened successfully: {ProjectName} from {FilePath}", projectName, selectedPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open project");
                
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
            await Task.CompletedTask;
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
        
        /// <summary>
        /// Complete project creation with workspace details, requirements import, and workspace setup
        /// </summary>
        public async Task CompleteProjectCreationAsync(string workspaceName, string projectName, string projectSavePath, string documentPath)
        {
            try
            {
                _logger.LogInformation("🔍 CompleteProjectCreationAsync called - documentPath: '{DocumentPath}', exists: {Exists}", 
                    documentPath, !string.IsNullOrWhiteSpace(documentPath) && File.Exists(documentPath));
                
                ShowProgress($"Creating project '{projectName}'...", 25);
                
                // 1. Set workspace path and configuration
                UpdateProgress("Setting up workspace configuration...", 40);
                
                _currentWorkspaceInfo = new WorkspaceInfo
                {
                    Name = projectName,
                    Path = projectSavePath,
                    AnythingLLMSlug = workspaceName,
                    HasUnsavedChanges = false,
                    LastModified = DateTime.Now
                };
                
                // 2. Import requirements first, then create workspace
                List<Requirement> importedRequirements = new();
                
                if (!string.IsNullOrWhiteSpace(documentPath) && File.Exists(documentPath))
                {
                    UpdateProgress("Importing requirements from document...", 60);
                    
                    try
                    {
                        // Default to Jama parser for .docx files since most of our documents are from Jama
                        // The Jama parser has better filtering for version history and baselines
                        var preferJamaParser = documentPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
                        var requirementService = App.ServiceProvider?.GetService<IRequirementService>();
                        
                        if (requirementService != null)
                        {
                            if (preferJamaParser)
                            {
                                importedRequirements = await Task.Run(() => requirementService.ImportRequirementsFromJamaAllDataDocx(documentPath));
                            }
                            else
                            {
                                importedRequirements = await Task.Run(() => requirementService.ImportRequirementsFromWord(documentPath));
                            }
                            
                            // Broadcast imported requirements to TestCaseGenerationMediator for UI sync
                            if (importedRequirements.Count > 0)
                            {
                                BroadcastToAllDomains(new TestCaseGenerationEvents.RequirementsImported
                                {
                                    Requirements = importedRequirements,
                                    SourceFile = documentPath,
                                    ImportType = preferJamaParser ? "Jama" : "Word",
                                    ImportTime = TimeSpan.Zero
                                });
                            }
                        }
                        else
                        {
                            _logger.LogWarning("❌ Could not get RequirementService from DI container");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error importing requirements from {DocumentPath}", documentPath);
                        ShowNotification($"Error importing requirements: {ex.Message}", DomainNotificationType.Warning);
                    }
                }
                
                // 3. Create workspace object with imported requirements
                var workspace = new Workspace
                {
                    Name = projectName,
                    Version = Workspace.SchemaVersion,
                    CreatedBy = Environment.UserName,
                    CreatedUtc = DateTime.UtcNow,
                    LastSavedUtc = DateTime.UtcNow,
                    SaveCount = 0,
                    SourceDocPath = documentPath,
                    Requirements = importedRequirements
                };

                // 4. Save workspace configuration
                UpdateProgress("Saving workspace configuration...", 80);
                
                // Create workspace directory if it doesn't exist
                var workspaceDir = Path.GetDirectoryName(projectSavePath);
                if (!string.IsNullOrEmpty(workspaceDir) && !Directory.Exists(workspaceDir))
                {
                    Directory.CreateDirectory(workspaceDir);
                }
                
                // Save workspace file
                _persistenceService.Save(projectSavePath, workspace);
                _logger.LogInformation("💾 Workspace file saved: {ProjectSavePath}", projectSavePath);
                
                UpdateProgress("Project created successfully!", 100);
                
                // Extract display project name
                var displayProjectName = projectName;
                if (displayProjectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    displayProjectName = System.IO.Path.GetFileNameWithoutExtension(displayProjectName);
                }
                
                // 5. Broadcast the project creation event with workspace data
                var projectCreatedEvent = new WorkspaceManagementEvents.ProjectCreated 
                { 
                    WorkspacePath = projectSavePath,
                    WorkspaceName = displayProjectName,
                    AnythingLLMWorkspaceSlug = workspaceName,
                    Workspace = workspace
                };
                
                PublishEvent(projectCreatedEvent);
                
                // Broadcast to other domains for cross-domain coordination
                _logger.LogInformation("📡 Broadcasting ProjectCreated event to other domains: {ProjectName}", displayProjectName);
                BroadcastToAllDomains(projectCreatedEvent);
                
                // Show success notification
                ShowNotification(
                    $"Project '{displayProjectName}' created successfully! Navigate to 'Requirements' to see imported data.", 
                    DomainNotificationType.Success);
                    
                HideProgress();
                
                // Navigate to active project state
                NavigateToStep("ProjectActive", _currentWorkspaceInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete project creation for project: {ProjectName}", projectName);
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error creating project: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
                throw; // Re-throw to let the caller handle the error
            }
        }
        
        /// <summary>
        /// Create a new project with proper warning if another project is currently open
        /// </summary>
        public async Task CreateNewProjectWithWarningAsync(string workspaceName, string projectName, string projectSavePath, string documentPath)
        {
            try
            {
                // Check if a project is currently open
                if (_currentWorkspaceInfo != null)
                {
                    string message = $"You currently have project '{_currentWorkspaceInfo.Name}' open.";
                    if (_currentWorkspaceInfo.HasUnsavedChanges)
                    {
                        message += " Creating a new project will close the current project and any unsaved changes will be lost.";
                    }
                    else
                    {
                        message += " Creating a new project will close the current project.";
                    }
                    message += "\n\nDo you want to continue?";
                    
                    // Use proper domain UI coordination for warning dialog
                    var continueCreation = await RequestUserConfirmation(message, "Project Already Open");
                    if (!continueCreation)
                    {
                        ShowNotification("Project creation cancelled.", DomainNotificationType.Info);
                        return;
                    }
                }
                
                // Proceed with project creation
                await CompleteProjectCreationAsync(workspaceName, projectName, projectSavePath, documentPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create new project with warning: {ProjectName}", projectName);
                
                PublishEvent(new WorkspaceManagementEvents.ProjectOperationError 
                { 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error creating project: {ex.Message}", DomainNotificationType.Error);
                throw;
            }
        }
        
        /// <summary>
        /// Request user confirmation via domain UI coordinator
        /// </summary>
        private async Task<bool> RequestUserConfirmation(string message, string title)
        {
            // This would ideally use the domain UI coordinator for proper dialog handling
            // For now, we'll use a simple approach that maintains the architecture
            var tcs = new TaskCompletionSource<bool>();
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var result = System.Windows.MessageBox.Show(message, title, 
                    System.Windows.MessageBoxButton.YesNo, 
                    System.Windows.MessageBoxImage.Warning);
                
                tcs.SetResult(result == System.Windows.MessageBoxResult.Yes);
            });
            
            return await tcs.Task;
        }
        
        /// <summary>
        /// Show save file dialog with protection against overwriting currently open project
        /// </summary>
        public (bool Success, string FilePath, string ProjectName) ShowSaveProjectDialog(string currentProjectName)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Project As",
                    Filter = "Test Case Editor Project (*.tcex.json)|*.tcex.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".tcex.json",
                    FileName = string.IsNullOrWhiteSpace(currentProjectName) ? "New Project.tcex.json" : $"{currentProjectName}.tcex.json"
                };

                if (dlg.ShowDialog() == true)
                {
                    var selectedPath = dlg.FileName;
                    
                    // Check if user is trying to overwrite the currently open project
                    if (_currentWorkspaceInfo != null && 
                        string.Equals(selectedPath, _currentWorkspaceInfo.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        var message = $"You cannot save the new project to '{Path.GetFileName(selectedPath)}' because it is the currently open project.\n\n" +
                                     "Please choose a different filename or location.";
                        
                        ShowNotification(message, DomainNotificationType.Warning);
                        
                        // Recursively show the dialog again until user picks a different file or cancels
                        return ShowSaveProjectDialog(currentProjectName);
                    }
                    
                    // Extract project name from chosen filename
                    var chosenName = Path.GetFileNameWithoutExtension(selectedPath);
                    var projectName = string.IsNullOrWhiteSpace(chosenName) ? "New Project" : chosenName;
                    
                    _logger.LogInformation("Project save location selected: {FilePath}, Project name: {ProjectName}", selectedPath, projectName);
                    return (true, selectedPath, projectName);
                }
                
                return (false, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing save project dialog");
                ShowNotification($"Error showing save dialog: {ex.Message}", DomainNotificationType.Error);
                return (false, string.Empty, string.Empty);
            }
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
            
            var projectOpenedEvent = new WorkspaceManagementEvents.ProjectOpened 
            { 
                WorkspacePath = _currentWorkspaceInfo.Path,
                WorkspaceName = workspaceName,
                AnythingLLMWorkspaceSlug = workspaceSlug,
                Workspace = workspace
            };
            
            PublishEvent(projectOpenedEvent);
            
            // Broadcast to other domains for cross-domain coordination
            _logger.LogInformation("📡 Broadcasting ProjectOpened event (AnythingLLM) to other domains: {ProjectName}", workspaceName);
            BroadcastToAllDomains(projectOpenedEvent);
            
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