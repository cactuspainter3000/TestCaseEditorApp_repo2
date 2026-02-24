using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services; // For RequirementAnalysisCache

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Interface for requirement analysis services that analyze requirement quality using LLM.
    /// Provides structured analysis with quality scores, issues, and recommendations.
    /// Moved from TestCaseGeneration domain to Requirements domain for architectural independence.
    /// </summary>
    public interface IRequirementAnalysisService
    {
        /// <summary>
        /// Enable/disable self-reflection feature. When enabled, the LLM will review its own responses for quality.
        /// </summary>
        bool EnableSelfReflection { get; set; }

        /// <summary>
        /// Enable/disable caching of analysis results. When enabled, identical requirement content will use cached results.
        /// </summary>
        bool EnableCaching { get; set; }

        /// <summary>
        /// Enable/disable thread cleanup after analysis. When enabled, analysis threads are deleted after completion.
        /// </summary>
        bool EnableThreadCleanup { get; set; }

        /// <summary>
        /// Timeout for LLM analysis operations. Default is 90 seconds to allow for RAG processing.
        /// </summary>
        TimeSpan AnalysisTimeout { get; set; }

        /// <summary>
        /// Current health status of the LLM service (null if no health monitor configured)
        /// </summary>
        LlmServiceHealthMonitor.HealthReport? ServiceHealth { get; }

        /// <summary>
        /// Whether the service is currently using fallback mode
        /// </summary>
        bool IsUsingFallback { get; }

        /// <summary>
        /// Current cache statistics (null if no cache configured)
        /// </summary>
        RequirementAnalysisCache.CacheStatistics? CacheStatistics { get; }

        /// <summary>
        /// Sets the workspace context for project-specific analysis
        /// </summary>
        /// <param name="workspaceName">Name of the project workspace to use for analysis</param>
        void SetWorkspaceContext(string? workspaceName);

        /// <summary>
        /// Analyzes a requirement for quality issues and generates structured analysis.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <param name="cancellationToken">Token to cancel the analysis operation</param>
        /// <returns>Structured analysis with quality score, issues, and recommendations</returns>
        Task<RequirementAnalysis> AnalyzeRequirementAsync(Requirement requirement, CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyzes a requirement with streaming support for real-time feedback and timeout enforcement.
        /// This method includes 90-second timeout protection to prevent indefinite hangs.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <param name="onPartialResult">Callback for partial LLM response chunks</param>
        /// <param name="onProgressUpdate">Callback for progress status updates</param>
        /// <param name="cancellationToken">Token to cancel the analysis operation</param>
        /// <returns>Structured analysis with quality score, issues, and recommendations</returns>
        Task<RequirementAnalysis> AnalyzeRequirementWithStreamingAsync(
            Requirement requirement,
            Action<string>? onPartialResult = null,
            Action<string>? onProgressUpdate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a prompt for inspection purposes.
        /// </summary>
        /// <param name="requirement">The requirement to generate prompt for</param>
        /// <returns>The generated prompt string</returns>
        string GeneratePromptForInspection(Requirement requirement);

        /// <summary>
        /// Gets detailed health information for the analysis service.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Detailed health report or null if not available</returns>
        Task<LlmServiceHealthMonitor.HealthReport?> GetDetailedHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates cached analysis for a specific requirement.
        /// </summary>
        /// <param name="requirementGlobalId">The global ID of the requirement to invalidate</param>
        void InvalidateCache(string requirementGlobalId);

        /// <summary>
        /// Clears all cached analysis data.
        /// </summary>
        void ClearAnalysisCache();

        // =====================================================
        // TASK 4.4: ENHANCED DERIVATION ANALYSIS CAPABILITIES
        // =====================================================

        /// <summary>
        /// Analyzes a requirement for ATP (Automated Test Procedure) content and derives system capabilities.
        /// Integrates with the systematic capability derivation service for comprehensive analysis.
        /// </summary>
        /// <param name="requirement">The requirement to analyze for ATP content</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Derivation analysis result with detected capabilities and gap analysis</returns>
        Task<RequirementDerivationAnalysis> AnalyzeRequirementDerivationAsync(Requirement requirement, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs comprehensive gap analysis between derived capabilities and existing requirements.
        /// Uses the RequirementGapAnalyzer for multi-dimensional comparison.
        /// </summary>
        /// <param name="derivedCapabilities">List of capabilities derived from ATP analysis</param>
        /// <param name="existingRequirements">Current requirements to compare against</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Gap analysis results with identified gaps, overlaps, and recommendations</returns>
        Task<RequirementGapAnalysisResult> AnalyzeRequirementGapAsync(
            IEnumerable<DerivedCapability> derivedCapabilities, 
            IEnumerable<Requirement> existingRequirements, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates testing workflows end-to-end using derived capabilities and gap analysis.
        /// Provides comprehensive testing workflow validation for enhanced quality assurance.
        /// </summary>
        /// <param name="requirements">Requirements to validate testing workflows for</param>
        /// <param name="testingContext">Optional context for testing validation</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Testing workflow validation result with recommendations</returns>
        Task<TestingWorkflowValidationResult> ValidateTestingWorkflowAsync(
            IEnumerable<Requirement> requirements, 
            TestingValidationContext? testingContext = null, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs batch analysis of multiple requirements for derivation capabilities.
        /// Optimized for processing large sets of requirements efficiently.
        /// </summary>
        /// <param name="requirements">Collection of requirements to analyze</param>
        /// <param name="batchOptions">Options for batch processing</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Collection of derivation analysis results</returns>
        Task<IEnumerable<RequirementDerivationAnalysis>> AnalyzeBatchDerivationAsync(
            IEnumerable<Requirement> requirements,
            BatchAnalysisOptions? batchOptions = null,
            Action<BatchAnalysisProgress>? onProgress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of requirement derivation analysis including ATP detection and capability derivation.
    /// </summary>
    public class RequirementDerivationAnalysis
    {
        /// <summary>
        /// The original requirement that was analyzed
        /// </summary>
        public required Requirement AnalyzedRequirement { get; set; }

        /// <summary>
        /// Whether ATP content was detected in the requirement
        /// </summary>
        public bool HasATPContent { get; set; }

        /// <summary>
        /// Confidence score for ATP detection (0.0 - 1.0)
        /// </summary>
        public double ATPDetectionConfidence { get; set; }

        /// <summary>
        /// List of capabilities derived from the requirement
        /// </summary>
        public List<DerivedCapability> DerivedCapabilities { get; set; } = new();

        /// <summary>
        /// Quality score for the derivation process (0.0 - 1.0)
        /// </summary>
        public double DerivationQuality { get; set; }

        /// <summary>
        /// Any issues found during derivation analysis
        /// </summary>
        public List<string> DerivationIssues { get; set; } = new();

        /// <summary>
        /// Recommendations for improving requirement derivation
        /// </summary>
        public List<string> Recommendations { get; set; } = new();

        /// <summary>
        /// Timestamp of the analysis
        /// </summary>
        public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Result of testing workflow validation analysis.
    /// </summary>
    public class TestingWorkflowValidationResult
    {
        /// <summary>
        /// Overall validation score (0.0 - 1.0)
        /// </summary>
        public double OverallScore { get; set; }

        /// <summary>
        /// Whether the testing workflow is considered valid
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Validation issues found
        /// </summary>
        public List<ValidationIssue> Issues { get; set; } = new();

        /// <summary>
        /// Recommendations for improving testing workflow
        /// </summary>
        public List<string> Recommendations { get; set; } = new();

        /// <summary>
        /// Testing coverage analysis
        /// </summary>
        public TestingCoverageAnalysis? CoverageAnalysis { get; set; }

        /// <summary>
        /// Timestamp of validation
        /// </summary>
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Context for testing validation operations.
    /// </summary>
    public class TestingValidationContext
    {
        /// <summary>
        /// Test environment context
        /// </summary>
        public string? TestEnvironment { get; set; }

        /// <summary>
        /// Testing methodology to validate against
        /// </summary>
        public string? TestingMethodology { get; set; }

        /// <summary>
        /// Additional validation options
        /// </summary>
        public Dictionary<string, object> ValidationOptions { get; set; } = new();
    }

    /// <summary>
    /// Options for batch analysis operations.
    /// </summary>
    public class BatchAnalysisOptions
    {
        /// <summary>
        /// Maximum number of concurrent analyses
        /// </summary>
        public int MaxConcurrency { get; set; } = 5;

        /// <summary>
        /// Whether to continue on individual failures
        /// </summary>
        public bool ContinueOnFailure { get; set; } = true;

        /// <summary>
        /// Timeout for individual analysis operations
        /// </summary>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromMinutes(2);
    }

    /// <summary>
    /// Progress information for batch analysis operations.
    /// </summary>
    public class BatchAnalysisProgress
    {
        /// <summary>
        /// Total number of requirements to analyze
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Number of completed analyses
        /// </summary>
        public int CompletedCount { get; set; }

        /// <summary>
        /// Number of failed analyses
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Current requirement being processed
        /// </summary>
        public string? CurrentRequirement { get; set; }

        /// <summary>
        /// Progress percentage (0.0 - 1.0)
        /// </summary>
        public double ProgressPercentage => TotalCount > 0 ? (double)CompletedCount / TotalCount : 0.0;
    }

    /// <summary>
    /// Testing coverage analysis result.
    /// </summary>
    public class TestingCoverageAnalysis
    {
        /// <summary>
        /// Percentage of requirements with testing coverage (0.0 - 1.0)
        /// </summary>
        public double CoveragePercentage { get; set; }

        /// <summary>
        /// Requirements without adequate testing coverage
        /// </summary>
        public List<string> UncoveredRequirements { get; set; } = new();

        /// <summary>
        /// Testing gaps identified
        /// </summary>
        public List<string> TestingGaps { get; set; } = new();
    }

    /// <summary>
    /// Validation issue found during testing workflow validation.
    /// </summary>
    public class ValidationIssue
    {
        /// <summary>
        /// Severity of the issue
        /// </summary>
        public ValidationSeverity Severity { get; set; }

        /// <summary>
        /// Description of the issue
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Related requirement ID (if applicable)
        /// </summary>
        public string? RequirementId { get; set; }

        /// <summary>
        /// Category of the validation issue
        /// </summary>
        public string? Category { get; set; }
    }

    /// <summary>
    /// Severity levels for validation issues.
    /// </summary>
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Result of requirement gap analysis integration with RequirementAnalysisService.
    /// </summary>
    public class RequirementGapAnalysisResult
    {
        /// <summary>
        /// Whether the gap analysis was successful
        /// </summary>
        public bool IsSuccessful { get; set; }

        /// <summary>
        /// The actual gap analysis result from the RequirementGapAnalyzer
        /// </summary>
        public GapAnalysisResult? GapAnalysisResult { get; set; }

        /// <summary>
        /// Error message if analysis failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Timestamp of analysis
        /// </summary>
        public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    }
}