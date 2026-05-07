using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Result of constraint processing operation
    /// </summary>
    public class ConstraintProcessingResult
    {
        public string ProcessingId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public IFilledForm OriginalForm { get; set; } = null!;
        public IFormTemplate Template { get; set; } = null!;
        
        public ConstraintProcessingAction ProcessingAction { get; set; }
        public ConstraintProcessingStatus ProcessingStatus { get; set; }
        public string ActionReason { get; set; } = string.Empty;
        
        public List<ConstraintViolation> HardViolations { get; set; } = new();
        public List<ConstraintViolation> SoftViolations { get; set; } = new();
        public List<ConstraintViolation> FlaggedIssues { get; set; } = new();
        
        public ConstraintViolationReport ViolationReport { get; set; } = null!;
        
        // Performance metrics
        public TimeSpan ProcessingTime { get; set; }
        public int TotalConstraintsEvaluated { get; set; }
    }

    /// <summary>
    /// Actions that can be taken based on constraint processing
    /// </summary>
    public enum ConstraintProcessingAction
    {
        Accept,    // No violations or only flagged issues - proceed normally
        Retry,     // Soft violations - attempt correction with feedback
        Reject     // Hard violations - stop processing and return error
    }

    /// <summary>
    /// Status of constraint processing
    /// </summary>
    public enum ConstraintProcessingStatus
    {
        Accepted,            // All constraints satisfied
        AcceptedWithFlags,   // Minor issues flagged but processing continues
        RequiresRetry,       // Soft violations need correction
        Rejected             // Hard violations prevent processing
    }

    /// <summary>
    /// Individual constraint violation details
    /// </summary>
    public class ConstraintViolation
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldConstraintType ConstraintType { get; set; }
        public ConstraintViolationType ViolationType { get; set; }
        public string Description { get; set; } = string.Empty;
        public ValidationSeverity Severity { get; set; }
        
        // Contextual information for remediation
        public string? ExpectedFormat { get; set; }
        public string? ExpectedRange { get; set; }
        public string[]? ValidOptions { get; set; }
        public object? ActualValue { get; set; }
        public object? SuggestedValue { get; set; }
        
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Types of constraint violations
    /// </summary>
    public enum ConstraintViolationType
    {
        MissingRequired,     // Required field is empty or missing
        FormatValidation,    // Value doesn't match expected format/pattern
        ValueOutOfRange,     // Numeric/date value outside allowed range
        InvalidOption,       // Value not in list of valid options
        QualityThreshold,    // Response quality below acceptable threshold
        GeneralValidation    // Other validation rule failures
    }

    /// <summary>
    /// Comprehensive violation report
    /// </summary>
    public class ConstraintViolationReport
    {
        public string ReportId { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        
        // Summary statistics
        public int TotalViolations { get; set; }
        public int HardRejectCount { get; set; }
        public int SoftRetryCount { get; set; }
        public int FlagOnlyCount { get; set; }
        
        // Detailed violations
        public List<ConstraintViolation> Violations { get; set; } = new();
        
        // Human-readable summary
        public string Summary { get; set; } = string.Empty;
        
        // Compliance metrics
        public double OverallComplianceScore { get; set; }
        public Dictionary<FieldCriticality, double> ComplianceByFieldType { get; set; } = new();
    }

    /// <summary>
    /// Result of retry operation
    /// </summary>
    public class RetryResult
    {
        public string RetryId { get; set; } = string.Empty;
        public DateTime InitiatedAt { get; set; }
        
        public IFilledForm OriginalForm { get; set; } = null!;
        public List<ConstraintViolation> ViolationsToResolve { get; set; } = new();
        
        // Generated retry prompt for LLM
        public string RetryPrompt { get; set; } = string.Empty;
        
        // Specific recommendations per violation
        public List<RetryRecommendation> Recommendations { get; set; } = new();
        
        public RetryStatus Status { get; set; }
        public int MaxRetryAttempts { get; set; } = 3;
        public int CurrentAttemptNumber { get; set; } = 1;
    }

    /// <summary>
    /// Status of retry operation
    /// </summary>
    public enum RetryStatus
    {
        Ready,           // Retry prompt generated and ready
        InProgress,      // Retry in progress
        Resolved,        // All violations resolved
        MaxAttemptsExceeded,  // Exceeded retry limit
        Escalated        // Manual intervention required
    }

    /// <summary>
    /// Specific recommendation for resolving a constraint violation
    /// </summary>
    public class RetryRecommendation
    {
        public string FieldName { get; set; } = string.Empty;
        public string Issue { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
        public RetryPriority Priority { get; set; }
        
        // Examples of correct values
        public List<string> CorrectExamples { get; set; } = new();
        
        // Automated suggestion if available
        public object? SuggestedValue { get; set; }
    }

    /// <summary>
    /// Priority level for retry recommendations
    /// </summary>
    public enum RetryPriority
    {
        Low,      // Minor issues that don't affect core functionality
        Medium,   // Important improvements that should be addressed
        High,     // Critical issues that must be resolved
        Critical  // Blocking issues preventing any progress
    }

    /// <summary>
    /// Degradation strategy configuration
    /// </summary>
    public class DegradationStrategy
    {
        public string StrategyName { get; set; } = string.Empty;
        
        // Field-specific constraint overrides
        public Dictionary<string, FieldConstraintType> FieldConstraintOverrides { get; set; } = new();
        
        // Quality thresholds for different contexts
        public double MinAcceptableQuality { get; set; } = 0.7;
        public double RetryQualityThreshold { get; set; } = 0.5;
        
        // Retry configuration
        public int MaxRetryAttempts { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        
        // Fallback behavior
        public ConstraintFallbackBehavior FallbackBehavior { get; set; } = ConstraintFallbackBehavior.AcceptPartial;
    }

    /// <summary>
    /// Fallback behavior when constraints cannot be satisfied
    /// </summary>
    public enum ConstraintFallbackBehavior
    {
        AcceptPartial,    // Accept partial completion with flagged issues
        UseDefaults,      // Fill missing fields with default values
        EscalateToHuman,  // Require manual intervention
        Reject            // Completely reject the response
    }

    /// <summary>
    /// Performance metrics for constraint processing
    /// </summary>
    public class ConstraintProcessingMetrics
    {
        public string MetricsId { get; set; } = string.Empty;
        public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
        
        // Processing performance
        public TimeSpan TotalProcessingTime { get; set; }
        public TimeSpan ConstraintEvaluationTime { get; set; }
        public TimeSpan ReportGenerationTime { get; set; }
        
        // Violation statistics
        public int TotalFieldsEvaluated { get; set; }
        public int TotalViolationsFound { get; set; }
        public Dictionary<ConstraintViolationType, int> ViolationTypeBreakdown { get; set; } = new();
        
        // Retry statistics
        public int RetryAttemptsRequired { get; set; }
        public double RetrySuccessRate { get; set; }
        
        // Quality metrics
        public double OverallComplianceRate { get; set; }
        public double ProcessingEfficiency { get; set; }
    }

    /// <summary>
    /// Concrete implementation of IFormValidationResult
    /// </summary>
    public class FormValidationResult : IFormValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> FieldViolations { get; set; } = new();
        public List<string> WarningMessages { get; set; } = new();
        public double ComplianceScore { get; set; }
        public ICriticalityScore CriticalityScore { get; set; } = new CriticalityScore();
        public bool RequiresRetry { get; set; }
        public bool RequiresManualReview { get; set; }
    }

    /// <summary>
    /// Concrete implementation of IFilledForm
    /// </summary>
    public class FilledForm : IFilledForm
    {
        public Dictionary<string, object> FieldValues { get; set; } = new();
        public IFormValidationResult ValidationResult { get; set; } = new FormValidationResult();
        public DateTime CompletedAt { get; set; }
        public string SourceContext { get; set; } = string.Empty;
        public double CompletionScore { get; set; }
    }
}