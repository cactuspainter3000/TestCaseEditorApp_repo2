using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Events
{
    /// <summary>
    /// Domain events for Test Case Creation workflow
    /// </summary>
    public class TestCaseCreationEvents
    {
        /// <summary>
        /// Test case was created by user
        /// </summary>
        public class TestCaseCreated
        {
            public required EditableTestCase TestCase { get; init; }
            public string CreatedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Test case was modified by user
        /// </summary>
        public class TestCaseModified
        {
            public required EditableTestCase TestCase { get; init; }
            public required string ModifiedField { get; init; }
            public string ModifiedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Test case was deleted by user
        /// </summary>
        public class TestCaseDeleted
        {
            public required string TestCaseId { get; init; }
            public required string TestCaseTitle { get; init; }
            public string DeletedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Multiple test cases were saved to requirement
        /// </summary>
        public class TestCasesSaved
        {
            public required Requirement Requirement { get; init; }
            public required IReadOnlyList<EditableTestCase> TestCases { get; init; }
            public string SavedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Test case selection changed in UI
        /// </summary>
        public class TestCaseSelectionChanged
        {
            public EditableTestCase? PreviousTestCase { get; init; }
            public EditableTestCase? CurrentTestCase { get; init; }
            public string SelectedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Request to generate test case command for external tool
        /// </summary>
        public class GenerateTestCaseCommandRequested
        {
            public required EditableTestCase TestCase { get; init; }
            public required string TargetFormat { get; init; } // "jama", "excel", "markdown", etc.
            public string RequestedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Requirement context changed (user switched requirements)
        /// </summary>
        public class RequirementContextChanged
        {
            public Requirement? PreviousRequirement { get; init; }
            public Requirement? CurrentRequirement { get; init; }
            public string ChangedBy { get; init; } = "User";
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }

        /// <summary>
        /// Workflow step changed in Test Case Creation
        /// </summary>
        public class StepChanged
        {
            public string Step { get; init; } = string.Empty;
            public object? ViewModel { get; init; }
            public DateTime Timestamp { get; init; } = DateTime.Now;
            public string CorrelationId { get; init; } = string.Empty;
        }
    }
}