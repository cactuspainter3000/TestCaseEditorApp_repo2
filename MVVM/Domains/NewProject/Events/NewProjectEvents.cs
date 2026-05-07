using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.Events
{
    /// <summary>
    /// Workspace Management domain events for type-safe communication within the domain.
    /// This includes project lifecycle operations: create, open, save, close, and workspace management.
    /// </summary>
    public class NewProjectEvents
    {
        /// <summary>
        /// Fired when navigation changes within workspace management workflow
        /// </summary>
        public class StepChanged
        {
            public string Step { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project creation workflow is started
        /// </summary>
        public class ProjectCreationStarted
        {
            public string WorkspaceName { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project is successfully created
        /// </summary>
        public class ProjectCreated
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public string WorkspaceName { get; set; } = string.Empty;
            public string? AnythingLLMWorkspaceSlug { get; set; }
            public Workspace? Workspace { get; set; }
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
        /// Fired when a project is successfully opened
        /// </summary>
        public class ProjectOpened
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public string WorkspaceName { get; set; } = string.Empty;
            public string? AnythingLLMWorkspaceSlug { get; set; }
            public Workspace Workspace { get; set; } = default!;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project save operation is initiated
        /// </summary>
        public class ProjectSaveStarted
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project is successfully saved
        /// </summary>
        public class ProjectSaved
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project close operation is initiated
        /// </summary>
        public class ProjectCloseStarted
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a project is successfully closed
        /// </summary>
        public class ProjectClosed
        {
            public string WorkspacePath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when workspace selection modal is requested
        /// </summary>
        public class WorkspaceSelectionRequested
        {
            public bool IsOpenExisting { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a workspace is selected from the selection modal
        /// </summary>
        public class WorkspaceSelected
        {
            public string WorkspaceSlug { get; set; } = string.Empty;
            public string WorkspaceName { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when an error occurs during project operations
        /// </summary>
        public class ProjectOperationError
        {
            public string Operation { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Fired when project requirements state changes (loaded/cleared/imported)
        /// </summary>
        public class RequirementsStateChanged
        {
            public bool HasRequirements { get; set; }
            public int RequirementCount { get; set; }
            public string Action { get; set; } = string.Empty; // "Imported", "AdditionalImported", "Cleared"
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Fired when requirements are successfully imported from Jama
        /// </summary>
        public class RequirementsImported
        {
            public string ProjectName { get; set; } = string.Empty;
            public int RequirementCount { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when workspace data is modified and needs to be saved
        /// </summary>
        public class WorkspaceModified
        {
            public string Reason { get; set; } = string.Empty; // e.g., "RequirementUpdated", "TestCaseAdded"
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Fired when workspace dirty state changes (has unsaved changes)
        /// </summary>
        public class WorkspaceDirtyStateChanged
        {
            public bool HasUnsavedChanges { get; set; }
            public string Source { get; set; } = string.Empty; // Which domain triggered the change
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a new project is created with Jama import, so other domains can react
        /// </summary>
        public class JamaProjectCreated
        {
            public int JamaProjectId { get; set; }
            public string JamaProjectName { get; set; } = string.Empty;
            public string ProjectName { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when any new project is successfully created, regardless of import source
        /// </summary>
        public class ProjectCreatedWithWorkspace
        {
            public string ProjectName { get; set; } = string.Empty;
            public string WorkspaceName { get; set; } = string.Empty;
            public string ProjectPath { get; set; } = string.Empty;
            public bool IsJamaImport { get; set; }
            public int? JamaProjectId { get; set; }
            public string? JamaProjectName { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
}