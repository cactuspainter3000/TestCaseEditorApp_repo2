using System.Text.Json;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of IWorkspaceContext that provides centralized workspace state management.
    /// Handles loading, caching, and change notification for workspace data across all domains.
    /// </summary>
    public class WorkspaceContextService : IWorkspaceContext
    {
        private readonly INewProjectMediator _workspaceManagementMediator;
        private readonly ILogger<WorkspaceContextService> _logger;
        
        private Workspace? _cachedWorkspace;
        private WorkspaceInfo? _cachedWorkspaceInfo;
        private DateTime _lastLoadTime = DateTime.MinValue;
        private readonly object _lock = new object();
        
        public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;
        
        public WorkspaceContextService(
            INewProjectMediator workspaceManagementMediator,
            ILogger<WorkspaceContextService> logger)
        {
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public Workspace? CurrentWorkspace
        {
            get
            {
                lock (_lock)
                {
                    EnsureWorkspaceLoaded();
                    return _cachedWorkspace;
                }
            }
        }
        
        public WorkspaceInfo? CurrentWorkspaceInfo
        {
            get
            {
                lock (_lock)
                {
                    EnsureWorkspaceInfoLoaded();
                    return _cachedWorkspaceInfo;
                }
            }
        }
        
        public bool HasWorkspace => CurrentWorkspace != null;
        
        public async Task RefreshAsync()
        {
            Workspace? previousWorkspace;
            Workspace? newWorkspace;
            WorkspaceInfo? workspaceInfo;
            
            lock (_lock)
            {
                previousWorkspace = _cachedWorkspace;
                _cachedWorkspace = null;
                _cachedWorkspaceInfo = null;
                _lastLoadTime = DateTime.MinValue;
                
                EnsureWorkspaceLoaded();
                newWorkspace = _cachedWorkspace;
                workspaceInfo = _cachedWorkspaceInfo;
            }
            
            // Fire change event outside of lock
            WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs
            {
                PreviousWorkspace = previousWorkspace,
                CurrentWorkspace = newWorkspace,
                WorkspaceInfo = workspaceInfo,
                ChangeType = WorkspaceChangeType.Refreshed
            });
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Internal method to notify of workspace changes from external sources.
        /// Called by workspace management mediators when workspace changes.
        /// </summary>
        internal void NotifyWorkspaceChanged(Workspace? previousWorkspace, Workspace? newWorkspace, WorkspaceChangeType changeType)
        {
            lock (_lock)
            {
                _cachedWorkspace = newWorkspace;
                _cachedWorkspaceInfo = null; // Will be reloaded on next access
                _lastLoadTime = DateTime.UtcNow;
            }
            
            WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs
            {
                PreviousWorkspace = previousWorkspace,
                CurrentWorkspace = newWorkspace,
                WorkspaceInfo = _cachedWorkspaceInfo,
                ChangeType = changeType
            });
        }
        
        private void EnsureWorkspaceLoaded()
        {
            var workspaceInfo = EnsureWorkspaceInfoLoaded();
            if (workspaceInfo == null)
            {
                _cachedWorkspace = null;
                return;
            }
            
            // Check if we need to reload
            if (_cachedWorkspace != null && 
                System.IO.File.Exists(workspaceInfo.Path) && 
                System.IO.File.GetLastWriteTimeUtc(workspaceInfo.Path) <= _lastLoadTime)
            {
                return; // Cache is still valid
            }
            
            // Load workspace from file
            try
            {
                if (System.IO.File.Exists(workspaceInfo.Path))
                {
                    var jsonContent = System.IO.File.ReadAllText(workspaceInfo.Path);
                    _cachedWorkspace = JsonSerializer.Deserialize<Workspace>(jsonContent);
                    _lastLoadTime = DateTime.UtcNow;
                    
                    _logger.LogDebug("Loaded workspace from: {Path}", workspaceInfo.Path);
                }
                else
                {
                    _cachedWorkspace = null;
                    _logger.LogWarning("Workspace file does not exist: {Path}", workspaceInfo.Path);
                }
            }
            catch (Exception ex)
            {
                _cachedWorkspace = null;
                _logger.LogError(ex, "Failed to load workspace from: {Path}", workspaceInfo.Path);
            }
        }
        
        private WorkspaceInfo? EnsureWorkspaceInfoLoaded()
        {
            if (_cachedWorkspaceInfo != null)
            {
                return _cachedWorkspaceInfo;
            }
            
            try
            {
                _cachedWorkspaceInfo = _workspaceManagementMediator.GetCurrentWorkspaceInfo();
                return _cachedWorkspaceInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current workspace info");
                return null;
            }
        }
    }
}