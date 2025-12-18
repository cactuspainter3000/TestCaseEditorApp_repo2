using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Utils
{
    /// <summary>
    /// Event replay service for debugging domain interactions and event flow.
    /// Captures domain events and provides replay capabilities for debugging.
    /// </summary>
    public class EventReplayService
    {
        private readonly ILogger<EventReplayService> _logger;
        private readonly List<EventRecord> _eventHistory;
        private readonly object _lock = new object();
        private readonly int _maxHistorySize;

        public EventReplayService(ILogger<EventReplayService> logger, int maxHistorySize = 1000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventHistory = new List<EventRecord>();
            _maxHistorySize = maxHistorySize;
        }

        /// <summary>
        /// Record an event for potential replay
        /// </summary>
        public void RecordEvent<TEvent>(TEvent domainEvent, string domainName, string mediatorType) where TEvent : class
        {
            lock (_lock)
            {
                var record = new EventRecord
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.Now,
                    EventType = typeof(TEvent).Name,
                    EventData = domainEvent,
                    DomainName = domainName,
                    MediatorType = mediatorType,
                    FullEventTypeName = typeof(TEvent).FullName ?? typeof(TEvent).Name
                };

                _eventHistory.Add(record);

                // Trim history if it gets too large
                if (_eventHistory.Count > _maxHistorySize)
                {
                    _eventHistory.RemoveAt(0);
                }

                _logger.LogDebug("[EventReplay] Recorded {EventType} from {Domain}.{Mediator}", 
                    record.EventType, domainName, mediatorType);
            }
        }

        /// <summary>
        /// Get event history for a specific domain
        /// </summary>
        public IReadOnlyList<EventRecord> GetDomainHistory(string domainName, int? maxEvents = null)
        {
            lock (_lock)
            {
                var query = _eventHistory.Where(e => e.DomainName.Equals(domainName, StringComparison.OrdinalIgnoreCase));
                
                if (maxEvents.HasValue)
                    query = query.TakeLast(maxEvents.Value);
                
                return query.ToList();
            }
        }

        /// <summary>
        /// Get complete event history
        /// </summary>
        public IReadOnlyList<EventRecord> GetCompleteHistory(int? maxEvents = null)
        {
            lock (_lock)
            {
                if (maxEvents.HasValue)
                    return _eventHistory.TakeLast(maxEvents.Value).ToList();
                
                return _eventHistory.ToList();
            }
        }

        /// <summary>
        /// Get events of a specific type
        /// </summary>
        public IReadOnlyList<EventRecord> GetEventsByType(string eventType, int? maxEvents = null)
        {
            lock (_lock)
            {
                var query = _eventHistory.Where(e => e.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase));
                
                if (maxEvents.HasValue)
                    query = query.TakeLast(maxEvents.Value);
                
                return query.ToList();
            }
        }

        /// <summary>
        /// Get events within a time range
        /// </summary>
        public IReadOnlyList<EventRecord> GetEventsByTimeRange(DateTime startTime, DateTime endTime)
        {
            lock (_lock)
            {
                return _eventHistory
                    .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
                    .ToList();
            }
        }

        /// <summary>
        /// Clear event history
        /// </summary>
        public void ClearHistory()
        {
            lock (_lock)
            {
                _eventHistory.Clear();
                _logger.LogInformation("[EventReplay] Event history cleared");
            }
        }

        /// <summary>
        /// Generate a summary of event activity
        /// </summary>
        public EventSummary GenerateSummary()
        {
            lock (_lock)
            {
                var summary = new EventSummary
                {
                    TotalEvents = _eventHistory.Count,
                    TimeRange = _eventHistory.Count > 0 ? 
                        new TimeRange(_eventHistory.First().Timestamp, _eventHistory.Last().Timestamp) : 
                        null,
                    DomainActivity = _eventHistory
                        .GroupBy(e => e.DomainName)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    EventTypeFrequency = _eventHistory
                        .GroupBy(e => e.EventType)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    MediatorActivity = _eventHistory
                        .GroupBy(e => $"{e.DomainName}.{e.MediatorType}")
                        .ToDictionary(g => g.Key, g => g.Count())
                };

                return summary;
            }
        }
    }

    /// <summary>
    /// Record of a domain event for replay purposes
    /// </summary>
    public class EventRecord
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string FullEventTypeName { get; set; } = string.Empty;
        public object EventData { get; set; } = default!;
        public string DomainName { get; set; } = string.Empty;
        public string MediatorType { get; set; } = string.Empty;

        public override string ToString() => 
            $"[{Timestamp:HH:mm:ss.fff}] {DomainName}.{EventType}";
    }

    /// <summary>
    /// Summary of event replay activity
    /// </summary>
    public class EventSummary
    {
        public int TotalEvents { get; set; }
        public TimeRange? TimeRange { get; set; }
        public Dictionary<string, int> DomainActivity { get; set; } = new();
        public Dictionary<string, int> EventTypeFrequency { get; set; } = new();
        public Dictionary<string, int> MediatorActivity { get; set; } = new();

        public override string ToString()
        {
            var topDomain = DomainActivity.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            var topEvent = EventTypeFrequency.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            
            return $"Events: {TotalEvents}, Most Active Domain: {topDomain.Key} ({topDomain.Value}), " +
                   $"Most Common Event: {topEvent.Key} ({topEvent.Value})";
        }
    }

    /// <summary>
    /// Time range for event filtering
    /// </summary>
    public class TimeRange
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public TimeRange(DateTime startTime, DateTime endTime)
        {
            StartTime = startTime;
            EndTime = endTime;
        }

        public TimeSpan Duration => EndTime - StartTime;

        public override string ToString() => 
            $"{StartTime:HH:mm:ss} - {EndTime:HH:mm:ss} ({Duration.TotalSeconds:F1}s)";
    }
}