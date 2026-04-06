using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.Events
{
    /// <summary>
    /// Open Project domain events for type-safe communication within the domain.
    /// This includes project opening operations: file selection, loading, validation.
    /// </summary>
    public class OpenProjectEvents
    {
        /// <summary>
        /// Fired when navigation changes within open project workflow
        /// </summary>
        public class StepChanged
        {
            public string Step { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project opening workflow is started
        /// </summary>
        public class ProjectOpenStarted
        {
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project file is selected for opening
        /// </summary>
        public class ProjectFileSelected
        {
            public string FilePath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project is successfully opened
        /// </summary>
        public class ProjectOpened
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public string WorkspaceName { get; set; } = string.Empty;
            public string? AnythingLLMWorkspaceSlug { get; set; }
            public Workspace? Workspace { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when project opening fails
        /// </summary>
        public class ProjectOpenFailed
        {
            public string? FilePath { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when project loading progresses
        /// </summary>
        public class ProjectLoadProgress
        {
            public string Step { get; set; } = string.Empty;
            public int PercentComplete { get; set; }
            public string? Details { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when workspace information is loaded
        /// </summary>
        public class WorkspaceLoaded
        {
            public Workspace Workspace { get; set; } = new();
            public int RequirementCount { get; set; }
            public int TestCaseCount { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
}