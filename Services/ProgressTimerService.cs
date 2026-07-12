using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Tracks progress message timing and displays how long each message persists on the GUI.
    /// Provides detailed diagnostics for understanding the progress flow during requirement analysis.
    /// </summary>
    public interface IProgressTimerService
    {
        void RecordProgressMessage(Requirement requirement, string stage, int percentage, string message);
        void RecordMessageChange(Requirement requirement, string oldMessage, string newMessage);
        void GenerateTimingReport(Requirement requirement);
    }

    public class ProgressTimerService : IProgressTimerService
    {
        private readonly ILogger<ProgressTimerService> _logger;
        private readonly Dictionary<string, ProgressMessageSession> _activeSessions = new();
        private readonly Dictionary<string, List<ProgressMessageTiming>> _sessionHistory = new();

        private class ProgressMessageSession
        {
            public DateTime StartTime { get; set; }
            public string CurrentStage { get; set; } = "";
            public int CurrentPercentage { get; set; }
            public string CurrentMessage { get; set; } = "";
            public int MessageCount { get; set; }
            public DateTime SessionStartTime { get; set; }
        }

        private class ProgressMessageTiming
        {
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string Stage { get; set; } = "";
            public int Percentage { get; set; }
            public string Message { get; set; } = "";
            public double DurationMs { get; set; }
            public int SequenceNumber { get; set; }

            public override string ToString()
            {
                var duration = EndTime.HasValue ? (EndTime.Value - StartTime).TotalMilliseconds : 0;
                return $"[{SequenceNumber:D2}] {StartTime:HH:mm:ss.fff} ({Stage}) {Percentage:D3}% | " +
                       $"Duration: {duration:F2}ms | Message: {Message}";
            }
        }

        public ProgressTimerService(ILogger<ProgressTimerService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void RecordProgressMessage(Requirement requirement, string stage, int percentage, string message)
        {
            if (requirement == null)
                return;

            var sessionKey = GetSessionKey(requirement);

            // Initialize session if needed
            if (!_activeSessions.ContainsKey(sessionKey))
            {
                _activeSessions[sessionKey] = new ProgressMessageSession
                {
                    SessionStartTime = DateTime.Now,
                    StartTime = DateTime.Now,
                    CurrentStage = stage,
                    CurrentPercentage = percentage,
                    CurrentMessage = message,
                    MessageCount = 0
                };

                if (!_sessionHistory.ContainsKey(sessionKey))
                {
                    _sessionHistory[sessionKey] = new List<ProgressMessageTiming>();
                }

                _logger.LogInformation("📊 [ProgressTimer] Analysis session started for requirement: {RequirementItem}", 
                    requirement.Item);
            }

            var session = _activeSessions[sessionKey];
            var history = _sessionHistory[sessionKey];

            // If message changed, complete the previous timing entry
            if (session.CurrentMessage != message || session.CurrentPercentage != percentage)
            {
                if (session.MessageCount > 0)
                {
                    var previousTiming = history.LastOrDefault();
                    if (previousTiming != null && !previousTiming.EndTime.HasValue)
                    {
                        previousTiming.EndTime = DateTime.Now;
                        previousTiming.DurationMs = (previousTiming.EndTime.Value - previousTiming.StartTime).TotalMilliseconds;

                        _logger.LogInformation(
                            "⏱️  [ProgressTimer] Message ended after {DurationMs:F2}ms: [{Stage}] {OldPercentage:D3}% - {OldMessage}",
                            previousTiming.DurationMs, previousTiming.Stage, previousTiming.Percentage, previousTiming.Message);
                    }
                }

                // Add new timing entry
                session.MessageCount++;
                var newTiming = new ProgressMessageTiming
                {
                    StartTime = DateTime.Now,
                    Stage = stage,
                    Percentage = percentage,
                    Message = message,
                    SequenceNumber = session.MessageCount,
                    EndTime = null,
                    DurationMs = 0
                };

                history.Add(newTiming);

                _logger.LogInformation(
                    "📝 [ProgressTimer] Message #{Sequence}: [{Stage}] {Percentage:D3}% - {Message}",
                    session.MessageCount, stage, percentage, message);

                session.CurrentStage = stage;
                session.CurrentPercentage = percentage;
                session.CurrentMessage = message;
                session.StartTime = DateTime.Now;
            }
        }

        public void RecordMessageChange(Requirement requirement, string oldMessage, string newMessage)
        {
            if (requirement == null)
                return;

            var sessionKey = GetSessionKey(requirement);
            if (!_activeSessions.ContainsKey(sessionKey))
                return;

            _logger.LogDebug("🔄 [ProgressTimer] Message changed from '{OldMessage}' to '{NewMessage}'", oldMessage, newMessage);
        }

        public void GenerateTimingReport(Requirement requirement)
        {
            if (requirement == null)
                return;

            var sessionKey = GetSessionKey(requirement);
            if (!_sessionHistory.ContainsKey(sessionKey))
            {
                _logger.LogWarning("⚠️  [ProgressTimer] No history found for requirement: {RequirementItem}", requirement.Item);
                return;
            }

            var history = _sessionHistory[sessionKey];
            var session = _activeSessions.ContainsKey(sessionKey) ? _activeSessions[sessionKey] : null;

            // Complete the final message timing
            if (history.Count > 0)
            {
                var lastTiming = history.Last();
                if (!lastTiming.EndTime.HasValue)
                {
                    lastTiming.EndTime = DateTime.Now;
                    lastTiming.DurationMs = (lastTiming.EndTime.Value - lastTiming.StartTime).TotalMilliseconds;
                }
            }

            // Generate report
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine($"📊 PROGRESS TIMER REPORT - Requirement: {requirement.Item}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();

            if (history.Count == 0)
            {
                sb.AppendLine("No progress messages recorded.");
            }
            else
            {
                sb.AppendLine("Progress Message Timeline:");
                sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");

                double totalDuration = 0;
                foreach (var timing in history)
                {
                    sb.AppendLine(timing.ToString());
                    totalDuration += timing.DurationMs;
                }

                sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
                sb.AppendLine($"Total Messages: {history.Count}");
                sb.AppendLine($"Total Duration: {totalDuration:F2}ms ({TimeSpan.FromMilliseconds(totalDuration):hh\\:mm\\:ss\\.fff})");

                // Calculate average message duration (guard against empty/zero-duration sequences)
                var completedDurations = history.Where(t => t.DurationMs > 0).ToList();
                if (completedDurations.Count > 0)
                {
                    var avgDuration = completedDurations.Average(t => t.DurationMs);
                    sb.AppendLine($"Average Message Duration: {avgDuration:F2}ms");

                    // Find longest-persisting completed message
                    var longestMessage = completedDurations.OrderByDescending(t => t.DurationMs).FirstOrDefault();
                    if (longestMessage != null)
                    {
                        sb.AppendLine($"Longest Message Duration: {longestMessage.DurationMs:F2}ms - [{longestMessage.Stage}] {longestMessage.Message}");
                    }
                }
                else
                {
                    sb.AppendLine("Average Message Duration: n/a (no completed message durations recorded)");
                }

                // Progress rate analysis
                sb.AppendLine();
                sb.AppendLine("Progress Rate Analysis:");
                sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
                
                var messagesByStage = history.GroupBy(t => t.Stage);
                foreach (var stageGroup in messagesByStage)
                {
                    var stageTiming = stageGroup.Sum(t => t.DurationMs);
                    var stageCount = stageGroup.Count();
                    sb.AppendLine($"Stage '{stageGroup.Key}': {stageCount} messages, {stageTiming:F2}ms total");
                }
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();

            _logger.LogInformation("{TimingReport}", sb.ToString());

            // Also write to debug output
            Debug.WriteLine(sb.ToString());
        }

        private string GetSessionKey(Requirement requirement)
        {
            return $"{requirement.GlobalId ?? requirement.Item}_{DateTime.Now:yyyyMMdd}";
        }

        public void ClearHistory()
        {
            _activeSessions.Clear();
            _sessionHistory.Clear();
            _logger.LogInformation("🧹 [ProgressTimer] History cleared");
        }
    }
}
