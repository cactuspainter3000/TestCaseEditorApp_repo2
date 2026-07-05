using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using static TestCaseEditorApp.MVVM.Events.CrossDomainMessages;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators
{
    /// <summary>
    /// Mediator for the Open Project domain.
    /// Coordinates project opening operations: file selection, loading, validation.
    /// Provides domain-specific UI coordination and cross-domain communication.
    /// </summary>
    public class OpenProjectMediator : BaseDomainMediator<OpenProjectEvents>, IOpenProjectMediator
    {
        private readonly AnythingLLMService _anythingLLMService;
        private readonly IJamaConnectService _jamaConnectService;
        private readonly TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator _newProjectMediator;

        public OpenProjectMediator(
            ILogger<OpenProjectMediator> logger,
            IDomainUICoordinator uiCoordinator,
            AnythingLLMService anythingLLMService,
            IJamaConnectService jamaConnectService,
            TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator newProjectMediator,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Open Project", performanceMonitor, eventReplay)
        {
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
        }

        /// <summary>
        /// Override BroadcastToAllDomains to also publish ProjectOpened events locally.
        /// This ensures UI infrastructure ViewModels (like TitleViewModel) that subscribe 
        /// directly to this mediator also receive the event.
        /// </summary>
        public override void BroadcastToAllDomains<T>(T notification)
        {
            // Broadcast to other mediators via DomainCoordinator
            base.BroadcastToAllDomains(notification);
            
            // Also publish locally for direct subscribers (UI infrastructure ViewModels)
            if (notification is OpenProjectEvents.ProjectOpened projectOpened)
            {
                _logger.LogDebug("[OpenProjectMediator] Publishing ProjectOpened locally for direct subscribers: {ProjectName}", 
                    projectOpened.WorkspaceName);
                PublishEvent(projectOpened);
            }
        }

        public override void NavigateToInitialStep()
        {
            _logger.LogDebug("[OpenProjectMediator] NavigateToInitialStep - Navigating to main view");
            NavigateToStep("Main", null);
        }

        public override void NavigateToFinalStep()
        {
            _logger.LogDebug("[OpenProjectMediator] NavigateToFinalStep");
        }

        public override bool CanNavigateBack()
        {
            return false; // Open project is single-step workflow
        }

        public override bool CanNavigateForward()
        {
            return false; // Open project is single-step workflow
        }

        /// <summary>
        /// Start open project workflow - navigate to UI instead of immediately showing dialog
        /// </summary>
        public async Task OpenProjectAsync()
        {
            try
            {
                _logger.LogInformation("Starting open project workflow - navigating to UI");
                
                // Publish domain event
                PublishEvent(new OpenProjectEvents.ProjectOpenStarted());
                
                // Navigate to the main view instead of immediately showing file dialog
                NavigateToInitialStep();
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start open project workflow");
                PublishEvent(new OpenProjectEvents.ProjectOpenFailed 
                { 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                ShowNotification($"Failed to start open project workflow: {ex.Message}", DomainNotificationType.Error);
            }
        }

        public async Task<bool> OpenProjectFileAsync(string filePath)
        {
            try
            {
                ShowProgress("Loading project file...", 25);

                // Validate file exists and is accessible
                if (!await ValidateProjectFileAsync(filePath))
                {
                    return false;
                }

                ShowProgress("Reading project data...", 50);

                // Load workspace using existing service (same as NewProjectMediator)
                var workspace = WorkspaceFileManager.Load(filePath);
                if (workspace == null)
                {
                    PublishEvent(new OpenProjectEvents.ProjectOpenFailed 
                    { 
                        FilePath = filePath, 
                        ErrorMessage = "Failed to load workspace data" 
                    });
                    ShowNotification("Failed to load project file. The file may be corrupted or invalid.", DomainNotificationType.Error);
                    return false;
                }

                // If this workspace is associated with Jama, Jama is the source of truth.
                // Pull requirements on open and persist them to the workspace before broadcasting.
                var resolvedJamaProjectId = await ResolveJamaProjectIdAsync(workspace);
                int? liveRequirementCount = null;
                int? liveTestCaseCount = null;

                if (resolvedJamaProjectId.HasValue)
                {
                    if (!_jamaConnectService.IsConfigured)
                    {
                        var error = "This project is associated with Jama, but Jama Connect is not configured. Open is blocked to prevent stale requirement data.";
                        _logger.LogError("❌ {Error}", error);
                        PublishEvent(new OpenProjectEvents.ProjectOpenFailed
                        {
                            FilePath = filePath,
                            ErrorMessage = error
                        });
                        ShowNotification(error, DomainNotificationType.Error);
                        return false;
                    }

                    ShowProgress("Syncing requirements from Jama...", 65);

                    List<Requirement> jamaRequirements;
                    try
                    {
                        jamaRequirements = await _newProjectMediator.GetJamaRequirementsAsync(resolvedJamaProjectId.Value);
                    }
                    catch (Exception ex)
                    {
                        var error = $"Failed to refresh requirements from Jama project {resolvedJamaProjectId.Value}: {ex.Message}";
                        _logger.LogError(ex, "❌ {Error}", error);
                        PublishEvent(new OpenProjectEvents.ProjectOpenFailed
                        {
                            FilePath = filePath,
                            ErrorMessage = error,
                            Exception = ex
                        });
                        ShowNotification(error, DomainNotificationType.Error);
                        return false;
                    }

                    workspace.Requirements = jamaRequirements ?? new List<Requirement>();
                    workspace.JamaProjectId = resolvedJamaProjectId.Value;
                    workspace.JamaProject = resolvedJamaProjectId.Value.ToString();
                    workspace.ImportSource = "Jama";

                    try
                    {
                        var projects = await _jamaConnectService.GetProjectsAsync(CancellationToken.None);
                        var matchingProject = projects.FirstOrDefault(p => p.Id == resolvedJamaProjectId.Value);
                        if (matchingProject != null)
                        {
                            workspace.JamaProjectName = matchingProject.Name;
                            if (string.IsNullOrWhiteSpace(workspace.JamaTestPlan))
                            {
                                workspace.JamaTestPlan = matchingProject.Name;
                            }

                            foreach (var req in workspace.Requirements.Where(r => string.IsNullOrWhiteSpace(r.Project)))
                            {
                                req.Project = matchingProject.Name;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not refresh Jama project metadata for project {ProjectId}", resolvedJamaProjectId.Value);
                    }

                    try
                    {
                        var (requirementTypeSuccess, requirementItemType) = await _jamaConnectService.GetRequirementItemTypeAsync(resolvedJamaProjectId.Value, CancellationToken.None);
                        if (requirementTypeSuccess && requirementItemType.HasValue)
                        {
                            liveRequirementCount = await _jamaConnectService.GetProjectItemCountAsync(
                                resolvedJamaProjectId.Value,
                                requirementItemType.Value,
                                CancellationToken.None);
                        }

                        var (testCaseTypeSuccess, testCaseItemType) = await _jamaConnectService.GetTestCaseItemTypeAsync(resolvedJamaProjectId.Value, CancellationToken.None);
                        if (testCaseTypeSuccess && testCaseItemType.HasValue)
                        {
                            liveTestCaseCount = await _jamaConnectService.GetProjectItemCountAsync(
                                resolvedJamaProjectId.Value,
                                testCaseItemType.Value,
                                CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not refresh live Jama counts for project {ProjectId}", resolvedJamaProjectId.Value);
                    }

                    WorkspaceFileManager.Save(filePath, workspace);

                    _logger.LogInformation("✅ Jama sync on open complete for project {ProjectId}. Requirements refreshed: {Count}",
                        resolvedJamaProjectId.Value,
                        workspace.Requirements.Count);

                    ShowNotification(
                        $"Refreshed {workspace.Requirements.Count} requirements from Jama project {resolvedJamaProjectId.Value}.",
                        DomainNotificationType.Success);
                }
                else
                {
                    _logger.LogWarning(
                        "Skipping Jama sync on open. No project ID could be resolved. ImportSource={ImportSource}, JamaProjectId={JamaProjectId}, JamaProject='{JamaProject}', JamaProjectName='{JamaProjectName}', JamaTestPlan='{JamaTestPlan}', RequirementCountInWorkspace={RequirementCount}",
                        workspace.ImportSource ?? "<none>",
                        workspace.JamaProjectId?.ToString() ?? "<none>",
                        workspace.JamaProject ?? "<none>",
                        workspace.JamaProjectName ?? "<none>",
                        workspace.JamaTestPlan ?? "<none>",
                        workspace.Requirements?.Count ?? 0);
                }

                var workspaceRequirementCount = workspace.Requirements?.Count ?? 0;
                var workspaceTestCaseCount = workspace.Requirements?.Sum(r => r.GeneratedTestCases?.Count ?? 0) ?? 0;
                var effectiveRequirementCount = liveRequirementCount ?? workspaceRequirementCount;
                var effectiveTestCaseCount = liveTestCaseCount ?? workspaceTestCaseCount;

                // Persist normalized/open-time workspace state even when Jama sync is skipped.
                // This keeps the workspace file aligned with the latest resolved metadata.
                try
                {
                    WorkspaceFileManager.Save(filePath, workspace);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not persist workspace updates on open for {FilePath}", filePath);
                }

                ShowProgress("Setting up workspace...", 75);

                // Extract project name from file path (same logic as NewProjectMediator)
                var projectName = Path.GetFileNameWithoutExtension(filePath);
                if (projectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    projectName = Path.GetFileNameWithoutExtension(projectName);
                }

                // Publish workspace loaded event
                PublishEvent(new OpenProjectEvents.WorkspaceLoaded 
                { 
                    Workspace = workspace,
                    RequirementCount = effectiveRequirementCount,
                    TestCaseCount = effectiveTestCaseCount
                });

                ShowProgress("Finalizing...", 90);

                // Determine AnythingLLM workspace slug by querying AnythingLLM service directly
                string? anythingLLMSlug = null;
                try 
                {
                    _logger.LogDebug("Looking up AnythingLLM workspace for project: {ProjectName}", projectName);
                    var workspaces = await _anythingLLMService.GetWorkspacesAsync();
                    var matchingWorkspace = workspaces?.FirstOrDefault(w => 
                        string.Equals(w.Name, projectName, StringComparison.OrdinalIgnoreCase));
                    
                    if (matchingWorkspace != null)
                    {
                        anythingLLMSlug = matchingWorkspace.Slug;
                        _logger.LogInformation("Found AnythingLLM workspace for project '{ProjectName}': '{WorkspaceName}' (slug: '{Slug}')", 
                            projectName, matchingWorkspace.Name, anythingLLMSlug);
                    }
                    else
                    {
                        _logger.LogInformation("No AnythingLLM workspace found matching project name: {ProjectName}", projectName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query AnythingLLM workspaces for project: {ProjectName}", projectName);
                }

                // Create the project opened event
                var projectOpenedEvent = new OpenProjectEvents.ProjectOpened
                {
                    WorkspacePath = filePath,
                    WorkspaceName = projectName,
                    AnythingLLMWorkspaceSlug = anythingLLMSlug,
                    Workspace = workspace,
                    RequirementCount = effectiveRequirementCount,
                    TestCaseCount = effectiveTestCaseCount
                };

                // Broadcast to other domains that project was opened (using same structure as NewProjectMediator)
                _logger.LogInformation("📡 Broadcasting ProjectOpened event to other domains: {ProjectName}", projectName);
                BroadcastToAllDomains(projectOpenedEvent);

                // Publish domain-specific success event (same event, but for internal domain subscribers)
                PublishEvent(projectOpenedEvent);

                ShowProgress("Complete", 100);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to open project file: {filePath}");
                PublishEvent(new OpenProjectEvents.ProjectOpenFailed 
                { 
                    FilePath = filePath, 
                    ErrorMessage = ex.Message, 
                    Exception = ex 
                });
                ShowNotification($"Failed to open project: {ex.Message}", DomainNotificationType.Error);
                return false;
            }
        }

        public Task<bool> ValidateProjectFileAsync(string filePath)
        {
            try
            {
                // Check file exists
                if (!File.Exists(filePath))
                {
                    ShowNotification($"Selected project file does not exist: {filePath}", DomainNotificationType.Error);
                    return Task.FromResult(false);
                }

                // Check file extension
                if (!filePath.EndsWith(".tcex.json", StringComparison.OrdinalIgnoreCase) && 
                    !filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    ShowNotification("Please select a valid Test Case Editor project file (.tcex.json)", DomainNotificationType.Error);
                    return Task.FromResult(false);
                }

                // Basic validation - try to load and see if it works
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating project file: {filePath}");
                ShowNotification($"Could not validate project file: {ex.Message}", DomainNotificationType.Error);
                return Task.FromResult(false);
            }
        }

        public Workspace? GetCurrentWorkspace()
        {
            // For open project, we don't maintain state - delegate to persistence service
            return null; // This would need to be implemented if needed
        }

        private async Task<int?> ResolveJamaProjectIdAsync(Workspace workspace)
        {
            if (workspace.JamaProjectId.HasValue && workspace.JamaProjectId.Value > 0)
            {
                return workspace.JamaProjectId.Value;
            }

            if (!string.IsNullOrWhiteSpace(workspace.JamaProject) && int.TryParse(workspace.JamaProject, out var parsedProjectId) && parsedProjectId > 0)
            {
                return parsedProjectId;
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

            var configuredProjectId = TryResolveConfiguredJamaProjectId();
            if (configuredProjectId.HasValue)
            {
                var workspaceClaimsJamaAssociation =
                    string.Equals(workspace.ImportSource, "Jama", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(workspace.JamaProject) ||
                    !string.IsNullOrWhiteSpace(workspace.JamaProjectName) ||
                    !string.IsNullOrWhiteSpace(workspace.JamaTestPlan) ||
                    (workspace.Requirements?.Any(r => !string.IsNullOrWhiteSpace(r.Project)) ?? false);

                if (workspaceClaimsJamaAssociation)
                {
                    _logger.LogInformation(
                        "Resolved Jama project ID from configured settings/environment fallback: {ProjectId}",
                        configuredProjectId.Value);
                    return configuredProjectId.Value;
                }
            }

            if (candidateNames.Count == 0 || !_jamaConnectService.IsConfigured)
            {
                return null;
            }

            var projects = await _jamaConnectService.GetProjectsAsync();
            var matchingProject = projects.FirstOrDefault(project =>
                candidateNames.Any(candidate =>
                    string.Equals(project.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project.Key, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(project.Id.ToString(), candidate, StringComparison.OrdinalIgnoreCase)));

            return matchingProject?.Id;
        }

        private int? TryResolveConfiguredJamaProjectId()
        {
            var envProjectId = (Environment.GetEnvironmentVariable("JAMA_PROJECT_ID") ?? string.Empty).Trim();
            if (int.TryParse(envProjectId, out var parsedEnvProjectId) && parsedEnvProjectId > 0)
            {
                return parsedEnvProjectId;
            }

            try
            {
                var settingsService = App.ServiceProvider?.GetService<IUserSettingsService>();
                var settingsProjectId = settingsService?.LoadSettings()?.JamaProjectId?.Trim();
                if (int.TryParse(settingsProjectId, out var parsedSettingsProjectId) && parsedSettingsProjectId > 0)
                {
                    return parsedSettingsProjectId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read Jama project ID from settings service fallback");
            }

            return null;
        }
    }
}