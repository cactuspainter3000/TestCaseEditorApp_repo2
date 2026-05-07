using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Provides consistent access to workspace context across all domains.
    /// Solves cross-domain data access problems by centralizing workspace state management.
    /// </summary>
    public interface IWorkspaceContext
    {
        /// <summary>
        /// The currently loaded workspace, or null if no workspace is loaded.
        /// Automatically loaded and cached from the workspace file.
        /// </summary>
        Workspace? CurrentWorkspace { get; }
        
        /// <summary>
        /// Information about the current workspace (path, name, etc.), or null if no workspace is loaded.
        /// </summary>
        WorkspaceInfo? CurrentWorkspaceInfo { get; }
        
        /// <summary>
        /// True if a workspace is currently loaded and available.
        /// </summary>
        bool HasWorkspace { get; }
        
        /// <summary>
        /// Fired when the workspace changes (load, unload, or content changes).
        /// </summary>
        event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;
        
        /// <summary>
        /// Refresh the workspace data from disk (for when external changes occur).
        /// </summary>
        Task RefreshAsync();
    }
    
    /// <summary>
    /// Event arguments for workspace change notifications.
    /// </summary>
    public class WorkspaceChangedEventArgs : EventArgs
    {
        public Workspace? PreviousWorkspace { get; init; }
        public Workspace? CurrentWorkspace { get; init; }
        public WorkspaceInfo? WorkspaceInfo { get; init; }
        public WorkspaceChangeType ChangeType { get; init; }
    }
    
    /// <summary>
    /// Types of workspace changes that can occur.
    /// </summary>
    public enum WorkspaceChangeType
    {
        Loaded,
        Unloaded,
        ContentChanged,
        Refreshed
    }
}