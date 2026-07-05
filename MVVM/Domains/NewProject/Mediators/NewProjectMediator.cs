using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using static TestCaseEditorApp.MVVM.Events.CrossDomainMessages;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.Mediators
{
    /// <summary>
    /// Mediator for the Workspace Management domain.
    /// Coordinates project lifecycle operations: create, open, save, close, and workspace management.
    /// Provides domain-specific UI coordination and cross-domain communication.
    /// </summary>
    public class NewProjectMediator : BaseDomainMediator<NewProjectEvents>, INewProjectMediator
    {
        private readonly IPersistenceService _persistenceService;
        private readonly IFileDialogService _fileDialogService;
        private readonly AnythingLLMService _anythingLLMService;
        private readonly SmartRequirementImporter _smartImporter;
        private readonly JamaConnectService _jamaConnectService;
            private readonly IUserSettingsService? _userSettingsService;
        private WorkspaceInfo? _currentWorkspaceInfo;
        
        // Form persistence state for architectural compliance
        private string? _draftProjectName;
        private string? _draftProjectPath;
        private string? _draftRequirementsPath;

        public NewProjectMediator(
            ILogger<NewProjectMediator> logger,
            IDomainUICoordinator uiCoordinator,
            IPersistenceService persistenceService,
            IFileDialogService fileDialogService,
            AnythingLLMService anythingLLMService,
            SmartRequirementImporter smartImporter,
            JamaConnectService jamaConnectService,
                IUserSettingsService? userSettingsService = null,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Workspace Management", performanceMonitor, eventReplay)
        {
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _smartImporter = smartImporter ?? throw new ArgumentNullException(nameof(smartImporter));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
                _userSettingsService = userSettingsService;
        }

        /// <summary>
        /// Override BroadcastToAllDomains to also publish WorkspaceModified events locally.
        /// This ensures UI infrastructure ViewModels (like TitleViewModel) that subscribe 
        /// directly to this mediator also receive the event.
        /// </summary>
        public override void BroadcastToAllDomains<T>(T notification)
        {
            // Broadcast to other mediators via DomainCoordinator
            base.BroadcastToAllDomains(notification);
            
            // Also publish locally for direct subscribers (UI infrastructure ViewModels)
            if (notification is NewProjectEvents.WorkspaceModified workspaceModified)
            {
                _logger.LogDebug("[NewProjectMediator] Publishing WorkspaceModified locally for direct subscribers: {Reason}", 
                    workspaceModified.Reason);
                PublishEvent(workspaceModified);
            }
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
            
            PublishEvent(new NewProjectEvents.StepChanged 
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
                ShowProgress("Initiating Requirements Workshop creation...", 0);
                
                _logger.LogInformation("Starting new project creation workflow");
                
                PublishEvent(new NewProjectEvents.ProjectCreationStarted());
                
                // Show workspace selection modal for new project
                ShowWorkspaceSelectionForNew();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start new project creation");
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    Operation = "CreateNewProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error creating Requirements Workshop: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        public async Task OpenProjectAsync()
        {
            try
            {
                ShowProgress("Opening Requirements Workshop...", 0);
                
                _logger.LogInformation("Starting project opening workflow");
                
                // Show file dialog to select .tcex.json file
                var selectedPath = _fileDialogService.ShowOpenFile(
                    title: "Open a Requirements Workshop",
                    filter: "Requirements Workshop|*.tcex.json|JSON Files|*.json|All Files|*.*"
                );
                
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    _logger.LogInformation("User cancelled project opening dialog");
                    HideProgress();
                    return;
                }
                
                ShowProgress("Loading workshop file...", 25);
                
                // Validate file exists
                if (!File.Exists(selectedPath))
                {
                    ShowNotification("Selected workshop file does not exist.", DomainNotificationType.Error);
                    HideProgress();
                    return;
                }
                
                ShowProgress("Reading workspace data...", 50);
                
                // Load workspace using existing service
                var workspace = WorkspaceFileManager.Load(selectedPath);
                if (workspace == null)
                {
                    ShowNotification("Failed to load workshop file. The file may be corrupted or invalid.", DomainNotificationType.Error);
                    HideProgress();
                    return;
                }
                
                ShowProgress("Setting up workshop...", 75);
                
                // Extract project name from file path
                var projectName = Path.GetFileNameWithoutExtension(selectedPath);
                // Remove .tcex extension if present
                if (projectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    projectName = Path.GetFileNameWithoutExtension(projectName);
                }
                
                // Project status will be communicated via cross-domain events below
                
                ShowProgress("Finalizing workshop setup...", 90);
                
                // Publish domain event
                var projectOpenedEvent = new NewProjectEvents.ProjectOpened 
                { 
                    WorkspacePath = selectedPath,
                    WorkspaceName = projectName,
                    Workspace = workspace
                };
                
                PublishEvent(projectOpenedEvent);
                
                // Broadcast to other domains for cross-domain coordination
                _logger.LogInformation("📡 Broadcasting ProjectOpened event to other domains: {ProjectName}", projectName);
                BroadcastToAllDomains(projectOpenedEvent);
                
                // Also broadcast RequirementsImported event to ensure NavigationViewModel sorting is applied
                if (workspace.Requirements?.Any() == true)
                {
                    _logger.LogInformation("📡 Broadcasting RequirementsImported event for project requirements: {RequirementCount}", workspace.Requirements.Count);
                    BroadcastToAllDomains(new TestCaseGenerationEvents.RequirementsImported
                    {
                        Requirements = workspace.Requirements,
                        SourceFile = selectedPath,
                        ImportType = "Project",
                        ImportTime = TimeSpan.Zero
                    });
                }
                
                // Store workspace info for future operations
                _currentWorkspaceInfo = new WorkspaceInfo
                {
                    Name = projectName,
                    Path = selectedPath,
                    LastModified = DateTime.Now
                };
                
                ShowProgress("Workshop opened successfully!", 100);
                
                // Show success notification
                ShowNotification(
                    $"Requirements Workshop '{projectName}' opened successfully. {workspace.Requirements?.Count ?? 0} requirements loaded.", 
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
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    Operation = "OpenProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error opening Requirements Workshop: {ex.Message}", DomainNotificationType.Error);
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
                
                // Ensure proper async behavior
                await Task.CompletedTask;

                ShowProgress("Saving Requirements Workshop...", 50);
                
                _logger.LogInformation("Saving project: {WorkspacePath}", _currentWorkspaceInfo.Path);
                
                PublishEvent(new NewProjectEvents.ProjectSaveStarted 
                { 
                    WorkspacePath = _currentWorkspaceInfo.Path 
                });
                
                // 1. Get current requirements from Requirements domain
                var requirementsMediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                var currentRequirements = requirementsMediator?.Requirements?.ToList() 
                    ?? new List<Requirement>();
                
                _logger.LogInformation("Gathering current workspace data - found {RequirementCount} requirements (from {Source})", 
                    currentRequirements.Count, 
                    "RequirementsMediator");
                
                // Debug: Check if any requirements have generated test cases before saving
                var totalTestCases = currentRequirements.Sum(r => r.GeneratedTestCases?.Count ?? 0);
                var requirementsWithTestCases = currentRequirements.Count(r => r.GeneratedTestCases?.Any() == true);
                _logger.LogInformation("💾 PROJECT SAVE DEBUG: Found {TotalTestCases} total generated test cases across {RequirementsWithTestCases}/{TotalRequirements} requirements", 
                    totalTestCases, requirementsWithTestCases, currentRequirements.Count);
                
                foreach (var req in currentRequirements.Where(r => r.GeneratedTestCases?.Any() == true))
                {
                    _logger.LogInformation("💾 PROJECT SAVE DEBUG: Requirement '{ReqId}' has {TestCaseCount} generated test cases: [{TestCaseIds}]", 
                        req.Item, req.GeneratedTestCases?.Count ?? 0, 
                        string.Join(", ", req.GeneratedTestCases?.Select(tc => tc.Id) ?? new List<string>()));
                }
                
                // 2. Build current workspace object with all data
                var workspace = new Workspace
                {
                    Name = _currentWorkspaceInfo.Name,
                    Requirements = currentRequirements,
                    Version = Workspace.SchemaVersion,
                    CreatedBy = Environment.UserName,
                    CreatedUtc = DateTime.UtcNow,
                    LastSavedUtc = DateTime.UtcNow,
                    SaveCount = 1 // Will be incremented in future versions
                    // Note: ImportSource will be auto-detected during load if missing
                };
                
                // 3. Validate workspace data before save
                var validationService = App.ServiceProvider?.GetService<IWorkspaceValidationService>();
                if (validationService != null)
                {
                    UpdateProgress("Validating workspace data...", 50);
                    var validationResult = validationService.ValidateWorkspace(workspace);
                    
                    if (!validationResult.IsValid)
                    {
                        var errorMsg = $"Validation failed: {validationResult.ErrorMessage}";
                        _logger.LogWarning("Workspace validation failed: {Error}", validationResult.ErrorMessage);
                        
                        PublishEvent(new NewProjectEvents.ProjectOperationError
                        {
                            Operation = "SaveProject",
                            ErrorMessage = errorMsg,
                            Exception = new InvalidOperationException(validationResult.ErrorMessage)
                        });
                        
                        ShowNotification(errorMsg, DomainNotificationType.Error);
                        HideProgress();
                        return;
                    }
                    
                    if (validationResult.Severity == ValidationSeverity.Warning)
                    {
                        _logger.LogWarning("Workspace validation warning: {Warning}", validationResult.ErrorMessage);
                        ShowNotification($"Warning: {validationResult.ErrorMessage}", DomainNotificationType.Warning);
                    }
                }
                
                UpdateProgress("Saving workspace data...", 85);
                
                // 4. Create workspace directory if it doesn't exist
                var workspaceDir = Path.GetDirectoryName(_currentWorkspaceInfo.Path);
                if (!string.IsNullOrEmpty(workspaceDir) && !Directory.Exists(workspaceDir))
                {
                    Directory.CreateDirectory(workspaceDir);
                }
                
                // 5. Save workspace file using persistence service
                _persistenceService.Save(_currentWorkspaceInfo.Path, workspace);
                _logger.LogInformation("💾 Workspace file saved: {WorkspacePath}", _currentWorkspaceInfo.Path);
                
                UpdateProgress("Save completed", 100);
                
                _currentWorkspaceInfo.HasUnsavedChanges = false;
                _currentWorkspaceInfo.LastModified = DateTime.Now;
                
                var projectSavedEvent = new NewProjectEvents.ProjectSaved 
                { 
                    WorkspacePath = _currentWorkspaceInfo.Path 
                };
                
                // Publish locally within WorkspaceManagement domain
                PublishEvent(projectSavedEvent);
                
                // Broadcast to all domains for cross-domain coordination
                BroadcastToAllDomains(projectSavedEvent);
                
                ShowNotification("Requirements Workshop saved successfully", DomainNotificationType.Success);
                HideProgress();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save project");
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    Operation = "SaveProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error saving Requirements Workshop: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        /// <summary>
        /// Undo the last save operation by restoring from the most recent backup
        /// </summary>
        public async Task UndoLastSaveAsync()
        {
            await Task.CompletedTask;
            try
            {
                if (_currentWorkspaceInfo == null)
                {
                    ShowNotification("No project is currently open", DomainNotificationType.Warning);
                    return;
                }

                ShowProgress("Checking for available backups...", 10);

                if (!_persistenceService.CanUndo(_currentWorkspaceInfo.Path))
                {
                    ShowNotification("No backups available to undo", DomainNotificationType.Info);
                    HideProgress();
                    return;
                }

                ShowProgress("Restoring from backup...", 50);

                // Perform the undo operation
                _persistenceService.UndoLastSave(_currentWorkspaceInfo.Path);

                ShowProgress("Reloading project data...", 75);

                // Reload the workspace to refresh UI
                var restoredWorkspace = WorkspaceFileManager.Load(_currentWorkspaceInfo.Path);
                if (restoredWorkspace != null)
                {
                    // Update workspace modified time
                    _currentWorkspaceInfo.LastModified = DateTime.Now;
                    _currentWorkspaceInfo.HasUnsavedChanges = false;

                    // Broadcast workspace reload event to update all UI
                    PublishEvent(new NewProjectEvents.ProjectOpened
                    {
                        Workspace = restoredWorkspace,
                        WorkspacePath = _currentWorkspaceInfo.Path,
                        WorkspaceName = _currentWorkspaceInfo.Name
                    });

                    ShowProgress("Undo completed successfully", 100);
                    await Task.Delay(500); // Brief pause to show completion

                    ShowNotification("Successfully undid last save operation", DomainNotificationType.Success);
                    HideProgress();

                    _logger.LogInformation("Successfully undid last save for project: {WorkspacePath}", _currentWorkspaceInfo.Path);
                }
                else
                {
                    ShowNotification("Failed to reload workspace after undo", DomainNotificationType.Error);
                    HideProgress();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to undo last save");
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    Operation = "UndoLastSave", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error undoing last save: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        /// <summary>
        /// Check if undo is available for the current project
        /// </summary>
        public bool CanUndoLastSave()
        {
            return _currentWorkspaceInfo != null && _persistenceService.CanUndo(_currentWorkspaceInfo.Path);
        }

        
        /// <summary>
        /// Import additional requirements to existing project (append mode)
        /// </summary>
        public async Task ImportAdditionalRequirementsAsync()
        {
            try
            {
                _logger.LogInformation("Starting Import Additional Requirements workflow");
                ShowProgress("Selecting file for import...", 10);
                
                var selectedFile = _fileDialogService.ShowOpenFile(
                    "Import Additional Requirements",
                    ".docx files|*.docx|.json files|*.json|All files|*.*"
                );
                
                if (!string.IsNullOrEmpty(selectedFile))
                {
                    _logger.LogInformation("File selected for additional requirements import: {FilePath}", selectedFile);
                    // Broadcast to TestCaseGeneration domain for processing
                    BroadcastToAllDomains(new ImportRequirementsRequest 
                    { 
                        DocumentPath = selectedFile,
                        RequestingDomain = "WorkspaceManagement",
                        PreferJamaParser = false
                    });
                    
                    ShowProgress("Import request sent", 100);
                    await Task.Delay(500); // Brief pause to show completion
                    HideProgress();
                }
                else
                {
                    _logger.LogInformation("Import Additional Requirements cancelled by user");
                    ShowNotification("Import cancelled", DomainNotificationType.Info);
                    HideProgress();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Import Additional Requirements");
                ShowNotification($"Import failed: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
                throw;
            }
        }

        public async Task CloseProjectAsync()
        {
            await Task.CompletedTask;
            try
            {
                _logger.LogInformation("🔍 CloseProjectAsync called - checking workspace state: {HasWorkspace}", 
                    _currentWorkspaceInfo != null ? $"Yes ({_currentWorkspaceInfo.Path})" : "No workspace loaded");
                
                if (_currentWorkspaceInfo == null)
                {
                    _logger.LogWarning("⚠️ CloseProjectAsync: No workspace to close (_currentWorkspaceInfo is null)");
                    return;
                }

                // Keep workspace JSON aligned with Jama before unload.
                await TrySyncWorkspaceFromJamaOnCloseAsync(_currentWorkspaceInfo.Path);

                ShowProgress("Closing Requirements Workshop...", 50);
                
                _logger.LogInformation("Closing project: {WorkspacePath}", _currentWorkspaceInfo.Path);
                
                PublishEvent(new NewProjectEvents.ProjectCloseStarted 
                { 
                    WorkspacePath = _currentWorkspaceInfo.Path 
                });
                
                var workspacePath = _currentWorkspaceInfo.Path;
                _currentWorkspaceInfo = null;
                
                // Clear workspace context cache when project is unloaded
                var workspaceContextService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.IWorkspaceContext>();
                if (workspaceContextService is TestCaseEditorApp.Services.WorkspaceContextService contextService)
                {
                    _logger.LogInformation("🧹 Clearing workspace context cache on project unload");
                    contextService.NotifyWorkspaceChanged(null, null, TestCaseEditorApp.Services.WorkspaceChangeType.Unloaded);
                }
                
                // Clear view configuration to ensure fresh routing on next project load
                var viewConfigService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.ViewConfigurationService>();
                if (viewConfigService != null)
                {
                    _logger.LogInformation("🧹 Clearing view configuration on project unload");
                    viewConfigService.ClearCurrentConfiguration();
                }
                
                var projectClosedEvent = new NewProjectEvents.ProjectClosed 
                { 
                    WorkspacePath = workspacePath 
                };
                
                PublishEvent(projectClosedEvent);
                
                // Broadcast to other domains for cross-domain coordination
                _logger.LogInformation("📡 Broadcasting ProjectClosed event to other domains: {WorkspacePath}", workspacePath);
                BroadcastToAllDomains(projectClosedEvent);
                
                ShowNotification("Requirements Workshop closed", DomainNotificationType.Info);
                HideProgress();
                
                NavigateToInitialStep();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close project");
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    Operation = "CloseProject", 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error closing Requirements Workshop: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
            }
        }

        private async Task TrySyncWorkspaceFromJamaOnCloseAsync(string workspacePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workspacePath) || !File.Exists(workspacePath))
                {
                    return;
                }

                var workspace = WorkspaceFileManager.Load(workspacePath);
                if (workspace == null)
                {
                    return;
                }

                var resolvedProjectId = await ResolveJamaProjectIdForWorkspaceAsync(workspace);
                if (!resolvedProjectId.HasValue)
                {
                    _logger.LogInformation(
                        "Skipping close-time Jama sync: no project ID resolved. Workspace={WorkspacePath}",
                        workspacePath);
                    return;
                }

                if (!_jamaConnectService.IsConfigured)
                {
                    _logger.LogWarning(
                        "Skipping close-time Jama sync for project {ProjectId}: Jama service is not configured.",
                        resolvedProjectId.Value);
                    return;
                }

                var jamaItems = await _jamaConnectService.GetRequirementsAsync(resolvedProjectId.Value, CancellationToken.None);
                var requirements = await _jamaConnectService.ConvertToRequirementsWithEnumDecodingAsync(
                    jamaItems,
                    resolvedProjectId.Value,
                    CancellationToken.None);

                workspace.Requirements = requirements ?? new List<Requirement>();
                workspace.JamaProjectId = resolvedProjectId.Value;
                workspace.JamaProject = resolvedProjectId.Value.ToString();
                workspace.ImportSource = "Jama";
                workspace.LastSavedUtc = DateTime.UtcNow;

                try
                {
                    var projects = await _jamaConnectService.GetProjectsAsync(CancellationToken.None);
                    var matchingProject = projects.FirstOrDefault(p => p.Id == resolvedProjectId.Value);
                    if (matchingProject != null)
                    {
                        workspace.JamaProjectName = matchingProject.Name;
                        if (string.IsNullOrWhiteSpace(workspace.JamaTestPlan))
                        {
                            workspace.JamaTestPlan = matchingProject.Name;
                        }
                    }
                }
                catch (Exception metadataEx)
                {
                    _logger.LogDebug(metadataEx, "Close-time Jama metadata refresh failed for project {ProjectId}", resolvedProjectId.Value);
                }

                WorkspaceFileManager.Save(workspacePath, workspace);
                _logger.LogInformation(
                    "✅ Close-time Jama sync complete for project {ProjectId}. Persisted {RequirementCount} requirements.",
                    resolvedProjectId.Value,
                    workspace.Requirements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Close-time Jama sync failed; proceeding with project close.");
            }
        }

        private async Task<int?> ResolveJamaProjectIdForWorkspaceAsync(Workspace workspace)
        {
            if (workspace.JamaProjectId.HasValue && workspace.JamaProjectId.Value > 0)
            {
                return workspace.JamaProjectId.Value;
            }

            if (!string.IsNullOrWhiteSpace(workspace.JamaProject)
                && int.TryParse(workspace.JamaProject, out var parsedProjectId)
                && parsedProjectId > 0)
            {
                return parsedProjectId;
            }

            var settingsProjectId = (_userSettingsService?.LoadSettings()?.JamaProjectId ?? string.Empty).Trim();
            if (int.TryParse(settingsProjectId, out var parsedSettingsId) && parsedSettingsId > 0)
            {
                return parsedSettingsId;
            }

            var envProjectId = (Environment.GetEnvironmentVariable("JAMA_PROJECT_ID") ?? string.Empty).Trim();
            if (int.TryParse(envProjectId, out var parsedEnvId) && parsedEnvId > 0)
            {
                return parsedEnvId;
            }

            var candidateNames = new[]
            {
                workspace.JamaProjectName,
                workspace.JamaTestPlan,
                workspace.JamaProject
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            if (candidateNames.Count == 0 || !_jamaConnectService.IsConfigured)
            {
                return null;
            }

            var projects = await _jamaConnectService.GetProjectsAsync(CancellationToken.None);
            var matchingProject = projects.FirstOrDefault(project =>
                candidateNames.Any(candidate =>
                    string.Equals(project.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project.Key, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project.Id.ToString(), candidate, StringComparison.OrdinalIgnoreCase)));

            return matchingProject?.Id;
        }

        public void ShowWorkspaceSelectionForOpen()
        {
            _logger.LogInformation("Showing workspace selection for opening existing project");
            
            PublishEvent(new NewProjectEvents.WorkspaceSelectionRequested 
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
            
            PublishEvent(new NewProjectEvents.WorkspaceSelectionRequested 
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
                
                PublishEvent(new NewProjectEvents.WorkspaceSelected 
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
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
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
            
            PublishEvent(new NewProjectEvents.ProjectCreated 
            { 
                WorkspacePath = _currentWorkspaceInfo.Path,
                WorkspaceName = workspaceName,
                AnythingLLMWorkspaceSlug = workspaceSlug
            });
            
            ShowNotification($"New project '{workspaceName}' created successfully", DomainNotificationType.Success);
            HideProgress();
            
            NavigateToStep("ProjectActive", _currentWorkspaceInfo);
            
            // Request navigation to NewProject section to show the project view
            RequestCrossDomainAction(new NavigateToSectionRequest 
            { 
                SectionName = "NewProject",
                Context = "Project created successfully"
            });
        }
        
        /// <summary>
        /// Complete project creation with workspace details, requirements import, and workspace setup
        /// </summary>
        public async Task<bool> CompleteProjectCreationAsync(string workspaceName, string projectName, string projectSavePath, string documentPath, string? anythingLLMWorkspaceSlug = null, int? jamaProjectId = null, string? jamaProjectName = null)
        {
            bool requirementsImportedSuccessfully = true;
            
            try
            {
                var resolvedAnythingLLMSlug = string.IsNullOrWhiteSpace(anythingLLMWorkspaceSlug)
                    ? workspaceName
                    : anythingLLMWorkspaceSlug;

                _logger.LogInformation("🔍 CompleteProjectCreationAsync called - documentPath: '{DocumentPath}', exists: {Exists}", 
                    documentPath, !string.IsNullOrWhiteSpace(documentPath) && File.Exists(documentPath));
                
                ShowProgress($"Creating project '{projectName}'...", 25);
                
                // 1. Set workspace path and configuration
                UpdateProgress("Setting up workspace configuration...", 40);
                
                _currentWorkspaceInfo = new WorkspaceInfo
                {
                    Name = projectName,
                    Path = projectSavePath,
                    AnythingLLMSlug = resolvedAnythingLLMSlug,
                    HasUnsavedChanges = false,
                    LastModified = DateTime.Now
                };
                
                // 2. Import requirements first, then create workspace
                List<Requirement> importedRequirements = new();
                
                // 🎯 Variables to preserve Jama project information
                string? jamaProjectIdString = null;
                int? jamaProjectIdCanonical = null;
                string? jamaProjectNameResolved = null;
                string? jamaTestPlan = null;

                if (jamaProjectId.HasValue)
                {
                    jamaProjectIdCanonical = jamaProjectId.Value;
                    jamaProjectIdString = jamaProjectId.Value.ToString();
                    jamaProjectNameResolved = jamaProjectName;
                    jamaTestPlan = jamaProjectName;
                    _logger.LogInformation("🔍 Using explicit Jama association for project creation - ProjectId: {ProjectId}, ProjectName: {ProjectName}", jamaProjectIdCanonical, jamaProjectNameResolved);
                }
                
                if (!string.IsNullOrWhiteSpace(documentPath) && File.Exists(documentPath))
                {
                    UpdateProgress("Importing requirements from document...", 60);
                    
                    try
                    {
                        // Check if this is a JSON file from Jama import
                        if (documentPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && 
                            Path.GetFileName(documentPath).StartsWith("JamaRequirements_", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("📄 Detected Jama JSON file, loading requirements directly");
                            
                            // Load requirements directly from JSON file created by Jama import
                            var jsonContent = await File.ReadAllTextAsync(documentPath);
                            var jamaWorkspace = JsonSerializer.Deserialize<Workspace>(jsonContent);
                            
                            if (jamaWorkspace?.Requirements != null)
                            {
                                importedRequirements = jamaWorkspace.Requirements;
                                
                                // 🎯 Preserve Jama project information from original workspace
                                jamaProjectIdString = jamaWorkspace.JamaProject;
                                jamaProjectIdCanonical = jamaWorkspace.JamaProjectId;
                                jamaProjectNameResolved = jamaWorkspace.JamaProjectName ?? jamaWorkspace.JamaTestPlan;
                                jamaTestPlan = jamaWorkspace.JamaTestPlan;

                                if (!jamaProjectIdCanonical.HasValue && !string.IsNullOrWhiteSpace(jamaProjectIdString) && int.TryParse(jamaProjectIdString, out var parsedProjectId))
                                {
                                    jamaProjectIdCanonical = parsedProjectId;
                                }
                                
                                _logger.LogInformation("✅ Successfully loaded {Count} requirements from Jama JSON file", 
                                    importedRequirements.Count);
                                _logger.LogInformation("🔍 DEBUG: Extracted Jama info - ProjectId: {ProjectId}, TestPlan: {TestPlan}", 
                                    jamaProjectIdString, jamaTestPlan);
                                
                                // Broadcast imported requirements so subscribed domains can refresh state
                                BroadcastToAllDomains(new TestCaseGenerationEvents.RequirementsImported
                                {
                                    Requirements = importedRequirements,
                                    SourceFile = jamaWorkspace.SourceDocPath ?? "Jama Connect",
                                    ImportType = "Jama Connect API",
                                    ImportTime = TimeSpan.FromSeconds(1) // Approximate since we already imported
                                });
                            }
                            else
                            {
                                requirementsImportedSuccessfully = false;
                                _logger.LogWarning("⚠️ Jama JSON file contains no requirements");
                                ShowNotification("Jama import file contains no requirements.", DomainNotificationType.Warning);
                            }
                        }
                        else
                        {
                            // Use SmartRequirementImporter for Word documents and other formats
                            var importResult = await _smartImporter.ImportRequirementsAsync(documentPath);
                            
                            if (importResult.Success)
                            {
                                importedRequirements = importResult.Requirements;
                                _logger.LogInformation("✅ Successfully imported {Count} requirements using {Method}", 
                                    importedRequirements.Count, importResult.ImportMethod);
                                
                                // Broadcast imported requirements so subscribed domains can refresh state
                                BroadcastToAllDomains(new TestCaseGenerationEvents.RequirementsImported
                                {
                                    Requirements = importedRequirements,
                                    SourceFile = documentPath,
                                    ImportType = importResult.ImportMethod,
                                    ImportTime = importResult.ImportDuration
                                });
                            }
                            else
                            {
                                requirementsImportedSuccessfully = false;
                                _logger.LogWarning("⚠️ Smart importer failed: {Error}", importResult.ErrorMessage);
                                ShowNotification(importResult.UserMessage, DomainNotificationType.Warning);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        requirementsImportedSuccessfully = false;
                        _logger.LogError(ex, "❌ Error importing requirements from {DocumentPath}", documentPath);
                        ShowNotification($"Requirements import failed: {ex.Message}. Project created but requirements were not imported.", DomainNotificationType.Warning);
                    }
                }
                
                // 3. Create workspace object with imported requirements
                
                // Determine ImportSource based on actual import type
                string importSource;
                
                // Debug: Check file path detection
                string lowerPath = documentPath.ToLowerInvariant();
                bool containsJama = lowerPath.Contains("jamarequirements");
                bool isJson = lowerPath.EndsWith(".json");
                _logger.LogInformation("🔍 DEBUG: File path detection - Path: '{0}', ContainsJama: {1}, IsJson: {2}", 
                    documentPath, containsJama, isJson);
                
                if (containsJama && isJson)
                {
                    // This is a Jama JSON file - preserve original ImportSource = "Jama"
                    importSource = "Jama";
                    _logger.LogInformation("🎯 Setting ImportSource to 'Jama' - detected Jama JSON file");
                }
                else
                {
                    // Document import - user explicitly chose document import method
                    importSource = "Document";
                    _logger.LogInformation("🎯 Setting ImportSource to 'Document' - user chose document import method");
                }
                        // Check if Jama is configured and available (even if no file was imported)
                        var jamaSettings = _userSettingsService?.LoadSettings();
                        bool jamaConfigured = !string.IsNullOrWhiteSpace(jamaSettings?.JamaBaseUrl) && 
                                             !string.IsNullOrWhiteSpace(jamaSettings?.JamaClientId) && 
                                             !string.IsNullOrWhiteSpace(jamaSettings?.JamaClientSecret);
                    
                        if (jamaConfigured && string.IsNullOrEmpty(documentPath))
                        {
                            // Jama is configured and no document was selected - default to Jama
                            importSource = "Jama";
                            _logger.LogInformation("🎯 Setting ImportSource to 'Jama' - Jama is configured and no document selected");
                        }
                        else if (jamaConfigured && !string.IsNullOrEmpty(documentPath))
                        {
                            // Jama is configured but user selected a document - respect their choice
                            importSource = "Document";
                            _logger.LogInformation("🎯 Setting ImportSource to 'Document' - user explicitly selected document over Jama");
                        }
                        else
                        {
                            // Jama not configured or user chose document - use document import
                            importSource = "Document";
                            _logger.LogInformation("🎯 Setting ImportSource to 'Document' - Jama not configured or document import chosen");
                        }
                
                if (!jamaProjectIdCanonical.HasValue && !string.IsNullOrWhiteSpace(jamaProjectIdString) && int.TryParse(jamaProjectIdString, out var parsedJamaId))
                {
                    jamaProjectIdCanonical = parsedJamaId;
                }

                var workspace = new Workspace
                {
                    Name = projectName,
                    Version = Workspace.SchemaVersion,
                    CreatedBy = Environment.UserName,
                    CreatedUtc = DateTime.UtcNow,
                    LastSavedUtc = DateTime.UtcNow,
                    SaveCount = 0,
                    SourceDocPath = documentPath,
                    AnythingLLMWorkspaceName = workspaceName,
                    AnythingLLMWorkspaceSlug = resolvedAnythingLLMSlug,
                    ImportSource = importSource,  // 🎯 Set based on actual content type
                    JamaProject = jamaProjectIdString,  // 🎯 Preserve Jama project ID for attachment scanning
                    JamaProjectId = jamaProjectIdCanonical,
                    JamaProjectName = jamaProjectNameResolved,
                    JamaTestPlan = jamaTestPlan,  // 🎯 Preserve Jama test plan name for display
                    Requirements = importedRequirements
                };

                // 🔍 Debug: Verify ImportSource before saving
                _logger.LogInformation($"🔍 DEBUG: About to save workspace with ImportSource = '{workspace.ImportSource}'");
                Console.WriteLine($"🔍 DEBUG: About to save workspace with ImportSource = '{workspace.ImportSource}'");

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

                // Embed standard RAG setup files in the background so project creation
                // is not blocked by long upload/verification operations.
                bool ragSetupDeferred = false;
                if (_anythingLLMService != null && !string.IsNullOrWhiteSpace(resolvedAnythingLLMSlug))
                {
                    UpdateProgress("Embedding standard RAG setup files...", 88);
                    ragSetupDeferred = true;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            _logger.LogInformation("[NewProjectMediator] [BACKGROUND] Starting standard RAG setup embedding for workspace '{WorkspaceSlug}'", resolvedAnythingLLMSlug);

                            var optimizationGuideUploaded = await _anythingLLMService.UploadOptimizationGuideAsync(resolvedAnythingLLMSlug);
                            var ragTrainingDocumentsUploaded = await _anythingLLMService.UploadRagTrainingDocumentsAsync(resolvedAnythingLLMSlug);
                            var ragSetupVerified = await VerifyStandardRagDocumentsEmbeddedAsync(resolvedAnythingLLMSlug);

                            if (optimizationGuideUploaded && ragTrainingDocumentsUploaded && ragSetupVerified)
                            {
                                _logger.LogInformation("✅ [BACKGROUND] Embedded standard RAG setup files for workspace '{WorkspaceSlug}'", resolvedAnythingLLMSlug);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ [BACKGROUND] Standard RAG setup embedding incomplete for workspace '{WorkspaceSlug}' (Guide={Guide}, Training={Training}, Verified={Verified})",
                                    resolvedAnythingLLMSlug, optimizationGuideUploaded, ragTrainingDocumentsUploaded, ragSetupVerified);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[NewProjectMediator] [BACKGROUND] Error embedding standard RAG setup files for workspace '{WorkspaceSlug}'", resolvedAnythingLLMSlug);
                        }
                    });
                }
                else
                {
                    _logger.LogWarning("⚠️ Skipping RAG setup embedding because AnythingLLM service or workspace slug is unavailable");
                }
                
                UpdateProgress("Project created successfully!", 100);
                
                // Extract display project name
                var displayProjectName = projectName;
                if (displayProjectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    displayProjectName = Path.GetFileNameWithoutExtension(displayProjectName);
                }
                
                // 5. Broadcast the project creation event with workspace data
                var projectCreatedEvent = new NewProjectEvents.ProjectCreated 
                { 
                    WorkspacePath = projectSavePath,
                    WorkspaceName = displayProjectName,
                    AnythingLLMWorkspaceSlug = workspace.AnythingLLMWorkspaceSlug,
                    Workspace = workspace
                };
                
                PublishEvent(projectCreatedEvent);
                
                // Broadcast to other domains for cross-domain coordination
                _logger.LogInformation("📡 Broadcasting ProjectCreated event to other domains: {ProjectName}", displayProjectName);
                BroadcastToAllDomains(projectCreatedEvent);
                
                // Update workspace context for proper view routing (ImportSource-based)
                var workspaceContextService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.IWorkspaceContext>();
                if (workspaceContextService is TestCaseEditorApp.Services.WorkspaceContextService contextService)
                {
                    _logger.LogInformation("🔄 Updating workspace context for created project: ImportSource='{ImportSource}'", workspace.ImportSource);
                    contextService.NotifyWorkspaceChanged(null, workspace, TestCaseEditorApp.Services.WorkspaceChangeType.Loaded);
                }
                
                // Clear view configuration to ensure fresh routing based on new workspace data
                var viewConfigService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.ViewConfigurationService>();
                if (viewConfigService != null)
                {
                    _logger.LogInformation("🔄 Clearing view configuration for fresh routing");
                    viewConfigService.ClearCurrentConfiguration();
                }
                
                // Show success notification with appropriate message based on import success
                if (requirementsImportedSuccessfully)
                {
                    ShowNotification(
                        $"Project '{displayProjectName}' created successfully! Navigate to 'Requirements' to see imported data.", 
                        DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification(
                        $"Project '{displayProjectName}' created but requirements import failed. You can manually import requirements later.", 
                        DomainNotificationType.Warning);
                }

                   // --- Write filtered AnythingLLM/project creation log slice to repo log file ---
                   try
                   {
                       var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                       var appLogPath = Path.Combine(userProfile, "TestCaseEditorApp", "logs", "app.log");
                       var repoRoot = ResolveRepositoryRootPath();
                       var repoLogPath = Path.Combine(repoRoot, "logs", "last-anythingllm.log");
                       var patterns = new[] { "[NewProject]", "[AnythingLLM]", "[RAG Sync]", "[RAG]", "CompleteProjectCreationAsync", "RAG setup", "optimization guide", "training document", "verification" };
                       if (File.Exists(appLogPath))
                       {
                           var lines = File.ReadAllLines(appLogPath)
                               .Where(line => patterns.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase)))
                               .TakeLast(120)
                               .ToList();
                           Directory.CreateDirectory(Path.GetDirectoryName(repoLogPath)!);
                           File.WriteAllLines(repoLogPath, lines);
                           _logger.LogInformation("[NewProjectMediator] Wrote filtered AnythingLLM log snapshot to {RepoLogPath}", repoLogPath);
                       }
                   }
                   catch (Exception logEx)
                   {
                       _logger.LogWarning(logEx, "[NewProjectMediator] Failed to write filtered AnythingLLM log to repo");
                   }

                if (ragSetupDeferred)
                {
                    ShowNotification(
                    $"Project '{displayProjectName}' was created. Standard RAG setup is continuing in the background.",
                    DomainNotificationType.Info);
                }
                    
                HideProgress();
                
                // Navigate to active project state
                NavigateToStep("ProjectActive", _currentWorkspaceInfo);
                
                // Return success status - true if project was created, but indicate if requirements import failed
                return requirementsImportedSuccessfully;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete project creation for project: {ProjectName}", projectName);
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error creating project: {ex.Message}", DomainNotificationType.Error);
                HideProgress();
                return false; // Return false instead of throwing to allow graceful handling
            }
        }

        private async Task<bool> VerifyStandardRagDocumentsEmbeddedAsync(string workspaceSlug)
        {
            try
            {
                var documentsElement = await _anythingLLMService.GetWorkspaceDocumentsAsync(workspaceSlug, skipSlugResolution: true);
                if (!documentsElement.HasValue || documentsElement.Value.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("⚠️ Could not verify standard RAG documents because workspace document listing is unavailable for '{WorkspaceSlug}'", workspaceSlug);
                    return false;
                }

                var requiredMarkers = new[]
                {
                    "ANYTHINGLM_OPTIMIZATION_GUIDE.md",
                    "RAG-JSON-Schema-Training.md",
                    "RAG-Learning-Examples.md",
                    "RAG-Optimization-Summary.md"
                };

                bool allFound = true;
                foreach (var marker in requiredMarkers)
                {
                    var found = documentsElement.Value.EnumerateArray().Any(doc =>
                    {
                        var text = doc.GetRawText();
                        return text.Contains(marker, StringComparison.OrdinalIgnoreCase);
                    });

                    if (!found)
                    {
                        _logger.LogWarning("⚠️ Standard RAG document marker '{DocumentMarker}' was not found in workspace '{WorkspaceSlug}'", marker, workspaceSlug);
                        allFound = false;
                    }
                }

                if (allFound)
                {
                    _logger.LogInformation("✅ Verified standard RAG documents are present in workspace '{WorkspaceSlug}'", workspaceSlug);
                }

                return allFound;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Failed to verify standard RAG documents for workspace '{WorkspaceSlug}'", workspaceSlug);
                return false;
            }
        }

        private static string ResolveRepositoryRootPath()
        {
            var candidates = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var startPath in candidates)
            {
                var current = new DirectoryInfo(startPath);
                while (current != null)
                {
                    var projectFile = Path.Combine(current.FullName, "TestCaseEditorApp.csproj");
                    if (File.Exists(projectFile))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }

            return Directory.GetCurrentDirectory();
        }
        
        /// <summary>
        /// Create a new project with proper warning if another project is currently open
        /// </summary>
        public async Task<bool> CreateNewProjectWithWarningAsync(string workspaceName, string projectName, string projectSavePath, string documentPath, string? anythingLLMWorkspaceSlug = null, int? jamaProjectId = null, string? jamaProjectName = null)
        {
            try
            {
                // Check if a real project is currently open (not just the auto-generated Default Workspace)
                if (_currentWorkspaceInfo != null && 
                    !string.IsNullOrEmpty(_currentWorkspaceInfo.Name) && 
                    _currentWorkspaceInfo.Name != "Default Workspace" &&
                    !_currentWorkspaceInfo.Path.Contains("default-workspace"))
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
                        return false;
                    }
                }
                
                // Proceed with project creation
                return await CompleteProjectCreationAsync(workspaceName, projectName, projectSavePath, documentPath, anythingLLMWorkspaceSlug, jamaProjectId, jamaProjectName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create new project with warning: {ProjectName}", projectName);
                
                PublishEvent(new NewProjectEvents.ProjectOperationError 
                { 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                
                ShowNotification($"Error creating project: {ex.Message}", DomainNotificationType.Error);
                return false;
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
            var workspace = new Workspace { 
                Name = workspaceName,
                ImportSource = "Manual"  // 🎯 Empty/manual workspace
            };
            
            var projectOpenedEvent = new NewProjectEvents.ProjectOpened 
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

        #region Form Persistence (Architectural Compliance)
        
        /// <summary>
        /// Saves draft project information for form persistence.
        /// Maintains user experience while preserving architectural integrity.
        /// </summary>
        public void SaveDraftProjectInfo(string? projectName, string? projectPath, string? requirementsPath)
        {
            _draftProjectName = projectName;
            _draftProjectPath = projectPath;
            _draftRequirementsPath = requirementsPath;
            
            _logger.LogDebug("Saved draft project info - Name: {Name}, Path: {Path}, Requirements: {Requirements}", 
                projectName, projectPath, requirementsPath);
        }
        
        /// <summary>
        /// Retrieves draft project information for new ViewModels.
        /// Allows form persistence without violating fail-fast architecture.
        /// </summary>
        public (string? projectName, string? projectPath, string? requirementsPath) GetDraftProjectInfo()
        {
            _logger.LogDebug("Retrieved draft project info - Name: {Name}, Path: {Path}, Requirements: {Requirements}", 
                _draftProjectName, _draftProjectPath, _draftRequirementsPath);
                
            return (_draftProjectName, _draftProjectPath, _draftRequirementsPath);
        }
        
        /// <summary>
        /// Clears draft project information when project is created or cancelled.
        /// </summary>
        public void ClearDraftProjectInfo()
        {
            _logger.LogDebug("Clearing draft project info");
            _draftProjectName = null;
            _draftProjectPath = null;
            _draftRequirementsPath = null;
        }

        /// <summary>
        /// Handle broadcast notifications from other domains
        /// </summary>
        public void HandleBroadcastNotification<T>(T notification) where T : class
        {
            _logger.LogDebug("Received broadcast notification: {NotificationType}", typeof(T).Name);
            
            switch (notification)
            {
                case WorkspaceContextChanged workspaceChanged:
                    HandleWorkspaceContextChanged(workspaceChanged);
                    break;
                    
                case TestCaseEditorApp.MVVM.Domains.OpenProject.Events.OpenProjectEvents.ProjectOpened projectOpened:
                    HandleProjectOpened(projectOpened);
                    break;
                    
                default:
                    _logger.LogDebug("Unhandled notification type: {NotificationType}", typeof(T).Name);
                    break;
            }
        }
        
        /// <summary>
        /// Handle project opened events from OpenProjectMediator to track current workspace
        /// </summary>
        private void HandleProjectOpened(TestCaseEditorApp.MVVM.Domains.OpenProject.Events.OpenProjectEvents.ProjectOpened notification)
        {
            _logger.LogInformation("🔔 NewProjectMediator received ProjectOpened event: {WorkspaceName} ({WorkspacePath})", 
                notification.WorkspaceName, notification.WorkspacePath);
                
            // Update our workspace tracking to match the opened project
            _currentWorkspaceInfo = new WorkspaceInfo
            {
                Name = notification.WorkspaceName,
                Path = notification.WorkspacePath,
                LastModified = DateTime.Now,
                AnythingLLMSlug = notification.AnythingLLMWorkspaceSlug
            };
            
            // Clear workspace context cache to ensure fresh data from new project
            var workspaceContextService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.IWorkspaceContext>();
            if (workspaceContextService is TestCaseEditorApp.Services.WorkspaceContextService contextService)
            {
                _logger.LogInformation("🔄 Refreshing workspace context cache for opened project");
                contextService.NotifyWorkspaceChanged(null, notification.Workspace, TestCaseEditorApp.Services.WorkspaceChangeType.Loaded);
            }
            
            // Clear view configuration to ensure fresh routing based on new workspace data
            var viewConfigService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.ViewConfigurationService>();
            if (viewConfigService != null)
            {
                _logger.LogInformation("🔄 Clearing view configuration for fresh routing");
                viewConfigService.ClearCurrentConfiguration();
            }
            
            _logger.LogInformation("✅ NewProjectMediator workspace tracking updated for: {WorkspaceName}", 
                notification.WorkspaceName);
        }

        /// <summary>
        /// Handle workspace context changes from other domains (e.g., requirement edits)
        /// </summary>
        private void HandleWorkspaceContextChanged(WorkspaceContextChanged notification)
        {
            _logger.LogDebug("Handling workspace context change: {ChangeType} from {Domain}", 
                notification.ChangeType, notification.OriginatingDomain);
                
            // Mark workspace as having unsaved changes when other domains report data changes
            if (notification.ChangeType == "RequirementDataChanged" && _currentWorkspaceInfo != null)
            {
                _currentWorkspaceInfo.HasUnsavedChanges = true;
                _logger.LogDebug("Workspace marked as dirty due to {ChangeType} from {Domain}", 
                    notification.ChangeType, notification.OriginatingDomain);
                    
                // Publish workspace management event so other components know about the state change
                PublishEvent(new NewProjectEvents.WorkspaceDirtyStateChanged
                {
                    HasUnsavedChanges = true,
                    Source = notification.OriginatingDomain
                });
            }
        }
        
        #endregion

        #region Jama Connect Integration

        /// <summary>
        /// Test connection to Jama Connect service
        /// </summary>
        public async Task<(bool Success, string Message)> TestJamaConnectionAsync()
        {
            try
            {
                if (!_jamaConnectService.IsConfigured)
                {
                    return (false, "Jama not configured. Set environment variables: JAMA_BASE_URL, JAMA_CLIENT_ID, JAMA_CLIENT_SECRET");
                }

                var result = await _jamaConnectService.TestConnectionAsync();
                _logger.LogInformation($"[NewProject] Jama connection test: {result.Success}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NewProject] Failed to test Jama connection");
                return (false, $"Error testing connection: {ex.Message}");
            }
        }

        /// <summary>
        /// Get available Jama projects
        /// </summary>
        public async Task<List<JamaProject>> GetJamaProjectsAsync()
        {
            try
            {
                if (!_jamaConnectService.IsConfigured)
                {
                    throw new InvalidOperationException("Jama not configured. Set environment variables: JAMA_BASE_URL, JAMA_CLIENT_ID, JAMA_CLIENT_SECRET");
                }

                var projects = await _jamaConnectService.GetProjectsAsync(CancellationToken.None);
                _logger.LogInformation($"[NewProject] Retrieved {projects.Count} Jama projects");
                return projects;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NewProject] Failed to get Jama projects");
                throw;
            }
        }

        /// <summary>
        /// Get requirements from a specific Jama project with enhanced field data population
        /// </summary>
        public async Task<List<Requirement>> GetJamaRequirementsAsync(int projectId)
        {
            try
            {
                var jamaItems = await _jamaConnectService.GetRequirementsAsync(projectId, CancellationToken.None);
                var requirements = await _jamaConnectService.ConvertToRequirementsWithEnumDecodingAsync(jamaItems, projectId, CancellationToken.None);
                _logger.LogInformation($"[NewProject] Retrieved {requirements.Count} requirements from Jama project {projectId} with enhanced field data");
                return requirements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[NewProject] Failed to get requirements from Jama project {projectId}");
                throw;
            }
        }

        /// <summary>
        /// Import requirements from Jama and create a JSON requirements file for standard processing pipeline
        /// </summary>
        public async Task<string> ImportJamaRequirementsAsync(int projectId, string projectName, string projectKey)
        {
            try
            {
                // Get the requirements data from Jama with enhanced field population
                var jamaItems = await _jamaConnectService.GetRequirementsAsync(projectId, CancellationToken.None);
                var requirements = await _jamaConnectService.ConvertToRequirementsWithEnumDecodingAsync(jamaItems, projectId, CancellationToken.None);

                // Set source project information
                foreach (var req in requirements)
                {
                    if (string.IsNullOrEmpty(req.Project))
                    {
                        req.Project = projectName;
                    }
                }

                // Create a temporary JSON file that can be processed by SmartRequirementImporter
                var tempPath = Path.Combine(Path.GetTempPath(), $"JamaRequirements_{projectKey}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                // Create a workspace object to serialize (this is the standard format)
                var workspace = new Workspace
                {
                    Name = $"Jama Import - {projectName}",
                    Version = Workspace.SchemaVersion,
                    CreatedBy = Environment.UserName,
                    CreatedUtc = DateTime.UtcNow,
                    LastSavedUtc = DateTime.UtcNow,
                    JamaProject = projectId.ToString(), // 🎯 Store project ID for attachment scanning
                    JamaTestPlan = projectName, // Store project name for display
                    Requirements = requirements,
                    SourceDocPath = $"Jama Project: {projectName} ({projectKey})",
                    ImportSource = "Jama"  // 🎯 Definitive flag for view routing
                };

                // 🔍 DEBUG: Log the ImportSource before saving
                _logger.LogInformation($"🔍 DEBUG: About to save Jama workspace with ImportSource = '{workspace.ImportSource}'");
                Console.WriteLine($"🔍 DEBUG: About to save Jama workspace with ImportSource = '{workspace.ImportSource}'");

                // Save workspace using the standard WorkspaceService.Save method for consistency
                global::WorkspaceService.Save(tempPath, workspace);

                // 🔍 DEBUG: Verify the saved file contains ImportSource
                var savedJson = File.ReadAllText(tempPath);
                if (savedJson.Contains("ImportSource"))
                {
                    _logger.LogInformation("✅ DEBUG: Saved JSON contains ImportSource field");
                    Console.WriteLine("✅ DEBUG: Saved JSON contains ImportSource field");
                }
                else
                {
                    _logger.LogError("❌ DEBUG: Saved JSON missing ImportSource field!");
                    Console.WriteLine("❌ DEBUG: Saved JSON missing ImportSource field!");
                    Console.WriteLine($"Saved JSON snippet: {savedJson.Substring(0, Math.Min(500, savedJson.Length))}");
                }

                _logger.LogInformation($"[NewProject] Successfully imported {requirements.Count} requirements from Jama project {projectName} to {tempPath}");
                return tempPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[NewProject] Failed to import requirements from Jama project {projectName}");
                throw;
            }
        }

        public async Task NotifyConnectionErrorAsync(string message)
        {
            // Publish a simple notification event that the notification workspace can listen for
            // This keeps it simple - just a lightweight error notification
            PublishEvent(new ConnectionErrorNotification
            {
                Message = message,
                Timestamp = DateTime.UtcNow,
                Source = "Jama Connect"
            });
            
            await Task.CompletedTask; // Keep it async for interface compliance
        }

        #endregion


    }

    /// <summary>
    /// Simple notification for connection errors
    /// </summary>
    public class ConnectionErrorNotification
    {
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cross-domain request for showing workspace selection modal
    /// </summary>
    public class ShowWorkspaceSelectionModalRequest
    {
        public bool ForOpenExisting { get; set; }
        public string DomainContext { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cross-domain request for navigating to a section
    /// </summary>
    public class NavigateToSectionRequest
    {
        public string SectionName { get; set; } = string.Empty;
        public string? Context { get; set; }
    }
}
