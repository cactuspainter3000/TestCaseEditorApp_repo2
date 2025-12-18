using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Requirements Management domain events for type-safe communication within the requirements domain
    /// </summary>
    public class RequirementsEvents
    {
        /// <summary>
        /// Fired when navigation changes within requirements workflow
        /// </summary>
        public class StepChanged
        {
            public string Step { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements import starts
        /// </summary>
        public class ImportStarted
        {
            public string FilePath { get; set; } = string.Empty;
            public string ImportType { get; set; } = string.Empty; // "Word", "Jama", "Excel", etc.
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements are successfully imported
        /// </summary>
        public class RequirementsImported
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string SourceFile { get; set; } = string.Empty;
            public string ImportType { get; set; } = string.Empty;
            public TimeSpan ImportTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements import fails
        /// </summary>
        public class ImportFailed
        {
            public string FilePath { get; set; } = string.Empty;
            public string ImportType { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a requirement is updated
        /// </summary>
        public class RequirementUpdated
        {
            public Requirement Requirement { get; set; } = default!;
            public string UpdatedBy { get; set; } = string.Empty;
            public List<string> ModifiedFields { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a requirement is selected for editing
        /// </summary>
        public class RequirementSelected
        {
            public Requirement Requirement { get; set; } = default!;
            public string SelectedBy { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirement analysis starts
        /// </summary>
        public class AnalysisStarted
        {
            public Requirement Requirement { get; set; } = default!;
            public string AnalysisType { get; set; } = string.Empty; // "Quality", "Completeness", "Traceability"
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirement analysis completes
        /// </summary>
        public class RequirementAnalyzed
        {
            public Requirement Requirement { get; set; } = default!;
            public RequirementAnalysis? Analysis { get; set; }
            public bool Success { get; set; }
            public TimeSpan AnalysisTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when batch analysis starts
        /// </summary>
        public class BatchAnalysisStarted
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string AnalysisType { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when batch analysis completes
        /// </summary>
        public class BatchAnalysisCompleted
        {
            public List<Requirement> Requirements { get; set; } = new();
            public int SuccessfulAnalyses { get; set; }
            public int FailedAnalyses { get; set; }
            public TimeSpan TotalTime { get; set; }
            public List<string> Errors { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements export starts
        /// </summary>
        public class ExportStarted
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string ExportType { get; set; } = string.Empty; // "CSV", "Excel", "ChatGPT", "Jama"
            public string OutputPath { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements are successfully exported
        /// </summary>
        public class RequirementsExported
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string ExportType { get; set; } = string.Empty;
            public string OutputPath { get; set; } = string.Empty;
            public bool Success { get; set; }
            public TimeSpan ExportTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements export fails
        /// </summary>
        public class ExportFailed
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string ExportType { get; set; } = string.Empty;
            public string OutputPath { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements collection changes (add/remove/clear)
        /// </summary>
        public class RequirementsCollectionChanged
        {
            public string Action { get; set; } = string.Empty; // "Add", "Remove", "Clear", "Replace"
            public List<Requirement> AffectedRequirements { get; set; } = new();
            public int NewCount { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirements are found by search/filter
        /// </summary>
        public class RequirementsFound
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string SearchCriteria { get; set; } = string.Empty;
            public int TotalSearched { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
}