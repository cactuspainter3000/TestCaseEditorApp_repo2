using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Integrates RAG feedback collection into the test case generation workflow.
    /// Automatically collects quality feedback and triggers parameter optimization.
    /// </summary>
    public class RAGFeedbackIntegrationService
    {
        private readonly ILogger<RAGFeedbackIntegrationService> _logger;
        private readonly RAGFeedbackService _feedbackService;
        private readonly RAGParameterOptimizer _parameterOptimizer;
        private readonly RAGContextService _ragContextService;

        // Configuration
        private const string TEST_CASE_GENERATION_WORKSPACE = "test-case-generation";
        private const int OPTIMIZATION_TRIGGER_INTERVAL = 5; // Optimize after N generations

        public RAGFeedbackIntegrationService(
            ILogger<RAGFeedbackIntegrationService> logger,
            RAGFeedbackService feedbackService,
            RAGParameterOptimizer parameterOptimizer,
            RAGContextService ragContextService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
            _parameterOptimizer = parameterOptimizer ?? throw new ArgumentNullException(nameof(parameterOptimizer));
            _ragContextService = ragContextService ?? throw new ArgumentNullException(nameof(ragContextService));
        }

        /// <summary>
        /// Collects feedback on a test case generation batch and triggers optimization if needed
        /// </summary>
        public async Task CollectGenerationFeedbackAsync(
            string workspaceSlug,
            List<LLMTestCase> generatedTestCases,
            List<Requirement> requirements,
            List<string> usedDocuments = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Calculate quality score based on generation success
                var qualityScore = CalculateQualityScore(generatedTestCases, requirements);

                _logger.LogInformation(
                    "[RAGFeedbackIntegration] Collecting feedback: Workspace={Workspace}, " +
                    "Quality={Quality:F1}%, TestCases={TestCaseCount}, Requirements={ReqCount}",
                    workspaceSlug, qualityScore, generatedTestCases?.Count ?? 0, requirements.Count);

                // Get current RAG parameters
                var currentParams = new RAGParameterSnapshot
                {
                    Temperature = 0.3,          // TODO: Get from AnythingLLM workspace settings
                    SimilarityThreshold = 0,    // TODO: Get from AnythingLLM workspace settings
                    TopN = 4                    // TODO: Get from AnythingLLM workspace settings
                };

                // Record the feedback
                _feedbackService.RecordGenerationFeedback(
                    workspaceSlug,
                    generatedTestCases,
                    requirements.Count,
                    qualityScore,
                    usedDocuments,
                    currentParams);

                // Record configuration for tracking
                _feedbackService.RecordParameterConfiguration(workspaceSlug, currentParams);

                // Check if optimization should be triggered
                if (_parameterOptimizer.ShouldTriggerOptimization(workspaceSlug))
                {
                    _logger.LogInformation("[RAGFeedbackIntegration] Optimization triggered for workspace '{Workspace}'", workspaceSlug);
                    
                    // Run optimization asynchronously (don't block test case generation)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await _parameterOptimizer.OptimizeParametersAsync(workspaceSlug, cancellationToken);
                            
                            if (result.ParametersApplied)
                            {
                                _logger.LogInformation(
                                    "[RAGFeedbackIntegration] Optimization applied: {Result}",
                                    $"Temp {result.PreviousParameters.Temperature:F2}→{result.NewParameters.Temperature:F2}, " +
                                    $"Threshold {result.PreviousParameters.SimilarityThreshold:F2}→{result.NewParameters.SimilarityThreshold:F2}, " +
                                    $"TopN {result.PreviousParameters.TopN}→{result.NewParameters.TopN}");
                            }
                            else
                            {
                                _logger.LogDebug("[RAGFeedbackIntegration] Optimization completed: {Status}", result.Status);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[RAGFeedbackIntegration] Error running optimization");
                        }
                    });
                }

                // Log feedback analytics
                var analytics = _feedbackService.ExportFeedbackAnalytics(workspaceSlug);
                _logger.LogDebug("[RAGFeedbackIntegration] Feedback analytics:\n{Analytics}", analytics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGFeedbackIntegration] Error collecting generation feedback");
            }
        }

        /// <summary>
        /// Gets current optimization insights for a workspace
        /// </summary>
        public RAGFeedbackInsights GetFeedbackInsights(string workspaceSlug)
        {
            try
            {
                var insights = new RAGFeedbackInsights { WorkspaceSlug = workspaceSlug };

                // Get suggestions
                var suggestions = _feedbackService.AnalyzeAndSuggestOptimizations(workspaceSlug);
                insights.GenerationCount = suggestions.GenerationCount;
                insights.AverageQualityScore = suggestions.AverageQualityScore;
                insights.Recommendations.AddRange(suggestions.Recommendations);

                // Get document ranking
                var docRanking = _feedbackService.GetDocumentRanking(workspaceSlug, 5);
                foreach (var doc in docRanking)
                {
                    insights.TopDocuments.Add(new DocumentInsight
                    {
                        DocumentName = doc.DocumentName,
                        Relevance = doc.AverageQualityScore,
                        UsageCount = doc.UsageCount
                    });
                }

                // Get optimization state
                var optState = _parameterOptimizer.GetOptimizationState(workspaceSlug);
                insights.CurrentParameters = optState.CurrentParameters;
                insights.OptimizationCount = optState.OptimizationCount;
                insights.LastOptimizationTime = optState.LastOptimizationTime;

                // Get optimization statistics
                var stats = _parameterOptimizer.GetStatistics();
                insights.TotalOptimizations = stats.SuccessfulOptimizations;
                insights.OptimizationSuccessRate = stats.SuccessRate;

                return insights;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGFeedbackIntegration] Error getting feedback insights");
                return new RAGFeedbackInsights { WorkspaceSlug = workspaceSlug };
            }
        }

        /// <summary>
        /// Gets parameter adjustment recommendations without applying them
        /// </summary>
        public RAGParameterAdjustment GetOptimizationRecommendations(string workspaceSlug)
        {
            return _feedbackService.RecommendParameterAdjustment(workspaceSlug);
        }

        /// <summary>
        /// Manually triggers optimization (for testing or user-initiated)
        /// </summary>
        public async Task<RAGParameterOptimizationResult> TriggerManualOptimizationAsync(
            string workspaceSlug,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[RAGFeedbackIntegration] Manual optimization triggered for workspace '{Workspace}'", workspaceSlug);
            return await _parameterOptimizer.OptimizeParametersAsync(workspaceSlug, cancellationToken);
        }

        /// <summary>
        /// Resets feedback and optimization state (for testing or reset)
        /// </summary>
        public void ResetOptimizationState(string workspaceSlug)
        {
            _feedbackService.ClearFeedback();
            _parameterOptimizer.ResetOptimizationState(workspaceSlug);
            _logger.LogInformation("[RAGFeedbackIntegration] Optimization state reset for workspace '{Workspace}'", workspaceSlug);
        }

        /// <summary>
        /// Gets comprehensive feedback report for a workspace
        /// </summary>
        public string GenerateFeedbackReport(string workspaceSlug)
        {
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("RAG FEEDBACK & OPTIMIZATION REPORT");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine();

            // Feedback analytics
            var feedbackAnalytics = _feedbackService.ExportFeedbackAnalytics(workspaceSlug);
            sb.AppendLine(feedbackAnalytics);
            sb.AppendLine();

            // Insights
            var insights = GetFeedbackInsights(workspaceSlug);
            sb.AppendLine("--- OPTIMIZATION STATE ---");
            sb.AppendLine($"Current Parameters: {insights.CurrentParameters}");
            sb.AppendLine($"Optimizations Applied: {insights.OptimizationCount}");
            if (insights.LastOptimizationTime.HasValue)
            {
                sb.AppendLine($"Last Optimization: {insights.LastOptimizationTime:G}");
            }
            sb.AppendLine($"Success Rate: {insights.OptimizationSuccessRate:F1}%");
            sb.AppendLine();

            // Top documents
            if (insights.TopDocuments.Any())
            {
                sb.AppendLine("--- TOP PERFORMING DOCUMENTS ---");
                foreach (var doc in insights.TopDocuments)
                {
                    sb.AppendLine($"  {doc.DocumentName}: {doc.Relevance:F1}% relevance ({doc.UsageCount} uses)");
                }
                sb.AppendLine();
            }

            // Recommendations
            if (insights.Recommendations.Any())
            {
                sb.AppendLine("--- RECOMMENDATIONS ---");
                foreach (var rec in insights.Recommendations)
                {
                    sb.AppendLine($"  • {rec}");
                }
            }

            return sb.ToString();
        }

        // Private helper methods

        private double CalculateQualityScore(List<LLMTestCase> testCases, List<Requirement> requirements)
        {
            // Basic quality calculation (can be enhanced with more sophisticated metrics)
            if (!requirements.Any())
                return 0;

            if (!testCases.Any())
                return 0; // No test cases = poor quality

            // Quality factors:
            // 1. Coverage: How many requirements are covered
            var coveredRequirements = new HashSet<string>();
            foreach (var testCase in testCases)
            {
                // Try to extract requirement numbers from test case ID or name
                // Simple heuristic: check if requirement Item appears in test case
                foreach (var req in requirements)
                {
                    if (!string.IsNullOrEmpty(req.Item) &&
                        (testCase.Id.Contains(req.Item) || testCase.Description.Contains(req.Item)))
                    {
                        coveredRequirements.Add(req.Item);
                    }
                }
            }

            var coverageScore = coveredRequirements.Count / (double)requirements.Count * 50; // 50% weight

            // 2. Test case count: More test cases = higher quality (up to a point)
            var testCaseScore = Math.Min(testCases.Count, requirements.Count * 2) / (double)(requirements.Count * 2) * 30; // 30% weight

            // 3. Completeness: Each test case has required fields
            var completeCount = testCases.Count(tc =>
                !string.IsNullOrEmpty(tc.Id) &&
                !string.IsNullOrEmpty(tc.Description) &&
                !string.IsNullOrEmpty(tc.ExpectedResult));

            var completenessScore = completeCount / (double)testCases.Count * 20; // 20% weight

            var totalScore = Math.Min(coverageScore + testCaseScore + completenessScore, 100);

            _logger.LogDebug(
                "[RAGFeedbackIntegration] Quality calculation: Coverage={Coverage:F1}%, TestCases={TestCases:F1}%, Completeness={Completeness:F1}%, Total={Total:F1}%",
                coverageScore, testCaseScore, completenessScore, totalScore);

            return totalScore;
        }
    }

    /// <summary>
    /// Feedback insights for UI display
    /// </summary>
    public class RAGFeedbackInsights
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public int GenerationCount { get; set; }
        public double AverageQualityScore { get; set; }
        public List<string> Recommendations { get; set; } = new();
        public List<DocumentInsight> TopDocuments { get; set; } = new();
        public RAGParameterSnapshot CurrentParameters { get; set; } = new();
        public int OptimizationCount { get; set; }
        public DateTime? LastOptimizationTime { get; set; }
        public int TotalOptimizations { get; set; }
        public double OptimizationSuccessRate { get; set; }
    }

    /// <summary>
    /// Document insight for display
    /// </summary>
    public class DocumentInsight
    {
        public string DocumentName { get; set; } = string.Empty;
        public double Relevance { get; set; }
        public int UsageCount { get; set; }
    }
}
