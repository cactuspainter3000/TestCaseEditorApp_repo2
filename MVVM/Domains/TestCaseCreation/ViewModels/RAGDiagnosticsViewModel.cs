using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels
{
    /// <summary>
    /// ViewModel for displaying RAG (Retrieval-Augmented Generation) diagnostics and metrics.
    /// Shows performance data, document usage, and optimization recommendations.
    /// </summary>
    public partial class RAGDiagnosticsViewModel : ObservableRecipient
    {
        private readonly ILogger<RAGDiagnosticsViewModel> _logger;
        private readonly RAGContextService _ragContextService;

        // Observables for UI binding
        [ObservableProperty]
        private string totalRequestsDisplay = "0";

        [ObservableProperty]
        private string successRateDisplay = "0%";

        [ObservableProperty]
        private string averageResponseTimeDisplay = "0ms";

        [ObservableProperty]
        private string documentCountDisplay = "0";

        [ObservableProperty]
        private string averageDocumentRelevanceDisplay = "0%";

        [ObservableProperty]
        private ObservableCollection<RAGWorkspaceMetricsDisplay> workspaceMetrics = new();

        [ObservableProperty]
        private ObservableCollection<string> recommendations = new();

        [ObservableProperty]
        private string analyticsExport = "No analytics data yet";

        [ObservableProperty]
        private bool isLoading = false;

        [ObservableProperty]
        private string statusMessage = "Ready to display RAG diagnostics";

        [ObservableProperty]
        private bool hasData = false;

        public RAGDiagnosticsViewModel(
            ILogger<RAGDiagnosticsViewModel> logger,
            RAGContextService ragContextService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ragContextService = ragContextService ?? throw new ArgumentNullException(nameof(ragContextService));
        }

        /// <summary>
        /// Refreshes all RAG diagnostics from the service
        /// </summary>
        public async Task RefreshDiagnosticsAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading RAG diagnostics...";

            try
            {
                await Task.Run(() =>
                {
                    var metrics = _ragContextService.GetPerformanceMetrics();

                    // Update main metrics
                    TotalRequestsDisplay = metrics.TotalRequests.ToString();
                    SuccessRateDisplay = $"{metrics.OverallSuccessRate:F1}%";
                    AverageResponseTimeDisplay = $"{metrics.AverageResponseTime.TotalSeconds:F2}s";
                    DocumentCountDisplay = metrics.DocumentCount.ToString();
                    AverageDocumentRelevanceDisplay = $"{metrics.AverageDocumentRelevance:F1}%";

                    // Update workspace metrics
                    var workspaceList = new ObservableCollection<RAGWorkspaceMetricsDisplay>();
                    foreach (var workspace in metrics.WorkspaceMetrics)
                    {
                        workspaceList.Add(new RAGWorkspaceMetricsDisplay
                        {
                            WorkspaceSlug = workspace.WorkspaceSlug,
                            TotalRequests = workspace.TotalRequests,
                            SuccessfulRequests = workspace.SuccessfulRequests,
                            FailedRequests = workspace.FailedRequests,
                            SuccessRatePercent = workspace.SuccessRate,
                            AverageDurationSeconds = workspace.AverageDuration.TotalSeconds,
                            DocumentStats = new ObservableCollection<DocumentUsageDisplay>(
                                workspace.DocumentStats.Select(d => new DocumentUsageDisplay
                                {
                                    DocumentName = d.DocumentName,
                                    Retrievals = d.TotalRetrievals,
                                    Relevance = d.RelevanceScore,
                                    RelevanceIndicator = GetRelevanceIndicator(d.RelevanceScore)
                                })
                            )
                        });
                    }
                    WorkspaceMetrics = workspaceList;

                    // Update recommendations
                    var allRecommendations = new ObservableCollection<string>();
                    if (metrics.WorkspaceMetrics.Any())
                    {
                        var firstWorkspace = metrics.WorkspaceMetrics.FirstOrDefault();
                        if (firstWorkspace?.Recommendations.Any() == true)
                        {
                            foreach (var rec in firstWorkspace.Recommendations)
                            {
                                allRecommendations.Add(rec);
                            }
                        }
                    }

                    if (!allRecommendations.Any())
                    {
                        allRecommendations.Add("📊 No optimization recommendations at this time. System is performing well.");
                    }

                    Recommendations = allRecommendations;

                    // Export analytics
                    AnalyticsExport = _ragContextService.ExportAnalytics();

                    HasData = metrics.TotalRequests > 0;
                    StatusMessage = HasData 
                        ? $"Updated: {metrics.TotalRequests} requests tracked" 
                        : "No RAG requests recorded yet";

                    _logger.LogInformation("[RAGDiagnosticsVM] Refreshed diagnostics: {Requests} requests, {SuccessRate:F1}% success rate",
                        metrics.TotalRequests, metrics.OverallSuccessRate);
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading diagnostics: {ex.Message}";
                _logger.LogError(ex, "[RAGDiagnosticsVM] Error refreshing diagnostics");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Clears all RAG diagnostics data
        /// </summary>
        public void ClearDiagnostics()
        {
            try
            {
                _ragContextService.ClearHistory();
                TotalRequestsDisplay = "0";
                SuccessRateDisplay = "0%";
                AverageResponseTimeDisplay = "0ms";
                DocumentCountDisplay = "0";
                AverageDocumentRelevanceDisplay = "0%";
                WorkspaceMetrics.Clear();
                Recommendations.Clear();
                AnalyticsExport = "No analytics data yet";
                HasData = false;
                StatusMessage = "Diagnostics cleared";
                _logger.LogInformation("[RAGDiagnosticsVM] Diagnostics cleared");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error clearing diagnostics: {ex.Message}";
                _logger.LogError(ex, "[RAGDiagnosticsVM] Error clearing diagnostics");
            }
        }

        /// <summary>
        /// Exports analytics to clipboard or file
        /// </summary>
        public string ExportAnalyticsText()
        {
            return AnalyticsExport;
        }

        private string GetRelevanceIndicator(double relevanceScore)
        {
            if (relevanceScore >= 80) return "✓ High";
            if (relevanceScore >= 50) return "~ Medium";
            if (relevanceScore > 0) return "✕ Low";
            return "⊘ None";
        }
    }

    /// <summary>
    /// Display model for workspace metrics
    /// </summary>
    public class RAGWorkspaceMetricsDisplay
    {
        public string WorkspaceSlug { get; set; } = string.Empty;
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double SuccessRatePercent { get; set; }
        public double AverageDurationSeconds { get; set; }
        public ObservableCollection<DocumentUsageDisplay> DocumentStats { get; set; } = new();

        public string StatusIndicator
        {
            get
            {
                if (SuccessRatePercent >= 90) return "✓ Excellent";
                if (SuccessRatePercent >= 75) return "~ Good";
                if (SuccessRatePercent >= 50) return "⚠ Fair";
                return "✕ Poor";
            }
        }
    }

    /// <summary>
    /// Display model for document usage
    /// </summary>
    public class DocumentUsageDisplay
    {
        public string DocumentName { get; set; } = string.Empty;
        public int Retrievals { get; set; }
        public double Relevance { get; set; }
        public string RelevanceIndicator { get; set; } = string.Empty;

        public string RelevancePercent => $"{Relevance:F0}%";
    }
}
