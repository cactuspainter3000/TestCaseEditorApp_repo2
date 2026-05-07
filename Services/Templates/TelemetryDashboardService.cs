using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Telemetry and Dashboard Service for Template Form Architecture
    /// Aggregates metrics from Tasks 6.3, 6.5, 6.8, 6.9 for enterprise monitoring
    /// 
    /// ARCHITECTURE: Central telemetry hub with time-series storage and real-time notifications
    /// INTEGRATION: Quality scores, compliance violations, A/B tests, field completion
    /// MONITORING: Confidence calibration, performance trends, dashboard exports
    /// 
    /// Task 6.10: Add Telemetry & Dashboard Integration
    /// </summary>
    public sealed class TelemetryDashboardService : ITelemetryDashboardService
    {
        private readonly ILogger<TelemetryDashboardService> _logger;
        private readonly IFieldLevelQualityService? _qualityService;
        private readonly IABTestingFramework? _abTestingFramework;
        private readonly IServiceComplianceWrapper? _complianceWrapper;
        
        private readonly List<ITelemetryObserver> _observers = new();
        private readonly object _lockObject = new();
        
        // Time-series storage
        private readonly List<LLMResponseTelemetry> _llmResponses = new();
        private readonly List<FieldCompletionEvent> _fieldEvents = new();
        private readonly List<ComplianceEventTelemetry> _complianceEvents = new();
        private readonly List<ABTestResult> _abTestResults = new();
        
        // Health tracking
        private long _totalEventsTracked = 0;
        private DateTime _lastEventTime = DateTime.UtcNow;
        private readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();

        public TelemetryDashboardService(
            ILogger<TelemetryDashboardService> logger,
            IFieldLevelQualityService? qualityService = null,
            IABTestingFramework? abTestingFramework = null,
            IServiceComplianceWrapper? complianceWrapper = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _qualityService = qualityService;
            _abTestingFramework = abTestingFramework;
            _complianceWrapper = complianceWrapper;

            var integrationMode = DetermineIntegrationMode();
            _logger.LogInformation(
                "📊 TelemetryDashboardService initialized. Integration: {IntegrationMode}",
                integrationMode);
        }

        public async Task<DashboardSnapshot> GetDashboardSnapshotAsync()
        {
            _logger.LogDebug("📸 Generating dashboard snapshot");

            var snapshot = new DashboardSnapshot
            {
                Timestamp = DateTime.UtcNow,
                TotalOperations = CalculateTotalOperations(),
                SuccessfulOperations = CalculateSuccessfulOperations()
            };

            snapshot.SuccessRate = snapshot.TotalOperations > 0
                ? (double)snapshot.SuccessfulOperations / snapshot.TotalOperations
                : 0.0;

            // Aggregate sub-dashboards
            snapshot.FieldCompletion = await GetFieldCompletionMetricsAsync();
            snapshot.ConfidenceCalibration = await GetConfidenceCalibrationMetricsAsync();
            snapshot.ABTestSummary = await GetABTestDashboardAsync();
            snapshot.ComplianceSummary = await GetComplianceDashboardAsync();
            snapshot.QualityTrends = await GetQualityTrendMetricsAsync(TimeSpan.FromDays(7));
            snapshot.Performance = GetPerformanceDashboard();
            snapshot.HealthStatus = GetHealthStatus();

            snapshot.AverageQualityScore = CalculateAverageQualityScore();
            snapshot.AverageFieldCompletionRate = snapshot.FieldCompletion.OverallCompletionRate;

            _logger.LogInformation(
                "✅ Dashboard snapshot generated: {TotalOps} operations, {SuccessRate:P} success, {QualityScore:F3} quality",
                snapshot.TotalOperations,
                snapshot.SuccessRate,
                snapshot.AverageQualityScore);

            return snapshot;
        }

        public async Task<FieldCompletionMetrics> GetFieldCompletionMetricsAsync()
        {
            var metrics = new FieldCompletionMetrics();

            lock (_lockObject)
            {
                if (_fieldEvents.Count == 0)
                {
                    return metrics;
                }

                metrics.TotalFieldsTracked = _fieldEvents.Count;
                metrics.CompletedFields = _fieldEvents.Count(e => e.CompletionStatus == FieldCompletionStatus.Complete);
                metrics.PartiallyCompletedFields = _fieldEvents.Count(e => e.CompletionStatus == FieldCompletionStatus.Partial);
                metrics.EmptyFields = _fieldEvents.Count(e => e.CompletionStatus == FieldCompletionStatus.Empty);

                metrics.OverallCompletionRate = metrics.TotalFieldsTracked > 0
                    ? (double)metrics.CompletedFields / metrics.TotalFieldsTracked
                    : 0.0;

                // Per-field breakdown
                var fieldGroups = _fieldEvents.GroupBy(e => e.FieldName);
                foreach (var group in fieldGroups)
                {
                    var stats = new FieldStatistics
                    {
                        FieldName = group.Key,
                        TotalOccurrences = group.Count(),
                        CompletedCount = group.Count(e => e.CompletionStatus == FieldCompletionStatus.Complete),
                        PartialCount = group.Count(e => e.CompletionStatus == FieldCompletionStatus.Partial),
                        EmptyCount = group.Count(e => e.CompletionStatus == FieldCompletionStatus.Empty),
                        AverageQualityScore = group.Average(e => e.QualityScore),
                        AverageConfidence = group.Average(e => e.Confidence)
                    };
                    stats.CompletionRate = stats.TotalOccurrences > 0
                        ? (double)stats.CompletedCount / stats.TotalOccurrences
                        : 0.0;
                    metrics.FieldBreakdown[group.Key] = stats;
                }

                // Completion by template
                var templateGroups = _fieldEvents
                    .Where(e => !string.IsNullOrEmpty(e.TemplateId))
                    .GroupBy(e => e.TemplateId);
                foreach (var group in templateGroups)
                {
                    var completionRate = (double)group.Count(e => e.CompletionStatus == FieldCompletionStatus.Complete) / group.Count();
                    metrics.CompletionByTemplate[group.Key] = completionRate;
                }

                // Completion trend (last 24 hours, hourly buckets)
                metrics.CompletionTrend = GenerateCompletionTrend(_fieldEvents);
            }

            return await Task.FromResult(metrics);
        }

        public async Task<ConfidenceCalibrationMetrics> GetConfidenceCalibrationMetricsAsync()
        {
            var metrics = new ConfidenceCalibrationMetrics();

            lock (_lockObject)
            {
                if (_llmResponses.Count == 0)
                {
                    return metrics;
                }

                metrics.TotalResponses = _llmResponses.Count;

                // Create calibration bins (0-0.2, 0.2-0.4, 0.4-0.6, 0.6-0.8, 0.8-1.0)
                var bins = new List<CalibrationBin>();
                for (int i = 0; i < 5; i++)
                {
                    double minConf = i * 0.2;
                    double maxConf = (i + 1) * 0.2;
                    
                    var binResponses = _llmResponses
                        .Where(r => r.LLMConfidence >= minConf && r.LLMConfidence < maxConf)
                        .ToList();

                    if (binResponses.Any())
                    {
                        var bin = new CalibrationBin
                        {
                            MinConfidence = minConf,
                            MaxConfidence = maxConf,
                            MeanConfidence = binResponses.Average(r => r.LLMConfidence),
                            MeanAccuracy = binResponses.Average(r => r.ActualQualityScore),
                            SampleCount = binResponses.Count
                        };
                        bin.CalibrationGap = Math.Abs(bin.MeanConfidence - bin.MeanAccuracy);
                        bins.Add(bin);
                    }
                }
                metrics.CalibrationBins = bins;

                // Calculate Expected Calibration Error (ECE)
                metrics.ExpectedCalibrationError = bins
                    .Sum(b => ((double)b.SampleCount / metrics.TotalResponses) * b.CalibrationGap);

                // Calibration score (1 - ECE)
                metrics.CalibrationScore = 1.0 - metrics.ExpectedCalibrationError;

                // Over/underconfidence
                var overconfident = _llmResponses.Count(r => r.LLMConfidence > r.ActualQualityScore);
                var underconfident = _llmResponses.Count(r => r.LLMConfidence < r.ActualQualityScore);
                metrics.OverconfidenceRate = (double)overconfident / metrics.TotalResponses;
                metrics.UnderconfidenceRate = (double)underconfident / metrics.TotalResponses;

                // Calibration trend
                metrics.CalibrationTrend = GenerateCalibrationTrend(_llmResponses);
            }

            _logger.LogDebug(
                "🎯 Confidence calibration: Score={CalibrationScore:F3}, ECE={ECE:F3}, Overconfident={Over:P}",
                metrics.CalibrationScore,
                metrics.ExpectedCalibrationError,
                metrics.OverconfidenceRate);

            return await Task.FromResult(metrics);
        }

        public async Task<ABTestDashboard> GetABTestDashboardAsync()
        {
            var dashboard = new ABTestDashboard();

            lock (_lockObject)
            {
                if (_abTestResults.Count == 0)
                {
                    return dashboard;
                }

                dashboard.TotalTests = _abTestResults.Count;
                dashboard.TemplateWins = _abTestResults.Count(t => t.Comparison.Winner == ApproachType.Template);
                dashboard.LegacyWins = _abTestResults.Count(t => t.Comparison.Winner == ApproachType.Legacy);
                dashboard.Ties = _abTestResults.Count(t => t.Comparison.Winner == ApproachType.Template && 
                                                           t.Comparison.QualityImprovement < 0.01 &&
                                                           t.Comparison.PerformanceImprovement < 0.01);

                dashboard.TemplateWinRate = dashboard.TotalTests > 0
                    ? (double)dashboard.TemplateWins / dashboard.TotalTests
                    : 0.0;

                // Average improvements
                dashboard.AverageQualityImprovement = _abTestResults.Average(t => t.Comparison.QualityImprovement);
                dashboard.AveragePerformanceImprovement = _abTestResults.Average(t => t.Comparison.PerformanceImprovement);
                dashboard.AverageCompletenessImprovement = _abTestResults.Average(t => t.Comparison.CompletenessImprovement);

                // Recent tests (last 10)
                dashboard.RecentTests = _abTestResults
                    .OrderByDescending(t => t.ExecutedAt)
                    .Take(10)
                    .Select(t => new ABTestSummary
                    {
                        TestId = t.TestId,
                        TestName = t.TestName,
                        ExecutedAt = t.ExecutedAt,
                        Winner = t.Comparison.Winner,
                        QualityImprovement = t.Comparison.QualityImprovement,
                        PerformanceImprovement = t.Comparison.PerformanceImprovement,
                        StatisticallySignificant = true // Placeholder
                    })
                    .ToList();

                // Trends
                dashboard.WinRateTrend = GenerateWinRateTrend(_abTestResults);
                dashboard.QualityImprovementTrend = GenerateQualityImprovementTrend(_abTestResults);
            }

            _logger.LogDebug(
                "🧪 A/B Test Dashboard: {TotalTests} tests, {WinRate:P} template win rate, {QualityImprovement:+0.0;-0.0}% quality improvement",
                dashboard.TotalTests,
                dashboard.TemplateWinRate,
                dashboard.AverageQualityImprovement * 100);

            return await Task.FromResult(dashboard);
        }

        public async Task<ComplianceDashboard> GetComplianceDashboardAsync()
        {
            var dashboard = new ComplianceDashboard();

            lock (_lockObject)
            {
                if (_complianceEvents.Count == 0)
                {
                    return dashboard;
                }

                dashboard.TotalComplianceChecks = _complianceEvents.Count;
                dashboard.PassedChecks = _complianceEvents.Count(e => e.Passed);
                dashboard.FailedChecks = _complianceEvents.Count(e => !e.Passed);
                dashboard.ComplianceRate = dashboard.TotalComplianceChecks > 0
                    ? (double)dashboard.PassedChecks / dashboard.TotalComplianceChecks
                    : 0.0;
                dashboard.AverageComplianceScore = _complianceEvents.Average(e => e.ComplianceScore);

                // Violations by type
                var allViolations = _complianceEvents.SelectMany(e => e.Violations);
                dashboard.ViolationsByType = allViolations
                    .GroupBy(v => v.ViolationType)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Violations by severity
                dashboard.ViolationsBySeverity = allViolations
                    .GroupBy(v => v.Severity)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Top violations
                dashboard.TopViolations = allViolations
                    .GroupBy(v => new { v.ViolationType, Severity = v.Severity })
                    .Select(g => new ViolationSummary
                    {
                        ViolationType = g.Key.ViolationType,
                        OccurrenceCount = g.Count(),
                        Severity = g.Key.Severity,
                        LastOccurrence = _complianceEvents
                            .Where(e => e.Violations.Any(v => v.ViolationType == g.Key.ViolationType))
                            .Max(e => e.Timestamp),
                        OperationName = _complianceEvents
                            .Where(e => e.Violations.Any(v => v.ViolationType == g.Key.ViolationType))
                            .Select(e => e.OperationName)
                            .FirstOrDefault() ?? "Unknown"
                    })
                    .OrderByDescending(v => v.OccurrenceCount)
                    .Take(10)
                    .ToList();

                // Compliance trend
                dashboard.ComplianceTrend = GenerateComplianceTrend(_complianceEvents);
            }

            _logger.LogDebug(
                "✅ Compliance Dashboard: {ComplianceRate:P} compliance rate, {ViolationCount} violations",
                dashboard.ComplianceRate,
                dashboard.ViolationsByType.Sum(kvp => kvp.Value));

            return await Task.FromResult(dashboard);
        }

        public async Task<QualityTrendMetrics> GetQualityTrendMetricsAsync(TimeSpan period)
        {
            var metrics = new QualityTrendMetrics { Period = period };
            var cutoffTime = DateTime.UtcNow - period;

            lock (_lockObject)
            {
                var recentEvents = _fieldEvents.Where(e => e.Timestamp >= cutoffTime).ToList();
                if (!recentEvents.Any())
                {
                    return metrics;
                }

                var previousEvents = _fieldEvents.Where(e => e.Timestamp < cutoffTime).ToList();

                metrics.CurrentQualityScore = recentEvents.Average(e => e.QualityScore);
                metrics.PreviousPeriodScore = previousEvents.Any()
                    ? previousEvents.Average(e => e.QualityScore)
                    : metrics.CurrentQualityScore;
                
                metrics.PercentageChange = metrics.PreviousPeriodScore > 0
                    ? ((metrics.CurrentQualityScore - metrics.PreviousPeriodScore) / metrics.PreviousPeriodScore) * 100
                    : 0.0;

                // Time-series trends
                metrics.QualityScoreTrend = GenerateQualityTrend(recentEvents);
                metrics.CompletenessScoreTrend = GenerateCompletenessTrend(recentEvents);
                metrics.ConfidenceScoreTrend = GenerateConfidenceTrend(recentEvents);

                // Quality by category (using field name as category)
                metrics.QualityByCategory = recentEvents
                    .GroupBy(e => e.FieldType)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .ToDictionary(g => g.Key, g => g.Average(e => e.QualityScore));

                // Top improving fields
                var fieldComparisons = AnalyzeFieldTrends(recentEvents, previousEvents);
                metrics.TopImprovingFields = fieldComparisons
                    .OrderByDescending(f => f.PercentageChange)
                    .Take(5)
                    .ToList();

                // Fields needing attention (declining quality)
                metrics.FieldsNeedingAttention = fieldComparisons
                    .Where(f => f.PercentageChange < -5.0) // More than 5% decline
                    .OrderBy(f => f.PercentageChange)
                    .Take(5)
                    .ToList();
            }

            _logger.LogDebug(
                "📈 Quality Trends (last {Period}): Current={Current:F3}, Change={Change:+0.0;-0.0}%",
                period,
                metrics.CurrentQualityScore,
                metrics.PercentageChange);

            return await Task.FromResult(metrics);
        }

        public async Task TrackLLMResponseAsync(LLMResponseTelemetry telemetry)
        {
            if (telemetry == null) throw new ArgumentNullException(nameof(telemetry));

            lock (_lockObject)
            {
                _llmResponses.Add(telemetry);
                _totalEventsTracked++;
                _lastEventTime = DateTime.UtcNow;

                // Prune old data (keep last 10,000 responses)
                if (_llmResponses.Count > 10000)
                {
                    var toRemove = _llmResponses.Count - 10000;
                    _llmResponses.RemoveRange(0, toRemove);
                }
            }

            _logger.LogDebug(
                "📝 LLM response tracked: Confidence={Confidence:F3}, Accuracy={Accuracy:F3}, Fields={Fields}/{Expected}",
                telemetry.LLMConfidence,
                telemetry.ActualQualityScore,
                telemetry.ParsedFieldCount,
                telemetry.ExpectedFieldCount);

            // Notify observers
            if (telemetry.LLMConfidence > 0 && _observers.Any())
            {
                var calibration = await GetConfidenceCalibrationMetricsAsync();
                await NotifyObserversAsync(observer => observer.OnConfidenceCalibrationUpdateAsync(calibration));
            }
        }

        public async Task TrackFieldCompletionAsync(FieldCompletionEvent completionEvent)
        {
            if (completionEvent == null) throw new ArgumentNullException(nameof(completionEvent));

            lock (_lockObject)
            {
                _fieldEvents.Add(completionEvent);
                _totalEventsTracked++;
                _lastEventTime = DateTime.UtcNow;

                // Prune old data
                if (_fieldEvents.Count > 50000)
                {
                    var toRemove = _fieldEvents.Count - 50000;
                    _fieldEvents.RemoveRange(0, toRemove);
                }
            }

            _logger.LogDebug(
                "📋 Field completion tracked: {FieldName} - {Status}, Quality={Quality:F3}",
                completionEvent.FieldName,
                completionEvent.CompletionStatus,
                completionEvent.QualityScore);

            // Notify observers
            await NotifyObserversAsync(observer => observer.OnFieldCompletionAsync(completionEvent));
        }

        public async Task TrackComplianceEventAsync(ComplianceEventTelemetry complianceEvent)
        {
            if (complianceEvent == null) throw new ArgumentNullException(nameof(complianceEvent));

            lock (_lockObject)
            {
                _complianceEvents.Add(complianceEvent);
                _totalEventsTracked++;
                _lastEventTime = DateTime.UtcNow;

                // Prune old data
                if (_complianceEvents.Count > 10000)
                {
                    var toRemove = _complianceEvents.Count - 10000;
                    _complianceEvents.RemoveRange(0, toRemove);
                }
            }

            _logger.LogDebug(
                "⚖️ Compliance event tracked: {OperationName} - {Result}, Score={Score:F3}, Violations={Count}",
                complianceEvent.OperationName,
                complianceEvent.Passed ? "PASS" : "FAIL",
                complianceEvent.ComplianceScore,
                complianceEvent.Violations.Count);

            // Notify observers
            if (complianceEvent.Violations.Any())
            {
                await NotifyObserversAsync(observer => observer.OnComplianceViolationAsync(complianceEvent));
            }
        }

        public async Task TrackABTestAsync(ABTestResult testResult)
        {
            if (testResult == null) throw new ArgumentNullException(nameof(testResult));

            lock (_lockObject)
            {
                _abTestResults.Add(testResult);
                _totalEventsTracked++;
                _lastEventTime = DateTime.UtcNow;

                // Prune old data
                if (_abTestResults.Count > 1000)
                {
                    var toRemove = _abTestResults.Count - 1000;
                    _abTestResults.RemoveRange(0, toRemove);
                }
            }

            _logger.LogInformation(
                "🧪 A/B test tracked: {TestName} - Winner: {Winner}, Quality: {QualityImprovement:+0.0;-0.0}%",
                testResult.TestName,
                testResult.Comparison.Winner,
                testResult.Comparison.QualityImprovement * 100);

            // Notify observers
            await NotifyObserversAsync(observer => observer.OnABTestCompletedAsync(testResult));
        }

        public void RegisterTelemetryObserver(ITelemetryObserver observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            lock (_lockObject)
            {
                _observers.Add(observer);
            }

            _logger.LogDebug("👀 Telemetry observer registered: {ObserverType}", observer.GetType().Name);
        }

        public async Task ExportDashboardDataAsync(string outputPath, DashboardExportFormat format)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required", nameof(outputPath));

            _logger.LogInformation("💾 Exporting dashboard data to: {Path} (Format: {Format})", outputPath, format);

            var snapshot = await GetDashboardSnapshotAsync();

            switch (format)
            {
                case DashboardExportFormat.Json:
                    await ExportAsJsonAsync(snapshot, outputPath);
                    break;
                case DashboardExportFormat.Csv:
                    await ExportAsCsvAsync(snapshot, outputPath);
                    break;
                case DashboardExportFormat.Html:
                    await ExportAsHtmlAsync(snapshot, outputPath);
                    break;
                default:
                    throw new NotSupportedException($"Export format {format} not supported");
            }

            _logger.LogInformation("✅ Dashboard data exported successfully");
        }

        public TelemetryHealthStatus GetHealthStatus()
        {
            var status = new TelemetryHealthStatus();

            lock (_lockObject)
            {
                status.TotalEventsTracked = _totalEventsTracked;
                status.LastUpdateTime = _lastEventTime;
                status.ActiveObservers = _observers.Count;

                // Calculate events per minute
                var elapsedMinutes = _uptimeStopwatch.Elapsed.TotalMinutes;
                status.EventsPerMinute = elapsedMinutes > 0
                    ? (long)(_totalEventsTracked / elapsedMinutes)
                    : 0;

                // Health checks
                if (_lastEventTime < DateTime.UtcNow.AddMinutes(-5))
                {
                    status.HealthWarnings.Add("No events received in the last 5 minutes");
                }

                if (_totalEventsTracked == 0)
                {
                    status.HealthWarnings.Add("No events tracked yet");
                }

                if (_observers.Count == 0)
                {
                    status.HealthWarnings.Add("No observers registered for real-time monitoring");
                }

                status.IsHealthy = !status.HealthErrors.Any();
            }

            return status;
        }

        // Private helper methods

        private string DetermineIntegrationMode()
        {
            var modes = new List<string>();
            if (_qualityService != null) modes.Add("QualityService");
            if (_abTestingFramework != null) modes.Add("ABTesting");
            if (_complianceWrapper != null) modes.Add("Compliance");
            return modes.Any() ? string.Join("+", modes) : "Standalone";
        }

        private long CalculateTotalOperations()
        {
            lock (_lockObject)
            {
                return _fieldEvents.Count + _complianceEvents.Count + _abTestResults.Count;
            }
        }

        private long CalculateSuccessfulOperations()
        {
            lock (_lockObject)
            {
                var successfulFields = _fieldEvents.Count(e => 
                    e.CompletionStatus == FieldCompletionStatus.Complete || 
                    e.CompletionStatus == FieldCompletionStatus.Partial);
                var successfulCompliance = _complianceEvents.Count(e => e.Passed);
                var successfulTests = _abTestResults.Count; // All tracked tests considered successful
                
                return successfulFields + successfulCompliance + successfulTests;
            }
        }

        private double CalculateAverageQualityScore()
        {
            lock (_lockObject)
            {
                if (!_fieldEvents.Any()) return 0.0;
                return _fieldEvents.Average(e => e.QualityScore);
            }
        }

        private PerformanceDashboard GetPerformanceDashboard()
        {
            var dashboard = new PerformanceDashboard();

            lock (_lockObject)
            {
                var responseTimes = _llmResponses.Select(r => (double)r.ResponseTimeMs).ToList();
                var complianceTimes = _complianceEvents.Select(e => (double)e.ExecutionTimeMs).ToList();
                var allTimes = responseTimes.Concat(complianceTimes).OrderBy(t => t).ToList();

                if (allTimes.Any())
                {
                    dashboard.AverageResponseTimeMs = allTimes.Average();
                    dashboard.MedianResponseTimeMs = CalculatePercentile(allTimes, 50);
                    dashboard.P95ResponseTimeMs = CalculatePercentile(allTimes, 95);
                    dashboard.P99ResponseTimeMs = CalculatePercentile(allTimes, 99);
                }

                // Average time by operation
                dashboard.AvgTimeByOperation = _llmResponses
                    .GroupBy(r => r.OperationName)
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .ToDictionary(g => g.Key, g => g.Average(r => (double)r.ResponseTimeMs));

                // Throughput
                var elapsedMinutes = _uptimeStopwatch.Elapsed.TotalMinutes;
                dashboard.OperationsPerMinute = elapsedMinutes > 0
                    ? CalculateTotalOperations() / elapsedMinutes
                    : 0.0;

                dashboard.ResponseTimeTrend = GenerateResponseTimeTrend(allTimes);
                dashboard.ThroughputTrend = GenerateThroughputTrend(_fieldEvents);
            }

            return dashboard;
        }

        private double CalculatePercentile(List<double> sortedValues, int percentile)
        {
            if (!sortedValues.Any()) return 0.0;
            
            var index = (int)Math.Ceiling(sortedValues.Count * (percentile / 100.0)) - 1;
            index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
            return sortedValues[index];
        }

        // Time-series generation methods

        private List<TimeSeriesDataPoint> GenerateCompletionTrend(List<FieldCompletionEvent> events)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var recentEvents = events.Where(e => e.Timestamp >= cutoff).ToList();

            for (int hour = 0; hour < 24; hour++)
            {
                var hourStart = DateTime.UtcNow.AddHours(-24 + hour);
                var hourEnd = hourStart.AddHours(1);
                var hourEvents = recentEvents.Where(e => e.Timestamp >= hourStart && e.Timestamp < hourEnd).ToList();

                if (hourEvents.Any())
                {
                    var completionRate = (double)hourEvents.Count(e => e.CompletionStatus == FieldCompletionStatus.Complete) / hourEvents.Count;
                    trend.Add(new TimeSeriesDataPoint
                    {
                        Timestamp = hourStart,
                        Value = completionRate,
                        Label = $"Hour {hour}"
                    });
                }
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateCalibrationTrend(List<LLMResponseTelemetry> responses)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var cutoff = DateTime.UtcNow.AddDays(-7);
            var recentResponses = responses.Where(r => r.Timestamp >= cutoff).ToList();

            for (int day = 0; day < 7; day++)
            {
                var dayStart = DateTime.UtcNow.AddDays(-7 + day).Date;
                var dayEnd = dayStart.AddDays(1);
                var dayResponses = recentResponses.Where(r => r.Timestamp >= dayStart && r.Timestamp < dayEnd).ToList();

                if (dayResponses.Any())
                {
                    var avgGap = dayResponses.Average(r => Math.Abs(r.LLMConfidence - r.ActualQualityScore));
                    var calibrationScore = 1.0 - avgGap;
                    trend.Add(new TimeSeriesDataPoint
                    {
                        Timestamp = dayStart,
                        Value = calibrationScore,
                        Label = dayStart.ToString("MM/dd")
                    });
                }
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateWinRateTrend(List<ABTestResult> results)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var groupedByDay = results
                .GroupBy(r => r.ExecutedAt.Date)
                .OrderBy(g => g.Key);

            foreach (var group in groupedByDay)
            {
                var winRate = (double)group.Count(r => r.Comparison.Winner == ApproachType.Template) / group.Count();
                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = group.Key,
                    Value = winRate,
                    Label = group.Key.ToString("MM/dd")
                });
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateQualityImprovementTrend(List<ABTestResult> results)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var groupedByDay = results
                .GroupBy(r => r.ExecutedAt.Date)
                .OrderBy(g => g.Key);

            foreach (var group in groupedByDay)
            {
                var avgImprovement = group.Average(r => r.Comparison.QualityImprovement);
                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = group.Key,
                    Value = avgImprovement,
                    Label = group.Key.ToString("MM/dd")
                });
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateComplianceTrend(List<ComplianceEventTelemetry> events)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var groupedByDay = events
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key);

            foreach (var group in groupedByDay)
            {
                var complianceRate = (double)group.Count(e => e.Passed) / group.Count();
                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = group.Key,
                    Value = complianceRate,
                    Label = group.Key.ToString("MM/dd")
                });
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateQualityTrend(List<FieldCompletionEvent> events)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var groupedByDay = events
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key);

            foreach (var group in groupedByDay)
            {
                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = group.Key,
                    Value = group.Average(e => e.QualityScore),
                    Label = group.Key.ToString("MM/dd")
                });
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateCompletenessTrend(List<FieldCompletionEvent> events)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var groupedByDay = events
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key);

            foreach (var group in groupedByDay)
            {
                var completionRate = (double)group.Count(e => e.CompletionStatus == FieldCompletionStatus.Complete) / group.Count();
                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = group.Key,
                    Value = completionRate,
                    Label = group.Key.ToString("MM/dd")
                });
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateConfidenceTrend(List<FieldCompletionEvent> events)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var groupedByDay = events
                .GroupBy(e => e.Timestamp.Date)
                .OrderBy(g => g.Key);

            foreach (var group in groupedByDay)
            {
                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = group.Key,
                    Value = group.Average(e => e.Confidence),
                    Label = group.Key.ToString("MM/dd")
                });
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateResponseTimeTrend(List<double> responseTimes)
        {
            // Simple trend: average response time over last 24 hours
            var trend = new List<TimeSeriesDataPoint>();
            var now = DateTime.UtcNow;

            for (int hour = 0; hour < 24; hour++)
            {
                var hourTime = now.AddHours(-24 + hour);
                if (responseTimes.Any())
                {
                    trend.Add(new TimeSeriesDataPoint
                    {
                        Timestamp = hourTime,
                        Value = responseTimes.Average(),
                        Label = $"Hour {hour}"
                    });
                }
            }

            return trend;
        }

        private List<TimeSeriesDataPoint> GenerateThroughputTrend(List<FieldCompletionEvent> events)
        {
            var trend = new List<TimeSeriesDataPoint>();
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var recentEvents = events.Where(e => e.Timestamp >= cutoff).ToList();

            for (int hour = 0; hour < 24; hour++)
            {
                var hourStart = DateTime.UtcNow.AddHours(-24 + hour);
                var hourEnd = hourStart.AddHours(1);
                var hourCount = recentEvents.Count(e => e.Timestamp >= hourStart && e.Timestamp < hourEnd);

                trend.Add(new TimeSeriesDataPoint
                {
                    Timestamp = hourStart,
                    Value = hourCount,
                    Label = $"Hour {hour}"
                });
            }

            return trend;
        }

        private List<FieldTrendSummary> AnalyzeFieldTrends(
            List<FieldCompletionEvent> recentEvents,
            List<FieldCompletionEvent> previousEvents)
        {
            var trends = new List<FieldTrendSummary>();

            var recentByField = recentEvents.GroupBy(e => e.FieldName);
            var previousByField = previousEvents.GroupBy(e => e.FieldName).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var recentGroup in recentByField)
            {
                var fieldName = recentGroup.Key;
                var currentScore = recentGroup.Average(e => e.QualityScore);
                var previousScore = previousByField.ContainsKey(fieldName)
                    ? previousByField[fieldName].Average(e => e.QualityScore)
                    : currentScore;

                var trend = new FieldTrendSummary
                {
                    FieldName = fieldName,
                    CurrentScore = currentScore,
                    PreviousScore = previousScore,
                    SampleCount = recentGroup.Count()
                };
                trend.PercentageChange = previousScore > 0
                    ? ((currentScore - previousScore) / previousScore) * 100
                    : 0.0;

                trends.Add(trend);
            }

            return trends;
        }

        // Export methods

        private async Task ExportAsJsonAsync(DashboardSnapshot snapshot, string outputPath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(snapshot, options);
            await File.WriteAllTextAsync(outputPath, json);
        }

        private async Task ExportAsCsvAsync(DashboardSnapshot snapshot, string outputPath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Metric,Value");
            csv.AppendLine($"Timestamp,{snapshot.Timestamp:O}");
            csv.AppendLine($"Total Operations,{snapshot.TotalOperations}");
            csv.AppendLine($"Success Rate,{snapshot.SuccessRate:P}");
            csv.AppendLine($"Average Quality Score,{snapshot.AverageQualityScore:F4}");
            csv.AppendLine($"Field Completion Rate,{snapshot.AverageFieldCompletionRate:P}");
            csv.AppendLine($"Calibration Score,{snapshot.ConfidenceCalibration.CalibrationScore:F4}");
            csv.AppendLine($"Compliance Rate,{snapshot.ComplianceSummary.ComplianceRate:P}");
            csv.AppendLine($"Template Win Rate,{snapshot.ABTestSummary.TemplateWinRate:P}");

            await File.WriteAllTextAsync(outputPath, csv.ToString());
        }

        private async Task ExportAsHtmlAsync(DashboardSnapshot snapshot, string outputPath)
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html><head><title>Telemetry Dashboard</title>");
            html.AppendLine("<style>body{font-family:Arial,sans-serif;margin:20px;}table{border-collapse:collapse;width:100%;}th,td{border:1px solid #ddd;padding:8px;text-align:left;}th{background-color:#4CAF50;color:white;}</style>");
            html.AppendLine("</head><body>");
            html.AppendLine("<h1>Template Form Architecture - Telemetry Dashboard</h1>");
            html.AppendLine($"<p>Generated: {snapshot.Timestamp:F}</p>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>Metric</th><th>Value</th></tr>");
            html.AppendLine($"<tr><td>Total Operations</td><td>{snapshot.TotalOperations}</td></tr>");
            html.AppendLine($"<tr><td>Success Rate</td><td>{snapshot.SuccessRate:P}</td></tr>");
            html.AppendLine($"<tr><td>Average Quality Score</td><td>{snapshot.AverageQualityScore:F4}</td></tr>");
            html.AppendLine($"<tr><td>Field Completion Rate</td><td>{snapshot.AverageFieldCompletionRate:P}</td></tr>");
            html.AppendLine($"<tr><td>Calibration Score</td><td>{snapshot.ConfidenceCalibration.CalibrationScore:F4}</td></tr>");
            html.AppendLine($"<tr><td>Compliance Rate</td><td>{snapshot.ComplianceSummary.ComplianceRate:P}</td></tr>");
            html.AppendLine($"<tr><td>Template Win Rate</td><td>{snapshot.ABTestSummary.TemplateWinRate:P}</td></tr>");
            html.AppendLine("</table>");
            html.AppendLine("</body></html>");

            await File.WriteAllTextAsync(outputPath, html.ToString());
        }

        // Observer notification helper

        private async Task NotifyObserversAsync(Func<ITelemetryObserver, Task> notifyAction)
        {
            List<ITelemetryObserver> observersSnapshot;
            lock (_lockObject)
            {
                observersSnapshot = new List<ITelemetryObserver>(_observers);
            }

            foreach (var observer in observersSnapshot)
            {
                try
                {
                    await notifyAction(observer);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying telemetry observer: {ObserverType}", observer.GetType().Name);
                }
            }
        }
    }
}
