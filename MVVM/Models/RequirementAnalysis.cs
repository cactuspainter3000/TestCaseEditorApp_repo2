using System;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents an LLM-powered quality analysis of a requirement.
    /// Contains structured feedback (score, issues, recommendations) plus freeform commentary.
    /// </summary>
    public class RequirementAnalysis
    {
        /// <summary>
        /// Overall quality score from 1-10 (10 being excellent).
        /// </summary>
        public int QualityScore { get; set; }

        /// <summary>
        /// Self-reported hallucination check from the LLM.
        /// "NO_FABRICATION" if only original requirement terms were used,
        /// "FABRICATED_DETAILS" if new technical details were added.
        /// </summary>
        public string? HallucinationCheck { get; set; }

        /// <summary>
        /// Identified issues with the requirement (ambiguity, testability problems, scope issues, etc.)
        /// </summary>
        public List<AnalysisIssue> Issues { get; set; } = new List<AnalysisIssue>();

        /// <summary>
        /// Specific actionable recommendations for improving the requirement.
        /// </summary>
        public List<AnalysisRecommendation> Recommendations { get; set; } = new List<AnalysisRecommendation>();

        /// <summary>
        /// Freeform text feedback from the LLM providing additional context and insights.
        /// </summary>
        public string? FreeformFeedback { get; set; }

        /// <summary>
        /// Complete rewritten requirement that addresses all identified issues.
        /// This is the main deliverable - the improved requirement text that should be used.
        /// </summary>
        public string? ImprovedRequirement { get; set; }

        /// <summary>
        /// When this analysis was generated.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether analysis has been performed (vs. pending or failed).
        /// </summary>
        public bool IsAnalyzed { get; set; }

        /// <summary>
        /// Error message if analysis failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// A specific issue identified in the requirement.
    /// </summary>
    public class AnalysisIssue
    {
        /// <summary>
        /// Category of issue (e.g., "Testability", "Clarity", "Scope", "Completeness")
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Severity level: "Low", "Medium", "High"
        /// </summary>
        public string Severity { get; set; } = "Medium";

        /// <summary>
        /// Description of the issue.
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// A specific recommendation for improving the requirement.
    /// </summary>
    public class AnalysisRecommendation
    {
        /// <summary>
        /// Category of recommendation (e.g., "Clarity", "Testability", "Structure")
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// The actionable recommendation.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Complete suggested rewrite of the requirement that incorporates 
        /// details from supplemental information directly into the requirement text.
        /// This is the actual improved requirement text the user should use.
        /// </summary>
        public string? SuggestedEdit { get; set; }
    }
}
