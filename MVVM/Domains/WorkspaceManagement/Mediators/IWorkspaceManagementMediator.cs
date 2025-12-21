using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators
{
    /// <summary>
    /// Interface for the Workspace Management domain mediator.
    /// Handles project lifecycle operations: create, open, save, close, and workspace management.
    /// </summary>
    public interface IWorkspaceManagementMediator
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