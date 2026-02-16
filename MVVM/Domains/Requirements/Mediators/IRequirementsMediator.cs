using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Mediators
{
    /// <summary>
    /// Requirements domain mediator interface.
    /// Defines the contract for requirements management functionality.
    /// Following architectural guide patterns for domain mediator interfaces.
    /// </summary>
    public interface IRequirementsMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====

        /// <summary>
        /// Subscribe to Requirements domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;

        /// <summary>
        /// Publish domain events
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;

        /// <summary>
        /// Mark mediator as registered and ready
        /// </summary>
        void MarkAsRegistered();

        // ===== STATE PROPERTIES =====

        /// <summary>
        /// Observable collection of requirements for UI binding
        /// </summary>
        ObservableCollection<Requirement> Requirements { get; }

        /// <summary>
        /// Currently selected requirement
        /// </summary>
        Requirement? CurrentRequirement { get; set; }

        /// <summary>
        /// Indicates if the domain has unsaved changes
        /// </summary>
        bool IsDirty { get; set; }

        /// <summary>
        /// Indicates if analysis operations are in progress
        /// </summary>
        bool IsAnalyzing { get; set; }

        /// <summary>
        /// Indicates if import operations are in progress
        /// </summary>
        bool IsImporting { get; set; }

        // ===== REQUIREMENTS MANAGEMENT =====

        /// <summary>
        /// Import requirements from a file with smart format detection
        /// </summary>
        Task<bool> ImportRequirementsAsync(string filePath, string importType = "Auto");

        /// <summary>
        /// Import additional requirements (append mode)
        /// </summary>
        Task<bool> ImportAdditionalRequirementsAsync(string filePath);

        /// <summary>
        /// Export requirements to a file in specified format
        /// </summary>
        Task<bool> ExportRequirementsAsync(IReadOnlyList<Requirement> requirements, string exportType, string outputPath);

        /// <summary>
        /// Clear all requirements from the collection
        /// </summary>
        void ClearRequirements();

        /// <summary>
        /// Add a single requirement to the collection
        /// </summary>
        void AddRequirement(Requirement requirement);

        /// <summary>
        /// Remove a requirement from the collection
        /// </summary>
        void RemoveRequirement(Requirement requirement);

        /// <summary>
        /// Update an existing requirement
        /// </summary>
        void UpdateRequirement(Requirement requirement, IReadOnlyList<string> modifiedFields);

        // ===== REQUIREMENT SELECTION =====

        /// <summary>
        /// Select a requirement for viewing/editing
        /// </summary>
        void SelectRequirement(Requirement requirement);

        /// <summary>
        /// Navigate to next requirement in collection
        /// </summary>
        bool NavigateToNext();

        /// <summary>
        /// Navigate to previous requirement in collection
        /// </summary>
        bool NavigateToPrevious();

        /// <summary>
        /// Get the index of current requirement in collection
        /// </summary>
        int GetCurrentRequirementIndex();

        // ===== ANALYSIS FUNCTIONALITY =====

        /// <summary>
        /// Analyze a single requirement using LLM services
        /// </summary>
        Task<bool> AnalyzeRequirementAsync(Requirement requirement);

        /// <summary>
        /// Analyze multiple requirements in batch
        /// </summary>
        Task<bool> AnalyzeBatchRequirementsAsync(IReadOnlyList<Requirement> requirements);

        /// <summary>
        /// Analyze all unanalyzed requirements
        /// </summary>
        Task<bool> AnalyzeUnanalyzedRequirementsAsync();

        /// <summary>
        /// Re-analyze requirements that have been modified
        /// </summary>
        Task<bool> ReAnalyzeModifiedRequirementsAsync();

        // ===== SEARCH & FILTERING =====

        /// <summary>
        /// Search requirements by text content
        /// </summary>
        IReadOnlyList<Requirement> SearchRequirements(string searchText);

        /// <summary>
        /// Filter requirements by analysis status
        /// </summary>
        IReadOnlyList<Requirement> FilterByAnalysisStatus(bool analyzed);

        /// <summary>
        /// Filter requirements by verification method
        /// </summary>
        IReadOnlyList<Requirement> FilterByVerificationMethod(VerificationMethod method);

        // ===== VALIDATION =====

        /// <summary>
        /// Validate a requirement for completeness and quality
        /// </summary>
        Task<ValidationResult> ValidateRequirementAsync(Requirement requirement);

        /// <summary>
        /// Validate all requirements in the collection
        /// </summary>
        Task<ValidationResult> ValidateAllRequirementsAsync();

        // ===== PROJECT INTEGRATION =====

        /// <summary>
        /// Load requirements from a project workspace
        /// </summary>
        Task<bool> LoadFromProjectAsync(Workspace workspace);

        /// <summary>
        /// Save requirements to the current project
        /// </summary>
        Task<bool> SaveToProjectAsync();

        /// <summary>
        /// Get the current project name from the workspace context
        /// </summary>
        string CurrentProjectName { get; }

        /// <summary>
        /// Update project context when project changes
        /// </summary>
        void UpdateProjectContext(string? projectName);

        // ===== CROSS-DOMAIN COMMUNICATION =====

        /// <summary>
        /// Broadcast notification to all domains
        /// </summary>
        void BroadcastToAllDomains<T>(T notification) where T : class;

        /// <summary>
        /// Handle cross-domain broadcast notifications
        /// </summary>
        void HandleBroadcastNotification<T>(T notification) where T : class;

        /// <summary>
        /// Determine if the current data source is from Jama Connect
        /// Used by ViewConfigurationService for proper view routing
        /// </summary>
        bool IsJamaDataSource();

        /// <summary>
        /// Navigate to Requirements Search in Attachments feature
        /// Following Architectural Guide AI patterns for domain-specific navigation
        /// </summary>
        void NavigateToRequirementsSearchAttachments();

        /// <summary>
        /// Trigger background attachment scanning for the specified project
        /// Called from OpenProject domain when automatic scanning is needed
        /// </summary>
        Task TriggerBackgroundAttachmentScanAsync(int projectId);
        
        /// <summary>
        /// Notify about attachment scan progress updates
        /// </summary>
        void NotifyAttachmentScanProgress(string progressText);

        /// <summary>
        /// Scan project attachments and return results with progress reporting
        /// Proper mediator method that replaces direct ViewModel service calls
        /// </summary>
        Task<List<JamaAttachment>> ScanProjectAttachmentsAsync(int projectId, IProgress<AttachmentScanProgressData>? progress = null);

        /// <summary>
        /// Parse attachment for requirements using document parsing service
        /// </summary>
        Task<List<Requirement>> ParseAttachmentRequirementsAsync(JamaAttachment attachment, int projectId, System.Action<string>? progressCallback = null);

        /// <summary>
        /// Import extracted requirements into the current project
        /// </summary>
        Task ImportRequirementsAsync(List<Requirement> requirements);

        /// <summary>
        /// Load available Jama projects for selection
        /// Proper mediator method following architectural patterns (no service locator anti-pattern)
        /// </summary>
        Task<List<JamaProject>> GetProjectsAsync();
    }

    /// <summary>
    /// Validation result for requirement validation operations
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
        public TimeSpan ValidationDuration { get; set; }
        public DateTime ValidatedAt { get; set; } = DateTime.Now;
    }
}