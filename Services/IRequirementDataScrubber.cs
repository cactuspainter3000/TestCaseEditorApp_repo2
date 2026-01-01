using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for cleaning, validating, and processing requirement data during imports.
    /// Provides consistent data quality across all import sources (Word, Jama, etc.).
    /// </summary>
    public interface IRequirementDataScrubber
    {
        /// <summary>
        /// Process requirements through validation and cleaning pipeline
        /// </summary>
        /// <param name="newRequirements">Requirements to be processed</param>
        /// <param name="existingRequirements">Current requirements for duplicate detection</param>
        /// <param name="context">Import context and settings</param>
        /// <returns>Processed results with clean requirements and validation issues</returns>
        Task<ScrubberResult> ProcessRequirementsAsync(
            List<Requirement> newRequirements, 
            List<Requirement> existingRequirements,
            ImportContext context);
    }

    /// <summary>
    /// Context information for requirement import processing
    /// </summary>
    public class ImportContext
    {
        public string FileName { get; set; } = string.Empty;
        public ImportType ImportType { get; set; } = ImportType.Replace;
        public ImportSource Source { get; set; } = ImportSource.Unknown;
        public DateTime ImportTimestamp { get; set; } = DateTime.Now;
        public string? UserNotes { get; set; }
    }

    /// <summary>
    /// Type of import operation being performed
    /// </summary>
    public enum ImportType
    {
        Replace,     // Clear existing, add new (current behavior)
        Additional,  // Append to existing (new functionality) 
        Update       // Future: Update existing requirements
    }

    /// <summary>
    /// Source of the import data
    /// </summary>
    public enum ImportSource
    {
        Unknown,
        Word,
        Jama,
        Json,
        Excel
    }

    /// <summary>
    /// Results from the requirement scrubbing process
    /// </summary>
    public class ScrubberResult
    {
        public List<Requirement> CleanRequirements { get; set; } = new();
        public List<Requirement> DuplicatesDetected { get; set; } = new();
        public List<RequirementIssue> ValidationIssues { get; set; } = new();
        public List<RequirementConflict> MergeConflicts { get; set; } = new();
        public ScrubberStats Statistics { get; set; } = new();
    }

    /// <summary>
    /// Validation issue found during scrubbing
    /// </summary>
    public class RequirementIssue
    {
        public Requirement Requirement { get; set; } = null!;
        public IssueType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string? SuggestedFix { get; set; }
    }

    /// <summary>
    /// Conflict requiring user resolution
    /// </summary>
    public class RequirementConflict
    {
        public Requirement NewRequirement { get; set; } = null!;
        public Requirement ExistingRequirement { get; set; } = null!;
        public string ConflictDescription { get; set; } = string.Empty;
        public ConflictResolutionStrategy SuggestedResolution { get; set; }
    }

    /// <summary>
    /// Statistics about the scrubbing operation
    /// </summary>
    public class ScrubberStats
    {
        public int TotalProcessed { get; set; }
        public int CleanRequirements { get; set; }
        public int DuplicatesSkipped { get; set; }
        public int IssuesFixed { get; set; }
        public int WarningsGenerated { get; set; }
        public int ErrorsFound { get; set; }
        public TimeSpan ProcessingTime { get; set; }
    }

    /// <summary>
    /// Type of validation issue
    /// </summary>
    public enum IssueType
    {
        Info,
        Warning,
        Error,
        Fixed
    }

    /// <summary>
    /// Strategy for resolving conflicts
    /// </summary>
    public enum ConflictResolutionStrategy
    {
        SkipNew,
        ReplaceExisting,
        Merge,
        AskUser
    }
}