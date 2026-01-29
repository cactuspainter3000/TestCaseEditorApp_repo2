using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Automatically optimizes RAG parameters based on feedback and effectiveness analysis.
    /// Implements adaptive learning to improve quality and coverage over time.
    /// </summary>
    public class RAGParameterOptimizer
    {
        private readonly ILogger<RAGParameterOptimizer> _logger;
        private readonly RAGFeedbackService _feedbackService;
        private readonly AnythingLLMService _anythingLLMService;

        // Optimization state per workspace
        private Dictionary<string, WorkspaceOptimizationState> _optimizationState = new();

        // Configuration limits
        private const double MIN_TEMPERATURE = 0.1;
        private const double MAX_TEMPERATURE = 0.7;
        private const double MIN_SIMILARITY_THRESHOLD = 0.0;
        private const double MAX_SIMILARITY_THRESHOLD = 0.5;
        private const int MIN_TOPN = 2;
        private const int MAX_TOPN = 8;

        // Learning rates
        private const double TEMPERATURE_ADJUSTMENT_RATE = 0.05;
        private const double SIMILARITY_ADJUSTMENT_RATE = 0.05;
        private const int TOPN_ADJUSTMENT_STEP = 1;

        // Optimization triggers
        private const int MIN_FEEDBACK_FOR_OPTIMIZATION = 5;
        private const double QUALITY_IMPROVEMENT_THRESHOLD = 10.0; // 10% improvement needed
        private const double QUALITY_DEGRADATION_THRESHOLD = -5.0; // 5% degradation triggers adjustment

        public RAGParameterOptimizer(
            ILogger<RAGParameterOptimizer> logger,
            RAGFeedbackService feedbackService,
            AnythingLLMService anythingLLMService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
        }

        /// <summary>
        /// Analyzes feedback and automatically applies optimal parameter adjustments
        /// </summary>
        public async Task<RAGParameterOptimizationResult> OptimizeParametersAsync(string workspaceSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[RAGOptimizer] Starting parameter optimization for workspace '{Workspace}'", workspaceSlug);

                var result = new RAGParameterOptimizationResult { WorkspaceSlug = workspaceSlug };

                // Initialize workspace state if needed
                if (!_optimizationState.ContainsKey(workspaceSlug))
                {
                    _optimizationState[workspaceSlug] = new WorkspaceOptimizationState { WorkspaceSlug = workspaceSlug };
                }

                var state = _optimizationState[workspaceSlug];

                // Get suggestions from feedback analysis
                var suggestions = _feedbackService.AnalyzeAndSuggestOptimizations(workspaceSlug);
                result.SuggestionsAnalyzed = suggestions.GenerationCount;

                // Need enough data before optimizing
                if (suggestions.GenerationCount < MIN_FEEDBACK_FOR_OPTIMIZATION)
                {
                    result.Status = "Insufficient feedback history";
                    result.Message = $"Need {MIN_FEEDBACK_FOR_OPTIMIZATION} generations to optimize, have {suggestions.GenerationCount}";
                    _logger.LogInformation("[RAGOptimizer] {Message}", result.Message);
                    return result;
                }

                // Get parameter recommendations
                var adjustment = _feedbackService.RecommendParameterAdjustment(workspaceSlug);
                result.AdjustmentRecommended = adjustment.HasChanges;

                if (!adjustment.HasChanges)
                {
                    result.Status = "No adjustments needed";
                    result.Message = "Current parameters are performing well";
                    return result;
                }

                // Apply recommended adjustments
                var previousParams = state.CurrentParameters;
                var newParams = new RAGParameterSnapshot
                {
                    Temperature = adjustment.RecommendedTemperature ?? previousParams.Temperature,
                    SimilarityThreshold = adjustment.RecommendedSimilarityThreshold ?? previousParams.SimilarityThreshold,
                    TopN = adjustment.RecommendedTopN ?? previousParams.TopN,
                    ContextHistory = previousParams.ContextHistory
                };

                // Update in AnythingLLM
                var updateSuccess = await ApplyParameterChangesAsync(workspaceSlug, newParams, cancellationToken);

                if (updateSuccess)
                {
                    state.CurrentParameters = newParams;
                    state.LastOptimizationTime = DateTime.UtcNow;
                    state.OptimizationCount++;

                    result.Status = "Parameters optimized";
                    result.PreviousParameters = previousParams;
                    result.NewParameters = newParams;
                    result.ParametersApplied = true;

                    foreach (var rationale in adjustment.Rationale)
                    {
                        result.Rationale.Add(rationale);
                    }

                    _logger.LogInformation(
                        "[RAGOptimizer] Parameters optimized for workspace '{Workspace}': " +
                        "Temp {OldTemp:F2}→{NewTemp:F2}, Threshold {OldThresh:F2}→{NewThresh:F2}, TopN {OldTopN}→{NewTopN}",
                        workspaceSlug, previousParams.Temperature, newParams.Temperature,
                        previousParams.SimilarityThreshold, newParams.SimilarityThreshold,
                        previousParams.TopN, newParams.TopN);
                }
                else
                {
                    result.Status = "Optimization failed";
                    result.Message = "Could not apply parameter changes to AnythingLLM";
                    _logger.LogWarning("[RAGOptimizer] Failed to apply parameter changes to workspace '{Workspace}'", workspaceSlug);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGOptimizer] Error during parameter optimization for workspace '{Workspace}'", workspaceSlug);
                return new RAGParameterOptimizationResult
                {
                    WorkspaceSlug = workspaceSlug,
                    Status = "Optimization error",
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Checks if optimization should be triggered based on feedback patterns
        /// </summary>
        public bool ShouldTriggerOptimization(string workspaceSlug, int recentGenerationCount = 5)
        {
            try
            {
                if (!_optimizationState.ContainsKey(workspaceSlug))
                {
                    return false;
                }

                var state = _optimizationState[workspaceSlug];

                // Check if enough time has passed since last optimization
                if (state.LastOptimizationTime.HasValue)
                {
                    var timeSinceLastOpt = DateTime.UtcNow - state.LastOptimizationTime.Value;
                    if (timeSinceLastOpt < TimeSpan.FromMinutes(10))
                    {
                        return false; // Too soon
                    }
                }

                // Check feedback trend
                var suggestions = _feedbackService.AnalyzeAndSuggestOptimizations(workspaceSlug);
                
                // Trigger if declining quality or low coverage
                if (suggestions.Recommendations.Any(r => r.Contains("DECLINING") || r.Contains("LOW")))
                {
                    return true;
                }

                // Trigger if we have good feedback to learn from
                if (suggestions.GenerationCount >= MIN_FEEDBACK_FOR_OPTIMIZATION)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGOptimizer] Error checking optimization trigger");
                return false;
            }
        }

        /// <summary>
        /// Gets current optimization state for a workspace
        /// </summary>
        public WorkspaceOptimizationState GetOptimizationState(string workspaceSlug)
        {
            if (!_optimizationState.ContainsKey(workspaceSlug))
            {
                _optimizationState[workspaceSlug] = new WorkspaceOptimizationState { WorkspaceSlug = workspaceSlug };
            }

            return _optimizationState[workspaceSlug];
        }

        /// <summary>
        /// Gets optimization history for a workspace
        /// </summary>
        public List<RAGParameterOptimizationResult> GetOptimizationHistory(string workspaceSlug)
        {
            if (!_optimizationState.ContainsKey(workspaceSlug))
            {
                return new List<RAGParameterOptimizationResult>();
            }

            return _optimizationState[workspaceSlug].OptimizationHistory;
        }

        /// <summary>
        /// Resets optimization state for a workspace
        /// </summary>
        public void ResetOptimizationState(string workspaceSlug)
        {
            if (_optimizationState.ContainsKey(workspaceSlug))
            {
                _optimizationState[workspaceSlug] = new WorkspaceOptimizationState { WorkspaceSlug = workspaceSlug };
                _logger.LogInformation("[RAGOptimizer] Optimization state reset for workspace '{Workspace}'", workspaceSlug);
            }
        }

        /// <summary>
        /// Gets optimization statistics
        /// </summary>
        public RAGOptimizationStatistics GetStatistics()
        {
            var stats = new RAGOptimizationStatistics();

            foreach (var state in _optimizationState.Values)
            {
                stats.TotalWorkspacesOptimized++;
                stats.TotalOptimizationAttempts += state.OptimizationCount;
                
                var successfulOpts = state.OptimizationHistory.Count(h => h.ParametersApplied);
                stats.SuccessfulOptimizations += successfulOpts;

                if (state.OptimizationHistory.Any())
                {
                    var lastOpt = state.OptimizationHistory.Last();
                    if (lastOpt.PreviousParameters != null && lastOpt.NewParameters != null)
                    {
                        if (lastOpt.PreviousParameters.Temperature != lastOpt.NewParameters.Temperature)
                            stats.TemperatureAdjustments++;
                        if (lastOpt.PreviousParameters.SimilarityThreshold != lastOpt.NewParameters.SimilarityThreshold)
                            stats.SimilarityThresholdAdjustments++;
                        if (lastOpt.PreviousParameters.TopN != lastOpt.NewParameters.TopN)
                            stats.TopNAdjustments++;
                    }
                }
            }

            stats.SuccessRate = stats.TotalOptimizationAttempts > 0
                ? (double)stats.SuccessfulOptimizations / stats.TotalOptimizationAttempts * 100
                : 0;

            return stats;
        }

        // Private helper methods

        private async Task<bool> ApplyParameterChangesAsync(string workspaceSlug, RAGParameterSnapshot newParams, CancellationToken cancellationToken)
        {
            try
            {
                // Update AnythingLLM workspace settings
                var success = await _anythingLLMService.UpdateWorkspaceParametersAsync(
                    workspaceSlug,
                    newParams.Temperature,
                    newParams.SimilarityThreshold,
                    newParams.TopN,
                    cancellationToken);

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGOptimizer] Error applying parameter changes");
                return false;
            }
        }
    }

    /// <summary>
    /// Current optimization state for a workspace
    /// </summary>
    public class WorkspaceOptimizationState
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public RAGParameterSnapshot CurrentParameters { get; set; } = new();
        public DateTime? LastOptimizationTime { get; set; }
        public int OptimizationCount { get; set; }
        public List<RAGParameterOptimizationResult> OptimizationHistory { get; set; } = new();
    }

    /// <summary>
    /// Result of a parameter optimization operation
    /// </summary>
    public class RAGParameterOptimizationResult
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int SuggestionsAnalyzed { get; set; }
        public bool AdjustmentRecommended { get; set; }
        public bool ParametersApplied { get; set; }
        public RAGParameterSnapshot? PreviousParameters { get; set; }
        public RAGParameterSnapshot? NewParameters { get; set; }
        public List<string> Rationale { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Overall optimization statistics
    /// </summary>
    public class RAGOptimizationStatistics
    {
        public int TotalWorkspacesOptimized { get; set; }
        public int TotalOptimizationAttempts { get; set; }
        public int SuccessfulOptimizations { get; set; }
        public double SuccessRate { get; set; }
        public int TemperatureAdjustments { get; set; }
        public int SimilarityThresholdAdjustments { get; set; }
        public int TopNAdjustments { get; set; }
    }
}
