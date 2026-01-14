namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Mediator for project workflow state changes across ViewModels.
    /// Provides decoupled communication when project creation progress is updated.
    /// </summary>
    public static class ProjectWorkflowMediator
    {
        public delegate void WorkflowStateChangedEventHandler(ProjectWorkflowState state);
        
        /// <summary>
        /// Fired when the project workflow state has been updated.
        /// </summary>
        public static event WorkflowStateChangedEventHandler? WorkflowStateChanged;

        /// <summary>
        /// Notify all subscribers that the project workflow state has changed.
        /// </summary>
        /// <param name="state">The current workflow state</param>
        public static void NotifyWorkflowStateChanged(ProjectWorkflowState state)
        {
            WorkflowStateChanged?.Invoke(state);
        }
    }

    /// <summary>
    /// Represents the current state of the project creation workflow
    /// </summary>
    public class ProjectWorkflowState
    {
        public bool CanProceed { get; set; }
        public bool HasWorkspaceName { get; set; }
        public bool IsWorkspaceCreated { get; set; }
        public bool HasSelectedDocument { get; set; }
        public bool HasProjectName { get; set; }
        public bool HasProjectSavePath { get; set; }
    }
}