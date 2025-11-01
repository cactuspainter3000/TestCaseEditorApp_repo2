// Workspace.cs


namespace TestCaseEditorApp.MVVM.Models
{
    using TestCaseEditorApp.MVVM.Models;

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
    }
}




