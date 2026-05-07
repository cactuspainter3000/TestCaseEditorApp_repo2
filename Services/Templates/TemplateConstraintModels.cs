using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Comprehensive result of template processing with constraint validation
    /// </summary>
    public class TemplateConstraintResult
    {
        // Core processing information
        public string ProcessingId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public TimeSpan TotalProcessingTime { get; set; }
        
        // Input/Template information
        public IFormTemplate Template { get; set; } = null!;
        public DegradationStrategy Strategy { get; set; } = null!;
        public string OriginalLlmResponse { get; set; } = string.Empty;
        
        // Parsing results
        public bool ParsingSucceeded { get; set; }
        public IFilledForm? ParsedForm { get; set; }
        
        // Constraint processing results
        public ConstraintProcessingResult? ConstraintProcessing { get; set; }
        
        // Processing decision and status
        public TemplateProcessingDecision ProcessingDecision { get; set; }
        public TemplateProcessingStatus ProcessingStatus { get; set; }
        public string DecisionReason { get; set; } = string.Empty;
        
        // Degradation handling
        public bool RequiresDegradation { get; set; }
        public bool DegradationApplied { get; set; }
        public DateTime? DegradationStartedAt { get; set; }
        public DateTime? DegradationCompletedAt { get; set; }
        public TimeSpan? DegradationTime { get; set; }
        public List<string> DegradationActions { get; set; } = new();
        
        // Retry handling
        public bool IsRetryAttempt { get; set; }
        public List<RetryRecord> RetryHistory { get; set; } = new();
        public TemplateConstraintResult? PreviousResult { get; set; }
        
        // Quality and compliance reporting
        public string? QualityReport { get; set; }
        public string? EscalationSummary { get; set; }
        
        // Error handling
        public string? ErrorMessage { get; set; }
        public Exception? Exception { get; set; }
        
        // Computed properties for easy access
        public bool IsSuccessful => ProcessingStatus == TemplateProcessingStatus.Completed || 
                                    ProcessingStatus == TemplateProcessingStatus.AcceptedWithDegradation;
        
        public bool HasViolations => ConstraintProcessing?.HardViolations.Any() == true || 
                                    ConstraintProcessing?.SoftViolations.Any() == true;
        
        public int TotalViolations => (ConstraintProcessing?.HardViolations.Count ?? 0) + 
                                     (ConstraintProcessing?.SoftViolations.Count ?? 0) + 
                                     (ConstraintProcessing?.FlaggedIssues.Count ?? 0);
        
        public double ComplianceScore => ConstraintProcessing?.ViolationReport?.OverallComplianceScore ?? 0.0;
    }

    /// <summary>
    /// Processing decision based on constraint evaluation
    /// </summary>
    public enum TemplateProcessingDecision
    {
        Accept,                    // No issues - proceed normally
        Retry,                     // Soft violations - attempt correction
        Reject,                    // Hard violations - cannot proceed
        AcceptWithDegradation     // Apply fallback strategy
    }

    /// <summary>
    /// Current status of template processing
    /// </summary>
    public enum TemplateProcessingStatus
    {
        InProgress,               // Processing ongoing
        Completed,                // Successfully completed with no issues
        AcceptedWithDegradation, // Completed with applied degradation strategy
        RequiresRetry,           // Needs retry due to soft violations
        Rejected,                // Rejected due to hard violations
        ParsingFailed,           // Could not parse LLM response
        EscalatedToHuman,       // Requires manual intervention
        Failed                   // Processing failed due to system error
    }

    /// <summary>
    /// Record of a retry attempt
    /// </summary>
    public class RetryRecord
    {
        public int AttemptNumber { get; set; }
        public DateTime InitiatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration => CompletedAt - InitiatedAt;
        
        public int ViolationsAddressed { get; set; }
        public string RetryPrompt { get; set; } = string.Empty;
        public string RetryResponse { get; set; } = string.Empty;
        
        public bool Successful { get; set; }
        public string? FailureReason { get; set; }
        
        // Comparison metrics
        public int ViolationsResolved { get; set; }
        public int NewViolationsIntroduced { get; set; }
        public double ImprovementScore { get; set; }
    }

    /// <summary>
    /// Configuration for field-level constraint enforcement
    /// </summary>
    public class FieldConstraintConfiguration
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldConstraintType ConstraintType { get; set; }
        
        // Override settings for specific contexts
        public Dictionary<string, FieldConstraintType> ContextOverrides { get; set; } = new();
        
        // Quality thresholds
        public double? MinQualityThreshold { get; set; }
        public double? RetryQualityThreshold { get; set; }
        
        // Custom validation rules
        public List<string> CustomValidationRules { get; set; } = new();
        
        // Default/fallback values
        public object? DefaultValue { get; set; }
        public bool AllowDefaultValue { get; set; } = false;
    }

    /// <summary>
    /// Context-aware degradation strategy selector
    /// </summary>
    public class DegradationStrategySelector
    {
        private readonly Dictionary<string, DegradationStrategy> _strategies = new();
        private readonly DegradationStrategy _defaultStrategy;

        public DegradationStrategySelector()
        {
            _defaultStrategy = CreateDefaultStrategy();
            InitializeStandardStrategies();
        }

        public DegradationStrategy SelectStrategy(string? context = null, IFormTemplate? template = null)
        {
            if (context != null && _strategies.ContainsKey(context))
            {
                return _strategies[context];
            }
            
            // Template-based selection
            if (template != null)
            {
                var templateStrategy = SelectStrategyForTemplate(template);
                if (templateStrategy != null)
                {
                    return templateStrategy;
                }
            }
            
            return _defaultStrategy;
        }

        private void InitializeStandardStrategies()
        {
            // High-stakes scenario - strict enforcement
            _strategies["high-stakes"] = new DegradationStrategy
            {
                StrategyName = "HighStakes",
                MinAcceptableQuality = 0.95,
                RetryQualityThreshold = 0.8,
                MaxRetryAttempts = 5,
                FallbackBehavior = ConstraintFallbackBehavior.EscalateToHuman
            };

            // Development/testing - permissive
            _strategies["development"] = new DegradationStrategy
            {
                StrategyName = "Development",
                MinAcceptableQuality = 0.5,
                RetryQualityThreshold = 0.3,
                MaxRetryAttempts = 2,
                FallbackBehavior = ConstraintFallbackBehavior.AcceptPartial
            };

            // Production - balanced
            _strategies["production"] = new DegradationStrategy
            {
                StrategyName = "Production",
                MinAcceptableQuality = 0.8,
                RetryQualityThreshold = 0.6,
                MaxRetryAttempts = 3,
                FallbackBehavior = ConstraintFallbackBehavior.UseDefaults
            };

            // Demo/prototype - very permissive
            _strategies["demo"] = new DegradationStrategy
            {
                StrategyName = "Demo",
                MinAcceptableQuality = 0.3,
                RetryQualityThreshold = 0.2,
                MaxRetryAttempts = 1,
                FallbackBehavior = ConstraintFallbackBehavior.UseDefaults
            };
        }

        private DegradationStrategy? SelectStrategyForTemplate(IFormTemplate template)
        {
            // Select strategy based on template characteristics
            var requiredFields = template.Fields.Count(f => f.Criticality == FieldCriticality.Required);
            var totalFields = template.Fields.Count;
            var criticalityRatio = (double)requiredFields / totalFields;

            if (criticalityRatio > 0.8) // High criticality template
            {
                return _strategies["high-stakes"];
            }
            else if (criticalityRatio < 0.3) // Low criticality template
            {
                return _strategies["development"];
            }

            return null; // Use default
        }

        private DegradationStrategy CreateDefaultStrategy()
        {
            return new DegradationStrategy
            {
                StrategyName = "Default",
                MinAcceptableQuality = 0.7,
                RetryQualityThreshold = 0.5,
                MaxRetryAttempts = 3,
                RetryDelay = TimeSpan.FromSeconds(1),
                FallbackBehavior = ConstraintFallbackBehavior.AcceptPartial
            };
        }
    }

    /// <summary>
    /// Comprehensive constraint processing report for monitoring and analytics
    /// </summary>
    public class ConstraintProcessingSummary
    {
        public DateTime ReportGeneratedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan ReportingPeriod { get; set; }
        
        // Overall statistics
        public int TotalProcessingRequests { get; set; }
        public int SuccessfulProcessing { get; set; }
        public int RequiredRetries { get; set; }
        public int RejectedRequests { get; set; }
        public int EscalatedToHuman { get; set; }
        
        // Performance metrics
        public TimeSpan AverageProcessingTime { get; set; }
        public TimeSpan AverageRetryTime { get; set; }
        public double AverageComplianceScore { get; set; }
        public double RetrySuccessRate { get; set; }
        
        // Violation analysis
        public Dictionary<ConstraintViolationType, int> ViolationTypeFrequency { get; set; } = new();
        public Dictionary<FieldConstraintType, int> ConstraintTypeBreakdown { get; set; } = new();
        public List<string> MostProblematicFields { get; set; } = new();
        
        // Quality trends
        public List<QualityTrendPoint> QualityTrends { get; set; } = new();
        public double QualityImprovementRate { get; set; }
        
        // Recommendations
        public List<string> OptimizationRecommendations { get; set; } = new();
    }

    /// <summary>
    /// Quality trend data point for analytics
    /// </summary>
    public class QualityTrendPoint
    {
        public DateTime Timestamp { get; set; }
        public double ComplianceScore { get; set; }
        public int ViolationCount { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public string? Context { get; set; }
    }

    /// <summary>
    /// Event arguments for constraint processing events
    /// </summary>
    public class ConstraintProcessingEventArgs : EventArgs
    {
        public string ProcessingId { get; set; } = string.Empty;
        public ConstraintProcessingEventType EventType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Message { get; set; }
        public object? Data { get; set; }
    }

    /// <summary>
    /// Types of constraint processing events
    /// </summary>
    public enum ConstraintProcessingEventType
    {
        ProcessingStarted,
        ConstraintViolationDetected,
        RetryInitiated,
        DegradationApplied,
        ProcessingCompleted,
        ProcessingFailed,
        EscalationTriggered
    }
}