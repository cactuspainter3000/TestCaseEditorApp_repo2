using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
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
        private readonly IPersistenceService _persistenceService;
        private readonly IFileDialogService _fileDialogService;
        private readonly AnythingLLMService _anythingLLMService;
        private readonly NotificationService _notificationService;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        private readonly IWorkspaceValidationService _workspaceValidationService;

        public OpenProjectMediator(
            ILogger<OpenProjectMediator> logger,
            IDomainUICoordinator uiCoordinator,
            IPersistenceService persistenceService,
            IFileDialogService fileDialogService,
            AnythingLLMService anythingLLMService,
            NotificationService notificationService,
            ITestCaseGenerationMediator testCaseGenerationMediator,
            IWorkspaceValidationService workspaceValidationService,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Open Project", performanceMonitor, eventReplay)
        {
            _persistenceService = persistenceService ?? throw new ArgumentNullException(nameof(persistenceService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _workspaceValidationService = workspaceValidationService ?? throw new ArgumentNullException(nameof(workspaceValidationService));
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
                    RequirementCount = workspace.Requirements?.Count ?? 0,
                    TestCaseCount = 0 // Workspace doesn't track test cases directly
                });

                ShowProgress("Finalizing...", 90);

                // Create the project opened event
                var projectOpenedEvent = new OpenProjectEvents.ProjectOpened
                {
                    WorkspacePath = filePath,
                    WorkspaceName = projectName,
                    Workspace = workspace
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

        public async Task<bool> ValidateProjectFileAsync(string filePath)
        {
            try
            {
                // Check file exists
                if (!File.Exists(filePath))
                {
                    ShowNotification($"Selected project file does not exist: {filePath}", DomainNotificationType.Error);
                    return false;
                }

                // Check file extension
                if (!filePath.EndsWith(".tcex.json", StringComparison.OrdinalIgnoreCase) && 
                    !filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    ShowNotification("Please select a valid Test Case Editor project file (.tcex.json)", DomainNotificationType.Error);
                    return false;
                }

                // Basic validation - try to load and see if it works
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating project file: {filePath}");
                ShowNotification($"Could not validate project file: {ex.Message}", DomainNotificationType.Error);
                return false;
            }
        }

        public Workspace? GetCurrentWorkspace()
        {
            // For open project, we don't maintain state - delegate to persistence service
            return null; // This would need to be implemented if needed
        }
    }
}