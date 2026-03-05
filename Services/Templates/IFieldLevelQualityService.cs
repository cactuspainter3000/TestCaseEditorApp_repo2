using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Interface for Field-Level Quality Service
    /// Provides field-level performance metrics, retry tracking, and confidence analysis
    /// ARCHITECTURAL COMPLIANCE: Interface-first design for dependency injection
    /// </summary>
    public interface IFieldLevelQualityService
    {
        /// <summary>
        /// Gets comprehensive quality metrics for a specific field
        /// </summary>
        /// <param name="fieldName">Name of the field to analyze</param>
        /// <param name="fieldType">Type of field (required/optional/enhancement)</param>
        /// <returns>Detailed field quality metrics</returns>
        Task<FieldQualityMetrics> GetFieldQualityMetricsAsync(string fieldName, FieldCriticality fieldType);

        /// <summary>
        /// Records field-level processing result for quality tracking
        /// </summary>
        /// <param name="result">Field processing result to record</param>
        Task RecordFieldProcessingResultAsync(FieldProcessingResult result);

        /// <summary>
        /// Gets retry rate statistics for a specific field type
        /// </summary>
        /// <param name="fieldType">Type of field to analyze</param>
        /// <param name="timeWindow">Time window for analysis (default: last 24 hours)</param>
        /// <returns>Retry rate statistics</returns>
        Task<RetryRateStatistics> GetRetryRateStatisticsAsync(FieldCriticality fieldType, TimeSpan? timeWindow = null);

        /// <summary>
        /// Analyzes confidence patterns across all field types
        /// </summary>
        /// <param name="timeWindow">Time window for analysis</param>
        /// <returns>Confidence pattern analysis</returns>
        Task<ConfidencePatternAnalysis> AnalyzeConfidencePatternsAsync(TimeSpan? timeWindow = null);

        /// <summary>
        /// Gets failure mode analysis for field types
        /// </summary>
        /// <param name="fieldType">Specific field type to analyze (null for all types)</param>
        /// <returns>Failure mode analysis</returns>
        Task<FailureModeAnalysis> GetFailureModeAnalysisAsync(FieldCriticality? fieldType = null);

        /// <summary>
        /// Evaluates template form completeness quality
        /// </summary>
        /// <param name="formTemplate">Template to evaluate</param>
        /// <param name="actualData">Actual form data</param>
        /// <returns>Template completeness quality score</returns>
        Task<TemplateCompletenessQuality> EvaluateTemplateCompletenessAsync(IFormTemplate formTemplate, Dictionary<string, object> actualData);

        /// <summary>
        /// Gets quality-based degradation recommendations
        /// </summary>
        /// <param name="fieldMetrics">Current field metrics</param>
        /// <param name="constraintViolations">Current constraint violations</param>
        /// <returns>Degradation strategy recommendations</returns>
        Task<QualityDegradationRecommendations> GetQualityDegradationRecommendationsAsync(
            IReadOnlyCollection<FieldQualityMetrics> fieldMetrics,
            IReadOnlyCollection<ConstraintViolation> constraintViolations);

        /// <summary>
        /// Gets real-time quality dashboard data
        /// </summary>
        /// <returns>Current quality dashboard metrics</returns>
        Task<QualityDashboardData> GetQualityDashboardDataAsync();

        /// <summary>
        /// Clears historical quality data older than specified retention period
        /// </summary>
        /// <param name="retentionPeriod">Period to retain data (default: 30 days)</param>
        /// <returns>Number of records cleaned up</returns>
        Task<int> CleanupHistoricalDataAsync(TimeSpan? retentionPeriod = null);
    }

    /// <summary>
    /// Field quality metrics with performance tracking
    /// </summary>
    public class FieldQualityMetrics
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldCriticality FieldType { get; set; }
        public DateTime LastUpdated { get; set; }
        
        // Performance metrics
        public double SuccessRate { get; set; }
        public double AverageConfidence { get; set; }
        public double AverageProcessingTime { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public int RetryAttempts { get; set; }
        
        // Quality indicators
        public double CompletenessScore { get; set; }
        public double AccuracyScore { get; set; }
        public double ConsistencyScore { get; set; }
        
        // Trend analysis
        public QualityTrend Recent7DayTrend { get; set; } = QualityTrend.Stable;
        public QualityTrend Recent24HourTrend { get; set; } = QualityTrend.Stable;
        
        // Common failure reasons
        public List<FailureReason> CommonFailures { get; set; } = new();
        
        // Metadata
        public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
    }

    /// <summary>
    /// Result of individual field processing operation
    /// </summary>
    public class FieldProcessingResult
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldCriticality FieldType { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public bool IsSuccessful { get; set; }
        public double ConfidenceScore { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public int RetryCount { get; set; }
        
        // Alias properties for test compatibility
        public bool Success => IsSuccessful;
        public DateTime Timestamp => ProcessedAt;
        public long ProcessingTimeMs => (long)ProcessingTime.TotalMilliseconds;
        public FieldCriticality Criticality => FieldType;
        public string TemplateName => TemplateId;
        
        // Processing details
        public string? FailureReason { get; set; }
        public ConstraintViolationType? ViolationType { get; set; }
        public string? RawValue { get; set; }
        public string? ProcessedValue { get; set; }
        
        // Context information
        public string TemplateId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public Dictionary<string, object> ProcessingContext { get; set; } = new();
    }

    /// <summary>
    /// Retry rate statistics for field types
    /// </summary>
    public class RetryRateStatistics
    {
        public FieldCriticality FieldType { get; set; }
        public TimeSpan AnalysisWindow { get; set; }
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
        
        public double OverallRetryRate { get; set; }
        public double AverageRetriesPerField { get; set; }
        public int TotalProcessingAttempts { get; set; }
        public int TotalRetries { get; set; }
        
        // Retry reason breakdown
        public Dictionary<string, int> RetryReasonCounts { get; set; } = new();
        public Dictionary<ConstraintViolationType, int> ViolationTypeRetries { get; set; } = new();
        
        // Performance impact
        public TimeSpan AverageRetryDelay { get; set; }
        public double RetrySuccessRate { get; set; }
    }

    /// <summary>
    /// Confidence pattern analysis across field types
    /// </summary>
    public class ConfidencePatternAnalysis
    {
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
        public TimeSpan AnalysisWindow { get; set; }
        
        // Overall patterns
        public double OverallAverageConfidence { get; set; }
        public double ConfidenceStandardDeviation { get; set; }
        
        // Field type breakdown
        public Dictionary<FieldCriticality, FieldTypeConfidencePattern> FieldTypePatterns { get; set; } = new();
        
        // Confidence distribution
        public List<ConfidenceBucket> ConfidenceDistribution { get; set; } = new();
        
        // Temporal patterns
        public List<TemporalConfidencePattern> TimeBasedPatterns { get; set; } = new();
        
        // Correlation insights
        public List<ConfidenceCorrelation> Correlations { get; set; } = new();
    }

    /// <summary>
    /// Failure mode analysis for field types
    /// </summary>
    public class FailureModeAnalysis
    {
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
        public FieldCriticality? SpecificFieldType { get; set; }
        
        // Top failure modes
        public List<FailureMode> TopFailureModes { get; set; } = new();
        
        // Field type specific failures
        public Dictionary<FieldCriticality, List<FailureMode>> FieldTypeFailures { get; set; } = new();
        
        // Constraint violation patterns
        public Dictionary<ConstraintViolationType, ConstraintFailurePattern> ConstraintFailurePatterns { get; set; } = new();
        
        // Recovery recommendations
        public List<FailureRecoveryRecommendation> RecoveryRecommendations { get; set; } = new();
        
        // Trend analysis
        public FailureModeTrend Recent7DayTrend { get; set; } = new();
    }

    /// <summary>
    /// Template completeness quality assessment
    /// </summary>
    public class TemplateCompletenessQuality
    {
        public string TemplateId { get; set; } = string.Empty;
        public DateTime AssessmentTime { get; set; } = DateTime.UtcNow;
        
        // Overall scores
        public double OverallCompletenessScore { get; set; } // 0.0 - 1.0
        public double RequiredFieldCompleteness { get; set; } // 0.0 - 1.0
        public double OptionalFieldCompleteness { get; set; } // 0.0 - 1.0
        public double EnhancementFieldCompleteness { get; set; } // 0.0 - 1.0
        
        // Field analysis
        public Dictionary<string, FieldCompletenessDetail> FieldDetails { get; set; } = new();
        public List<string> MissingRequiredFields { get; set; } = new();
        public List<string> MissingOptionalFields { get; set; } = new();
        
        // Quality insights
        public List<QualityInsight> QualityInsights { get; set; } = new();
        public List<CompletionRecommendation> Recommendations { get; set; } = new();
        
        // Constraint adherence
        public double ConstraintAdherenceScore { get; set; }
        public List<ConstraintViolation> ActiveViolations { get; set; } = new();
    }

    /// <summary>
    /// Quality-based degradation recommendations
    /// </summary>
    public class QualityDegradationRecommendations
    {
        public DateTime RecommendationTime { get; set; } = DateTime.UtcNow;
        
        // Recommended strategies
        public List<DegradationStrategy> RecommendedStrategies { get; set; } = new();
        
        // Field-specific recommendations
        public Dictionary<string, FieldDegradationRecommendation> FieldRecommendations { get; set; } = new();
        
        // Constraint adjustments
        public List<ConstraintAdjustmentRecommendation> ConstraintAdjustments { get; set; } = new();
        
        // Performance impact estimates
        public DegradationImpactEstimate ImpactEstimate { get; set; } = new();
        
        // Confidence in recommendations
        public double RecommendationConfidence { get; set; }
    }

    /// <summary>
    /// Real-time quality dashboard data
    /// </summary>
    public class QualityDashboardData
    {
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        // Current system health
        public SystemQualityHealth SystemHealth { get; set; } = new();
        
        // Field type performance
        public Dictionary<FieldCriticality, FieldTypePerformance> FieldTypePerformance { get; set; } = new();
        
        // Active issues
        public List<QualityIssue> ActiveIssues { get; set; } = new();
        
        // Recent trends
        public QualityTrendSummary RecentTrends { get; set; } = new();
        
        // Capacity metrics
        public CapacityMetrics Capacity { get; set; } = new();
    }

    // Supporting enums and classes
    
    public enum QualityTrend
    {
        Improving,
        Stable,
        Declining,
        Volatile,
        Unknown
    }

    public class FailureReason
    {
        public string ReasonCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public double ImpactSeverity { get; set; } // 0.0 - 1.0
    }
}