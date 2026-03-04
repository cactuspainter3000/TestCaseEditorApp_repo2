using System;
using System.Collections.Generic;
using TestCaseEditorApp.Services.Templates;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Enhanced Quality Scoring Models for Field-Level Performance Metrics
    /// Supporting models for Task 6.5 Quality Scoring Integration Enhancement
    /// ARCHITECTURAL COMPLIANCE: Proper model separation for Template Form Architecture integration
    /// </summary>
    /// 
    /// <summary>
    /// Confidence pattern for specific field type
    /// </summary>
    public class FieldTypeConfidencePattern
    {
        public FieldCriticality FieldType { get; set; }
        public double AverageConfidence { get; set; }
        public double ConfidenceVariance { get; set; }
        public int SampleCount { get; set; }
        public List<ConfidenceTrend> TrendData { get; set; } = new();
    }

    /// <summary>
    /// Confidence distribution bucket
    /// </summary>
    public class ConfidenceBucket
    {
        public double MinConfidence { get; set; }
        public double MaxConfidence { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
        public List<FieldCriticality> DominantFieldTypes { get; set; } = new();
    }

    /// <summary>
    /// Temporal confidence pattern
    /// </summary>
    public class TemporalConfidencePattern
    {
        public DateTime TimePoint { get; set; }
        public double AverageConfidence { get; set; }
        public int ProcessingVolume { get; set; }
        public Dictionary<FieldCriticality, double> FieldTypeConfidences { get; set; } = new();
    }

    /// <summary>
    /// Confidence correlation analysis
    /// </summary>
    public class ConfidenceCorrelation
    {
        public string CorrelationFactor { get; set; } = string.Empty; // e.g., "ProcessingTime", "RetryCount", "FieldComplexity"
        public double CorrelationCoefficient { get; set; } // -1.0 to 1.0
        public CorrelationStrength Strength { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Individual failure mode
    /// </summary>
    public class FailureMode
    {
        public string FailureModeId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public double ImpactSeverity { get; set; } // 0.0 - 1.0
        public List<FieldCriticality> AffectedFieldTypes { get; set; } = new();
        public List<string> CommonTriggers { get; set; } = new();
        public string RecommendedMitigation { get; set; } = string.Empty;
    }

    /// <summary>
    /// Constraint failure pattern analysis
    /// </summary>
    public class ConstraintFailurePattern
    {
        public ConstraintViolationType ViolationType { get; set; }
        public int TotalViolations { get; set; }
        public double ViolationRate { get; set; }
        public Dictionary<FieldCriticality, int> FieldTypeBreakdown { get; set; } = new();
        public List<string> CommonViolationReasons { get; set; } = new();
        public TimeSpan AverageResolutionTime { get; set; }
        public double AutoResolutionRate { get; set; }
    }

    /// <summary>
    /// Failure recovery recommendation
    /// </summary>
    public class FailureRecoveryRecommendation
    {
        public string RecommendationId { get; set; } = string.Empty;
        public string FailureModeId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public RecoveryStrategy Strategy { get; set; }
        public double ExpectedSuccessRate { get; set; }
        public TimeSpan EstimatedImplementationTime { get; set; }
        public List<string> Prerequisites { get; set; } = new();
    }

    /// <summary>
    /// Failure mode trend analysis
    /// </summary>
    public class FailureModeTrend
    {
        public QualityTrend OverallTrend { get; set; }
        public Dictionary<string, QualityTrend> IndividualFailureModeTrends { get; set; } = new();
        public List<EmergingFailureMode> EmergingModes { get; set; } = new();
        public List<string> ResolvingModes { get; set; } = new();
        public double TrendConfidence { get; set; }
    }

    /// <summary>
    /// Field completeness detail
    /// </summary>
    public class FieldCompletenessDetail
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldCriticality FieldType { get; set; }
        public bool IsPresent { get; set; }
        public bool IsValid { get; set; }
        public double QualityScore { get; set; } // 0.0 - 1.0
        public string? ValidationMessage { get; set; }
        public List<string> QualityIssues { get; set; } = new();
    }

    /// <summary>
    /// Quality insight
    /// </summary>
    public class QualityInsight
    {
        public string InsightId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public InsightSeverity Severity { get; set; }
        public InsightCategory Category { get; set; }
        public double Confidence { get; set; } // 0.0 - 1.0
        public List<string> AffectedFields { get; set; } = new();
        public string? RecommendedAction { get; set; }
    }

    /// <summary>
    /// Completion recommendation
    /// </summary>
    public class CompletionRecommendation
    {
        public string RecommendationId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public RecommendationPriority Priority { get; set; }
        public double ExpectedQualityImprovement { get; set; } // 0.0 - 1.0
        public List<string> TargetFields { get; set; } = new();
        public string? ImplementationGuidance { get; set; }
    }

    /// <summary>
    /// Field degradation recommendation
    /// </summary>
    public class FieldDegradationRecommendation
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldCriticality FieldType { get; set; }
        public DegradationAction Action { get; set; }
        public string Justification { get; set; } = string.Empty;
        public double QualityImpact { get; set; } // 0.0 - 1.0
        public double PerformanceGain { get; set; } // 0.0 - 1.0
        public List<string> Alternatives { get; set; } = new();
    }

    /// <summary>
    /// Constraint adjustment recommendation
    /// </summary>
    public class ConstraintAdjustmentRecommendation
    {
        public string ConstraintId { get; set; } = string.Empty;
        public FieldConstraintType ConstraintType { get; set; }
        public ConstraintAdjustmentType AdjustmentType { get; set; }
        public string CurrentValue { get; set; } = string.Empty;
        public string RecommendedValue { get; set; } = string.Empty;
        public string Justification { get; set; } = string.Empty;
        public double ExpectedImprovementRate { get; set; } // 0.0 - 1.0
    }

    /// <summary>
    /// Degradation impact estimate
    /// </summary>
    public class DegradationImpactEstimate
    {
        public double QualityImpactScore { get; set; } // 0.0 - 1.0
        public double PerformanceGainScore { get; set; } // 0.0 - 1.0
        public TimeSpan EstimatedTimeReduction { get; set; }
        public double ResourceUtilizationImprovement { get; set; } // 0.0 - 1.0
        public List<RiskFactor> IdentifiedRisks { get; set; } = new();
        public double OverallRecommendationScore { get; set; } // 0.0 - 1.0
    }

    /// <summary>
    /// System quality health
    /// </summary>
    public class SystemQualityHealth
    {
        public HealthStatus OverallStatus { get; set; }
        public double OverallHealthScore { get; set; } // 0.0 - 1.0
        public Dictionary<QualityDimension, double> DimensionScores { get; set; } = new();
        public List<HealthAlert> ActiveAlerts { get; set; } = new();
        public DateTime LastAssessment { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Field type performance metrics
    /// </summary>
    public class FieldTypePerformance
    {
        public FieldCriticality FieldType { get; set; }
        public double SuccessRate { get; set; }
        public double AverageConfidence { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public int TotalProcessed { get; set; }
        public QualityTrend RecentTrend { get; set; }
        public List<PerformanceAlert> Alerts { get; set; } = new();
    }

    /// <summary>
    /// Quality issue
    /// </summary>
    public class QualityIssue
    {
        public string IssueId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IssueSeverity Severity { get; set; }
        public IssueCategory Category { get; set; }
        public DateTime DetectedAt { get; set; }
        public List<string> AffectedFields { get; set; } = new();
        public string? RecommendedAction { get; set; }
        public IssueStatus Status { get; set; }
    }

    /// <summary>
    /// Quality trend summary
    /// </summary>
    public class QualityTrendSummary
    {
        public QualityTrend Last24Hours { get; set; }
        public QualityTrend Last7Days { get; set; }
        public QualityTrend Last30Days { get; set; }
        public Dictionary<FieldCriticality, QualityTrend> FieldTypeTrends { get; set; } = new();
        public List<TrendHighlight> KeyHighlights { get; set; } = new();
    }

    /// <summary>
    /// Capacity metrics
    /// </summary>
    public class CapacityMetrics
    {
        public double CurrentUtilization { get; set; } // 0.0 - 1.0
        public int MaxConcurrentProcessing { get; set; }
        public int CurrentActiveProcessing { get; set; }
        public TimeSpan AverageQueueTime { get; set; }
        public int QueuedItems { get; set; }
        public CapacityStatus Status { get; set; }
        public DateTime NextCapacityReview { get; set; }
    }

    // Supporting enums

    public enum CorrelationStrength
    {
        VeryWeak,
        Weak,
        Moderate,
        Strong,
        VeryStrong
    }

    public enum RecoveryStrategy
    {
        AutomaticRetry,
        FallbackToDefault,
        ManualIntervention,
        ConstraintRelaxation,
        AlternativeProcessing,
        GracefulDegradation
    }

    public enum InsightSeverity
    {
        Critical,
        High,
        Medium,
        Low,
        Info
    }

    public enum InsightCategory
    {
        Performance,
        Quality,
        Reliability,
        Efficiency,
        UserExperience,
        SystemHealth
    }

    public enum RecommendationPriority
    {
        Critical,
        High,
        Medium,
        Low,
        Optional
    }

    public enum DegradationApproach
    {
        FieldExclusion,
        ConstraintRelaxation,
        QualityThresholdReduction,
        TimeoutReduction,
        FallbackToSimple,
        GracefulDegradation
    }

    public enum DegradationAction
    {
        MaintainCurrent,
        RelaxConstraints,
        ExcludeFromProcessing,
        UseDefaultValue,
        ReduceQualityThreshold,
        SimplifyValidation
    }

    public enum ConstraintAdjustmentType
    {
        Relax,
        Tighten,
        Disable,
        ModifyThreshold,
        ChangeStrategy
    }

    public enum HealthStatus
    {
        Excellent,
        Good,
        Fair,
        Poor,
        Critical,
        Unknown
    }

    public enum QualityDimension
    {
        Accuracy,
        Completeness,
        Consistency,
        Reliability,
        Performance,
        Usability
    }

    public enum IssueSeverity
    {
        Critical,
        High,
        Medium,
        Low,
        Info
    }

    public enum IssueCategory
    {
        QualityDegradation,
        PerformanceIssue,
        ReliabilityProblem,
        ConfigurationError,
        SystemOverload,
        DataQualityIssue
    }

    public enum IssueStatus
    {
        New,
        InProgress,
        UnderInvestigation,
        Resolved,
        Closed,
        Acknowledged
    }

    public enum CapacityStatus
    {
        Normal,
        High,
        Critical,
        Overloaded,
        Maintenance
    }

    // Supporting data classes

    public class ConfidenceTrend
    {
        public DateTime TimePoint { get; set; }
        public double Confidence { get; set; }
        public int SampleSize { get; set; }
    }

    public class EmergingFailureMode
    {
        public string FailureModeId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int RecentFrequency { get; set; }
        public double GrowthRate { get; set; }
        public double SeverityEstimate { get; set; }
    }

    public class StrategyStep
    {
        public int StepNumber { get; set; }
        public string Description { get; set; } = string.Empty;
        public TimeSpan EstimatedDuration { get; set; }
        public List<string> Prerequisites { get; set; } = new();
    }

    public class RiskFactor
    {
        public string RiskId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public RiskLevel Level { get; set; }
        public string MitigationStrategy { get; set; } = string.Empty;
    }

    public class HealthAlert
    {
        public string AlertId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public DateTime TriggeredAt { get; set; }
        public QualityDimension AffectedDimension { get; set; }
    }

    public class PerformanceAlert
    {
        public string AlertId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public PerformanceMetric AffectedMetric { get; set; }
        public double Threshold { get; set; }
        public double CurrentValue { get; set; }
    }

    public class TrendHighlight
    {
        public string HighlightId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TrendType Type { get; set; }
        public double Impact { get; set; }
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum PerformanceMetric
    {
        SuccessRate,
        AverageProcessingTime,
        ConfidenceScore,
        RetryRate,
        QueueTime
    }

    public enum TrendType
    {
        Improvement,
        Degradation,
        Anomaly,
        SeasonalPattern,
        NewIssue
    }
}