using System;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Core analysis engine for requirement quality assessment.
    /// Orchestrates the complete analysis workflow while maintaining clean separation of concerns.
    /// This service extracts business logic from the monolithic AnalysisVM following MVVM principles.
    /// </summary>
    public interface IRequirementAnalysisEngine
    {
        /// <summary>
        /// Analyzes a requirement using the configured LLM service and returns comprehensive results.
        /// This method encapsulates the complete analysis workflow including caching, health monitoring, and result processing.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <param name="progressCallback">Optional callback for progress updates during analysis</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        /// <returns>Complete analysis results with quality scores, issues, and recommendations</returns>
        Task<RequirementAnalysis> AnalyzeRequirementAsync(
            Requirement requirement, 
            Action<string>? progressCallback = null, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates that the analysis engine is properly configured and operational.
        /// Checks LLM connectivity, cache availability, and other health indicators.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        /// <returns>True if engine is ready for analysis, false otherwise</returns>
        Task<bool> ValidateEngineHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets current performance metrics and health status for the analysis engine.
        /// Useful for monitoring, troubleshooting, and user feedback.
        /// </summary>
        /// <returns>Engine status including cache performance, LLM health, and recent metrics</returns>
        AnalysisEngineStatus GetEngineStatus();

        /// <summary>
        /// Generates a diagnostic prompt that shows exactly what will be sent to the LLM.
        /// Useful for debugging analysis issues and understanding prompt construction.
        /// </summary>
        /// <param name="requirement">The requirement to generate diagnostic prompt for</param>
        /// <returns>Complete prompt text that would be sent to the LLM</returns>
        string GeneratePromptForInspection(Requirement requirement);
    }

    /// <summary>
    /// Status information for the analysis engine including health and performance metrics.
    /// </summary>
    public class AnalysisEngineStatus
    {
        /// <summary>
        /// Whether the engine is ready to perform analysis.
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Human-readable status message describing current engine state.
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// Cache performance metrics (hit rate, entry count, etc.)
        /// </summary>
        public string? CacheStatistics { get; set; }

        /// <summary>
        /// LLM service health status and connectivity information.
        /// </summary>
        public string? LLMHealthStatus { get; set; }

        /// <summary>
        /// Recent performance metrics (average response time, success rate, etc.)
        /// </summary>
        public string? PerformanceMetrics { get; set; }

        /// <summary>
        /// When this status was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}