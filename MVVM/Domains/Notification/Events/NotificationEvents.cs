using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Domains.Notification.Events
{
    /// <summary>
    /// Domain event container for the Notification system
    /// Handles cross-domain status updates and progress tracking
    /// </summary>
    public class NotificationEvents
    {
        /// <summary>
        /// Event for LLM connection status changes
        /// </summary>
        public class LlmStatusChanged
        {
            public bool IsConnected { get; init; }
            public string StatusText { get; init; } = string.Empty;
            public string? Provider { get; init; }
            public string? Model { get; init; }
            public DateTime Timestamp { get; init; } = DateTime.Now;
        }

        /// <summary>
        /// Event for requirements progress updates across all domains
        /// </summary>
        public class RequirementsProgressChanged
        {
            public int TotalRequirements { get; init; }
            public int AnalyzedRequirements { get; init; }
            public int RequirementsWithTestCases { get; init; }
            public string SourceDomain { get; init; } = string.Empty;
            public DateTime Timestamp { get; init; } = DateTime.Now;
        }

        /// <summary>
        /// Event for current requirement verification method updates
        /// </summary>
        public class CurrentRequirementChanged
        {
            public string? RequirementId { get; init; }
            public string? VerificationMethod { get; init; }
            public string? RequirementTitle { get; init; }
            public string SourceDomain { get; init; } = string.Empty;
            public DateTime Timestamp { get; init; } = DateTime.Now;
        }

        /// <summary>
        /// Event for domain-specific status updates
        /// </summary>
        public class DomainStatusChanged
        {
            public string DomainName { get; init; } = string.Empty;
            public string StatusMessage { get; init; } = string.Empty;
            public string StatusType { get; init; } = "Info"; // Info, Success, Warning, Error
            public Dictionary<string, object> AdditionalData { get; init; } = new();
            public DateTime Timestamp { get; init; } = DateTime.Now;
        }

        /// <summary>
        /// Event for notification area visibility and content management
        /// </summary>
        public class NotificationAreaUpdate
        {
            public bool IsVisible { get; init; } = true;
            public string? CustomMessage { get; init; }
            public string? RequestingDomain { get; init; }
            public DateTime Timestamp { get; init; } = DateTime.Now;
        }
    }
}