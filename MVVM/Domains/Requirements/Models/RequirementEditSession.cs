using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Models
{
    /// <summary>
    /// Represents a single requirement edit session within the Cleanup workspace.
    /// Tracks original state from Jama + user edits + staging state.
    /// </summary>
    public class RequirementEditSession
    {
        /// <summary>
        /// Unique identifier from Jama (never changes)
        /// </summary>
        public int JamaId { get; set; }

        /// <summary>
        /// Unique key from Jama (e.g., "REQ-001")
        /// </summary>
        public string? JamaKey { get; set; }

        // === NAME ===
        /// <summary>
        /// Original name as loaded from Jama
        /// </summary>
        public string? OriginalName { get; set; }

        /// <summary>
        /// Current name (may be edited by user)
        /// </summary>
        public string? CurrentName { get; set; }

        // === DESCRIPTION ===
        /// <summary>
        /// Original description as loaded from Jama
        /// </summary>
        public string? OriginalDescription { get; set; }

        /// <summary>
        /// Current description (may be edited by user)
        /// </summary>
        public string? CurrentDescription { get; set; }

        // === ADDITIONAL FIELDS ===
        /// <summary>
        /// All Jama item fields (may include custom lookups, etc.)
        /// </summary>
        public Dictionary<string, object?>? Fields { get; set; } = new();

        /// <summary>
        /// Current state of all fields (may be edited)
        /// </summary>
        public Dictionary<string, object?>? CurrentFields { get; set; } = new();

        // === INCOSE VALIDATION ===
        /// <summary>
        /// INCOSE validation issues (if any)
        /// </summary>
        public string? IncoseValidationStatus { get; set; }

        /// <summary>
        /// INCOSE issues detail (serialized JSON or text)
        /// </summary>
        public string? IncoseIssuesJson { get; set; }

        // === LLM ANALYSIS ===
        /// <summary>
        /// LLM analysis status (NotAnalyzed, Analyzing, Complete, Failed)
        /// </summary>
        public string? LlmAnalysisStatus { get; set; }

        /// <summary>
        /// LLM analysis results (serialized JSON)
        /// </summary>
        public string? LlmAnalysisJson { get; set; }

        // === EDIT STATE ===
        /// <summary>
        /// Who last edited this requirement
        /// </summary>
        public string? EditedBy { get; set; }

        /// <summary>
        /// When this requirement was last edited
        /// </summary>
        public DateTime? LastEdited { get; set; }

        /// <summary>
        /// When this requirement was last saved to workspace
        /// </summary>
        public DateTime? LastSaved { get; set; }

        // === STAGING STATE ===
        /// <summary>
        /// Is this requirement staged for commit to Jama?
        /// </summary>
        public bool StagedForCommit { get; set; } = false;

        /// <summary>
        /// Has this requirement been committed to Jama in this session?
        /// </summary>
        public bool CommittedToJama { get; set; } = false;

        /// <summary>
        /// When was this committed to Jama (if committed)
        /// </summary>
        public DateTime? CommittedAt { get; set; }

        /// <summary>
        /// Who committed this to Jama
        /// </summary>
        public string? CommittedBy { get; set; }

        // === HELPERS ===
        /// <summary>
        /// Has this requirement been modified from original?
        /// </summary>
        public bool HasChanges =>
            (CurrentName != OriginalName) ||
            (CurrentDescription != OriginalDescription) ||
            (CurrentFields?.Count != Fields?.Count); // Simplified check

        /// <summary>
        /// Display name for UI (prefers current, falls back to original)
        /// </summary>
        public string DisplayName => !string.IsNullOrWhiteSpace(CurrentName) ? CurrentName : (OriginalName ?? $"Requirement {JamaId}");

        /// <summary>
        /// Display description for UI
        /// </summary>
        public string DisplayDescription => !string.IsNullOrWhiteSpace(CurrentDescription) ? CurrentDescription : (OriginalDescription ?? "");

        /// <summary>
        /// Reset edits to original state (undo)
        /// </summary>
        public void ResetToOriginal()
        {
            CurrentName = OriginalName;
            CurrentDescription = OriginalDescription;
            CurrentFields = new Dictionary<string, object?>(Fields ?? new());
            LastEdited = null;
            StagedForCommit = false;
        }

        /// <summary>
        /// Mark as staged
        /// </summary>
        public void Stage()
        {
            StagedForCommit = true;
        }

        /// <summary>
        /// Mark as unstaged
        /// </summary>
        public void Unstage()
        {
            StagedForCommit = false;
        }

        /// <summary>
        /// Mark as committed
        /// </summary>
        public void MarkCommitted(string committedBy)
        {
            CommittedToJama = true;
            CommittedAt = DateTime.UtcNow;
            CommittedBy = committedBy;
        }
    }

    /// <summary>
    /// Container for all edit sessions in a workspace
    /// </summary>
    public class RequirementEditWorkspace
    {
        /// <summary>
        /// Session timestamp
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Project ID this workspace is for
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// Container ID (if importing to specific container)
        /// </summary>
        public int? TargetContainerId { get; set; }

        /// <summary>
        /// All edit sessions in this workspace
        /// </summary>
        public List<RequirementEditSession> Requirements { get; set; } = new();

        /// <summary>
        /// Get all requirements staged for commit
        /// </summary>
        public List<RequirementEditSession> GetStagedRequirements() =>
            Requirements.FindAll(r => r.StagedForCommit && !r.CommittedToJama);

        /// <summary>
        /// Get all requirements with unsaved changes
        /// </summary>
        public List<RequirementEditSession> GetUnsavedRequirements() =>
            Requirements.FindAll(r => r.HasChanges && !r.CommittedToJama && (r.LastEdited > r.LastSaved));

        /// <summary>
        /// Get all committed requirements in this session
        /// </summary>
        public List<RequirementEditSession> GetCommittedRequirements() =>
            Requirements.FindAll(r => r.CommittedToJama);
    }
}
