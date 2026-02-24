using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents a prompt template with performance tracking and refinement history
    /// </summary>
    public class ManagedPrompt
    {
        /// <summary>
        /// Unique identifier for this prompt
        /// </summary>
        public string PromptId { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable name for the prompt
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what this prompt is intended to accomplish
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The actual prompt template text with placeholders
        /// </summary>
        public string TemplateText { get; set; } = string.Empty;

        /// <summary>
        /// Version number for tracking prompt evolution
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// When this prompt version was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Who/what created this prompt (human, system, refinement)
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// Current status of this prompt
        /// </summary>
        public PromptStatus Status { get; set; } = PromptStatus.Active;

        /// <summary>
        /// Performance metrics for this prompt
        /// </summary>
        public PromptPerformanceMetrics Performance { get; set; } = new PromptPerformanceMetrics();

        /// <summary>
        /// Categories or tags for prompt organization
        /// </summary>
        public List<string> Categories { get; set; } = new List<string>();

        /// <summary>
        /// Parameters that can be substituted in the template
        /// </summary>
        public List<PromptParameter> Parameters { get; set; } = new List<PromptParameter>();

        /// <summary>
        /// History of refinements applied to this prompt
        /// </summary>
        public List<PromptRefinement> RefinementHistory { get; set; } = new List<PromptRefinement>();

        /// <summary>
        /// Parent prompt ID if this was derived from another prompt
        /// </summary>
        public string ParentPromptId { get; set; } = string.Empty;

        /// <summary>
        /// Child prompts that were derived from this one
        /// </summary>
        public List<string> ChildPromptIds { get; set; } = new List<string>();

        /// <summary>
        /// Configuration for A/B testing this prompt
        /// </summary>
        public ABTestConfiguration ABTestConfig { get; set; } = new ABTestConfiguration();
    }

    /// <summary>
    /// Status of a managed prompt
    /// </summary>
    public enum PromptStatus
    {
        Draft,
        Active,
        Testing,
        Deprecated,
        Archived
    }

    /// <summary>
    /// Performance metrics for a prompt across multiple uses
    /// </summary>
    public class PromptPerformanceMetrics
    {
        /// <summary>
        /// Total number of times this prompt has been used
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// Average quality score achieved with this prompt
        /// </summary>
        public double AverageQualityScore { get; set; }

        /// <summary>
        /// Success rate (ratio of good outcomes)
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Average processing time when using this prompt
        /// </summary>
        public TimeSpan AverageProcessingTime { get; set; }

        /// <summary>
        /// Rate of validation approval when using this prompt
        /// </summary>
        public double ValidationApprovalRate { get; set; }

        /// <summary>
        /// Distribution of quality scores
        /// </summary>
        public QualityScoreDistribution ScoreDistribution { get; set; } = new QualityScoreDistribution();

        /// <summary>
        /// Performance trend over time
        /// </summary>
        public PerformanceTrend Trend { get; set; } = new PerformanceTrend();

        /// <summary>
        /// Last time metrics were updated
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Distribution of quality scores for statistical analysis
    /// </summary>
    public class QualityScoreDistribution
    {
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Mean { get; set; }
        public double Median { get; set; }
        public double StandardDeviation { get; set; }
        public List<double> Percentiles { get; set; } = new List<double>(); // 25th, 50th, 75th, 95th
    }

    /// <summary>
    /// Performance trend analysis over time
    /// </summary>
    public class PerformanceTrend
    {
        public TrendDirection Direction { get; set; } = TrendDirection.Stable;
        public double TrendStrength { get; set; } // 0.0 to 1.0
        public DateTime AnalysisDate { get; set; }
        public List<PerformanceDataPoint> DataPoints { get; set; } = new List<PerformanceDataPoint>();
    }

    public enum TrendDirection
    {
        Improving,
        Declining,
        Stable,
        Volatile
    }

    /// <summary>
    /// A single performance measurement point
    /// </summary>
    public class PerformanceDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double QualityScore { get; set; }
        public double ProcessingTimeMs { get; set; }
        public bool ValidationApproved { get; set; }
    }

    /// <summary>
    /// Parameter that can be substituted in a prompt template
    /// </summary>
    public class PromptParameter
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ParameterType Type { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
    }

    public enum ParameterType
    {
        Text,
        Number,
        Boolean,
        List,
        JSON
    }

    /// <summary>
    /// Represents a refinement applied to a prompt
    /// </summary>
    public class PromptRefinement
    {
        /// <summary>
        /// Unique identifier for this refinement
        /// </summary>
        public string RefinementId { get; set; } = string.Empty;

        /// <summary>
        /// When this refinement was applied
        /// </summary>
        public DateTime AppliedAt { get; set; }

        /// <summary>
        /// What triggered this refinement
        /// </summary>
        public RefinementTrigger Trigger { get; set; }

        /// <summary>
        /// Source of the refinement (automated, human, etc.)
        /// </summary>
        public string RefinementSource { get; set; } = string.Empty;

        /// <summary>
        /// Description of what was changed
        /// </summary>
        public string ChangeDescription { get; set; } = string.Empty;

        /// <summary>
        /// The old prompt text before refinement
        /// </summary>
        public string OldPromptText { get; set; } = string.Empty;

        /// <summary>
        /// The new prompt text after refinement
        /// </summary>
        public string NewPromptText { get; set; } = string.Empty;

        /// <summary>
        /// Rationale for the refinement
        /// </summary>
        public string Rationale { get; set; } = string.Empty;

        /// <summary>
        /// Performance before the refinement
        /// </summary>
        public PromptPerformanceSnapshot PerformanceBefore { get; set; } = new PromptPerformanceSnapshot();

        /// <summary>
        /// Performance after the refinement (filled in over time)
        /// </summary>
        public PromptPerformanceSnapshot PerformanceAfter { get; set; } = new PromptPerformanceSnapshot();

        /// <summary>
        /// Whether this refinement improved performance
        /// </summary>
        public bool? WasSuccessful { get; set; }

        /// <summary>
        /// Additional metadata about the refinement
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// What triggered a prompt refinement
    /// </summary>
    public enum RefinementTrigger
    {
        PoorPerformance,
        ValidationFeedback,
        QualityScoreDecline,
        ManualReview,
        ScheduledOptimization,
        ABTestResults,
        ErrorPatternDetection
    }

    /// <summary>
    /// Performance snapshot at a specific point in time
    /// </summary>
    public class PromptPerformanceSnapshot
    {
        public DateTime CapturedAt { get; set; }
        public double AverageQualityScore { get; set; }
        public double SuccessRate { get; set; }
        public int SampleSize { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
        public double ValidationApprovalRate { get; set; }
    }

    /// <summary>
    /// Configuration for A/B testing prompts
    /// </summary>
    public class ABTestConfiguration
    {
        /// <summary>
        /// Whether this prompt is currently in A/B testing
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Test group this prompt belongs to
        /// </summary>
        public string TestGroup { get; set; } = string.Empty;

        /// <summary>
        /// Percentage of traffic to send to this prompt (0.0 to 1.0)
        /// </summary>
        public double TrafficPercentage { get; set; }

        /// <summary>
        /// When the A/B test started
        /// </summary>
        public DateTime TestStartDate { get; set; }

        /// <summary>
        /// When the A/B test should end
        /// </summary>
        public DateTime TestEndDate { get; set; }

        /// <summary>
        /// Key metrics being measured in the test
        /// </summary>
        public List<string> KeyMetrics { get; set; } = new List<string>();

        /// <summary>
        /// Statistical significance threshold
        /// </summary>
        public double SignificanceThreshold { get; set; } = 0.05;
    }

    /// <summary>
    /// Result of analyzing prompt performance and generating refinements
    /// </summary>
    public class PromptAnalysisResult
    {
        /// <summary>
        /// Unique identifier for this analysis session
        /// </summary>
        public string AnalysisId { get; set; } = string.Empty;

        /// <summary>
        /// When this analysis was performed
        /// </summary>
        public DateTime AnalyzedAt { get; set; }

        /// <summary>
        /// Prompt that was analyzed
        /// </summary>
        public ManagedPrompt AnalyzedPrompt { get; set; } = new ManagedPrompt();

        /// <summary>
        /// Time period that was analyzed
        /// </summary>
        public TimeSpan AnalysisPeriod { get; set; }

        /// <summary>
        /// Issues identified with the current prompt
        /// </summary>
        public List<PromptIssue> IdentifiedIssues { get; set; } = new List<PromptIssue>();

        /// <summary>
        /// Suggested improvements for the prompt
        /// </summary>
        public List<PromptImprovement> SuggestedImprovements { get; set; } = new List<PromptImprovement>();

        /// <summary>
        /// Generated refined prompt variants
        /// </summary>
        public List<ManagedPrompt> RefinedVariants { get; set; } = new List<ManagedPrompt>();

        /// <summary>
        /// Confidence in the analysis (0.0 to 1.0)
        /// </summary>
        public double AnalysisConfidence { get; set; }

        /// <summary>
        /// Recommended actions based on analysis
        /// </summary>
        public List<string> RecommendedActions { get; set; } = new List<string>();
    }

    /// <summary>
    /// An issue identified with a prompt
    /// </summary>
    public class PromptIssue
    {
        public string IssueId { get; set; } = string.Empty;
        public IssueType Type { get; set; }
        public IssueSeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Evidence { get; set; } = string.Empty;
        public double ImpactScore { get; set; } // 0.0 to 1.0
    }

    public enum IssueType
    {
        AmbiguousInstructions,
        InconsistentResults,
        LowQualityOutputs,
        HighProcessingTime,
        ValidationRejections,
        MissingContext,
        UnclearExpectations
    }

    public enum IssueSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// A suggested improvement to a prompt
    /// </summary>
    public class PromptImprovement
    {
        public string ImprovementId { get; set; } = string.Empty;
        public ImprovementType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ProposedChange { get; set; } = string.Empty;
        public double ExpectedImpact { get; set; } // 0.0 to 1.0
        public double ImplementationEffort { get; set; } // 0.0 to 1.0
        public List<string> RelatedIssueIds { get; set; } = new List<string>();
    }

    public enum ImprovementType
    {
        ClarifyInstructions,
        AddExamples,
        ImproveStructure,
        AddConstraints,
        SimplifyLanguage,
        AddContext,
        SpecifyFormat
    }

    /// <summary>
    /// Configuration for prompt refinement operations
    /// </summary>
    public class PromptRefinementOptions
    {
        /// <summary>
        /// Minimum performance threshold to trigger refinement
        /// </summary>
        public double PerformanceThreshold { get; set; } = 0.7;

        /// <summary>
        /// Minimum sample size before analyzing performance
        /// </summary>
        public int MinSampleSize { get; set; } = 10;

        /// <summary>
        /// Whether to automatically apply refinements or require approval
        /// </summary>
        public bool AutoApplyRefinements { get; set; } = false;

        /// <summary>
        /// Maximum number of refinement variants to generate
        /// </summary>
        public int MaxVariants { get; set; } = 3;

        /// <summary>
        /// Whether to enable A/B testing of refined prompts
        /// </summary>
        public bool EnableABTesting { get; set; } = true;

        /// <summary>
        /// Duration for A/B tests
        /// </summary>
        public TimeSpan ABTestDuration { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// Analysis window for performance evaluation
        /// </summary>
        public TimeSpan AnalysisWindow { get; set; } = TimeSpan.FromDays(30);
    }

    /// <summary>
    /// Result of a prompt refinement operation
    /// </summary>
    public class PromptRefinementResult
    {
        /// <summary>
        /// Unique identifier for this refinement operation
        /// </summary>
        public string RefinementOperationId { get; set; } = string.Empty;

        /// <summary>
        /// When the refinement was performed
        /// </summary>
        public DateTime RefinedAt { get; set; }

        /// <summary>
        /// Original prompt that was refined
        /// </summary>
        public ManagedPrompt OriginalPrompt { get; set; } = new ManagedPrompt();

        /// <summary>
        /// Refined prompt variants that were generated
        /// </summary>
        public List<ManagedPrompt> RefinedPrompts { get; set; } = new List<ManagedPrompt>();

        /// <summary>
        /// Analysis that led to this refinement
        /// </summary>
        public PromptAnalysisResult AnalysisResult { get; set; } = new PromptAnalysisResult();

        /// <summary>
        /// Whether the refinement was successful
        /// </summary>
        public bool WasSuccessful { get; set; }

        /// <summary>
        /// Error message if refinement failed
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// A/B test configuration if testing is enabled
        /// </summary>
        public ABTestConfiguration ABTestConfig { get; set; } = new ABTestConfiguration();
    }

    /// <summary>
    /// Performance comparison between different prompts
    /// </summary>
    public class PromptPerformanceComparison
    {
        /// <summary>
        /// Prompts being compared
        /// </summary>
        public List<ManagedPrompt> ComparedPrompts { get; set; } = new List<ManagedPrompt>();

        /// <summary>
        /// Comparison period
        /// </summary>
        public TimeSpan ComparisonPeriod { get; set; }

        /// <summary>
        /// Statistical significance of differences
        /// </summary>
        public Dictionary<string, double> SignificanceLevels { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Winner of the comparison
        /// </summary>
        public string WinningPromptId { get; set; } = string.Empty;

        /// <summary>
        /// Detailed performance metrics for each prompt
        /// </summary>
        public Dictionary<string, PromptPerformanceMetrics> DetailedMetrics { get; set; } = new Dictionary<string, PromptPerformanceMetrics>();

        /// <summary>
        /// Recommendations based on comparison
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();
    }

    /// <summary>
    /// Real-time monitoring of prompt performance
    /// </summary>
    public class PromptPerformanceMonitor
    {
        /// <summary>
        /// Currently active prompts being monitored
        /// </summary>
        public List<string> ActivePromptIds { get; set; } = new List<string>();

        /// <summary>
        /// Real-time performance metrics
        /// </summary>
        public Dictionary<string, PromptPerformanceSnapshot> CurrentPerformance { get; set; } = new Dictionary<string, PromptPerformanceSnapshot>();

        /// <summary>
        /// Alerts and notifications
        /// </summary>
        public List<PerformanceAlert> ActiveAlerts { get; set; } = new List<PerformanceAlert>();

        /// <summary>
        /// When monitoring data was last updated
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Alert for prompt performance issues
    /// </summary>
    public class PerformanceAlert
    {
        public string AlertId { get; set; } = string.Empty;
        public string PromptId { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime TriggeredAt { get; set; }
        public bool IsResolved { get; set; }
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
}