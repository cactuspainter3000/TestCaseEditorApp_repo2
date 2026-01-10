using System;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Model for LLM learning feedback data
    /// Contains original LLM output and user improvements for training
    /// </summary>
    public class LLMLearningFeedback
    {
        /// <summary>
        /// Original requirement text that was analyzed
        /// </summary>
        public string OriginalRequirement { get; set; } = string.Empty;

        /// <summary>
        /// LLM-generated rewrite of the requirement
        /// </summary>
        public string LLMGeneratedRewrite { get; set; } = string.Empty;

        /// <summary>
        /// User-improved version of the LLM rewrite
        /// </summary>
        public string UserImprovedVersion { get; set; } = string.Empty;

        /// <summary>
        /// Percentage of changes made by user (100 - similarity)
        /// </summary>
        public double ChangePercentage { get; set; }

        /// <summary>
        /// User feedback category or type of improvement
        /// </summary>
        public string? FeedbackCategory { get; set; }

        /// <summary>
        /// Context about where the edit occurred (e.g., "requirement rewrite", "analysis suggestion")
        /// </summary>
        public string? Context { get; set; }

        /// <summary>
        /// Optional user comments about why they made changes
        /// </summary>
        public string? UserComments { get; set; }

        /// <summary>
        /// Timestamp when feedback was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Requirement ID for correlation
        /// </summary>
        public string? RequirementId { get; set; }
    }
}