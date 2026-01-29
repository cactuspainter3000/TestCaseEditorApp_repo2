using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Manages RAG (Retrieval-Augmented Generation) context tracking and diagnostics.
    /// Monitors document retrieval, provides effectiveness metrics, and optimizes RAG parameters.
    /// </summary>
    public class RAGContextService
    {
        private readonly ILogger<RAGContextService> _logger;
        private readonly AnythingLLMService _anythingLLMService;

        // RAG performance tracking
        private List<RAGRequest> _requestHistory = new();
        private Dictionary<string, DocumentUsageStats> _documentStats = new();
        
        // Configuration
        public const string TEST_CASE_GENERATION_WORKSPACE = "test-case-generation";
        public const string REQUIREMENTS_ANALYSIS_WORKSPACE = "requirements-analysis";

        public RAGContextService(
            ILogger<RAGContextService> logger,
            AnythingLLMService anythingLLMService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
        }

        /// <summary>
        /// Tracks a RAG request for analysis and optimization
        /// </summary>
        public void TrackRAGRequest(string workspaceSlug, string prompt, string? response, bool successful, TimeSpan duration)
        {
            var request = new RAGRequest
            {
                Timestamp = DateTime.UtcNow,
                WorkspaceSlug = workspaceSlug,
                PromptLength = prompt.Length,
                ResponseLength = response?.Length ?? 0,
                Successful = successful,
                Duration = duration,
                PromptPreview = prompt.Length > 200 ? prompt.Substring(0, 200) + "..." : prompt,
                ResponsePreview = response?.Length > 200 ? response.Substring(0, 200) + "..." : response ?? "[No Response]"
            };

            _requestHistory.Add(request);
            _logger.LogInformation(
                "[RAG] Request tracked: Workspace={Workspace}, Success={Success}, Duration={Duration}ms, Prompt={PromptLen}B, Response={ResponseLen}B",
                workspaceSlug, successful, duration.TotalMilliseconds, prompt.Length, response?.Length ?? 0);

            // Keep only recent history (last 100 requests)
            if (_requestHistory.Count > 100)
            {
                _requestHistory = _requestHistory.Skip(_requestHistory.Count - 100).ToList();
            }
        }

        /// <summary>
        /// Records document usage statistics for RAG optimization
        /// </summary>
        public void TrackDocumentUsage(string documentName, bool wasRelevant, string? extractedContext = null)
        {
            if (!_documentStats.ContainsKey(documentName))
            {
                _documentStats[documentName] = new DocumentUsageStats { DocumentName = documentName };
            }

            var stats = _documentStats[documentName];
            stats.TotalRetrievals++;
            if (wasRelevant) stats.RelevantRetrievals++;
            if (!string.IsNullOrEmpty(extractedContext))
            {
                stats.LastExtractedContext = extractedContext;
            }

            stats.RelevanceScore = stats.TotalRetrievals > 0 
                ? (double)stats.RelevantRetrievals / stats.TotalRetrievals * 100 
                : 0;

            _logger.LogDebug("[RAG] Document usage tracked: {Document}, Relevance={Relevance:F1}%, Retrievals={Count}",
                documentName, stats.RelevanceScore, stats.TotalRetrievals);
        }

        /// <summary>
        /// Gets RAG context summary for a workspace
        /// </summary>
        public RAGContextSummary GetRAGContextSummary(string workspaceSlug)
        {
            var workspaceRequests = _requestHistory
                .Where(r => r.WorkspaceSlug == workspaceSlug)
                .ToList();

            var successCount = workspaceRequests.Count(r => r.Successful);
            var failureCount = workspaceRequests.Count(r => !r.Successful);
            var avgDuration = workspaceRequests.Any() 
                ? workspaceRequests.Average(r => r.Duration.TotalMilliseconds) 
                : 0;

            return new RAGContextSummary
            {
                WorkspaceSlug = workspaceSlug,
                TotalRequests = workspaceRequests.Count,
                SuccessfulRequests = successCount,
                FailedRequests = failureCount,
                SuccessRate = workspaceRequests.Count > 0 
                    ? (double)successCount / workspaceRequests.Count * 100 
                    : 0,
                AverageDuration = TimeSpan.FromMilliseconds(avgDuration),
                DocumentStats = _documentStats.Values.ToList(),
                RecentRequests = workspaceRequests.TakeLast(5).ToList(),
                Recommendations = GenerateRAGRecommendations(workspaceSlug, workspaceRequests)
            };
        }

        /// <summary>
        /// Verifies RAG configuration and uploads documents if needed
        /// </summary>
        public async Task<bool> EnsureRAGConfiguredAsync(string workspaceSlug, bool forceRefresh = false)
        {
            try
            {
                _logger.LogInformation("[RAG] Ensuring RAG configuration for workspace '{Workspace}' (Force refresh: {Force})",
                    workspaceSlug, forceRefresh);

                // Check if workspace has documents uploaded
                var hasDocuments = await VerifyWorkspaceDocumentsAsync(workspaceSlug);
                
                if (!hasDocuments || forceRefresh)
                {
                    _logger.LogInformation("[RAG] Uploading RAG training documents to workspace '{Workspace}'", workspaceSlug);
                    var uploadSuccess = await _anythingLLMService.UploadRagTrainingDocumentsAsync(workspaceSlug);
                    
                    if (!uploadSuccess)
                    {
                        _logger.LogWarning("[RAG] Failed to upload training documents to workspace '{Workspace}'", workspaceSlug);
                        return false;
                    }
                }

                // Verify workspace settings are optimized
                var settingsOk = await _anythingLLMService.ValidateWorkspaceSystemPromptAsync(workspaceSlug);
                
                if (!settingsOk)
                {
                    _logger.LogInformation("[RAG] Configuring optimal settings for workspace '{Workspace}'", workspaceSlug);
                    await _anythingLLMService.ConfigureWorkspaceSettingsAsync(workspaceSlug);
                }

                _logger.LogInformation("[RAG] RAG configuration verified for workspace '{Workspace}'", workspaceSlug);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAG] Error ensuring RAG configuration for workspace '{Workspace}'", workspaceSlug);
                return false;
            }
        }

        /// <summary>
        /// Verifies that required documents are uploaded to workspace
        /// </summary>
        private async Task<bool> VerifyWorkspaceDocumentsAsync(string workspaceSlug)
        {
            try
            {
                // This would require access to workspace documents list from AnythingLLM API
                // For now, we assume documents need to be verified/uploaded
                // In future: query AnythingLLM to check which documents are actually in the workspace
                
                _logger.LogDebug("[RAG] Verifying documents in workspace '{Workspace}'", workspaceSlug);
                
                // Placeholder: Always return false to ensure documents are uploaded
                // TODO: Implement actual document verification via AnythingLLM API
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RAG] Error verifying workspace documents");
                return false;
            }
        }

        /// <summary>
        /// Generates recommendations for RAG optimization based on performance
        /// </summary>
        private List<string> GenerateRAGRecommendations(string workspaceSlug, List<RAGRequest> requests)
        {
            var recommendations = new List<string>();

            if (!requests.Any())
            {
                recommendations.Add("No RAG requests recorded yet. RAG data will be tracked as requests are processed.");
                return recommendations;
            }

            var successRate = requests.Count(r => r.Successful) / (double)requests.Count * 100;
            var avgDuration = requests.Average(r => r.Duration.TotalMilliseconds);

            // Analyze success rate
            if (successRate < 50)
            {
                recommendations.Add("⚠️ LOW SUCCESS RATE: Consider reviewing workspace configuration or increasing timeout values");
            }
            else if (successRate < 80)
            {
                recommendations.Add("⚠️ MODERATE SUCCESS RATE: Some requests are failing. Monitor logs for patterns.");
            }
            else if (successRate == 100)
            {
                recommendations.Add("✓ EXCELLENT SUCCESS RATE: RAG configuration is working well");
            }

            // Analyze response time
            if (avgDuration > 120000) // > 2 minutes
            {
                recommendations.Add("⏱️ SLOW RESPONSES: LLM is taking a long time. Consider reducing batch size or using faster model");
            }
            else if (avgDuration < 5000) // < 5 seconds
            {
                recommendations.Add("✓ FAST RESPONSES: RAG processing is efficient");
            }

            // Analyze document usage
            var unusedDocs = _documentStats.Where(d => d.Value.TotalRetrievals == 0).ToList();
            if (unusedDocs.Any())
            {
                recommendations.Add($"📄 UNUSED DOCUMENTS: {string.Join(", ", unusedDocs.Select(d => d.Key))} haven't been retrieved. They may be irrelevant or context threshold is too high.");
            }

            var lowRelevanceDocs = _documentStats.Where(d => d.Value.RelevanceScore < 50 && d.Value.TotalRetrievals > 5).ToList();
            if (lowRelevanceDocs.Any())
            {
                recommendations.Add($"❌ LOW RELEVANCE: {string.Join(", ", lowRelevanceDocs.Select(d => d.Key))} have low relevance scores. Consider updating or removing these documents.");
            }

            // Request size analysis
            var avgPromptSize = requests.Average(r => r.PromptLength);
            if (avgPromptSize > 5000)
            {
                recommendations.Add("📝 LARGE PROMPTS: Prompts are quite large. Consider using batch processing with smaller requirement sets.");
            }

            return recommendations;
        }

        /// <summary>
        /// Gets all RAG performance metrics
        /// </summary>
        public RAGPerformanceMetrics GetPerformanceMetrics()
        {
            return new RAGPerformanceMetrics
            {
                TotalRequests = _requestHistory.Count,
                SuccessfulRequests = _requestHistory.Count(r => r.Successful),
                FailedRequests = _requestHistory.Count(r => !r.Successful),
                OverallSuccessRate = _requestHistory.Count > 0
                    ? (double)_requestHistory.Count(r => r.Successful) / _requestHistory.Count * 100
                    : 0,
                AverageResponseTime = _requestHistory.Any()
                    ? TimeSpan.FromMilliseconds(_requestHistory.Average(r => r.Duration.TotalMilliseconds))
                    : TimeSpan.Zero,
                DocumentCount = _documentStats.Count,
                AverageDocumentRelevance = _documentStats.Values.Any()
                    ? _documentStats.Values.Average(d => d.RelevanceScore)
                    : 0,
                WorkspaceMetrics = _requestHistory
                    .GroupBy(r => r.WorkspaceSlug)
                    .Select(g => GetRAGContextSummary(g.Key))
                    .ToList()
            };
        }

        /// <summary>
        /// Clears RAG history (for testing or reset)
        /// </summary>
        public void ClearHistory()
        {
            _requestHistory.Clear();
            _documentStats.Clear();
            _logger.LogInformation("[RAG] RAG history cleared");
        }

        /// <summary>
        /// Exports RAG analytics for debugging
        /// </summary>
        public string ExportAnalytics()
        {
            var metrics = GetPerformanceMetrics();
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=== RAG PERFORMANCE ANALYTICS ===");
            sb.AppendLine($"Total Requests: {metrics.TotalRequests}");
            sb.AppendLine($"Success Rate: {metrics.OverallSuccessRate:F1}%");
            sb.AppendLine($"Average Response Time: {metrics.AverageResponseTime.TotalSeconds:F2}s");
            sb.AppendLine($"Documents Tracked: {metrics.DocumentCount}");
            sb.AppendLine();

            foreach (var workspace in metrics.WorkspaceMetrics)
            {
                sb.AppendLine($"--- Workspace: {workspace.WorkspaceSlug} ---");
                sb.AppendLine($"  Requests: {workspace.TotalRequests} (Success: {workspace.SuccessfulRequests}, Failed: {workspace.FailedRequests})");
                sb.AppendLine($"  Success Rate: {workspace.SuccessRate:F1}%");
                sb.AppendLine($"  Avg Duration: {workspace.AverageDuration.TotalSeconds:F2}s");
                
                if (workspace.DocumentStats.Any())
                {
                    sb.AppendLine("  Document Stats:");
                    foreach (var doc in workspace.DocumentStats)
                    {
                        sb.AppendLine($"    - {doc.DocumentName}: {doc.RelevanceScore:F1}% relevance ({doc.TotalRetrievals} retrievals)");
                    }
                }

                if (workspace.Recommendations.Any())
                {
                    sb.AppendLine("  Recommendations:");
                    foreach (var rec in workspace.Recommendations)
                    {
                        sb.AppendLine($"    • {rec}");
                    }
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents a single RAG request for tracking
    /// </summary>
    public class RAGRequest
    {
        public DateTime Timestamp { get; set; }
        public string WorkspaceSlug { get; set; } = string.Empty;
        public int PromptLength { get; set; }
        public int ResponseLength { get; set; }
        public bool Successful { get; set; }
        public TimeSpan Duration { get; set; }
        public string PromptPreview { get; set; } = string.Empty;
        public string ResponsePreview { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary of RAG context performance for a workspace
    /// </summary>
    public class RAGContextSummary
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public List<DocumentUsageStats> DocumentStats { get; set; } = new();
        public List<RAGRequest> RecentRequests { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Document usage statistics for RAG optimization
    /// </summary>
    public class DocumentUsageStats
    {
        public string DocumentName { get; set; } = string.Empty;
        public int TotalRetrievals { get; set; }
        public int RelevantRetrievals { get; set; }
        public double RelevanceScore { get; set; }
        public string? LastExtractedContext { get; set; }
    }

    /// <summary>
    /// Overall RAG performance metrics
    /// </summary>
    public class RAGPerformanceMetrics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double OverallSuccessRate { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public int DocumentCount { get; set; }
        public double AverageDocumentRelevance { get; set; }
        public List<RAGContextSummary> WorkspaceMetrics { get; set; } = new();
    }
}
