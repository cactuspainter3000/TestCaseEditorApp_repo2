// Workspace.cs
using System.Text.Json.Serialization;

namespace TestCaseEditorApp.MVVM.Models
{
    // ---- Simple JSON-persisted workspace ----
    public class Workspace
    {
        public const int SchemaVersion = 1;
        public int Version { get; set; } = SchemaVersion;
        public string? SourceDocPath { get; set; }
        public DateTime LastSavedUtc { get; set; }
        public List<Requirement> Requirements { get; set; } = new();

        /// <summary>Default Jama Project name to use on export (CSV "Project" column).</summary>
        public string? JamaProject { get; set; }

        /// <summary>Default Jama Test Plan name to use on export (CSV "Test Plan" column).</summary>
        public string? JamaTestPlan { get; set; }

        public DefaultsBlock? Defaults { get; set; }
        public string? Name { get; internal set; }

        // Metadata for tracking workspace lifecycle
        public string? CreatedBy { get; set; }
        public DateTime? CreatedUtc { get; set; }
        public int SaveCount { get; set; }
        public Dictionary<string, WorkStatus> RequirementStatus { get; set; } = new();
    }

    /// <summary>
    /// Tracks work status for each requirement to provide quick overview.
    /// </summary>
    public class WorkStatus
    {
        public bool HasQuestions { get; set; }
        public bool HasTestCases { get; set; }
        public bool HasAssumptions { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }
}




