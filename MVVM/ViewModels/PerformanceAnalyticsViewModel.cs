using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the Performance Analytics Dashboard - demonstrates Phase 7 advanced monitoring capabilities.
    /// Provides real-time performance metrics, analytics, and optimization recommendations.
    /// </summary>
    public partial class PerformanceAnalyticsViewModel : ObservableObject, IDisposable
    {
        private readonly PerformanceMonitoringService _performanceMonitor;
        private readonly ILogger<PerformanceAnalyticsViewModel> _logger;
        private readonly System.Timers.Timer _refreshTimer;
        private bool _isDisposed = false;
        
        [ObservableProperty] private bool isMonitoringActive = true;
        [ObservableProperty] private string overallSystemHealth = "Good";
        [ObservableProperty] private int totalOperationsToday;
        [ObservableProperty] private double averageResponseTime;
        [ObservableProperty] private double successRate = 100.0;
        [ObservableProperty] private string? selectedTimeframe = "Last Hour";
        
        public List<string> TimeframeOptions { get; } = new()
        {
            "Last Hour", "Last 6 Hours", "Last 24 Hours", "Last Week"
        };
        
        [ObservableProperty] private List<PerformanceMetricDisplay> metricDisplays = new();
        [ObservableProperty] private List<OptimizationRecommendation> recommendations = new();
        
        public PerformanceAnalyticsViewModel(PerformanceMonitoringService performanceMonitor, ILogger<PerformanceAnalyticsViewModel> logger)
        {
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Set up auto-refresh
            _refreshTimer = new System.Timers.Timer(TimeSpan.FromSeconds(5).TotalMilliseconds);
            _refreshTimer.Elapsed += OnRefreshTimerElapsed;
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();
            
            // Initial load
            _ = RefreshMetricsAsync();
        }
        
        [RelayCommand]
        private async Task RefreshMetricsAsync()
        {
            try
            {
                var metrics = _performanceMonitor.GetAllMetrics();
                
                // Update overview metrics
                TotalOperationsToday = metrics.Sum(m => m.TotalExecutions);
                AverageResponseTime = metrics.Any() ? 
                    metrics.Average(m => m.AverageDuration.TotalMilliseconds) : 0;
                SuccessRate = metrics.Any() ? 
                    (double)metrics.Sum(m => m.SuccessfulExecutions) / metrics.Sum(m => m.TotalExecutions) * 100 : 100;
                    
                // Update system health
                OverallSystemHealth = CalculateSystemHealth(metrics);
                
                // Update metric displays
                MetricDisplays = metrics.Select(m => new PerformanceMetricDisplay
                {
                    OperationName = m.OperationKey,
                    TotalExecutions = m.TotalExecutions,
                    SuccessRate = m.TotalExecutions > 0 ? (double)m.SuccessfulExecutions / m.TotalExecutions * 100 : 100,
                    AverageTime = m.AverageDuration.TotalMilliseconds,
                    MinTime = m.MinDuration.TotalMilliseconds,
                    MaxTime = m.MaxDuration.TotalMilliseconds,
                    LastExecuted = m.LastExecution,
                    HealthStatus = CalculateOperationHealth(m)
                }).OrderByDescending(m => m.TotalExecutions).ToList();
                
                // Generate optimization recommendations
                await GenerateRecommendationsAsync(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing performance metrics");
            }
        }
        
        [RelayCommand]
        private async Task ClearMetricsAsync()
        {
            try
            {
                _performanceMonitor.ClearAllMetrics();
                await RefreshMetricsAsync();
                _logger.LogInformation("Performance metrics cleared by user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing performance metrics");
            }
        }
        
        [RelayCommand]
        private void ToggleMonitoring()
        {
            IsMonitoringActive = !IsMonitoringActive;
            // In a full implementation, this would pause/resume monitoring
            _logger.LogInformation("Performance monitoring {Status}", IsMonitoringActive ? "enabled" : "disabled");
        }
        
        [RelayCommand]
        private async Task ExportMetricsAsync()
        {
            try
            {
                var exportData = _performanceMonitor.GenerateSummaryReport();
                // In a full implementation, this would save to file or copy to clipboard
                _logger.LogInformation("Performance metrics export requested - {DataLength} characters", exportData.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting performance metrics");
            }
        }
        
        private string CalculateSystemHealth(IReadOnlyList<PerformanceMetric> metrics)
        {
            if (!metrics.Any()) return "Good";
            
            var avgSuccessRate = (double)metrics.Sum(m => m.SuccessfulExecutions) / Math.Max(1, metrics.Sum(m => m.TotalExecutions)) * 100;
            var avgResponseTime = metrics.Average(m => m.AverageDuration.TotalMilliseconds);
            
            return avgSuccessRate switch
            {
                >= 95 when avgResponseTime < 1000 => "Excellent",
                >= 90 when avgResponseTime < 2000 => "Good", 
                >= 80 when avgResponseTime < 5000 => "Fair",
                _ => "Poor"
            };
        }
        
        private string CalculateOperationHealth(PerformanceMetric metric)
        {
            var successRate = metric.TotalExecutions > 0 ? 
                (double)metric.SuccessfulExecutions / metric.TotalExecutions * 100 : 100;
            var avgTime = metric.AverageDuration.TotalMilliseconds;
            
            return successRate switch
            {
                >= 98 when avgTime < 500 => "Excellent",
                >= 95 when avgTime < 1000 => "Good",
                >= 85 when avgTime < 2000 => "Fair",
                _ => "Poor"
            };
        }
        
        private async Task GenerateRecommendationsAsync(IReadOnlyList<PerformanceMetric> metrics)
        {
            var newRecommendations = new List<OptimizationRecommendation>();
            
            foreach (var metric in metrics)
            {
                var successRate = metric.TotalExecutions > 0 ? 
                    (double)metric.SuccessfulExecutions / metric.TotalExecutions * 100 : 100;
                    
                // Generate recommendations based on performance patterns
                if (successRate < 90)
                {
                    newRecommendations.Add(new OptimizationRecommendation
                    {
                        Priority = "High",
                        Category = "Reliability",
                        Operation = metric.OperationKey,
                        Issue = $"Success rate is {successRate:F1}% (below 90% threshold)",
                        Recommendation = "Review error handling and add retry logic for transient failures",
                        EstimatedImpact = "Could improve success rate by 5-10%"
                    });
                }
                
                if (metric.AverageDuration.TotalMilliseconds > 2000)
                {
                    newRecommendations.Add(new OptimizationRecommendation
                    {
                        Priority = "Medium",
                        Category = "Performance",
                        Operation = metric.OperationKey,
                        Issue = $"Average response time is {metric.AverageDuration.TotalMilliseconds:F0}ms (above 2s threshold)",
                        Recommendation = "Consider implementing caching or optimizing the operation logic",
                        EstimatedImpact = "Could reduce response time by 30-50%"
                    });
                }
                
                if (metric.MaxDuration.TotalMilliseconds > metric.AverageDuration.TotalMilliseconds * 3)
                {
                    newRecommendations.Add(new OptimizationRecommendation
                    {
                        Priority = "Low", 
                        Category = "Consistency",
                        Operation = metric.OperationKey,
                        Issue = $"High response time variance (max: {metric.MaxDuration.TotalMilliseconds:F0}ms, avg: {metric.AverageDuration.TotalMilliseconds:F0}ms)",
                        Recommendation = "Investigate causes of response time spikes and add timeout handling",
                        EstimatedImpact = "Would improve user experience consistency"
                    });
                }
            }
            
            Recommendations = newRecommendations.Take(10).ToList(); // Limit to top 10 recommendations
        }
        
        private async void OnRefreshTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            if (!_isDisposed && IsMonitoringActive)
            {
                await RefreshMetricsAsync();
            }
        }
        
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _refreshTimer?.Dispose();
                _isDisposed = true;
            }
        }
    }
    
    public class PerformanceMetricDisplay
    {
        public string OperationName { get; init; } = string.Empty;
        public int TotalExecutions { get; init; }
        public double SuccessRate { get; init; }
        public double AverageTime { get; init; }
        public double MinTime { get; init; }
        public double MaxTime { get; init; }
        public DateTime LastExecuted { get; init; }
        public string HealthStatus { get; init; } = string.Empty;
    }
    
    public class OptimizationRecommendation
    {
        public string Priority { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public string Issue { get; init; } = string.Empty;
        public string Recommendation { get; init; } = string.Empty;
        public string EstimatedImpact { get; init; } = string.Empty;
    }
}