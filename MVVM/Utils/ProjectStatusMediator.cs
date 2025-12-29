using System;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Mediator for Project status updates across ViewModels.
    /// Provides decoupled communication when project state changes.
    /// Uses the same pattern as AnythingLLMMediator for consistency.
    /// </summary>
    public static class ProjectStatusMediator
    {
        public delegate void ProjectStatusUpdatedEventHandler(ProjectStatus status);
        
        /// <summary>
        /// Fired when project status has been updated.
        /// </summary>
        public static event ProjectStatusUpdatedEventHandler? ProjectStatusUpdated;
        
        /// <summary>
        /// Stores the last known status for late subscribers
        /// </summary>
        private static ProjectStatus? _lastStatus;

        /// <summary>
        /// Notify all subscribers that project status has changed.
        /// </summary>
        /// <param name="status">The current project status</param>
        public static void NotifyProjectStatusUpdated(ProjectStatus status)
        {
            _lastStatus = status; // Store for late subscribers
            ProjectStatusUpdated?.Invoke(status);
        }
        
        /// <summary>
        /// Requests the current status to be broadcast. Used when ViewModels are created 
        /// after initial status updates have already occurred.
        /// </summary>
        public static void RequestCurrentStatus()
        {
            if (_lastStatus != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ProjectStatusMediator] Broadcasting last known status: IsOpen={_lastStatus.IsProjectOpen}, Name={_lastStatus.ProjectName}");
                ProjectStatusUpdated?.Invoke(_lastStatus);
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[ProjectStatusMediator] No previous status available to broadcast");
            }
        }
    }

    /// <summary>
    /// Represents the current status of project
    /// </summary>
    public class ProjectStatus
    {
        public bool IsProjectOpen { get; set; }
        public string ProjectName { get; set; } = "";
        public int TestCaseCount { get; set; } = 0;
    }
}