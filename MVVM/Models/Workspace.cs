// Workspace.cs
using System.Text.Json.Serialization;

namespace TestCaseEditorApp.MVVM.Models
{
    // ---- Simple JSON-persisted workspace ----
    public class Workspace
    {
        public const int SchemaVersion = 2;
        public int Version { get; set; } = SchemaVersion;
        public string? SourceDocPath { get; set; }
        public DateTime LastSavedUtc { get; set; }
        public List<Requirement> Requirements { get; set; } = new();

        /// <summary>Default Jama Project name to use on export (CSV "Project" column).</summary>
        public string? JamaProject { get; set; }

        /// <summary>Canonical Jama Project ID to restore when reopening a project.</summary>
        public int? JamaProjectId { get; set; }

        /// <summary>Canonical Jama Project name to restore when reopening a project.</summary>
        public string? JamaProjectName { get; set; }

        /// <summary>Default Jama Test Plan name to use on export (CSV "Test Plan" column).</summary>
        public string? JamaTestPlan { get; set; }

        /// <summary>Human-readable AnythingLLM workspace name associated with this project.</summary>
        public string? AnythingLLMWorkspaceName { get; set; }

        /// <summary>Canonical AnythingLLM workspace slug associated with this project.</summary>
        public string? AnythingLLMWorkspaceSlug { get; set; }

        public DefaultsBlock? Defaults { get; set; }
        public string? Name { get; set; }

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




