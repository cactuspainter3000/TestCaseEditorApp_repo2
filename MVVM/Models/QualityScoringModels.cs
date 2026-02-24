using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Comprehensive quality score result for a capability derivation
    /// </summary>
    public class DerivationQualityScore
    {
        /// <summary>
        /// Unique identifier for this scoring instance
        /// </summary>
        public string ScoringId { get; set; } = string.Empty;

        /// <summary>
        /// ID of the derivation result that was scored
        /// </summary>
        public string DerivationResultId { get; set; } = string.Empty;

        /// <summary>
        /// When this scoring was performed
        /// </summary>
        public DateTime ScoredAt { get; set; }

        /// <summary>
        /// Version of the scoring algorithm used
        /// </summary>
        public string ScoringVersion { get; set; } = string.Empty;

        /// <summary>
        /// Overall weighted quality score (0.0 to 1.0)
        /// </summary>
        public double OverallScore { get; set; }

        /// <summary>
        /// Individual dimension scores for detailed analysis
        /// </summary>
        public Dictionary<string, double> DimensionScores { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Confidence level in the quality assessment
        /// </summary>
        public ConfidenceLevel ConfidenceLevel { get; set; }

        /// <summary>
        /// Areas identified as needing improvement
        /// </summary>
        public List<string> ImprovementAreas { get; set; } = new List<string>();

        /// <summary>
        /// Specific actionable recommendations for improvement
        /// </summary>
        public List<string> ActionableRecommendations { get; set; } = new List<string>();

        /// <summary>
        /// Performance relative to historical scores
        /// </summary>
        public RelativePerformance RelativePerformance { get; set; } = new RelativePerformance();
    }

    /// <summary>
    /// Configuration options for quality scoring
    /// </summary>
    public class QualityScoringOptions
    {
        /// <summary>
        /// Whether to include detailed dimension analysis
        /// </summary>
        public bool IncludeDetailedAnalysis { get; set; } = true;

        /// <summary>
        /// Whether to generate improvement recommendations
        /// </summary>
        public bool GenerateRecommendations { get; set; } = true;

        /// <summary>
        /// Whether to assess relative performance against historical data
        /// </summary>
        public bool AssessRelativePerformance { get; set; } = true;

        /// <summary>
        /// Custom weight adjustments for dimensions
        /// </summary>
        public Dictionary<string, double> CustomWeights { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Minimum threshold for flagging improvement areas
        /// </summary>
        public double ImprovementThreshold { get; set; } = 0.7;
    }

    /// <summary>
    /// Confidence level in quality assessment
    /// </summary>
    public enum ConfidenceLevel
    {
        Low,     // Limited data or high uncertainty
        Medium,  // Reasonable confidence in assessment
        High     // High confidence with comprehensive data
    }

    /// <summary>
    /// Performance comparison with historical data
    /// </summary>
    public class RelativePerformance
    {
        /// <summary>
        /// Percentile ranking compared to historical scores (0-100)
        /// </summary>
        public double Percentile { get; set; }

        /// <summary>
        /// Whether this score is above historical average
        /// </summary>
        public bool? IsAboveAverage { get; set; }

        /// <summary>
        /// Historical average score for comparison
        /// </summary>
        public double HistoricalAverage { get; set; }

        /// <summary>
        /// Number of historical scores in comparison
        /// </summary>
        public int HistoricalCount { get; set; }
    }

    /// <summary>
    /// Self-evaluation report for the quality scoring system
    /// </summary>
    public class SelfEvaluationReport
    {
        /// <summary>
        /// Unique identifier for this evaluation
        /// </summary>
        public string EvaluationId { get; set; } = string.Empty;

        /// <summary>
        /// When this evaluation was performed
        /// </summary>
        public DateTime EvaluatedAt { get; set; }

        /// <summary>
        /// Version of the scoring system evaluated
        /// </summary>
        public string ScoringVersion { get; set; } = string.Empty;

        /// <summary>
        /// Consistency of scoring across similar derivations (0.0 to 1.0)
        /// </summary>
        public double ScoringConsistency { get; set; }

        /// <summary>
        /// How well scores predict actual validation outcomes (0.0 to 1.0)
        /// </summary>
        public double PredictiveAccuracy { get; set; }

        /// <summary>
        /// Detected biases in the scoring system
        /// </summary>
        public BiasDetectionReport BiasDetection { get; set; } = new BiasDetectionReport();

        /// <summary>
        /// System performance metrics
        /// </summary>
        public SystemPerformanceMetrics SystemMetrics { get; set; } = new SystemPerformanceMetrics();

        /// <summary>
        /// Quality of score calibration (0.0 to 1.0)
        /// </summary>
        public double CalibrationQuality { get; set; }

        /// <summary>
        /// Recommendations for improving the scoring system itself
        /// </summary>
        public List<string> SystemImprovements { get; set; } = new List<string>();
    }

    /// <summary>
    /// Report on detected biases in the scoring system
    /// </summary>
    public class BiasDetectionReport
    {
        /// <summary>
        /// List of detected biases with descriptions
        /// </summary>
        public List<string> DetectedBiases { get; set; } = new List<string>();

        /// <summary>
        /// Severity of bias issues detected
        /// </summary>
        public BiasSeverity Severity => DetectedBiases.Count switch
        {
            0 => BiasSeverity.None,
            1 => BiasSeverity.Low,
            2 or 3 => BiasSeverity.Medium,
            _ => BiasSeverity.High
        };
    }

    /// <summary>
    /// Severity of detected bias
    /// </summary>
    public enum BiasSeverity
    {
        None,    // No biases detected
        Low,     // Minor biases, monitoring recommended
        Medium,  // Notable biases, correction recommended
        High     // Significant biases, immediate correction required
    }

    /// <summary>
    /// System performance metrics for the quality scorer
    /// </summary>
    public class SystemPerformanceMetrics
    {
        /// <summary>
        /// Total number of scorings performed
        /// </summary>
        public int TotalScoringsPerformed { get; set; }

        /// <summary>
        /// Average time to perform a scoring operation
        /// </summary>
        public TimeSpan AverageProcessingTime { get; set; }

        /// <summary>
        /// Distribution of scores across ranges
        /// </summary>
        public Dictionary<string, int> ScoreDistribution { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Current memory usage in MB
        /// </summary>
        public long MemoryUsage { get; set; }
    }

    /// <summary>
    /// Real-time quality feedback during active derivation
    /// </summary>
    public class RealTimeQualityFeedback
    {
        /// <summary>
        /// Unique identifier for this feedback instance
        /// </summary>
        public string FeedbackId { get; set; } = string.Empty;

        /// <summary>
        /// When this feedback was generated
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// The ATP step text being analyzed
        /// </summary>
        public string InputAtpStep { get; set; } = string.Empty;

        /// <summary>
        /// Current quality indicators for immediate feedback
        /// </summary>
        public Dictionary<string, double> CurrentQualityIndicators { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Early warnings about potential quality issues
        /// </summary>
        public List<string> QualityWarnings { get; set; } = new List<string>();

        /// <summary>
        /// Immediate improvement suggestions for in-progress work
        /// </summary>
        public List<string> ImmediateImprovements { get; set; } = new List<string>();
    }

    /// <summary>
    /// Quality scoring weights for different dimensions
    /// </summary>
    public class QualityWeights
    {
        private readonly Dictionary<string, double> _weights = new Dictionary<string, double>
        {
            ["TaxonomyCompliance"] = 0.20,      // Critical for categorization accuracy
            ["Completeness"] = 0.15,            // Important for thorough analysis
            ["Specificity"] = 0.15,             // Critical for implementation clarity
            ["AtpAlignment"] = 0.125,           // Important for traceability
            ["Testability"] = 0.125,            // Important for verification
            ["Consistency"] = 0.10,             // Important for quality
            ["Feasibility"] = 0.075,            // Moderate importance
            ["SpecificationCompleteness"] = 0.075 // Moderate importance
        };

        /// <summary>
        /// Get weight for a specific dimension
        /// </summary>
        public double GetWeight(string dimension)
        {
            return _weights.TryGetValue(dimension, out var weight) ? weight : 0.1;
        }

        /// <summary>
        /// Set custom weight for a dimension
        /// </summary>
        public void SetWeight(string dimension, double weight)
        {
            _weights[dimension] = Math.Max(0.0, Math.Min(1.0, weight));
        }

        /// <summary>
        /// Get all current weights
        /// </summary>
        public Dictionary<string, double> GetAllWeights()
        {
            return new Dictionary<string, double>(_weights);
        }
    }

    /// <summary>
    /// Historical quality score record for trend analysis
    /// </summary>
    public class QualityScoreRecord
    {
        /// <summary>
        /// Unique identifier for the scoring instance
        /// </summary>
        public string ScoreId { get; set; } = string.Empty;

        /// <summary>
        /// When this score was recorded
        /// </summary>
        public DateTime ScoredAt { get; set; }

        /// <summary>
        /// Overall quality score achieved
        /// </summary>
        public double OverallScore { get; set; }

        /// <summary>
        /// Number of capabilities in the scored derivation
        /// </summary>
        public int CapabilityCount { get; set; }

        /// <summary>
        /// Length of the ATP step input text
        /// </summary>
        public int AtpLength { get; set; }

        /// <summary>
        /// Context information for this scoring
        /// </summary>
        public Dictionary<string, object> Context { get; set; } = new Dictionary<string, object>();
    }

    // Quality Scoring Integration Models

    /// <summary>
    /// Result of a quality-guided derivation process
    /// </summary>
    public class QualityGuidedDerivationResult
    {
        /// <summary>
        /// Unique session identifier
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// When the process started
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// When the process completed
        /// </summary>
        public DateTime CompletedAt { get; set; }

        /// <summary>
        /// Total processing duration
        /// </summary>
        public TimeSpan ProcessingDuration { get; set; }

        /// <summary>
        /// Original ATP step input
        /// </summary>
        public string InputAtpStep { get; set; } = string.Empty;

        /// <summary>
        /// Initial derivation result
        /// </summary>
        public DerivationResult InitialDerivation { get; set; } = new DerivationResult();

        /// <summary>
        /// Quality score for initial derivation
        /// </summary>
        public DerivationQualityScore QualityScore { get; set; } = new DerivationQualityScore();

        /// <summary>
        /// Refined derivation (if quality-based refinement was performed)
        /// </summary>
        public DerivationResult RefinedDerivation { get; set; }

        /// <summary>
        /// Quality score for refined derivation
        /// </summary>
        public DerivationQualityScore RefinedQualityScore { get; set; }

        /// <summary>
        /// Generated synthetic training examples
        /// </summary>
        public List<SyntheticTrainingExample> SyntheticTrainingExamples { get; set; } = new List<SyntheticTrainingExample>();
    }

    /// <summary>
    /// Options for quality guidance during derivation
    /// </summary>
    public class QualityGuidanceOptions
    {
        /// <summary>
        /// Minimum quality threshold for accepting initial derivation
        /// </summary>
        public double QualityThreshold { get; set; } = 0.7;

        /// <summary>
        /// Whether to enable automatic quality-based refinement
        /// </summary>
        public bool EnableAutoRefinement { get; set; } = true;

        /// <summary>
        /// Whether to generate training examples from results
        /// </summary>      
        public bool GenerateTrainingExamples { get; set; } = true;

        /// <summary>
        /// Scoring options to use
        /// </summary>
        public QualityScoringOptions ScoringOptions { get; set; } = new QualityScoringOptions();
    }

    /// <summary>
    /// Correlation result between quality scores and validation outcomes
    /// </summary>
    public class QualityValidationCorrelationResult
    {
        /// <summary>
        /// Unique identifier for this correlation analysis
        /// </summary>
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// When this analysis was performed
        /// </summary>
        public DateTime AnalyzedAt { get; set; }

        /// <summary>
        /// Overall correlation coefficient between quality scores and validation outcomes
        /// </summary>
        public double OverallCorrelation { get; set; }

        /// <summary>
        /// Correlation for individual quality dimensions
        /// </summary>
        public Dictionary<string, double> DimensionCorrelations { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Accuracy of quality score predictions (0.0 to 1.0)
        /// </summary>
        public double PredictionAccuracy { get; set; }

        /// <summary>
        /// Identified improvements needed for scoring system
        /// </summary>
        public List<string> ScoringImprovements { get; set; } = new List<string>();
    }

    /// <summary>
    /// Pair of quality score and validation result for correlation analysis
    /// </summary>
    public class QualityValidationPair
    {
        /// <summary>
        /// Quality score assigned to derivation
        /// </summary>
        public DerivationQualityScore QualityScore { get; set; } = new DerivationQualityScore();

        /// <summary>
        /// Human validation result for the same derivation
        /// </summary>
        public TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult ValidationResult { get; set; } = new TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult();
    }

    /// <summary>
    /// Historical record of quality-validation correlation
    /// </summary>
    public class QualityValidationCorrelation
    {
        /// <summary>
        /// Quality score that was assigned
        /// </summary>
        public double QualityScore { get; set; }

        /// <summary>
        /// Validation decision that was made
        /// </summary>
        public TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationDecision ValidationOutcome { get; set; }

        /// <summary>
        /// Correlation analysis this belongs to
        /// </summary>
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// When this correlation was recorded
        /// </summary>
        public DateTime RecordedAt { get; set; }
    }

    /// <summary>
    /// Result of quality feedback loop analysis
    /// </summary>
    public class QualityFeedbackLoopResult
    {
        /// <summary>
        /// Unique identifier for this feedback loop run
        /// </summary>
        public string LoopId { get; set; } = string.Empty;

        /// <summary>
        /// When the loop started
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// When the loop completed
        /// </summary>
        public DateTime CompletedAt { get; set; }

        /// <summary>
        /// Time window analyzed
        /// </summary>
        public TimeSpan AnalysisWindow { get; set; }

        /// <summary>
        /// Overall feedback metrics
        /// </summary>
        public QualityFeedbackMetrics FeedbackMetrics { get; set; } = new QualityFeedbackMetrics();

        /// <summary>
        /// Systematic issues identified
        /// </summary>
        public List<string> SystematicIssues { get; set; } = new List<string>();

        /// <summary>
        /// Recommended calibration adjustments
        /// </summary>
        public Dictionary<string, double> CalibrationAdjustments { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Scoring system improvement recommendations
        /// </summary>
        public List<string> ScoringRecommendations { get; set; } = new List<string>();

        /// <summary>
        /// Self-evaluation report (if requested)
        /// </summary>
        public SelfEvaluationReport SelfEvaluationReport { get; set; }

        /// <summary>
        /// Potential for improvement (0.0 to 1.0)
        /// </summary>
        public double ImprovementPotential { get; set; }
    }

    /// <summary>
    /// Options for quality feedback loop
    /// </summary>
    public class QualityFeedbackOptions
    {
        /// <summary>
        /// Whether to include self-evaluation in feedback loop
        /// </summary>
        public bool IncludeSelfEvaluation { get; set; } = true;

        /// <summary>
        /// Minimum correlation strength to flag for attention
        /// </summary>
        public double MinCorrelationThreshold { get; set; } = 0.3;

        /// <summary>
        /// Whether to generate calibration adjustments
        /// </summary>
        public bool GenerateCalibrationAdjustments { get; set; } = true;
    }

    /// <summary>
    /// Metrics from quality feedback analysis
    /// </summary>
    public class QualityFeedbackMetrics
    {
        /// <summary>
        /// Total number of correlations analyzed
        /// </summary>
        public int TotalCorrelations { get; set; }

        /// <summary>
        /// Average quality score in the analysis window
        /// </summary>
        public double AverageQualityScore { get; set; }

        /// <summary>
        /// Rate of validation approvals (0.0 to 1.0)
        /// </summary>
        public double ApprovalRate { get; set; }

        /// <summary>
        /// Strength of correlation between scores and validation
        /// </summary>
        public double CorrelationStrength { get; set; }

        /// <summary>
        /// Variance in quality scores
        /// </summary>
        public double ScoreVariance { get; set; }
    }

    /// <summary>
    /// Active quality metrics for real-time monitoring
    /// </summary>
    public class ActiveQualityMetrics
    {
        /// <summary>
        /// Unique identifier for this metrics snapshot
        /// </summary>
        public string MetricsId { get; set; } = string.Empty;

        /// <summary>
        /// When these metrics were captured
        /// </summary>
        public DateTime CapturedAt { get; set; }

        /// <summary>
        /// Number of currently active derivation sessions
        /// </summary>
        public int ActiveSessions { get; set; }

        /// <summary>
        /// Average quality score from recent derivations
        /// </summary>
        public double RecentAverageQualityScore { get; set; }

        /// <summary>
        /// Recent validation approval rate
        /// </summary>
        public double RecentValidationApprovalRate { get; set; }

        /// <summary>
        /// Total correlations recorded in system
        /// </summary>
        public int TotalCorrelationsRecorded { get; set; }

        /// <summary>
        /// System uptime in hours
        /// </summary>
        public double SystemUptimeHours { get; set; }
    }

    /// <summary>
    /// Quality feedback session tracking
    /// </summary>
    public class QualityFeedbackSession
    {
        /// <summary>
        /// Unique session identifier
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// When session started
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// When session completed
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// ATP step being processed
        /// </summary>
        public string AtpStepText { get; set; } = string.Empty;

        /// <summary>
        /// History of feedback provided during session
        /// </summary>
        public List<RealTimeQualityFeedback> FeedbackHistory { get; set; } = new List<RealTimeQualityFeedback>();
    }
}