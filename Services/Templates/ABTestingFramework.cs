using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// A/B Testing Framework for empirical Template Form Architecture validation
    /// Compares template-based vs legacy JSON parsing with statistical analysis
    /// 
    /// PURPOSE: Measure and validate Template Form Architecture improvements
    /// METRICS: Quality, performance, completeness, success rates
    /// ANALYSIS: T-tests, confidence intervals, effect sizes
    /// 
    /// Task 6.9: Create A/B Testing Framework
    /// </summary>
    public sealed class ABTestingFramework : IABTestingFramework
    {
        private readonly IServiceComplianceWrapper? _complianceWrapper;
        private readonly IFieldLevelQualityService? _qualityService;
        private readonly ILogger<ABTestingFramework> _logger;
        private readonly List<IABTestObserver> _observers = new();
        private readonly List<ABTestResult> _executedTests = new();

        public ABTestingFramework(
            ILogger<ABTestingFramework> logger,
            IServiceComplianceWrapper? complianceWrapper = null,
            IFieldLevelQualityService? qualityService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _complianceWrapper = complianceWrapper;
            _qualityService = qualityService;

            _logger.LogInformation(
                "ABTestingFramework initialized. ComplianceWrapper: {HasCompliance}, QualityService: {HasQuality}",
                _complianceWrapper != null,
                _qualityService != null);
        }

        public async Task<ABTestResult> ExecuteABTestAsync<TInput, TResult>(
            ABTestConfig<TInput, TResult> config) where TResult : class
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var result = new ABTestResult
            {
                TestId = config.TestId,
                TestName = config.TestName,
                ExecutedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>(config.Metadata)
            };

            _logger.LogInformation("🧪 Starting A/B Test: {TestName} (ID: {TestId})", config.TestName, config.TestId);

            // Notify observers
            await NotifyTestStartedAsync(config.TestId, config.TestName);

            try
            {
                // Execute both approaches with iterations if configured
                var templateResults = new List<ApproachExecutionResult>();
                var legacyResults = new List<ApproachExecutionResult>();

                for (int i = 0; i < config.Iterations; i++)
                {
                    _logger.LogDebug("  Iteration {Iteration}/{Total}", i + 1, config.Iterations);

                    // Execute template approach
                    var templateResult = await ExecuteApproachAsync(
                        config.TemplateApproach,
                        config.Input,
                        ApproachType.Template,
                        config);
                    templateResults.Add(templateResult);

                    // Execute legacy approach
                    var legacyResult = await ExecuteApproachAsync(
                        config.LegacyApproach,
                        config.Input,
                        ApproachType.Legacy,
                        config);
                    legacyResults.Add(legacyResult);
                }

                // Aggregate results across iterations
                result.TemplateResult = AggregateExecutionResults(templateResults);
                result.LegacyResult = AggregateExecutionResults(legacyResults);

                // Compare the two approaches
                result.Comparison = CompareApproaches(result.TemplateResult, result.LegacyResult);

                // Log results
                LogTestResults(result);

                // Store for later analysis
                _executedTests.Add(result);

                // Notify observers
                await NotifyTestCompletedAsync(result);

                _logger.LogInformation(
                    "✅ A/B Test completed: {TestName}. Winner: {Winner}",
                    config.TestName,
                    result.Comparison.Winner);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ A/B Test failed: {TestName}", config.TestName);
                await NotifyTestErrorAsync(config.TestId, ex);
                throw;
            }

            return result;
        }

        public async Task<BatchABTestResult> ExecuteBatchABTestAsync<TInput, TResult>(
            BatchABTestConfig<TInput, TResult> config) where TResult : class
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var batchResult = new BatchABTestResult
            {
                BatchId = config.BatchId,
                BatchName = config.BatchName,
                ExecutedAt = DateTime.UtcNow
            };

            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "📊 Starting Batch A/B Test: {BatchName} ({Count} tests, Parallel: {Parallel})",
                config.BatchName,
                config.TestConfigs.Count,
                config.RunInParallel);

            try
            {
                if (config.RunInParallel)
                {
                    // Execute tests in parallel
                    var tasks = config.TestConfigs.Select(testConfig => ExecuteABTestAsync(testConfig));
                    batchResult.IndividualResults = (await Task.WhenAll(tasks)).ToList();
                }
                else
                {
                    // Execute tests sequentially
                    foreach (var testConfig in config.TestConfigs)
                    {
                        var testResult = await ExecuteABTestAsync(testConfig);
                        batchResult.IndividualResults.Add(testResult);
                    }
                }

                stopwatch.Stop();
                batchResult.TotalDuration = stopwatch.Elapsed;

                // Aggregate results
                var templateResults = batchResult.IndividualResults.Select(r => r.TemplateResult).ToList();
                var legacyResults = batchResult.IndividualResults.Select(r => r.LegacyResult).ToList();

                batchResult.TemplateAggregates = CalculateAggregateMetrics(templateResults, ApproachType.Template);
                batchResult.LegacyAggregates = CalculateAggregateMetrics(legacyResults, ApproachType.Legacy);

                // Perform comprehensive analysis
                batchResult.Analysis = await AnalyzeResultsAsync(
                    batchResult.IndividualResults,
                    batchResult.IndividualResults);

                _logger.LogInformation(
                    "✅ Batch A/B Test completed: {BatchName}. Template Win Rate: {WinRate:P2}",
                    config.BatchName,
                    batchResult.Analysis.TemplateWinRate);

                // Notify observers
                await NotifyBatchCompletedAsync(batchResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Batch A/B Test failed: {BatchName}", config.BatchName);
                throw;
            }

            return batchResult;
        }

        public async Task<ComparisonAnalysis> AnalyzeResultsAsync(
            List<ABTestResult> templateResults,
            List<ABTestResult> legacyResults)
        {
            var analysis = new ComparisonAnalysis();

            if (!templateResults.Any() || !legacyResults.Any())
            {
                _logger.LogWarning("⚠️ Cannot analyze results: insufficient data");
                return analysis;
            }

            // Extract quality scores for statistical analysis
            var templateQualityScores = templateResults
                .Select(r => r.TemplateResult.QualityScore)
                .ToList();

            var legacyQualityScores = legacyResults
                .Select(r => r.LegacyResult.QualityScore)
                .ToList();

            // Calculate statistical significance
            analysis.Significance = CalculateStatisticalSignificance(
                templateQualityScores,
                legacyQualityScores);

            // Determine overall winner
            var templateWins = templateResults.Count(r => r.Comparison.Winner == ApproachType.Template);
            var legacyWins = templateResults.Count(r => r.Comparison.Winner == ApproachType.Legacy);
            var ties = templateResults.Count(r => r.Comparison.Winner == ApproachType.Tie);

            analysis.TemplateWinRate = (double)templateWins / templateResults.Count;
            analysis.OverallWinner = templateWins > legacyWins ? ApproachType.Template : ApproachType.Legacy;
            analysis.Confidence = Math.Max(analysis.TemplateWinRate, 1 - analysis.TemplateWinRate);

            // Calculate average improvements
            foreach (MetricType metric in Enum.GetValues(typeof(MetricType)))
            {
                var improvements = templateResults
                    .Where(r => r.Comparison.MetricComparisons.ContainsKey(metric))
                    .Select(r => r.Comparison.MetricComparisons[metric].PercentageImprovement)
                    .ToList();

                if (improvements.Any())
                {
                    analysis.AverageImprovements[metric] = improvements.Average();
                }
            }

            // Generate insights
            analysis.KeyInsights = GenerateKeyInsights(analysis, templateResults);
            analysis.Recommendations = GenerateRecommendations(analysis);

            await Task.CompletedTask;
            return analysis;
        }

        public StatisticalSignificance CalculateStatisticalSignificance(
            List<double> templateScores,
            List<double> legacyScores)
        {
            var result = new StatisticalSignificance();

            if (templateScores.Count < 2 || legacyScores.Count < 2)
            {
                _logger.LogWarning("⚠️ Insufficient data for statistical significance calculation");
                return result;
            }

            // Calculate means
            var templateMean = templateScores.Average();
            var legacyMean = legacyScores.Average();

            // Calculate standard deviations
            var templateStdDev = CalculateStandardDeviation(templateScores);
            var legacyStdDev = CalculateStandardDeviation(legacyScores);

            // Calculate pooled standard deviation
            var n1 = templateScores.Count;
            var n2 = legacyScores.Count;
            var pooledStdDev = Math.Sqrt(
                ((n1 - 1) * templateStdDev * templateStdDev + (n2 - 1) * legacyStdDev * legacyStdDev)
                / (n1 + n2 - 2));

            // Calculate t-statistic
            result.TStatistic = (templateMean - legacyMean) / (pooledStdDev * Math.Sqrt(1.0 / n1 + 1.0 / n2));
            result.DegreesOfFreedom = n1 + n2 - 2;

            // Approximate p-value (simplified)
            result.PValue = CalculatePValue(Math.Abs(result.TStatistic), result.DegreesOfFreedom);
            result.IsSignificant = result.PValue < 0.05;

            // Calculate effect size (Cohen's d)
            result.EffectSize = (templateMean - legacyMean) / pooledStdDev;
            result.EffectSizeInterpretation = InterpretEffectSize(result.EffectSize);

            // Calculate 95% confidence interval
            var standardError = pooledStdDev * Math.Sqrt(1.0 / n1 + 1.0 / n2);
            var tCritical = 1.96; // Approximate for large samples
            var meanDifference = templateMean - legacyMean;

            result.ConfidenceInterval95 = new ConfidenceInterval
            {
                Mean = meanDifference,
                Lower = meanDifference - tCritical * standardError,
                Upper = meanDifference + tCritical * standardError,
                ConfidenceLevel = 0.95
            };

            return result;
        }

        public void RegisterTestObserver(IABTestObserver observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            _observers.Add(observer);
            _logger.LogDebug("📊 Registered A/B test observer: {ObserverType}", observer.GetType().Name);
        }

        public async Task ExportTestResultsAsync(string outputPath, ABTestExportFormat format)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path cannot be empty", nameof(outputPath));

            _logger.LogInformation("📤 Exporting {Count} test results to {Path} (Format: {Format})",
                _executedTests.Count, outputPath, format);

            try
            {
                switch (format)
                {
                    case ABTestExportFormat.Json:
                        await ExportAsJsonAsync(outputPath);
                        break;

                    case ABTestExportFormat.Csv:
                        await ExportAsCsvAsync(outputPath);
                        break;

                    default:
                        throw new NotSupportedException($"Export format {format} not yet implemented");
                }

                _logger.LogInformation("✅ Test results exported successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to export test results");
                throw;
            }
        }

        // Private helper methods

        private async Task<ApproachExecutionResult> ExecuteApproachAsync<TInput, TResult>(
            Func<TInput, Task<TResult>> approach,
            TInput input,
            ApproachType approachType,
            ABTestConfig<TInput, TResult> config) where TResult : class
        {
            var result = new ApproachExecutionResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Execute the approach
                var output = await approach(input);
                stopwatch.Stop();

                result.Success = true;
                result.Result = output;
                result.ExecutionTime = stopwatch.Elapsed;

                // Extract metrics
                await ExtractMetricsAsync(result, output, config);

                // Validate result if validator provided
                if (config.ResultValidator != null)
                {
                    result.ValidationResult = await config.ResultValidator(output);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Success = false;
                result.ExecutionTime = stopwatch.Elapsed;
                result.Errors.Add(ex.Message);
                _logger.LogError(ex, "❌ {ApproachType} approach execution failed", approachType);
            }

            return result;
        }

        private async Task ExtractMetricsAsync<TInput, TResult>(
            ApproachExecutionResult result,
            TResult output,
            ABTestConfig<TInput, TResult> config) where TResult : class
        {
            // Quality score
            if (output is IFilledForm filledForm)
            {
                result.QualityScore = filledForm.ValidationResult?.ComplianceScore ?? 0.0;
                result.FieldCompletenessScore = filledForm.CompletionScore;
                result.ParsedFieldCount = filledForm.FieldValues.Count;
                result.TotalExpectedFields = config.TemplateForm?.Fields.Count ?? 0;
            }
            else
            {
                // For non-form results, use basic metrics
                result.QualityScore = result.Success ? 1.0 : 0.0;
            }

            // Populate detailed metrics
            result.DetailedMetrics[MetricType.ExecutionTime] = result.ExecutionTime.TotalMilliseconds;
            result.DetailedMetrics[MetricType.QualityScore] = result.QualityScore;
            result.DetailedMetrics[MetricType.FieldCompleteness] = result.FieldCompletenessScore;
            result.DetailedMetrics[MetricType.ParsedFieldCount] = result.ParsedFieldCount;
            result.DetailedMetrics[MetricType.ErrorCount] = result.Errors.Count;

            await Task.CompletedTask;
        }

        private ApproachExecutionResult AggregateExecutionResults(
            List<ApproachExecutionResult> results)
        {
            if (!results.Any())
                return new ApproachExecutionResult();

            var aggregated = new ApproachExecutionResult
            {
                Success = results.All(r => r.Success),
                Result = results.First().Result, // Use first result as representative
                ExecutionTime = TimeSpan.FromMilliseconds(results.Average(r => r.ExecutionTime.TotalMilliseconds)),
                QualityScore = results.Average(r => r.QualityScore),
                FieldCompletenessScore = results.Average(r => r.FieldCompletenessScore),
                ParsedFieldCount = (int)results.Average(r => r.ParsedFieldCount),
                TotalExpectedFields = results.First().TotalExpectedFields,
                Errors = results.SelectMany(r => r.Errors).Distinct().ToList(),
                Warnings = results.SelectMany(r => r.Warnings).Distinct().ToList()
            };

            // Aggregate detailed metrics
            foreach (var metric in Enum.GetValues(typeof(MetricType)).Cast<MetricType>())
            {
                var values = results
                    .Where(r => r.DetailedMetrics.ContainsKey(metric))
                    .Select(r => r.DetailedMetrics[metric])
                    .ToList();

                if (values.Any())
                {
                    aggregated.DetailedMetrics[metric] = values.Average();
                }
            }

            return aggregated;
        }

        private ApproachComparison CompareApproaches(
            ApproachExecutionResult templateResult,
            ApproachExecutionResult legacyResult)
        {
            var comparison = new ApproachComparison();

            // Performance comparison (lower is better for execution time)
            comparison.PerformanceImprovement =
                ((legacyResult.ExecutionTime.TotalMilliseconds - templateResult.ExecutionTime.TotalMilliseconds)
                / legacyResult.ExecutionTime.TotalMilliseconds) * 100;

            // Quality comparison (higher is better)
            comparison.QualityImprovement =
                ((templateResult.QualityScore - legacyResult.QualityScore)
                / Math.Max(legacyResult.QualityScore, 0.001)) * 100;

            // Completeness comparison (higher is better)
            comparison.CompletenessImprovement =
                ((templateResult.FieldCompletenessScore - legacyResult.FieldCompletenessScore)
                / Math.Max(legacyResult.FieldCompletenessScore, 0.001)) * 100;

            // Success rate comparison
            comparison.SuccessRateDifference = (templateResult.Success ? 1.0 : 0.0) - (legacyResult.Success ? 1.0 : 0.0);

            // Metric-by-metric comparison
            foreach (var metric in Enum.GetValues(typeof(MetricType)).Cast<MetricType>())
            {
                if (templateResult.DetailedMetrics.ContainsKey(metric) &&
                    legacyResult.DetailedMetrics.ContainsKey(metric))
                {
                    var templateValue = templateResult.DetailedMetrics[metric];
                    var legacyValue = legacyResult.DetailedMetrics[metric];
                    var difference = templateValue - legacyValue;

                    // For execution time, lower is better
                    var isLowerBetter = metric == MetricType.ExecutionTime || metric == MetricType.ErrorCount;
                    var improvement = isLowerBetter
                        ? ((legacyValue - templateValue) / Math.Max(legacyValue, 0.001)) * 100
                        : ((templateValue - legacyValue) / Math.Max(legacyValue, 0.001)) * 100;

                    comparison.MetricComparisons[metric] = new MetricComparison
                    {
                        Metric = metric,
                        TemplateValue = templateValue,
                        LegacyValue = legacyValue,
                        Difference = difference,
                        PercentageImprovement = improvement,
                        Winner = improvement > 0 ? ApproachType.Template :
                                improvement < 0 ? ApproachType.Legacy : ApproachType.Tie
                    };
                }
            }

            // Determine overall winner
            var templateWins = comparison.MetricComparisons.Count(m => m.Value.Winner == ApproachType.Template);
            var legacyWins = comparison.MetricComparisons.Count(m => m.Value.Winner == ApproachType.Legacy);

            comparison.Winner = templateWins > legacyWins ? ApproachType.Template :
                               templateWins < legacyWins ? ApproachType.Legacy : ApproachType.Tie;

            // Generate key findings
            comparison.KeyFindings = GenerateComparisonFindings(comparison);

            return comparison;
        }

        private AggregateMetrics CalculateAggregateMetrics(
            List<ApproachExecutionResult> results,
            ApproachType approachType)
        {
            if (!results.Any())
                return new AggregateMetrics();

            var metrics = new AggregateMetrics
            {
                TotalTests = results.Count,
                SuccessfulTests = results.Count(r => r.Success),
                TotalErrors = results.Sum(r => r.Errors.Count),
                TotalWarnings = results.Sum(r => r.Warnings.Count)
            };

            metrics.SuccessRate = (double)metrics.SuccessfulTests / metrics.TotalTests;

            // Execution time statistics
            var executionTimes = results.Select(r => r.ExecutionTime.TotalMilliseconds).ToList();
            metrics.AverageExecutionTimeMs = executionTimes.Average();
            metrics.MedianExecutionTimeMs = CalculateMedian(executionTimes);
            metrics.StdDevExecutionTimeMs = CalculateStandardDeviation(executionTimes);

            // Quality score statistics
            var qualityScores = results.Select(r => r.QualityScore).ToList();
            metrics.AverageQualityScore = qualityScores.Average();
            metrics.MedianQualityScore = CalculateMedian(qualityScores);
            metrics.StdDevQualityScore = CalculateStandardDeviation(qualityScores);
            metrics.MinQualityScore = qualityScores.Min();
            metrics.MaxQualityScore = qualityScores.Max();

            // Field completeness statistics
            var completenessScores = results.Select(r => r.FieldCompletenessScore).ToList();
            metrics.AverageFieldCompleteness = completenessScores.Average();
            metrics.MedianFieldCompleteness = CalculateMedian(completenessScores);

            // Detailed metric statistics
            foreach (var metric in Enum.GetValues(typeof(MetricType)).Cast<MetricType>())
            {
                var values = results
                    .Where(r => r.DetailedMetrics.ContainsKey(metric))
                    .Select(r => r.DetailedMetrics[metric])
                    .ToList();

                if (values.Any())
                {
                    metrics.MetricStatistics[metric] = new MetricStatistics
                    {
                        Metric = metric,
                        Mean = values.Average(),
                        Median = CalculateMedian(values),
                        StdDev = CalculateStandardDeviation(values),
                        Min = values.Min(),
                        Max = values.Max(),
                        Variance = CalculateVariance(values),
                        AllValues = values
                    };
                }
            }

            return metrics;
        }

        private List<string> GenerateKeyInsights(
            ComparisonAnalysis analysis,
            List<ABTestResult> results)
        {
            var insights = new List<string>();

            // Win rate insight
            if (analysis.TemplateWinRate > 0.7)
            {
                insights.Add($"Template approach wins {analysis.TemplateWinRate:P0} of tests - strong evidence of superiority");
            }
            else if (analysis.TemplateWinRate > 0.5)
            {
                insights.Add($"Template approach wins {analysis.TemplateWinRate:P0} of tests - moderate advantage");
            }
            else if (analysis.TemplateWinRate < 0.3)
            {
                insights.Add($"Legacy approach wins {1 - analysis.TemplateWinRate:P0} of tests - template needs improvement");
            }

            // Statistical significance insight
            if (analysis.Significance.IsSignificant)
            {
                insights.Add($"Results are statistically significant (p = {analysis.Significance.PValue:F4}) - differences are not due to chance");
            }
            else
            {
                insights.Add($"Results are not statistically significant (p = {analysis.Significance.PValue:F4}) - more data needed");
            }

            // Effect size insight
            insights.Add($"Effect size: {analysis.Significance.EffectSizeInterpretation} ({analysis.Significance.EffectSize:F2})");

            // Metric-specific insights
            if (analysis.AverageImprovements.ContainsKey(MetricType.QualityScore))
            {
                var qualityImprovement = analysis.AverageImprovements[MetricType.QualityScore];
                if (qualityImprovement > 10)
                {
                    insights.Add($"Quality scores improved by {qualityImprovement:F1}% on average with template approach");
                }
            }

            return insights;
        }

        private List<string> GenerateRecommendations(ComparisonAnalysis analysis)
        {
            var recommendations = new List<string>();

            if (analysis.TemplateWinRate > 0.7 && analysis.Significance.IsSignificant)
            {
                recommendations.Add("RECOMMENDED: Migrate to template-based approach for production use");
                recommendations.Add("Template approach shows significant improvements across multiple metrics");
            }
            else if (analysis.TemplateWinRate > 0.5)
            {
                recommendations.Add("CONSIDER: Template approach shows promise but needs more validation");
                recommendations.Add("Run additional tests to build confidence before full migration");
            }
            else
            {
                recommendations.Add("CAUTION: Template approach not yet ready for production");
                recommendations.Add("Investigate root causes of template approach underperformance");
            }

            if (!analysis.Significance.IsSignificant)
            {
                recommendations.Add("Increase sample size to achieve statistical significance");
            }

            return recommendations;
        }

        private List<string> GenerateComparisonFindings(ApproachComparison comparison)
        {
            var findings = new List<string>();

            if (comparison.QualityImprovement > 10)
            {
                findings.Add($"Quality improved by {comparison.QualityImprovement:F1}%");
            }

            if (comparison.PerformanceImprovement > 10)
            {
                findings.Add($"Performance improved by {comparison.PerformanceImprovement:F1}%");
            }
            else if (comparison.PerformanceImprovement < -10)
            {
                findings.Add($"Performance degraded by {Math.Abs(comparison.PerformanceImprovement):F1}%");
            }

            if (comparison.CompletenessImprovement > 5)
            {
                findings.Add($"Field completeness improved by {comparison.CompletenessImprovement:F1}%");
            }

            return findings;
        }

        private void LogTestResults(ABTestResult result)
        {
            _logger.LogInformation("  📈 Template: Success={Success}, Quality={Quality:F2}, Time={TimeMs}ms",
                result.TemplateResult.Success,
                result.TemplateResult.QualityScore,
                result.TemplateResult.ExecutionTime.TotalMilliseconds);

            _logger.LogInformation("  📉 Legacy: Success={Success}, Quality={Quality:F2}, Time={TimeMs}ms",
                result.LegacyResult.Success,
                result.LegacyResult.QualityScore,
                result.LegacyResult.ExecutionTime.TotalMilliseconds);

            _logger.LogInformation("  🏆 Winner: {Winner}, Quality Improvement: {Improvement:F1}%",
                result.Comparison.Winner,
                result.Comparison.QualityImprovement);
        }

        // Statistical helper methods

        private double CalculateMedian(List<double> values)
        {
            if (!values.Any()) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            int count = sorted.Count;
            if (count % 2 == 0)
            {
                return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
            }
            return sorted[count / 2];
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            double sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }

        private double CalculateVariance(List<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        }

        private double CalculatePValue(double tStatistic, int degreesOfFreedom)
        {
            // Simplified p-value calculation (approximation)
            // For production, use a proper statistical library
            if (degreesOfFreedom < 1) return 1.0;

            double absT = Math.Abs(tStatistic);

            // Rough approximation using normal distribution for large df
            if (degreesOfFreedom > 30)
            {
                return 2.0 * (1.0 - ApproximateNormalCDF(absT));
            }

            // Very rough approximation for smaller df
            if (absT < 1.5) return 0.2;
            if (absT < 2.0) return 0.05;
            if (absT < 2.5) return 0.02;
            if (absT < 3.0) return 0.01;
            return 0.001;
        }

        private double ApproximateNormalCDF(double z)
        {
            // Approximation of cumulative distribution function for standard normal
            return 0.5 * (1.0 + Math.Tanh(0.7978845608 * (z + 0.044715 * Math.Pow(z, 3))));
        }

        private string InterpretEffectSize(double effectSize)
        {
            double abs = Math.Abs(effectSize);
            if (abs < 0.2) return "Negligible";
            if (abs < 0.5) return "Small";
            if (abs < 0.8) return "Medium";
            return "Large";
        }

        // Export methods

        private async Task ExportAsJsonAsync(string outputPath)
        {
            var json = JsonSerializer.Serialize(_executedTests, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(outputPath, json);
        }

        private async Task ExportAsCsvAsync(string outputPath)
        {
            var lines = new List<string>
            {
                "TestId,TestName,ExecutedAt,Winner,TemplateQuality,LegacyQuality,QualityImprovement,TemplateTime,LegacyTime,PerformanceImprovement"
            };

            foreach (var test in _executedTests)
            {
                lines.Add($"{test.TestId},{test.TestName},{test.ExecutedAt:o},{test.Comparison.Winner}," +
                         $"{test.TemplateResult.QualityScore:F4},{test.LegacyResult.QualityScore:F4}," +
                         $"{test.Comparison.QualityImprovement:F2}," +
                         $"{test.TemplateResult.ExecutionTime.TotalMilliseconds:F2}," +
                         $"{test.LegacyResult.ExecutionTime.TotalMilliseconds:F2}," +
                         $"{test.Comparison.PerformanceImprovement:F2}");
            }

            await File.WriteAllLinesAsync(outputPath, lines);
        }

        // Observer notification methods

        private async Task NotifyTestStartedAsync(string testId, string testName)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    await observer.OnTestStartedAsync(testId, testName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Observer notification failed (TestStarted)");
                }
            }
        }

        private async Task NotifyTestCompletedAsync(ABTestResult result)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    await observer.OnTestCompletedAsync(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Observer notification failed (TestCompleted)");
                }
            }
        }

        private async Task NotifyBatchCompletedAsync(BatchABTestResult result)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    await observer.OnBatchCompletedAsync(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Observer notification failed (BatchCompleted)");
                }
            }
        }

        private async Task NotifyTestErrorAsync(string testId, Exception exception)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    await observer.OnTestErrorAsync(testId, exception);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Observer notification failed (TestError)");
                }
            }
        }
    }
}
