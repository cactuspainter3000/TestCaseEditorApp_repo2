using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Telemetry and Dashboard Integration for Template Form Architecture
    /// Provides enterprise monitoring, field completion rates, and confidence calibration metrics
    /// 
    /// PURPOSE: Real-time visibility into Template Form Architecture performance and quality
    /// INTEGRATION: Aggregates metrics from Tasks 6.3, 6.5, 6.8, 6.9
    /// MONITORING: Field completion, confidence calibration, A/B test results, compliance violations
    /// 
    /// Task 6.10: Add Telemetry & Dashboard Integration
    /// </summary>
    public interface ITelemetryDashboardService
    {
        /// <summary>
        /// Get current dashboard snapshot with all metrics
        /// </summary>
        Task<DashboardSnapshot> GetDashboardSnapshotAsync();

        /// <summary>
        /// Get field completion rates across all templates
        /// </summary>
        Task<FieldCompletionMetrics> GetFieldCompletionMetricsAsync();

        /// <summary>
        /// Get confidence calibration analysis (LLM confidence vs actual accuracy)
        /// </summary>
        Task<ConfidenceCalibrationMetrics> GetConfidenceCalibrationMetricsAsync();

        /// <summary>
        /// Get A/B test summary dashboard
        /// </summary>
        Task<ABTestDashboard> GetABTestDashboardAsync();

        /// <summary>
        /// Get compliance violation trends and statistics
        /// </summary>
        Task<ComplianceDashboard> GetComplianceDashboardAsync();

        /// <summary>
        /// Get quality score trends over time
        /// </summary>
        Task<QualityTrendMetrics> GetQualityTrendMetricsAsync(TimeSpan period);

        /// <summary>
        /// Track LLM response with confidence score for calibration analysis
        /// </summary>
        Task TrackLLMResponseAsync(LLMResponseTelemetry telemetry);

        /// <summary>
        /// Track field completion event
        /// </summary>
        Task TrackFieldCompletionAsync(FieldCompletionEvent completionEvent);

        /// <summary>
        /// Track compliance event
        /// </summary>
        Task TrackComplianceEventAsync(ComplianceEventTelemetry complianceEvent);

        /// <summary>
        /// Track A/B test execution
        /// </summary>
        Task TrackABTestAsync(ABTestResult testResult);

        /// <summary>
        /// Register a telemetry observer for real-time monitoring
        /// </summary>
        void RegisterTelemetryObserver(ITelemetryObserver observer);

        /// <summary>
        /// Export dashboard data for external monitoring systems
        /// </summary>
        Task ExportDashboardDataAsync(string outputPath, DashboardExportFormat format);

        /// <summary>
        /// Get telemetry health status
        /// </summary>
        TelemetryHealthStatus GetHealthStatus();
    }

    /// <summary>
    /// Complete dashboard snapshot with all metrics
    /// </summary>
    public class DashboardSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string SnapshotId { get; set; } = Guid.NewGuid().ToString();
        
        // Aggregate statistics
        public long TotalOperations { get; set; }
        public long SuccessfulOperations { get; set; }
        public double SuccessRate { get; set; }
        public double AverageQualityScore { get; set; }
        public double AverageFieldCompletionRate { get; set; }
        
        // Sub-dashboards
        public FieldCompletionMetrics FieldCompletion { get; set; } = new();
        public ConfidenceCalibrationMetrics ConfidenceCalibration { get; set; } = new();
        public ABTestDashboard ABTestSummary { get; set; } = new();
        public ComplianceDashboard ComplianceSummary { get; set; } = new();
        public QualityTrendMetrics QualityTrends { get; set; } = new();
        
        // Performance metrics
        public PerformanceDashboard Performance { get; set; } = new();
        
        // System health
        public TelemetryHealthStatus HealthStatus { get; set; } = new();
    }

    /// <summary>
    /// Field completion rate metrics
    /// </summary>
    public class FieldCompletionMetrics
    {
        public double OverallCompletionRate { get; set; }
        public int TotalFieldsTracked { get; set; }
        public int CompletedFields { get; set; }
        public int PartiallyCompletedFields { get; set; }
        public int EmptyFields { get; set; }
        
        // Per-field breakdown
        public Dictionary<string, FieldStatistics> FieldBreakdown { get; set; } = new();
        
        // Completion rates by template
        public Dictionary<string, double> CompletionByTemplate { get; set; } = new();
        
        // Completion rates by schema
        public Dictionary<string, double> CompletionBySchema { get; set; } = new();
        
        // Trend over time
        public List<TimeSeriesDataPoint> CompletionTrend { get; set; } = new();
    }

    /// <summary>
    /// Individual field statistics
    /// </summary>
    public class FieldStatistics
    {
        public string FieldName { get; set; } = "Unknown";
        public int TotalOccurrences { get; set; }
        public int CompletedCount { get; set; }
        public int PartialCount { get; set; }
        public int EmptyCount { get; set; }
        public double CompletionRate { get; set; }
        public double AverageQualityScore { get; set; }
        public double AverageConfidence { get; set; }
    }

    /// <summary>
    /// Confidence calibration metrics (LLM confidence vs actual accuracy)
    /// </summary>
    public class ConfidenceCalibrationMetrics
    {
        /// <summary>
        /// Overall calibration score (0-1, 1 = perfectly calibrated)
        /// </summary>
        public double CalibrationScore { get; set; }
        
        /// <summary>
        /// Expected calibration error (ECE)
        /// </summary>
        public double ExpectedCalibrationError { get; set; }
        
        /// <summary>
        /// Calibration by confidence bins
        /// </summary>
        public List<CalibrationBin> CalibrationBins { get; set; } = new();
        
        /// <summary>
        /// Overconfidence rate (LLM confidence > actual accuracy)
        /// </summary>
        public double OverconfidenceRate { get; set; }
        
        /// <summary>
        /// Underconfidence rate (LLM confidence < actual accuracy)
        /// </summary>
        public double UnderconfidenceRate { get; set; }
        
        /// <summary>
        /// Total responses tracked
        /// </summary>
        public int TotalResponses { get; set; }
        
        /// <summary>
        /// Calibration trend over time
        /// </summary>
        public List<TimeSeriesDataPoint> CalibrationTrend { get; set; } = new();
    }

    /// <summary>
    /// Calibration bin for confidence analysis
    /// </summary>
    public class CalibrationBin
    {
        public double MinConfidence { get; set; }
        public double MaxConfidence { get; set; }
        public double MeanConfidence { get; set; }
        public double MeanAccuracy { get; set; }
        public int SampleCount { get; set; }
        public double CalibrationGap { get; set; } // |MeanConfidence - MeanAccuracy|
    }

    /// <summary>
    /// A/B test dashboard summary
    /// </summary>
    public class ABTestDashboard
    {
        public int TotalTests { get; set; }
        public int TemplateWins { get; set; }
        public int LegacyWins { get; set; }
        public int Ties { get; set; }
        public double TemplateWinRate { get; set; }
        
        // Performance improvements
        public double AverageQualityImprovement { get; set; }
        public double AveragePerformanceImprovement { get; set; }
        public double AverageCompletenessImprovement { get; set; }
        
        // Recent tests
        public List<ABTestSummary> RecentTests { get; set; } = new();
        
        // Trend over time
        public List<TimeSeriesDataPoint> WinRateTrend { get; set; } = new();
        public List<TimeSeriesDataPoint> QualityImprovementTrend { get; set; } = new();
    }

    /// <summary>
    /// Summary of a single A/B test
    /// </summary>
    public class ABTestSummary
    {
        public string TestId { get; set; } = string.Empty;
        public string TestName { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; }
        public ApproachType Winner { get; set; }
        public double QualityImprovement { get; set; }
        public double PerformanceImprovement { get; set; }
        public bool StatisticallySignificant { get; set; }
    }

    /// <summary>
    /// Compliance violation dashboard
    /// </summary>
    public class ComplianceDashboard
    {
        public int TotalComplianceChecks { get; set; }
        public int PassedChecks { get; set; }
        public int FailedChecks { get; set; }
        public double ComplianceRate { get; set; }
        public double AverageComplianceScore { get; set; }
        
        // Violations by type
        public Dictionary<string, int> ViolationsByType { get; set; } = new();
        
        // Violations by severity
        public Dictionary<ValidationSeverity, int> ViolationsBySeverity { get; set; } = new();
        
        // Top violating operations
        public List<ViolationSummary> TopViolations { get; set; } = new();
        
        // Compliance trend over time
        public List<TimeSeriesDataPoint> ComplianceTrend { get; set; } = new();
    }

    /// <summary>
    /// Violation summary for reporting
    /// </summary>
    public class ViolationSummary
    {
        public string ViolationType { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public int OccurrenceCount { get; set; }
        public ValidationSeverity Severity { get; set; }
        public DateTime LastOccurrence { get; set; }
    }

    /// <summary>
    /// Quality score trends over time
    /// </summary>
    public class QualityTrendMetrics
    {
        public TimeSpan Period { get; set; }
        public double CurrentQualityScore { get; set; }
        public double PreviousPeriodScore { get; set; }
        public double PercentageChange { get; set; }
        
        // Time-series data
        public List<TimeSeriesDataPoint> QualityScoreTrend { get; set; } = new();
        public List<TimeSeriesDataPoint> CompletenessScoreTrend { get; set; } = new();
        public List<TimeSeriesDataPoint> ConfidenceScoreTrend { get; set; } = new();
        
        // Quality breakdown by category
        public Dictionary<string, double> QualityByCategory { get; set; } = new();
        
        // Top improving fields
        public List<FieldTrendSummary> TopImprovingFields { get; set; } = new();
        
        // Fields needing attention
        public List<FieldTrendSummary> FieldsNeedingAttention { get; set; } = new();
    }

    /// <summary>
    /// Field trend summary for quality analysis
    /// </summary>
    public class FieldTrendSummary
    {
        public string FieldName { get; set; } = string.Empty;
        public double CurrentScore { get; set; }
        public double PreviousScore { get; set; }
        public double PercentageChange { get; set; }
        public int SampleCount { get; set; }
    }

    /// <summary>
    /// Performance metrics dashboard
    /// </summary>
    public class PerformanceDashboard
    {
        public double AverageResponseTimeMs { get; set; }
        public double MedianResponseTimeMs { get; set; }
        public double P95ResponseTimeMs { get; set; }
        public double P99ResponseTimeMs { get; set; }
        
        // Breakdown by operation type
        public Dictionary<string, double> AvgTimeByOperation { get; set; } = new();
        
        // Performance trend
        public List<TimeSeriesDataPoint> ResponseTimeTrend { get; set; } = new();
        
        // Throughput
        public double OperationsPerMinute { get; set; }
        public List<TimeSeriesDataPoint> ThroughputTrend { get; set; } = new();
    }

    /// <summary>
    /// Time-series data point for trending
    /// </summary>
    public class TimeSeriesDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public string Label { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// LLM response telemetry for confidence calibration
    /// </summary>
    public class LLMResponseTelemetry
    {
        public string ResponseId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string OperationName { get; set; } = string.Empty;
        
        // Confidence tracking
        public double LLMConfidence { get; set; } // 0-1 from LLM
        public double ActualQualityScore { get; set; } // 0-1 measured quality
        public bool WasCorrect { get; set; } // Binary correctness
        
        // Response details
        public int ResponseLength { get; set; }
        public int ParsedFieldCount { get; set; }
        public int ExpectedFieldCount { get; set; }
        public bool PassedValidation { get; set; }
        
        // Performance
        public long ResponseTimeMs { get; set; }
        
        // Context
        public string TemplateId { get; set; } = string.Empty;
        public string SchemaId { get; set; } = string.Empty;
        public Dictionary<string, object> CustomData { get; set; } = new();
    }

    /// <summary>
    /// Field completion event telemetry
    /// </summary>
    public class FieldCompletionEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public string FieldName { get; set; } = string.Empty;
        public string TemplateId { get; set; } = string.Empty;
        public string SchemaId { get; set; } = string.Empty;
        
        // Completion status
        public FieldCompletionStatus CompletionStatus { get; set; }
        public double QualityScore { get; set; }
        public double Confidence { get; set; }
        
        // Field details
        public int? FieldLength { get; set; }
        public string FieldType { get; set; } = string.Empty;
        public bool WasRequired { get; set; }
        
        // Context
        public string OperationName { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Compliance event telemetry
    /// </summary>
    public class ComplianceEventTelemetry
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public string OperationName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public double ComplianceScore { get; set; }
        
        // Violations
        public List<ComplianceViolation> Violations { get; set; } = new();
        
        // Performance
        public long ExecutionTimeMs { get; set; }
        
        // Context
        public string SessionId { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Telemetry health status
    /// </summary>
    public class TelemetryHealthStatus
    {
        public bool IsHealthy { get; set; } = true;
        public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
        public long TotalEventsTracked { get; set; }
        public long EventsPerMinute { get; set; }
        public int ActiveObservers { get; set; }
        public List<string> HealthWarnings { get; set; } = new();
        public List<string> HealthErrors { get; set; } = new();
    }

    /// <summary>
    /// Observer interface for real-time telemetry monitoring
    /// </summary>
    public interface ITelemetryObserver
    {
        Task OnDashboardUpdateAsync(DashboardSnapshot snapshot);
        Task OnFieldCompletionAsync(FieldCompletionEvent completionEvent);
        Task OnConfidenceCalibrationUpdateAsync(ConfidenceCalibrationMetrics calibration);
        Task OnABTestCompletedAsync(ABTestResult testResult);
        Task OnComplianceViolationAsync(ComplianceEventTelemetry complianceEvent);
    }

    /// <summary>
    /// Field completion status enum
    /// </summary>
    public enum FieldCompletionStatus
    {
        Empty,
        Partial,
        Complete,
        Invalid
    }

    /// <summary>
    /// Dashboard export format
    /// </summary>
    public enum DashboardExportFormat
    {
        Json,
        Csv,
        Excel,
        Html
    }
}
