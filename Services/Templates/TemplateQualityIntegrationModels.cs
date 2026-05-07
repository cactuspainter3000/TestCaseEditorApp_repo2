using System;
using System.Collections.Generic;
using TestCaseEditorApp.Services.Templates;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Additional models for Task 6.5 Quality Scoring Integration Enhancement
    /// Supporting models for Template Form Architecture integration with quality scoring
    /// ARCHITECTURAL COMPLIANCE: Proper model organization for enhanced quality scoring
    /// </summary>
    /// 
    /// <summary>
    /// Comprehensive template form quality assessment result
    /// </summary>
    public class TemplateFormQualityAssessment
    {
        public string TemplateId { get; set; } = string.Empty;
        public DateTime AssessmentTime { get; set; } = DateTime.UtcNow;
        public string SessionId { get; set; } = string.Empty;
        
        // Core quality evaluations
        public TemplateCompletenessQuality CompletenessEvaluation { get; set; } = new();
        public Dictionary<string, FieldQualityMetrics> FieldQualityAnalysis { get; set; } = new();
        public ConstraintValidationResult ConstraintValidationResult { get; set; } = new();
        public QualityDegradationRecommendations DegradationRecommendations { get; set; } = new();
        
        // Overall assessment results
        public double OverallQualityScore { get; set; } // 0.0 - 1.0
        public List<TemplateQualityImprovement> QualityImprovements { get; set; } = new();
        public QualityOutcomePredictions QualityPredictions { get; set; } = new();
        
        // Metadata
        public TimeSpan AssessmentDuration { get; set; }
        public Dictionary<string, object> AssessmentMetadata { get; set; } = new();
    }

    /// <summary>
    /// Options for template quality assessment
    /// </summary>
    public class TemplateQualityOptions
    {
        public bool IncludeFieldLevelAnalysis { get; set; } = true;
        public bool GenerateDegradationRecommendations { get; set; } = true;
        public bool AnalyzeConfidencePatterns { get; set; } = true;
        public double QualityThreshold { get; set; } = 0.8;
        public TimeSpan AnalysisTimeWindow { get; set; } = TimeSpan.FromHours(24);
        public int MaxRecommendations { get; set; } = 10;
    }

    /// <summary>
    /// Field-level quality analysis result
    /// </summary>
    public class FieldLevelQualityAnalysisResult
    {
        public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
        public string TemplateId { get; set; } = string.Empty;
        
        // Field-specific metrics
        public Dictionary<string, FieldQualityMetrics> FieldMetrics { get; set; } = new();
        
        // Retry and failure analysis
        public Dictionary<FieldCriticality, RetryRateStatistics> RetryStatistics { get; set; } = new();
        public ConfidencePatternAnalysis ConfidenceAnalysis { get; set; } = new();
        public FailureModeAnalysis FailureModeAnalysis { get; set; } = new();
        
        // Summary statistics
        public double OverallFieldSuccessRate { get; set; }
        public double AverageConfidenceScore { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }

    /// <summary>
    /// Constraint degradation processing result
    /// </summary>
    public class ConstraintDegradationResult
    {
        public string TemplateId { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        
        // Original and modified data
        public Dictionary<string, object> OriginalFormData { get; set; } = new();
        public Dictionary<string, object> ModifiedFormData { get; set; } = new();
        
        // Applied degradation actions
        public List<FieldDegradationAction> AppliedActions { get; set; } = new();
        public List<ConstraintAdjustmentAction> ConstraintAdjustments { get; set; } = new();
        
        // Impact assessment
        public double QualityRetentionScore { get; set; } // 0.0 - 1.0
        public double PerformanceImprovementScore { get; set; } // 0.0 - 1.0
        
        // Final validation
        public ConstraintValidationResult FinalValidationResult { get; set; } = new();
        
        // Metadata
        public TimeSpan ProcessingTime { get; set; }
        public Dictionary<string, object> ProcessingMetadata { get; set; } = new();
    }

    /// <summary>
    /// Template form quality dashboard data
    /// </summary>
    public class TemplateFormQualityDashboard
    {
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        // Field-level quality data
        public QualityDashboardData FieldLevelQuality { get; set; } = new();
        
        // System-level metrics
        public TemplateSystemMetrics SystemMetrics { get; set; } = new();
        
        // Quality trends
        public List<TemplateQualityTrend> QualityTrends { get; set; } = new();
        
        // Template performance
        public Dictionary<string, TemplatePerformanceMetrics> ActiveTemplatesPerformance { get; set; } = new();
        
        // Capacity metrics
        public CapacityMetrics CapacityMetrics { get; set; } = new();
        
        // Recent alerts and issues
        public List<QualityAlert> ActiveAlerts { get; set; } = new();
    }

    /// <summary>
    /// Quality improvement recommendation
    /// </summary>
    public class TemplateQualityImprovement
    {
        public string ImprovementId { get; set; } = string.Empty;
        public QualityImprovementType Type { get; set; }
        public QualityImprovementPriority Priority { get; set; }
        
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> TargetFields { get; set; } = new();
        
        // Impact assessment
        public double ExpectedImpact { get; set; } // 0.0 - 1.0
        public TimeSpan EstimatedImplementationTime { get; set; }
        public double ImplementationComplexity { get; set; } // 0.0 - 1.0
        
        // Implementation details
        public string ImplementationGuidance { get; set; } = string.Empty;
        public List<string> Prerequisites { get; set; } = new();
        public Dictionary<string, object> ImprovementMetadata { get; set; } = new();
    }

    /// <summary>
    /// Quality outcome predictions
    /// </summary>
    public class QualityOutcomePredictions
    {
        public double PredictedSuccessRate { get; set; } // 0.0 - 1.0
        public double ConfidenceInPrediction { get; set; } // 0.0 - 1.0
        
        public List<string> RiskFactors { get; set; } = new();
        public List<string> PositiveFactors { get; set; } = new();
        public List<string> RecommendedActions { get; set; } = new();
        
        // Prediction model metadata
        public string PredictionModel { get; set; } = "DefaultQualityPredictor";
        public DateTime PredictionMadeAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, double> FactorWeights { get; set; } = new();
    }

    /// <summary>
    /// Field degradation action
    /// </summary>
    public class FieldDegradationAction
    {
        public string FieldName { get; set; } = string.Empty;
        public DegradationAction Action { get; set; }
        public string Justification { get; set; } = string.Empty;
        
        // Impact metrics
        public double QualityImpact { get; set; } // 0.0 - 1.0
        public double PerformanceGain { get; set; } // 0.0 - 1.0
        
        // Action details
        public string ActionDetails { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public bool WasSuccessful { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Constraint adjustment action
    /// </summary>
    public class ConstraintAdjustmentAction
    {
        public string ConstraintId { get; set; } = string.Empty;
        public ConstraintAdjustmentType AdjustmentType { get; set; }
        public string OriginalValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public string Justification { get; set; } = string.Empty;
        
        // Adjustment metadata
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
        public bool WasSuccessful { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public double ExpectedImprovementRate { get; set; } // 0.0 - 1.0
    }

    /// <summary>
    /// Template quality trend
    /// </summary>
    public class TemplateQualityTrend
    {
        public DateTime TimePoint { get; set; }
        public double QualityScore { get; set; }
        public int ProcessingVolume { get; set; }
        public string TrendDirection { get; set; } = string.Empty; // "Improving", "Stable", "Declining"
        
        // Supporting data
        public Dictionary<QualityDimension, double> DimensionScores { get; set; } = new();
        public List<string> SignificantEvents { get; set; } = new();
    }

    /// <summary>
    /// Template performance metrics
    /// </summary>
    public class TemplatePerformanceMetrics
    {
        public string TemplateId { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        
        // Usage metrics
        public int TotalUsages { get; set; }
        public DateTime LastUsed { get; set; }
        public double UsageFrequency { get; set; }
        
        // Quality metrics
        public double AverageQualityScore { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageCompletionTime { get; set; }
        
        // Issue tracking
        public int ActiveIssues { get; set; }
        public int ResolvedIssues { get; set; }
        public List<string> CommonIssues { get; set; } = new();
    }

    /// <summary>
    /// Quality alert
    /// </summary>
    public class QualityAlert
    {
        public string AlertId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public QualityAlertSeverity Severity { get; set; }
        public QualityAlertCategory Category { get; set; }
        
        public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
        public string? TemplateId { get; set; }
        public List<string> AffectedFields { get; set; } = new();
        
        public string? RecommendedAction { get; set; }
        public QualityAlertStatus Status { get; set; } = QualityAlertStatus.Active;
    }

    // Supporting enums

    public enum QualityImprovementType
    {
        MissingRequiredField,
        ConstraintViolation,
        LowConfidenceField,
        HighRetryRate,
        PerformanceIssue,
        ConsistencyIssue,
        ValidationError,
        ConfigurationError
    }

    public enum QualityImprovementPriority
    {
        Critical,
        High,
        Medium,
        Low,
        Optional
    }

    public enum QualityAlertSeverity
    {
        Critical,
        High,
        Medium,
        Low,
        Info
    }

    public enum QualityAlertCategory
    {
        QualityDegradation,
        PerformanceIssue,
        ConfigurationError,
        SystemHealth,
        CapacityIssue,
        SecurityAlert
    }

    public enum QualityAlertStatus
    {
        Active,
        Acknowledged,
        InProgress,
        Resolved,
        Dismissed
    }

    /// <summary>
    /// Result of constraint validation for quality integration
    /// </summary>
    public class ConstraintValidationResult
    {
        public List<ConstraintViolation> Violations { get; set; } = new();
        public bool IsValid => Violations.Count == 0;
        public DateTime ValidationTime { get; set; } = DateTime.UtcNow;
        public string? ValidationSummary { get; set; }
    }
}
