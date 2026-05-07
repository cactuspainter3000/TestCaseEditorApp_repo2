using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Events
{
    /// <summary>
    /// Event definitions for the Requirements domain.
    /// Following architectural guide patterns for domain event organization.
    /// </summary>
    public class RequirementsEvents
    {
        /// <summary>
        /// Published when requirements are imported from external sources
        /// </summary>
        public class RequirementsImported
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string SourceFile { get; set; } = string.Empty;
            public string ImportMethod { get; set; } = string.Empty;
            public TimeSpan ImportDuration { get; set; }
            public DateTime ImportTime { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when requirement import fails
        /// </summary>
        public class RequirementsImportFailed
        {
            public string FilePath { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public string? FormatAnalysis { get; set; }
            public Exception? Exception { get; set; }
        }

        /// <summary>
        /// Published when a requirement is selected for viewing/editing
        /// </summary>
        public class RequirementSelected
        {
            public Requirement Requirement { get; set; } = null!;
            public string SelectedBy { get; set; } = string.Empty;
            public DateTime SelectedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when a requirement is updated/modified
        /// </summary>
        public class RequirementUpdated
        {
            public Requirement Requirement { get; set; } = null!;
            public List<string> ModifiedFields { get; set; } = new();
            public string UpdatedBy { get; set; } = string.Empty;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when requirement analysis starts
        /// </summary>
        public class RequirementAnalysisStarted
        {
            public Requirement Requirement { get; set; } = null!;
            public string AnalysisType { get; set; } = string.Empty;
            public DateTime StartedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when requirement analysis completes
        /// </summary>
        public class RequirementAnalyzed
        {
            public Requirement Requirement { get; set; } = null!;
            public RequirementAnalysis? Analysis { get; set; }
            public bool Success { get; set; }
            public TimeSpan AnalysisTime { get; set; }
            public string? ErrorMessage { get; set; }
            public DateTime CompletedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when RAG analysis fails and falls back to traditional LLM
        /// </summary>
        public class RAGAnalysisFallback
        {
            public Requirement Requirement { get; set; } = null!;
            public string FailureReason { get; set; } = string.Empty;
            public string FailureDetails { get; set; } = string.Empty;
            public string RecommendedAction { get; set; } = string.Empty;
            public DateTime OccurredAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when the requirements collection changes (add/remove/clear)
        /// </summary>
        public class RequirementsCollectionChanged
        {
            public string Action { get; set; } = string.Empty; // Add, Remove, Clear, Import
            public List<Requirement> AffectedRequirements { get; set; } = new();
            public int NewCount { get; set; }
            public DateTime ChangedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when requirements are exported
        /// </summary>
        public class RequirementsExported
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string ExportType { get; set; } = string.Empty;
            public string OutputPath { get; set; } = string.Empty;
            public bool Success { get; set; }
            public TimeSpan ExportTime { get; set; }
            public DateTime ExportedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when domain workflow state changes (dirty, analyzing, etc.)
        /// </summary>
        public class WorkflowStateChanged
        {
            public string PropertyName { get; set; } = string.Empty;
            public object? NewValue { get; set; }
            public object? OldValue { get; set; }
            public DateTime ChangedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when batch operations start (batch analysis, etc.)
        /// </summary>
        public class BatchOperationStarted
        {
            public string OperationType { get; set; } = string.Empty;
            public List<Requirement> TargetRequirements { get; set; } = new();
            public DateTime StartedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when batch operations complete
        /// </summary>
        public class BatchOperationCompleted
        {
            public string OperationType { get; set; } = string.Empty;
            public List<Requirement> TargetRequirements { get; set; } = new();
            public int SuccessCount { get; set; }
            public int FailureCount { get; set; }
            public List<string> Errors { get; set; } = new();
            public TimeSpan Duration { get; set; }
            public DateTime CompletedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when navigation to Requirements Search in Attachments is requested
        /// Following Architectural Guide AI patterns for domain navigation events
        /// </summary>
        public class NavigateToAttachmentSearch
        {
            public string TargetView { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Published to update attachment scanning progress
        /// </summary>
        public class AttachmentScanProgress
        {
            public string ProgressText { get; set; } = string.Empty;
            public int ProjectId { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when attachment scanning starts
        /// </summary>
        public class AttachmentScanStarted
        {
            public int ProjectId { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public DateTime StartTime { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when attachment scanning completes
        /// </summary>
        public class AttachmentScanCompleted
        {
            public int ProjectId { get; set; }
            public int AttachmentCount { get; set; }
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public TimeSpan Duration { get; set; }
            public DateTime CompletedTime { get; set; } = DateTime.Now;
            public List<JamaAttachment> Attachments { get; set; } = new();
        }

        /// <summary>
        /// Published when Document Scraper functionality becomes available for Word document imports
        /// </summary>
        public class DocumentScraperAvailable
        {
            public string WorkspaceName { get; set; } = string.Empty;
            public string ProjectPath { get; set; } = string.Empty;
            public string ImportSource { get; set; } = string.Empty;
            public bool HasJamaAssociation { get; set; }
            public DateTime AvailableTime { get; set; } = DateTime.Now;
        }
        /// <summary>
        /// Published when document parsing starts
        /// </summary>
        public class DocumentParsingStarted
        {
            public string DocumentName { get; set; } = string.Empty;
            public int AttachmentId { get; set; }
            public int ProjectId { get; set; }
            public DateTime StartTime { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published to update document parsing progress
        /// </summary>
        public class DocumentParsingProgress
        {
            public string DocumentName { get; set; } = string.Empty;
            public int AttachmentId { get; set; }
            public string StatusMessage { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Published when document parsing completes
        /// </summary>
        public class DocumentParsingCompleted
        {
            public string DocumentName { get; set; } = string.Empty;
            public int AttachmentId { get; set; }
            public int RequirementsFound { get; set; }
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public TimeSpan Duration { get; set; }
            public DateTime CompletedTime { get; set; } = DateTime.Now;
            public List<Requirement> ExtractedRequirements { get; set; } = new();
        }

        /// <summary>
        /// Published when document parsing is canceled
        /// </summary>
        public class DocumentParsingCanceled
        {
            public string DocumentName { get; set; } = string.Empty;
            public int AttachmentId { get; set; }
            public DateTime CanceledTime { get; set; } = DateTime.Now;
        }    }

    /// <summary>
    /// Progress data for attachment scanning operations
    /// Used with IProgress<T> for real-time progress reporting
    /// </summary>
    public class AttachmentScanProgressData
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public int Percentage => Total > 0 ? (int)((double)Current / Total * 100) : 0;
        public List<JamaAttachment> CurrentAttachments { get; set; } = new();
    }
}