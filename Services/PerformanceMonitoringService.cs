using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Performance monitoring service for tracking domain operations and system health.
    /// Provides metrics collection, performance monitoring, and debugging capabilities.
    /// </summary>
    public class PerformanceMonitoringService
    {
        private readonly ILogger<PerformanceMonitoringService> _logger;
        private readonly Dictionary<string, PerformanceMetric> _metrics;
        private readonly object _lock = new object();

        public PerformanceMonitoringService(ILogger<PerformanceMonitoringService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = new Dictionary<string, PerformanceMetric>();
        }

        /// <summary>
        /// Start tracking a performance operation
        /// </summary>
        public IDisposable StartOperation(string operationName, string domainContext = "")
        {
            return new PerformanceTracker(this, operationName, domainContext);
        }

        /// <summary>
        /// Record a completed operation's performance metrics
        /// </summary>
        public void RecordOperation(string operationName, TimeSpan duration, string domainContext = "", bool success = true)
        {
            lock (_lock)
            {
                var key = $"{domainContext}.{operationName}";
                
                if (!_metrics.TryGetValue(key, out var metric))
                {
                    metric = new PerformanceMetric(key);
                    _metrics[key] = metric;
                }

                metric.RecordExecution(duration, success);

                // Log slow operations
                if (duration.TotalSeconds > 5)
                {
                    _logger.LogWarning("[PERF] Slow operation detected: {Operation} took {Duration:F2}s in domain {Domain}", 
                        operationName, duration.TotalSeconds, domainContext);
                }
            }
        }

        /// <summary>
        /// Get performance summary for all tracked operations
        /// </summary>
        public IReadOnlyDictionary<string, PerformanceMetric> GetPerformanceSummary()
        {
            lock (_lock)
            {
                return new Dictionary<string, PerformanceMetric>(_metrics);
            }
        }

        /// <summary>
        /// Clear all performance metrics (useful for testing)
        /// </summary>
        public void ClearMetrics()
        {
            lock (_lock)
            {
                _metrics.Clear();
            }
        }

        /// <summary>
        /// Performance tracker that automatically records timing when disposed
        /// </summary>
        private class PerformanceTracker : IDisposable
        {
            private readonly PerformanceMonitoringService _service;
            private readonly string _operationName;
            private readonly string _domainContext;
            private readonly Stopwatch _stopwatch;
            private bool _disposed = false;

            public PerformanceTracker(PerformanceMonitoringService service, string operationName, string domainContext)
            {
                _service = service;
                _operationName = operationName;
                _domainContext = domainContext;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _stopwatch.Stop();
                    _service.RecordOperation(_operationName, _stopwatch.Elapsed, _domainContext);
                    _disposed = true;
                }
            }
        }
    }

    /// <summary>
    /// Performance metrics for a specific operation
    /// </summary>
    public class PerformanceMetric
    {
        private readonly object _lock = new object();
        
        public string OperationKey { get; }
        public int TotalExecutions { get; private set; }
        public int SuccessfulExecutions { get; private set; }
        public int FailedExecutions { get; private set; }
        public TimeSpan TotalDuration { get; private set; }
        public TimeSpan MinDuration { get; private set; } = TimeSpan.MaxValue;
        public TimeSpan MaxDuration { get; private set; } = TimeSpan.MinValue;
        public DateTime FirstExecution { get; private set; }
        public DateTime LastExecution { get; private set; }

        public PerformanceMetric(string operationKey)
        {
            OperationKey = operationKey ?? throw new ArgumentNullException(nameof(operationKey));
        }

        public void RecordExecution(TimeSpan duration, bool success)
        {
            lock (_lock)
            {
                TotalExecutions++;
                
                if (success)
                    SuccessfulExecutions++;
                else
                    FailedExecutions++;

                TotalDuration = TotalDuration.Add(duration);
                
                if (duration < MinDuration)
                    MinDuration = duration;
                    
                if (duration > MaxDuration)
                    MaxDuration = duration;

                if (TotalExecutions == 1)
                    FirstExecution = DateTime.Now;
                    
                LastExecution = DateTime.Now;
            }
        }

        public TimeSpan AverageDuration => TotalExecutions > 0 ? 
            TimeSpan.FromTicks(TotalDuration.Ticks / TotalExecutions) : 
            TimeSpan.Zero;

        public double SuccessRate => TotalExecutions > 0 ? 
            (double)SuccessfulExecutions / TotalExecutions : 
            0.0;

        public override string ToString() => 
            $"{OperationKey}: {TotalExecutions} executions, " +
            $"{SuccessRate:P1} success rate, " +
            $"avg {AverageDuration.TotalMilliseconds:F1}ms";
    }
}