using System;
using System.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.Startup.Events
{
    /// <summary>
    /// Events for the Startup domain, managing initial application state
    /// </summary>
    public class StartupEvents
    {
        /// <summary>
        /// Published when the startup sequence begins
        /// </summary>
        public class StartupInitiated
        {
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string InitiatedBy { get; set; } = "Application";
        }

        /// <summary>
        /// Published when startup sequence completes
        /// </summary>
        public class StartupCompleted
        {
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public TimeSpan StartupDuration { get; set; }
        }

        /// <summary>
        /// Published when startup status changes
        /// </summary>
        public class StartupStatusChanged
        {
            public string Status { get; set; } = string.Empty;
            public double ProgressPercent { get; set; }
            public bool IsVisible { get; set; } = true;
        }

        /// <summary>
        /// Request to transition from startup to specific domain
        /// </summary>
        public class RequestDomainTransition
        {
            public string TargetDomain { get; set; } = string.Empty;
            public object? TransitionData { get; set; }
        }
    }
}