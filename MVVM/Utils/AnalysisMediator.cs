using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Mediator for analysis-related events across ViewModels.
    /// Provides decoupled communication when analysis data is updated.
    /// </summary>
    public static class AnalysisMediator
    {
        public delegate void AnalysisUpdatedEventHandler(Requirement requirement);
        
        /// <summary>
        /// Fired when a requirement's analysis has been updated.
        /// </summary>
        public static event AnalysisUpdatedEventHandler? AnalysisUpdated;

        /// <summary>
        /// Notify all subscribers that a requirement's analysis has been updated.
        /// </summary>
        /// <param name="requirement">The requirement whose analysis was updated</param>
        public static void NotifyAnalysisUpdated(Requirement requirement)
        {
            AnalysisUpdated?.Invoke(requirement);
        }
    }
}
