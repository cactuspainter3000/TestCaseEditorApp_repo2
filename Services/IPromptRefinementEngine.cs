using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for intelligent prompt refinement and optimization system.
    /// Analyzes prompt performance, identifies improvements, and automatically generates refined variants.
    /// </summary>
    public interface IPromptRefinementEngine
    {
        /// <summary>
        /// Analyzes the performance of a managed prompt and identifies areas for improvement
        /// </summary>
        /// <param name="promptId">ID of the prompt to analyze</param>
        /// <param name="analysisWindow">Time period to analyze (defaults to last 30 days)</param>
        /// <returns>Analysis result with identified issues and suggested improvements</returns>
        Task<PromptAnalysisResult> AnalyzePromptPerformanceAsync(
            string promptId, 
            TimeSpan? analysisWindow = null);

        /// <summary>
        /// Generates refined variants of a prompt based on performance analysis
        /// </summary>
        /// <param name="promptId">ID of the prompt to refine</param>
        /// <param name="options">Configuration options for refinement</param>
        /// <returns>Refinement result with generated prompt variants</returns>
        Task<PromptRefinementResult> RefinePromptAsync(
            string promptId, 
            PromptRefinementOptions? options = null);

        /// <summary>
        /// Compares performance between multiple prompts to identify the best performers
        /// </summary>
        /// <param name="promptIds">IDs of prompts to compare</param>
        /// <param name="comparisonPeriod">Time period for comparison</param>
        /// <returns>Detailed performance comparison with recommendations</returns>
        Task<PromptPerformanceComparison> ComparePromptPerformanceAsync(
            IEnumerable<string> promptIds, 
            TimeSpan comparisonPeriod);

        /// <summary>
        /// Sets up A/B testing for prompt variants to determine optimal performance
        /// </summary>
        /// <param name="promptVariants">Prompt variants to test</param>
        /// <param name="testConfiguration">A/B test configuration</param>
        /// <returns>A/B test session ID for tracking results</returns>
        Task<string> StartABTestAsync(
            IEnumerable<ManagedPrompt> promptVariants, 
            ABTestConfiguration testConfiguration);

        /// <summary>
        /// Retrieves results from an active or completed A/B test
        /// </summary>
        /// <param name="abTestId">ID of the A/B test session</param>
        /// <returns>Current A/B test results and recommendations</returns>
        Task<PromptPerformanceComparison> GetABTestResultsAsync(string abTestId);

        /// <summary>
        /// Records the outcome of using a specific prompt for performance tracking
        /// </summary>
        /// <param name="promptId">ID of the prompt that was used</param>
        /// <param name="qualityScore">Quality score achieved</param>
        /// <param name="processingTime">Time taken to process</param>
        /// <param name="validationApproved">Whether validation approved the result</param>
        /// <param name="contextMetadata">Additional context about the usage</param>
        /// <returns>Task representing the recording operation</returns>
        Task RecordPromptUsageAsync(
            string promptId,
            double qualityScore,
            TimeSpan processingTime,
            bool? validationApproved = null,
            Dictionary<string, object>? contextMetadata = null);

        /// <summary>
        /// Gets real-time performance monitoring data for active prompts
        /// </summary>
        /// <param name="promptIds">Specific prompts to monitor (optional - all active if null)</param>
        /// <returns>Current performance monitoring data</returns>
        Task<PromptPerformanceMonitor> GetPerformanceMonitorAsync(IEnumerable<string>? promptIds = null);

        /// <summary>
        /// Automatically identifies prompts that need refinement based on performance thresholds
        /// </summary>
        /// <param name="options">Configuration for automatic analysis</param>
        /// <returns>List of prompts that need attention with analysis results</returns>
        Task<List<PromptAnalysisResult>> IdentifyPromptsNeedingRefinementAsync(
            PromptRefinementOptions? options = null);

        /// <summary>
        /// Executes the complete refinement loop: analyze, refine, and test
        /// </summary>
        /// <param name="promptId">ID of the prompt to process through the refinement loop</param>
        /// <param name="options">Configuration options</param>
        /// <returns>Complete refinement loop result</returns>
        Task<PromptRefinementLoopResult> ExecuteRefinementLoopAsync(
            string promptId, 
            PromptRefinementOptions? options = null);

        /// <summary>
        /// Gets all managed prompts with their current performance status
        /// </summary>
        /// <param name="includeArchived">Whether to include archived prompts</param>
        /// <returns>List of all managed prompts</returns>
        Task<List<ManagedPrompt>> GetAllManagedPromptsAsync(bool includeArchived = false);

        /// <summary>
        /// Creates or updates a managed prompt in the system
        /// </summary>
        /// <param name="prompt">Prompt to create or update</param>
        /// <returns>Updated prompt with system-generated IDs and metadata</returns>
        Task<ManagedPrompt> SaveManagedPromptAsync(ManagedPrompt prompt);

        /// <summary>
        /// Retrieves a specific managed prompt by ID
        /// </summary>
        /// <param name="promptId">ID of the prompt to retrieve</param>
        /// <returns>Managed prompt if found, null otherwise</returns>
        Task<ManagedPrompt?> GetManagedPromptAsync(string promptId);

        /// <summary>
        /// Archives a prompt (marks it as no longer active)
        /// </summary>
        /// <param name="promptId">ID of the prompt to archive</param>
        /// <param name="reason">Reason for archiving</param>
        /// <returns>Task representing the archival operation</returns>
        Task ArchivePromptAsync(string promptId, string reason);
    }

    /// <summary>
    /// Result of executing the complete prompt refinement loop
    /// </summary>
    public class PromptRefinementLoopResult
    {
        /// <summary>
        /// Unique identifier for this refinement loop execution
        /// </summary>
        public string LoopExecutionId { get; set; } = string.Empty;

        /// <summary>
        /// When the loop started
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// When the loop completed
        /// </summary>
        public DateTime CompletedAt { get; set; }

        /// <summary>
        /// Total processing time for the loop
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt - StartedAt;

        /// <summary>
        /// Original prompt that was processed
        /// </summary>
        public ManagedPrompt OriginalPrompt { get; set; } = new ManagedPrompt();

        /// <summary>
        /// Analysis result from performance evaluation
        /// </summary>
        public PromptAnalysisResult AnalysisResult { get; set; } = new PromptAnalysisResult();

        /// <summary>
        /// Refinement result with generated variants
        /// </summary>
        public PromptRefinementResult RefinementResult { get; set; } = new PromptRefinementResult();

        /// <summary>
        /// A/B test session ID if testing was initiated
        /// </summary>
        public string ABTestSessionId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the loop execution was successful
        /// </summary>
        public bool WasSuccessful { get; set; }

        /// <summary>
        /// Any errors or issues encountered during the loop
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Actions taken during the loop execution
        /// </summary>
        public List<string> ActionsTaken { get; set; } = new List<string>();

        /// <summary>
        /// Recommendations for next steps
        /// </summary>
        public List<string> NextStepsRecommendations { get; set; } = new List<string>();
    }
}