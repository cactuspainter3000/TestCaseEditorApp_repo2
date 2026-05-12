using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.Services.Logging
{
    /// <summary>
    /// Optional context information for requirements analysis log snapshots.
    /// Helps future debugging by providing method names, triggers, retry info, and custom comments.
    /// All properties are optional; use only what's relevant to your call site.
    /// </summary>
    public class AnalysisSnapshotContext
    {
        /// <summary>
        /// Name of the method that triggered the snapshot capture (e.g., nameof(OnAnalyzeClicked)).
        /// </summary>
        public string? MethodName { get; set; }

        /// <summary>
        /// What triggered this analysis (e.g., "UserButton", "AutoRetry", "ImportFlow", "BackgroundValidation").
        /// </summary>
        public string? TriggeredBy { get; set; }

        /// <summary>
        /// Retry attempt number (1 for first try, 2+ for retries).
        /// </summary>
        public int? RetryAttempt { get; set; }

        /// <summary>
        /// Elapsed time in milliseconds for the analysis operation.
        /// </summary>
        public long? ElapsedMilliseconds { get; set; }

        /// <summary>
        /// The requirement ID or case identifier being analyzed.
        /// </summary>
        public string? RequirementId { get; set; }

        /// <summary>
        /// Custom comments to aid understanding of code changes or debugging context.
        /// Example: "Testing JSON parser behavior after merge", "User reported timeout on second attempt", etc.
        /// </summary>
        public string? Comments { get; set; }

        /// <summary>
        /// Additional metadata as key-value pairs for extensibility.
        /// </summary>
        public Dictionary<string, object>? CustomData { get; set; }

        /// <summary>
        /// Timestamp when the context was created (set automatically).
        /// </summary>
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;

        /// <summary>
        /// Returns a human-readable summary of the context for logging.
        /// </summary>
        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(MethodName))
                parts.Add($"Method: {MethodName}");

            if (!string.IsNullOrEmpty(TriggeredBy))
                parts.Add($"Trigger: {TriggeredBy}");

            if (RetryAttempt.HasValue)
                parts.Add($"Attempt: {RetryAttempt}");

            if (ElapsedMilliseconds.HasValue)
                parts.Add($"Duration: {ElapsedMilliseconds}ms");

            if (!string.IsNullOrEmpty(RequirementId))
                parts.Add($"RequirementId: {RequirementId}");

            if (!string.IsNullOrEmpty(Comments))
                parts.Add($"Notes: {Comments}");

            return parts.Count > 0 ? string.Join(" | ", parts) : "(empty context)";
        }
    }
}
