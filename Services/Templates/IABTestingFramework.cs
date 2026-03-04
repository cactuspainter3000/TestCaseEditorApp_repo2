using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// A/B Testing Framework for Template Form Architecture validation
    /// Compares template-based vs legacy JSON parsing approaches
    /// 
    /// PURPOSE: Empirical validation of Template Form Architecture benefits
    /// METRICS: Quality scores, parsing success rates, field completion, performance
    /// 
    /// Task 6.9: Create A/B Testing Framework
    /// </summary>
    public interface IABTestingFramework
    {
        /// <summary>
        /// Execute A/B test comparing template vs legacy approach
        /// </summary>
        Task<ABTestResult> ExecuteABTestAsync<TInput, TResult>(
            ABTestConfig<TInput, TResult> config) where TResult : class;

        /// <summary>
        /// Execute batch A/B test with multiple test cases
        /// </summary>
        Task<BatchABTestResult> ExecuteBatchABTestAsync<TInput, TResult>(
            BatchABTestConfig<TInput, TResult> config) where TResult : class;

        /// <summary>
        /// Analyze and compare aggregate results from multiple tests
        /// </summary>
        Task<ComparisonAnalysis> AnalyzeResultsAsync(
            List<ABTestResult> templateResults,
            List<ABTestResult> legacyResults);

        /// <summary>
        /// Get statistical significance between two approaches
        /// </summary>
        StatisticalSignificance CalculateStatisticalSignificance(
            List<double> templateScores,
            List<double> legacyScores);

        /// <summary>
        /// Register an A/B test execution observer for real-time monitoring
        /// </summary>
        void RegisterTestObserver(IABTestObserver observer);

        /// <summary>
        /// Export A/B test results for external analysis
        /// </summary>
        Task ExportTestResultsAsync(string outputPath, ABTestExportFormat format);
    }

    /// <summary>
    /// Configuration for A/B test execution
    /// </summary>
    public class ABTestConfig<TInput, TResult> where TResult : class
    {
        /// <summary>
        /// Unique test identifier
        /// </summary>
        public string TestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Test name for reporting
        /// </summary>
        public string TestName { get; set; } = "UnnamedTest";

        /// <summary>
        /// Input data for the test
        /// </summary>
        public TInput Input { get; set; } = default!;

        /// <summary>
        /// Template-based approach implementation
        /// </summary>
        public Func<TInput, Task<TResult>> TemplateApproach { get; set; } = null!;

        /// <summary>
        /// Legacy JSON parsing approach implementation
        /// </summary>
        public Func<TInput, Task<TResult>> LegacyApproach { get; set; } = null!;

        /// <summary>
        /// Template form for template approach validation
        /// </summary>
        public IFormTemplate? TemplateForm { get; set; }

        /// <summary>
        /// Expected schema for validation
        /// </summary>
        public EnvelopeSchema? ExpectedSchema { get; set; }

        /// <summary>
        /// Validation function to verify result correctness
        /// </summary>
        public Func<TResult, Task<ABTestValidationResult>>? ResultValidator { get; set; }

        /// <summary>
        /// Metrics to collect during test execution
        /// </summary>
        public List<MetricType> MetricsToCollect { get; set; } = new()
        {
            MetricType.ExecutionTime,
            MetricType.QualityScore,
            MetricType.SuccessRate,
            MetricType.FieldCompleteness
        };

        /// <summary>
        /// Number of iterations to run for statistical validity
        /// </summary>
        public int Iterations { get; set; } = 1;

        /// <summary>
        /// Enable detailed logging
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;

        /// <summary>
        /// Metadata for test tracking
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Configuration for batch A/B testing
    /// </summary>
    public class BatchABTestConfig<TInput, TResult> where TResult : class
    {
        public string BatchId { get; set; } = Guid.NewGuid().ToString();
        public string BatchName { get; set; } = "UnnamedBatch";
        public List<ABTestConfig<TInput, TResult>> TestConfigs { get; set; } = new();
        public bool RunInParallel { get; set; } = false;
        public int MaxDegreeOfParallelism { get; set; } = 4;
    }

    /// <summary>
    /// Result of A/B test execution
    /// </summary>
    public class ABTestResult
    {
        public string TestId { get; set; } = Guid.NewGuid().ToString();
        public string TestName { get; set; } = "UnnamedTest";
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Template approach execution results
        /// </summary>
        public ApproachExecutionResult TemplateResult { get; set; } = new();

        /// <summary>
        /// Legacy approach execution results
        /// </summary>
        public ApproachExecutionResult LegacyResult { get; set; } = new();

        /// <summary>
        /// Comparison of the two approaches
        /// </summary>
        public ApproachComparison Comparison { get; set; } = new();

        /// <summary>
        /// Test metadata
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Batch test result aggregation
    /// </summary>
    public class BatchABTestResult
    {
        public string BatchId { get; set; } = Guid.NewGuid().ToString();
        public string BatchName { get; set; } = "UnnamedBatch";
        public List<ABTestResult> IndividualResults { get; set; } = new();
        public AggregateMetrics TemplateAggregates { get; set; } = new();
        public AggregateMetrics LegacyAggregates { get; set; } = new();
        public ComparisonAnalysis Analysis { get; set; } = new();
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan TotalDuration { get; set; }
    }

    /// <summary>
    /// Execution result for a single approach
    /// </summary>
    public class ApproachExecutionResult
    {
        public bool Success { get; set; }
        public object? Result { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public double QualityScore { get; set; }
        public double FieldCompletenessScore { get; set; }
        public int ParsedFieldCount { get; set; }
        public int TotalExpectedFields { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<MetricType, double> DetailedMetrics { get; set; } = new();
        public ABTestValidationResult? ValidationResult { get; set; }
    }

    /// <summary>
    /// Comparison between template and legacy approaches
    /// </summary>
    public class ApproachComparison
    {
        /// <summary>
        /// Winner of the comparison (Template, Legacy, or Tie)
        /// </summary>
        public ApproachType Winner { get; set; }

        /// <summary>
        /// Performance improvement (positive = template better)
        /// </summary>
        public double PerformanceImprovement { get; set; }

        /// <summary>
        /// Quality score improvement (positive = template better)
        /// </summary>
        public double QualityImprovement { get; set; }

        /// <summary>
        /// Field completeness improvement (positive = template better)
        /// </summary>
        public double CompletenessImprovement { get; set; }

        /// <summary>
        /// Success rate comparison
        /// </summary>
        public double SuccessRateDifference { get; set; }

        /// <summary>
        /// Detailed metric-by-metric comparison
        /// </summary>
        public Dictionary<MetricType, MetricComparison> MetricComparisons { get; set; } = new();

        /// <summary>
        /// Summary of key findings
        /// </summary>
        public List<string> KeyFindings { get; set; } = new();
    }

    /// <summary>
    /// Statistical aggregates across multiple tests
    /// </summary>
    public class AggregateMetrics
    {
        public int TotalTests { get; set; }
        public int SuccessfulTests { get; set; }
        public double SuccessRate { get; set; }
        public double AverageExecutionTimeMs { get; set; }
        public double MedianExecutionTimeMs { get; set; }
        public double StdDevExecutionTimeMs { get; set; }
        public double AverageQualityScore { get; set; }
        public double MedianQualityScore { get; set; }
        public double StdDevQualityScore { get; set; }
        public double AverageFieldCompleteness { get; set; }
        public double MedianFieldCompleteness { get; set; }
        public double MinQualityScore { get; set; }
        public double MaxQualityScore { get; set; }
        public int TotalErrors { get; set; }
        public int TotalWarnings { get; set; }
        public Dictionary<MetricType, MetricStatistics> MetricStatistics { get; set; } = new();
    }

    /// <summary>
    /// Comprehensive comparison analysis
    /// </summary>
    public class ComparisonAnalysis
    {
        /// <summary>
        /// Overall winner across all tests
        /// </summary>
        public ApproachType OverallWinner { get; set; }

        /// <summary>
        /// Confidence level in the winner determination (0.0 - 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Statistical significance of the comparison
        /// </summary>
        public StatisticalSignificance Significance { get; set; } = new();

        /// <summary>
        /// Win rate (percentage of times template approach won)
        /// </summary>
        public double TemplateWinRate { get; set; }

        /// <summary>
        /// Average improvements across all metrics
        /// </summary>
        public Dictionary<MetricType, double> AverageImprovements { get; set; } = new();

        /// <summary>
        /// Category-level analysis
        /// </summary>
        public List<CategoryAnalysis> CategoryAnalyses { get; set; } = new();

        /// <summary>
        /// Recommendations based on analysis
        /// </summary>
        public List<string> Recommendations { get; set; } = new();

        /// <summary>
        /// Key insights from comparison
        /// </summary>
        public List<string> KeyInsights { get; set; } = new();
    }

    /// <summary>
    /// Statistical significance calculation
    /// </summary>
    public class StatisticalSignificance
    {
        /// <summary>
        /// P-value from statistical test (typically t-test)
        /// </summary>
        public double PValue { get; set; }

        /// <summary>
        /// T-statistic value
        /// </summary>
        public double TStatistic { get; set; }

        /// <summary>
        /// Degrees of freedom
        /// </summary>
        public int DegreesOfFreedom { get; set; }

        /// <summary>
        /// Is statistically significant at p < 0.05
        /// </summary>
        public bool IsSignificant { get; set; }

        /// <summary>
        /// Confidence interval (95%)
        /// </summary>
        public ConfidenceInterval ConfidenceInterval95 { get; set; } = new();

        /// <summary>
        /// Effect size (Cohen's d)
        /// </summary>
        public double EffectSize { get; set; }

        /// <summary>
        /// Interpretation of effect size
        /// </summary>
        public string EffectSizeInterpretation { get; set; } = "Unknown";
    }

    /// <summary>
    /// Confidence interval
    /// </summary>
    public class ConfidenceInterval
    {
        public double Lower { get; set; }
        public double Upper { get; set; }
        public double Mean { get; set; }
        public double ConfidenceLevel { get; set; } = 0.95;
    }

    /// <summary>
    /// Metric-specific comparison
    /// </summary>
    public class MetricComparison
    {
        public MetricType Metric { get; set; }
        public double TemplateValue { get; set; }
        public double LegacyValue { get; set; }
        public double Difference { get; set; }
        public double PercentageImprovement { get; set; }
        public ApproachType Winner { get; set; }
    }

    /// <summary>
    /// Statistical metrics for a specific measurement
    /// </summary>
    public class MetricStatistics
    {
        public MetricType Metric { get; set; }
        public double Mean { get; set; }
        public double Median { get; set; }
        public double StdDev { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Variance { get; set; }
        public List<double> AllValues { get; set; } = new();
    }

    /// <summary>
    /// Category-specific analysis (e.g., by criticality level)
    /// </summary>
    public class CategoryAnalysis
    {
        public string CategoryName { get; set; } = "Unknown";
        public int TemplateWins { get; set; }
        public int LegacyWins { get; set; }
        public int Ties { get; set; }
        public double AverageImprovement { get; set; }
        public List<string> Insights { get; set; } = new();
    }

    /// <summary>
    /// Validation result for A/B test execution
    /// </summary>
    public class ABTestValidationResult
    {
        public bool IsValid { get; set; }
        public double Score { get; set; }
        public List<string> Violations { get; set; } = new();
    }

    /// <summary>
    /// Observer interface for A/B test execution monitoring
    /// </summary>
    public interface IABTestObserver
    {
        Task OnTestStartedAsync(string testId, string testName);
        Task OnTestCompletedAsync(ABTestResult result);
        Task OnBatchCompletedAsync(BatchABTestResult batchResult);
        Task OnTestErrorAsync(string testId, Exception exception);
    }

    /// <summary>
    /// Types of approaches being compared
    /// </summary>
    public enum ApproachType
    {
        Template,
        Legacy,
        Tie
    }

    /// <summary>
    /// Types of metrics to collect
    /// </summary>
    public enum MetricType
    {
        ExecutionTime,
        QualityScore,
        SuccessRate,
        FieldCompleteness,
        ParsedFieldCount,
        ErrorCount,
        WarningCount,
        ConfidenceScore,
        ValidationScore,
        ComplianceScore
    }

    /// <summary>
    /// Export formats for test results
    /// </summary>
    public enum ABTestExportFormat
    {
        Json,
        Csv,
        Excel,
        Html
    }
}
