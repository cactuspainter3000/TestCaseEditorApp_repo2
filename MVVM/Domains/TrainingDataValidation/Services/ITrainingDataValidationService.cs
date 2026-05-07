using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services
{
    /// <summary>
    /// Represents the current state of the validation workflow
    /// </summary>
    public enum ValidationWorkflowState
    {
        Ready,
        Generating,
        Validating,
        Complete,
        Error,
        Paused
    }
    /// <summary>
    /// Service interface for managing training data validation workflows
    /// </summary>
    public interface ITrainingDataValidationService
    {
        /// <summary>
        /// Records a human validation decision for a synthetic training example
        /// </summary>
        Task RecordValidationAsync(ValidationResult validationResult);

        /// <summary>
        /// Saves the current validation session state for resuming later
        /// </summary>
        Task SaveValidationSessionAsync(ValidationSession session);

        /// <summary>
        /// Loads a previously saved validation session
        /// </summary>
        Task<ValidationSession?> LoadValidationSessionAsync(string sessionId);

        /// <summary>
        /// Gets all validation sessions for the current user
        /// </summary>
        Task<List<ValidationSession>> GetUserValidationSessionsAsync();

        /// <summary>
        /// Exports validated training data to the specified format
        /// </summary>
        Task ExportTrainingDataAsync(List<SyntheticTrainingExample> approvedExamples, string outputPath);

        /// <summary>
        /// Gets validation statistics and metrics
        /// </summary>
        Task<ValidationMetrics> GetValidationMetricsAsync();

        /// <summary>
        /// Analyzes validation patterns to identify improvement opportunities
        /// </summary>
        Task<ValidationAnalysis> AnalyzeValidationPatternsAsync();

        /// <summary>
        /// Validates the quality of a synthetic training example
        /// </summary>
        Task<QualityAssessment> AssessExampleQualityAsync(SyntheticTrainingExample example);
    }

    /// <summary>
    /// Represents a human validation decision
    /// </summary>
    public class ValidationResult
    {
        public string ExampleId { get; set; } = string.Empty;
        public ValidationDecision Decision { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime ValidatedAt { get; set; }
        public string ValidatedBy { get; set; } = string.Empty;
        public SyntheticTrainingExample? OriginalExample { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public double ConfidenceScore { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// Types of validation decisions
    /// </summary>
    public enum ValidationDecision
    {
        Approved,
        Rejected,
        RequiresEdits,
        Skipped,
        Flagged
    }

    /// <summary>
    /// Represents a saved validation workflow session
    /// </summary>
    public class ValidationSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<SyntheticTrainingExample> PendingExamples { get; set; } = new();
        public List<ValidationResult> CompletedValidations { get; set; } = new();
        public int CurrentIndex { get; set; }
        public ValidationWorkflowState WorkflowState { get; set; }
        public Dictionary<string, object> SessionMetadata { get; set; } = new();
    }

    /// <summary>
    /// Validation metrics and statistics
    /// </summary>
    public class ValidationMetrics
    {
        public int TotalExamplesValidated { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int RequiresEditsCount { get; set; }
        public int SkippedCount { get; set; }
        public double ApprovalRate => TotalExamplesValidated > 0 ? (double)ApprovedCount / TotalExamplesValidated : 0;
        public double RejectionRate => TotalExamplesValidated > 0 ? (double)RejectedCount / TotalExamplesValidated : 0;
        public TimeSpan AverageValidationTime { get; set; }
        public Dictionary<string, int> CategoryBreakdown { get; set; } = new();
        public DateTime? LastValidationDate { get; set; }
    }

    /// <summary>
    /// Analysis of validation patterns and trends
    /// </summary>
    public class ValidationAnalysis
    {
        public List<string> CommonRejectionReasons { get; set; } = new();
        public List<string> QualityImprovementSuggestions { get; set; } = new();
        public Dictionary<string, double> CategoryPerformance { get; set; } = new();
        public List<ValidationTrend> Trends { get; set; } = new();
        public double OverallValidationConsistency { get; set; }
    }

    /// <summary>
    /// Represents a validation trend over time
    /// </summary>
    public class ValidationTrend
    {
        public string Metric { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public double Value { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    /// <summary>
    /// Quality assessment result for a training example
    /// </summary>
    public class QualityAssessment
    {
        public double OverallScore { get; set; }
        public Dictionary<string, double> DimensionScores { get; set; } = new();
        public List<string> StrengthAreas { get; set; } = new();
        public List<string> ImprovementAreas { get; set; } = new();
        public bool MeetsThreshold { get; set; }
        public string Assessment { get; set; } = string.Empty;
    }
}