using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Test Case Generation domain events for type-safe communication within the domain.
    /// This includes the entire "Test Case Generator" menu section: requirements management,
    /// assumptions, questions, test case creation, analysis, and export workflows.
    /// </summary>
    public class TestCaseGenerationEvents
    {
        /// <summary>
        /// Fired when navigation changes within test case generation workflow
        /// </summary>
        public class StepChanged
        {
            public string Step { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a requirement is selected for test case generation
        /// </summary>
        public class RequirementSelected
        {
            public Requirement Requirement { get; set; } = default!;
            public string SelectedBy { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when assumptions are updated for current requirement
        /// </summary>
        public class AssumptionsUpdated
        {
            public List<string> Assumptions { get; set; } = new();
            public Requirement Requirement { get; set; } = default!;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when clarifying questions are answered
        /// </summary>
        public class QuestionsAnswered
        {
            public List<ClarifyingQuestionData> Questions { get; set; } = new();
            public Requirement Requirement { get; set; } = default!;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test case generation starts
        /// </summary>
        public class GenerationStarted
        {
            public Requirement Requirement { get; set; } = default!;
            public VerificationMethod Method { get; set; }
            public List<string> Assumptions { get; set; } = new();
            public List<ClarifyingQuestionData> Questions { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test cases are successfully generated
        /// </summary>
        public class TestCasesGenerated
        {
            public List<TestCase> TestCases { get; set; } = new();
            public Requirement SourceRequirement { get; set; } = default!;
            public VerificationMethod Method { get; set; }
            public string LlmResponse { get; set; } = string.Empty;
            public TimeSpan GenerationTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test case generation fails
        /// </summary>
        public class GenerationFailed
        {
            public Requirement Requirement { get; set; } = default!;
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test cases are validated
        /// </summary>
        public class TestCasesValidated
        {
            public List<TestCase> TestCases { get; set; } = new();
            public bool IsValid { get; set; }
            public List<string> ValidationErrors { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when the support view changes in the Test Case Generator
        /// </summary>
        public class SupportViewChanged
        {
            public string SupportView { get; set; } = string.Empty;
            public bool IsAnalysisView { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test case generation workflow is completed
        /// </summary>
        public class GenerationCompleted
        {
            public Requirement Requirement { get; set; } = default!;
            public List<TestCase> FinalTestCases { get; set; } = new();
            public bool Success { get; set; }
            public TimeSpan TotalTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        // ===== REQUIREMENTS MANAGEMENT EVENTS (Consolidated from RequirementsEvents) =====
        
        /// <summary>
        /// Fired when requirements import starts
        /// </summary>
        public class RequirementsImportStarted
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
        public class RequirementsImportFailed
        {
            public string FilePath { get; set; } = string.Empty;
            public string ImportType { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public string? FormatAnalysis { get; set; }
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when additional requirements are imported in append mode
        /// </summary>
        public class AdditionalRequirementsImported
        {
            public List<Requirement> Requirements { get; set; } = new();
            public int AppendedCount { get; set; }
            public string SourceFile { get; set; } = string.Empty;
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
        /// Fired when a requirement edit is requested (to show editor view)
        /// </summary>
        public class RequirementEditRequested
        {
            public Requirement Requirement { get; set; } = default!;
            public string RequestedBy { get; set; } = string.Empty; // "AnalysisView", "RequirementsGrid", etc.
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when requirement analysis starts
        /// </summary>
        public class RequirementAnalysisStarted
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
        public class RequirementsExportStarted
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
        public class RequirementsExportFailed
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
        
        /// <summary>
        /// Fired when the current requirement changes in domain state
        /// </summary>
        public class RequirementChanged
        {
            public Requirement? Requirement { get; set; }
            public string ChangedBy { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when workflow state properties change (IsDirty, IsBatchAnalyzing, etc.)
        /// </summary>
        public class WorkflowStateChanged
        {
            public string PropertyName { get; set; } = string.Empty;
            public object? NewValue { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Fired when save operation is requested from domain ViewModels
        /// </summary>
        public class SaveRequested
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string RequestedBy { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Fired when refresh operation is requested from domain ViewModels
        /// </summary>
        public class RefreshRequested
        {
            public string RequestedBy { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
}