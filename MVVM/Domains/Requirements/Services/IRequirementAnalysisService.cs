using System;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services; // For health monitor and cache classes

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
    }
}