using System;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Events for the 3-level breadcrumb system
    /// </summary>
    public static class BreadcrumbEvents
    {
        /// <summary>
        /// Section level breadcrumb changed (Test Case Generator, Requirements, etc.)
        /// </summary>
        public class SectionChanged
        {
            public string Section { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Project level breadcrumb changed (project name)
        /// </summary>
        public class ProjectChanged
        {
            public string? Project { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Context level breadcrumb changed (specific requirement, test case, etc.)
        /// </summary>
        public class ContextChanged
        {
            public string? Context { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Complete breadcrumb composed from all levels
        /// </summary>
        public class BreadcrumbComposed
        {
            public string FullBreadcrumb { get; set; } = string.Empty;
            public string Section { get; set; } = string.Empty;
            public string? Project { get; set; }
            public string? Context { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
}