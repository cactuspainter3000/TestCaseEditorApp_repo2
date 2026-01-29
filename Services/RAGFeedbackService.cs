using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Tracks and analyzes RAG effectiveness feedback to optimize parameters.
    /// Learns which documents and settings work best for different requirement types.
    /// </summary>
    public class RAGFeedbackService
    {
        private readonly ILogger<RAGFeedbackService> _logger;
        private readonly RAGContextService _ragContextService;

        // Feedback history
        private List<RAGGenerationFeedback> _feedbackHistory = new();
        
        // Performance correlation tracking
        private Dictionary<string, DocumentEffectiveness> _documentEffectiveness = new();
        private Dictionary<string, ParameterEffectiveness> _parameterEffectiveness = new();

        // Configuration tracking
        private Dictionary<string, WorkspaceConfigHistory> _configHistory = new();

        public RAGFeedbackService(
            ILogger<RAGFeedbackService> logger,
            RAGContextService ragContextService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ragContextService = ragContextService ?? throw new ArgumentNullException(nameof(ragContextService));
        }

        /// <summary>
        /// Records feedback on a test case generation batch
        /// </summary>
        public void RecordGenerationFeedback(
            string workspaceSlug,
            List<LLMTestCase> generatedTestCases,
            int requirementCount,
            double qualityScore, // 0-100, where 100 is perfect
            List<string> usedDocuments = null,
            RAGParameterSnapshot parameters = null)
        {
            try
            {
                var feedback = new RAGGenerationFeedback
                {
                    Timestamp = DateTime.UtcNow,
                    WorkspaceSlug = workspaceSlug,
                    TestCaseCount = generatedTestCases?.Count ?? 0,
                    RequirementCount = requirementCount,
                    QualityScore = Math.Clamp(qualityScore, 0, 100),
                    CoverageRatio = requirementCount > 0 ? (generatedTestCases?.Count ?? 0) / (double)requirementCount : 0,
                    UsedDocuments = usedDocuments ?? new List<string>(),
                    Parameters = parameters ?? new RAGParameterSnapshot()
                };

                _feedbackHistory.Add(feedback);

                // Update document effectiveness
                if (usedDocuments != null)
                {
                    foreach (var doc in usedDocuments)
                    {
                        if (!_documentEffectiveness.ContainsKey(doc))
                        {
                            _documentEffectiveness[doc] = new DocumentEffectiveness { DocumentName = doc };
                        }

                        var docStats = _documentEffectiveness[doc];
                        docStats.UsageCount++;
                        docStats.TotalQualityScore += qualityScore;
                        docStats.AverageQualityScore = docStats.TotalQualityScore / docStats.UsageCount;

                        // Track impact on coverage
                        docStats.ContributionToCoverage.Add(feedback.CoverageRatio);
                    }
                }

                // Update parameter effectiveness
                if (parameters != null)
                {
                    var paramKey = parameters.GetHash();
                    if (!_parameterEffectiveness.ContainsKey(paramKey))
                    {
                        _parameterEffectiveness[paramKey] = new ParameterEffectiveness 
                        { 
                            ParameterSnapshot = parameters 
                        };
                    }

                    var paramStats = _parameterEffectiveness[paramKey];
                    paramStats.UsageCount++;
                    paramStats.TotalQualityScore += qualityScore;
                    paramStats.AverageQualityScore = paramStats.TotalQualityScore / paramStats.UsageCount;
                }

                _logger.LogInformation(
                    "[RAGFeedback] Generation feedback recorded: Workspace={Workspace}, Quality={Quality:F1}%, Coverage={Coverage:F1}%, Docs={DocCount}",
                    workspaceSlug, qualityScore, feedback.CoverageRatio * 100, usedDocuments?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGFeedback] Error recording generation feedback");
            }
        }

        /// <summary>
        /// Analyzes feedback to identify improvement opportunities
        /// </summary>
        public RAGOptimizationSuggestions AnalyzeAndSuggestOptimizations(string workspaceSlug)
        {
            try
            {
                var suggestions = new RAGOptimizationSuggestions { WorkspaceSlug = workspaceSlug };

                // Get relevant feedback for this workspace
                var workspaceFeedback = _feedbackHistory
                    .Where(f => f.WorkspaceSlug == workspaceSlug)
                    .ToList();

                if (!workspaceFeedback.Any())
                {
                    suggestions.Recommendations.Add("No feedback history yet. Continue generating test cases to build optimization data.");
                    return suggestions;
                }

                // Analyze quality trends
                var avgQuality = workspaceFeedback.Average(f => f.QualityScore);
                suggestions.AverageQualityScore = avgQuality;
                suggestions.GenerationCount = workspaceFeedback.Count;

                AnalyzeQualityTrends(workspaceFeedback, suggestions);
                AnalyzeDocumentEffectiveness(workspaceFeedback, suggestions);
                AnalyzeParameterImpact(workspaceFeedback, suggestions);
                AnalyzeCoveragePatterns(workspaceFeedback, suggestions);

                _logger.LogInformation(
                    "[RAGFeedback] Analysis complete: {WorkspaceSlug}, AvgQuality={Quality:F1}%, Suggestions={Count}",
                    workspaceSlug, avgQuality, suggestions.Recommendations.Count);

                return suggestions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGFeedback] Error analyzing feedback for workspace {Workspace}", workspaceSlug);
                return new RAGOptimizationSuggestions 
                { 
                    WorkspaceSlug = workspaceSlug,
                    Recommendations = new List<string> { $"Error analyzing feedback: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Recommends parameter adjustments based on feedback analysis
        /// </summary>
        public RAGParameterAdjustment RecommendParameterAdjustment(string workspaceSlug)
        {
            try
            {
                var adjustment = new RAGParameterAdjustment { WorkspaceSlug = workspaceSlug };

                var workspaceFeedback = _feedbackHistory
                    .Where(f => f.WorkspaceSlug == workspaceSlug)
                    .OrderByDescending(f => f.Timestamp)
                    .Take(20) // Last 20 generations
                    .ToList();

                if (!workspaceFeedback.Any())
                {
                    adjustment.Status = "No feedback history";
                    return adjustment;
                }

                var avgQuality = workspaceFeedback.Average(f => f.QualityScore);
                var qualityTrend = CalculateTrend(workspaceFeedback.Select(f => f.QualityScore).ToList());
                var avgCoverage = workspaceFeedback.Average(f => f.CoverageRatio);

                // Recommend temperature adjustment
                if (avgQuality < 50)
                {
                    adjustment.RecommendedTemperature = 0.2; // More deterministic
                    adjustment.Rationale.Add("Low quality scores suggest high hallucination. Reduce temperature for more consistent responses.");
                }
                else if (avgQuality > 85 && qualityTrend > 0)
                {
                    adjustment.RecommendedTemperature = 0.35; // Slight increase for diversity
                    adjustment.Rationale.Add("High and improving quality. Can afford slightly higher temperature for output diversity.");
                }
                else
                {
                    adjustment.RecommendedTemperature = 0.3; // Stay at current
                }

                // Recommend similarity threshold adjustment
                if (avgCoverage < 0.5)
                {
                    adjustment.RecommendedSimilarityThreshold = 0; // Use all documents
                    adjustment.Rationale.Add("Low coverage. Reduce similarity threshold to use more documents and context.");
                }
                else if (avgCoverage > 0.9)
                {
                    adjustment.RecommendedSimilarityThreshold = 0.1; // Be more selective
                    adjustment.Rationale.Add("High coverage achieved. Increase threshold to focus on most relevant documents.");
                }
                else
                {
                    adjustment.RecommendedSimilarityThreshold = 0; // Stay at current
                }

                // Recommend topN adjustment
                if (avgQuality < 60)
                {
                    adjustment.RecommendedTopN = 6; // More context
                    adjustment.Rationale.Add("Low quality with limited context. Increase topN to provide more reference documents.");
                }
                else if (avgQuality > 80 && avgCoverage > 0.8)
                {
                    adjustment.RecommendedTopN = 3; // Less noise
                    adjustment.Rationale.Add("High quality with good coverage. Reduce topN to focus on most relevant documents.");
                }
                else
                {
                    adjustment.RecommendedTopN = 4; // Keep current
                }

                adjustment.Status = "Optimization recommended";
                return adjustment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAGFeedback] Error recommending parameter adjustment");
                return new RAGParameterAdjustment 
                { 
                    WorkspaceSlug = workspaceSlug,
                    Status = "Error generating recommendation"
                };
            }
        }

        /// <summary>
        /// Gets document effectiveness ranking
        /// </summary>
        public List<DocumentEffectiveness> GetDocumentRanking(string workspaceSlug, int topN = 10)
        {
            var workspaceFeedback = _feedbackHistory
                .Where(f => f.WorkspaceSlug == workspaceSlug)
                .ToList();

            var docs = new Dictionary<string, DocumentEffectiveness>();

            foreach (var feedback in workspaceFeedback)
            {
                foreach (var doc in feedback.UsedDocuments)
                {
                    if (!docs.ContainsKey(doc))
                    {
                        docs[doc] = new DocumentEffectiveness { DocumentName = doc };
                    }

                    docs[doc].UsageCount++;
                    docs[doc].TotalQualityScore += feedback.QualityScore;
                    docs[doc].AverageQualityScore = docs[doc].TotalQualityScore / docs[doc].UsageCount;
                }
            }

            return docs.Values
                .OrderByDescending(d => d.AverageQualityScore)
                .ThenByDescending(d => d.UsageCount)
                .Take(topN)
                .ToList();
        }

        /// <summary>
        /// Records parameter configuration for tracking
        /// </summary>
        public void RecordParameterConfiguration(string workspaceSlug, RAGParameterSnapshot parameters)
        {
            if (!_configHistory.ContainsKey(workspaceSlug))
            {
                _configHistory[workspaceSlug] = new WorkspaceConfigHistory { WorkspaceSlug = workspaceSlug };
            }

            _configHistory[workspaceSlug].Configurations.Add(new ConfigurationChange
            {
                Timestamp = DateTime.UtcNow,
                Parameters = parameters
            });

            _logger.LogDebug("[RAGFeedback] Configuration recorded for workspace {Workspace}: Temp={Temp}, Threshold={Threshold}, TopN={TopN}",
                workspaceSlug, parameters.Temperature, parameters.SimilarityThreshold, parameters.TopN);
        }

        /// <summary>
        /// Exports feedback data for analysis
        /// </summary>
        public string ExportFeedbackAnalytics(string workspaceSlug)
        {
            var workspaceFeedback = _feedbackHistory
                .Where(f => f.WorkspaceSlug == workspaceSlug)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== RAG FEEDBACK ANALYTICS ===");
            sb.AppendLine($"Workspace: {workspaceSlug}");
            sb.AppendLine($"Total Generations: {workspaceFeedback.Count}");
            
            if (workspaceFeedback.Any())
            {
                var avgQuality = workspaceFeedback.Average(f => f.QualityScore);
                var minQuality = workspaceFeedback.Min(f => f.QualityScore);
                var maxQuality = workspaceFeedback.Max(f => f.QualityScore);
                var avgCoverage = workspaceFeedback.Average(f => f.CoverageRatio) * 100;

                sb.AppendLine($"Average Quality: {avgQuality:F1}%");
                sb.AppendLine($"Quality Range: {minQuality:F1}% - {maxQuality:F1}%");
                sb.AppendLine($"Average Coverage: {avgCoverage:F1}%");
                
                sb.AppendLine("\n--- Document Effectiveness ---");
                var ranking = GetDocumentRanking(workspaceSlug);
                foreach (var doc in ranking)
                {
                    sb.AppendLine($"{doc.DocumentName}: {doc.AverageQualityScore:F1}% (used {doc.UsageCount} times)");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Clears feedback history
        /// </summary>
        public void ClearFeedback()
        {
            _feedbackHistory.Clear();
            _documentEffectiveness.Clear();
            _parameterEffectiveness.Clear();
            _configHistory.Clear();
            _logger.LogInformation("[RAGFeedback] Feedback history cleared");
        }

        // Helper methods
        private void AnalyzeQualityTrends(List<RAGGenerationFeedback> feedback, RAGOptimizationSuggestions suggestions)
        {
            var qualities = feedback.Select(f => f.QualityScore).ToList();
            var trend = CalculateTrend(qualities);

            if (trend > 5)
            {
                suggestions.Recommendations.Add("📈 POSITIVE TREND: Quality is improving. Current configuration is effective.");
            }
            else if (trend < -5)
            {
                suggestions.Recommendations.Add("📉 DECLINING TREND: Quality is degrading. Consider parameter adjustment or document refresh.");
            }

            if (qualities.Average() < 60)
            {
                suggestions.Recommendations.Add("⚠️ LOW AVERAGE QUALITY: Consider increasing temperature, reducing topN, or reviewing documents.");
            }
            else if (qualities.Average() >= 85)
            {
                suggestions.Recommendations.Add("✓ HIGH QUALITY: Current configuration is working well.");
            }
        }

        private void AnalyzeDocumentEffectiveness(List<RAGGenerationFeedback> feedback, RAGOptimizationSuggestions suggestions)
        {
            var allDocs = new Dictionary<string, List<double>>();
            
            foreach (var f in feedback)
            {
                foreach (var doc in f.UsedDocuments)
                {
                    if (!allDocs.ContainsKey(doc))
                    {
                        allDocs[doc] = new List<double>();
                    }
                    allDocs[doc].Add(f.QualityScore);
                }
            }

            var highImpactDocs = allDocs
                .Where(kvp => kvp.Value.Count > 0 && kvp.Value.Average() > 80)
                .OrderByDescending(kvp => kvp.Value.Average())
                .ToList();

            if (highImpactDocs.Any())
            {
                suggestions.Recommendations.Add($"📚 HIGH IMPACT DOCUMENTS: {string.Join(", ", highImpactDocs.Take(2).Select(d => d.Key))} consistently produce high quality results.");
            }

            var lowImpactDocs = allDocs
                .Where(kvp => kvp.Value.Count > 3 && kvp.Value.Average() < 50)
                .OrderBy(kvp => kvp.Value.Average())
                .ToList();

            if (lowImpactDocs.Any())
            {
                suggestions.Recommendations.Add($"❌ LOW IMPACT DOCUMENTS: {string.Join(", ", lowImpactDocs.Take(2).Select(d => d.Key))} correlate with poor results. Consider updating or removing.");
            }
        }

        private void AnalyzeParameterImpact(List<RAGGenerationFeedback> feedback, RAGOptimizationSuggestions suggestions)
        {
            var groupedByTemp = feedback.GroupBy(f => Math.Round(f.Parameters.Temperature, 2));
            var tempEffectiveness = groupedByTemp
                .Select(g => new { Temp = g.Key, AvgQuality = g.Average(f => f.QualityScore), Count = g.Count() })
                .OrderByDescending(t => t.AvgQuality)
                .ToList();

            if (tempEffectiveness.Count > 1 && tempEffectiveness[0].AvgQuality - tempEffectiveness.Last().AvgQuality > 10)
            {
                suggestions.Recommendations.Add($"🌡️ TEMPERATURE IMPACT: Temperature {tempEffectiveness[0].Temp} produced {tempEffectiveness[0].AvgQuality:F1}% vs {tempEffectiveness.Last().AvgQuality:F1}% at {tempEffectiveness.Last().Temp}.");
            }
        }

        private void AnalyzeCoveragePatterns(List<RAGGenerationFeedback> feedback, RAGOptimizationSuggestions suggestions)
        {
            var avgCoverage = feedback.Average(f => f.CoverageRatio);
            
            if (avgCoverage < 0.5)
            {
                suggestions.Recommendations.Add($"🎯 LOW COVERAGE: Only {avgCoverage * 100:F0}% of requirements are covered. Increase batch size or improve prompts.");
            }
            else if (avgCoverage > 0.95)
            {
                suggestions.Recommendations.Add($"✓ EXCELLENT COVERAGE: {avgCoverage * 100:F0}% coverage ratio shows good RAG effectiveness.");
            }
        }

        private double CalculateTrend(List<double> values)
        {
            if (values.Count < 2) return 0;

            var recent = values.TakeLast(5).Average();
            var older = values.Take(Math.Max(5, values.Count / 2)).Average();
            return recent - older;
        }
    }

    /// <summary>
    /// Snapshot of RAG parameters for tracking
    /// </summary>
    public class RAGParameterSnapshot
    {
        public double Temperature { get; set; } = 0.3;
        public double SimilarityThreshold { get; set; } = 0;
        public int TopN { get; set; } = 4;
        public int ContextHistory { get; set; } = 20;
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        public string GetHash()
        {
            return $"{Temperature:F2}_{SimilarityThreshold:F2}_{TopN}_{ContextHistory}";
        }

        public override string ToString()
        {
            return $"Temp={Temperature:F2}, Similarity={SimilarityThreshold:F2}, TopN={TopN}, History={ContextHistory}";
        }
    }

    /// <summary>
    /// Individual generation feedback record
    /// </summary>
    public class RAGGenerationFeedback
    {
        public DateTime Timestamp { get; set; }
        public string WorkspaceSlug { get; set; } = string.Empty;
        public int TestCaseCount { get; set; }
        public int RequirementCount { get; set; }
        public double QualityScore { get; set; } // 0-100
        public double CoverageRatio { get; set; } // 0-1
        public List<string> UsedDocuments { get; set; } = new();
        public RAGParameterSnapshot Parameters { get; set; } = new();
    }

    /// <summary>
    /// Document effectiveness analysis
    /// </summary>
    public class DocumentEffectiveness
    {
        public string DocumentName { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public double TotalQualityScore { get; set; }
        public double AverageQualityScore { get; set; }
        public List<double> ContributionToCoverage { get; set; } = new();

        public double AverageCoverageContribution => ContributionToCoverage.Any() ? ContributionToCoverage.Average() : 0;
    }

    /// <summary>
    /// Parameter configuration effectiveness
    /// </summary>
    public class ParameterEffectiveness
    {
        public RAGParameterSnapshot ParameterSnapshot { get; set; } = new();
        public int UsageCount { get; set; }
        public double TotalQualityScore { get; set; }
        public double AverageQualityScore { get; set; }
    }

    /// <summary>
    /// Configuration history for workspace
    /// </summary>
    public class WorkspaceConfigHistory
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public List<ConfigurationChange> Configurations { get; set; } = new();
    }

    /// <summary>
    /// Single configuration change record
    /// </summary>
    public class ConfigurationChange
    {
        public DateTime Timestamp { get; set; }
        public RAGParameterSnapshot Parameters { get; set; } = new();
    }

    /// <summary>
    /// Optimization suggestions from feedback analysis
    /// </summary>
    public class RAGOptimizationSuggestions
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public int GenerationCount { get; set; }
        public double AverageQualityScore { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Recommended parameter adjustments
    /// </summary>
    public class RAGParameterAdjustment
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public double? RecommendedTemperature { get; set; }
        public double? RecommendedSimilarityThreshold { get; set; }
        public int? RecommendedTopN { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> Rationale { get; set; } = new();

        public bool HasChanges => RecommendedTemperature.HasValue || 
                                  RecommendedSimilarityThreshold.HasValue || 
                                  RecommendedTopN.HasValue;
    }
}
