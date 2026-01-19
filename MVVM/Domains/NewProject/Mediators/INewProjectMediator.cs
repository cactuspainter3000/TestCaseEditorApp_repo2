using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.Mediators
{
    /// <summary>
    /// Interface for the Workspace Management domain mediator.
    /// Handles project lifecycle operations: create, open, save, close, and workspace management.
    /// </summary>
    public interface INewProjectMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to WorkspaceManagement domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from WorkspaceManagement domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish events within WorkspaceManagement domain
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;
        
        /// <summary>
        /// Current step in the WorkspaceManagement workflow
        /// </summary>
        string? CurrentStep { get; }
        
        /// <summary>
        /// Current ViewModel being displayed in this domain
        /// </summary>
        object? CurrentViewModel { get; }
        
        /// <summary>
        /// Whether this mediator is properly registered and ready
        /// </summary>
        bool IsRegistered { get; }
        
        /// <summary>
        /// Domain name for this mediator
        /// </summary>
        string DomainName { get; }
        
        /// <summary>
        /// Import additional requirements to existing project (append mode)
        /// </summary>
        Task ImportAdditionalRequirementsAsync();
        
        /// <summary>
        /// Mark this mediator as registered for fail-fast validation
        /// </summary>
        void MarkAsRegistered();
        
        // ===== CROSS-DOMAIN COMMUNICATION =====
        
        /// <summary>
        /// Request action from another domain
        /// </summary>
        void RequestCrossDomainAction<T>(T request) where T : class;
        
        /// <summary>
        /// Broadcast notification to all domains
        /// </summary>
        void BroadcastToAllDomains<T>(T notification) where T : class;
        
        // ===== NAVIGATION WITHIN DOMAIN =====
        
        /// <summary>
        /// Navigate to initial step in workspace management workflow
        /// </summary>
        void NavigateToInitialStep();
        
        /// <summary>
        /// Navigate to final step in workspace management workflow  
        /// </summary>
        void NavigateToFinalStep();
        
        /// <summary>
        /// Navigate to a specific step in the workflow
        /// </summary>
        void NavigateToStep(string stepName, object? context = null);
        
        /// <summary>
        /// Check if can navigate back in workflow
        /// </summary>
        bool CanNavigateBack();
        
        /// <summary>
        /// Check if can navigate forward in workflow
        /// </summary>
        bool CanNavigateForward();
        
        // ===== DOMAIN-SPECIFIC OPERATIONS =====
        
        /// <summary>
        /// Initiate new project creation workflow
        /// </summary>
        Task CreateNewProjectAsync();
        
        /// <summary>
        /// Initiate existing project opening workflow
        /// </summary>
        Task OpenProjectAsync();
        
        /// <summary>
        /// Save the current project
        /// </summary>
        Task SaveProjectAsync();
        
        /// <summary>
        /// Undo the last save operation by restoring from backup
        /// </summary>
        Task UndoLastSaveAsync();
        
        /// <summary>
        /// Check if undo is available for the current project
        /// </summary>
        bool CanUndoLastSave();
        
        /// <summary>
        /// Close the current project
        /// </summary>
        Task CloseProjectAsync();
        
        /// <summary>
        /// Show workspace selection dialog for opening existing project
        /// </summary>
        void ShowWorkspaceSelectionForOpen();
        
        /// <summary>
        /// Show workspace selection dialog for creating new project
        /// </summary>
        void ShowWorkspaceSelectionForNew();
        
        /// <summary>
        /// Handle workspace selection completion
        /// </summary>
        Task OnWorkspaceSelectedAsync(string workspaceSlug, string workspaceName, bool isNewProject);
        
        /// <summary>
        /// Get current workspace information
        /// </summary>
        WorkspaceInfo? GetCurrentWorkspaceInfo();
        
        /// <summary>
        /// Check if there are unsaved changes
        /// </summary>
        bool HasUnsavedChanges();
        
        // ===== UI COORDINATION =====
        
        /// <summary>
        /// Show progress for workspace operations
        /// </summary>
        void ShowProgress(string message, double percentage = 0);
        
        /// <summary>
        /// Update progress for workspace operations
        /// </summary>
        void UpdateProgress(string message, double percentage);
        
        /// <summary>
        /// Hide progress indicator
        /// </summary>
        void HideProgress();
        
        /// <summary>
        /// Show notification with domain context
        /// </summary>
        void ShowNotification(string message, DomainNotificationType type = DomainNotificationType.Info);
        
        /// <summary>
        /// Complete project creation with workspace details and document import
        /// </summary>
        Task<bool> CompleteProjectCreationAsync(string workspaceName, string projectName, string projectSavePath, string documentPath);
        
        /// <summary>
        /// Create a new project with proper warning dialog if another project is currently open
        /// </summary>
        Task<bool> CreateNewProjectWithWarningAsync(string workspaceName, string projectName, string projectSavePath, string documentPath);
        
        /// <summary>
        /// Show save file dialog with protection against overwriting currently open project
        /// Returns tuple of (success, filePath, projectName)
        /// </summary>
        (bool Success, string FilePath, string ProjectName) ShowSaveProjectDialog(string currentProjectName);
        
        // ===== FORM PERSISTENCE (ARCHITECTURAL COMPLIANCE) =====
        
        /// <summary>
        /// Saves draft project information for form persistence while maintaining architectural integrity.
        /// </summary>
        void SaveDraftProjectInfo(string? projectName, string? projectPath, string? requirementsPath);
        
        /// <summary>
        /// Retrieves draft project information for new ViewModels.
        /// Allows form persistence without violating fail-fast architecture.
        /// </summary>
        (string? projectName, string? projectPath, string? requirementsPath) GetDraftProjectInfo();
        
        /// <summary>
        /// Clears draft project information when project is created or cancelled.
        /// </summary>
        void ClearDraftProjectInfo();
        
        // ===== JAMA CONNECT INTEGRATION =====
        
        /// <summary>
        /// Test connection to Jama Connect service
        /// </summary>
        Task<(bool Success, string Message)> TestJamaConnectionAsync();
        
        /// <summary>
        /// Get available Jama projects
        /// </summary>
        Task<List<JamaProject>> GetJamaProjectsAsync();
        
        /// <summary>
        /// Get requirements from a specific Jama project
        /// </summary>
        Task<List<Requirement>> GetJamaRequirementsAsync(int projectId);
        
        /// <summary>
        /// Import requirements from Jama and create temporary file
        /// </summary>
        Task<string> ImportJamaRequirementsAsync(int projectId, string projectName, string projectKey);
    }
    
    /// <summary>
    /// Workspace information structure
    /// </summary>
    public class WorkspaceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? AnythingLLMSlug { get; set; }
        public bool HasUnsavedChanges { get; set; }
        public DateTime LastModified { get; set; }
    }
}