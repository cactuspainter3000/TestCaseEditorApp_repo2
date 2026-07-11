using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Implementation of the requirement analysis engine that consolidates analysis functionality
    /// from the TestCaseGeneration domain into the Requirements domain.
    /// 
    /// This service extracts the core business logic from TestCaseGenerator_AnalysisVM (1,808 lines)
    /// following proper separation of concerns and MVVM principles.
    /// </summary>
    public class RequirementAnalysisEngine : IRequirementAnalysisEngine
    {
        private readonly IRequirementAnalysisService _analysisService;
        private readonly ILogger<RequirementAnalysisEngine> _logger;
        private readonly IncoseConsistentContentChecker _incoseChecker = new();

        public RequirementAnalysisEngine(
            IRequirementAnalysisService analysisService,
            ILogger<RequirementAnalysisEngine> logger)
        {
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RequirementAnalysis> AnalyzeRequirementAsync(
            Requirement requirement, 
            Action<string>? progressCallback = null, 
            CancellationToken cancellationToken = default)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            _logger.LogDebug("[AnalysisEngine] Starting analysis for requirement {RequirementId}", requirement.Item);

            try
            {
                progressCallback?.Invoke("Uploading|Initializing analysis...|0");

                // Check if we already have recent analysis
                if (requirement.Analysis?.IsAnalyzed == true && 
                    requirement.Analysis.Timestamp > DateTime.UtcNow.AddHours(-1))
                {
                    _logger.LogDebug("[AnalysisEngine] Using cached analysis for {RequirementId}", requirement.Item);
                    progressCallback?.Invoke("Processing|Using cached analysis result|25");
                    return requirement.Analysis;
                }

                progressCallback?.Invoke("Processing|Running INCOSE structural check...|30");

                // --- Deterministic INCOSE check (no LLM, instant) ---
                var incoseResult = _incoseChecker.Check(requirement.Name ?? requirement.Description ?? string.Empty);
                var incoseIssues = _incoseChecker.ToAnalysisIssues(incoseResult);

                _logger.LogInformation("[AnalysisEngine] INCOSE check for {RequirementId}: Type={Type}, Passed={Passed}, Issues={IssueCount}",
                    requirement.Item, incoseResult.RequirementType, incoseResult.Passed, incoseIssues.Count);

                if (incoseIssues.Count > 0)
                    progressCallback?.Invoke($"Processing|INCOSE structural issues found: {incoseIssues.Count}|40");

                progressCallback?.Invoke("Processing|Analyzing requirement quality with LLM...|50");

                // Delegate to the existing analysis service with streaming support for timeout enforcement
                var analysis = await _analysisService.AnalyzeRequirementWithStreamingAsync(
                    requirement, 
                    onPartialResult: null, // Engine doesn't need partial results
                    onProgressUpdate: progress => 
                    {
                        // Enhance the progress message with stage and percentage
                        progressCallback?.Invoke($"Processing|{progress}|65");
                    },
                    cancellationToken: cancellationToken);

                if (analysis.IsAnalyzed)
                {
                    // Merge INCOSE issues into LLM analysis results (prepend so they appear first)
                    if (incoseIssues.Count > 0)
                    {
                        analysis.Issues ??= new List<AnalysisIssue>();
                        analysis.Issues.InsertRange(0, incoseIssues);
                        _logger.LogInformation("[AnalysisEngine] Merged {IncoseCount} INCOSE issues into analysis for {RequirementId}",
                            incoseIssues.Count, requirement.Item);
                    }

                    // Annotate freeform feedback with INCOSE classification
                    var incoseSummary = $"[INCOSE Pattern: {incoseResult.RequirementType}]" +
                        (incoseResult.Passed ? " Structural check passed." : $" Structural issues detected. Canonical form: {incoseResult.CanonicalFormSuggestion}");
                    analysis.FreeformFeedback = string.IsNullOrEmpty(analysis.FreeformFeedback)
                        ? incoseSummary
                        : $"{incoseSummary}\n\n{analysis.FreeformFeedback}";

                    _logger.LogInformation("[AnalysisEngine] Analysis completed successfully for {RequirementId}. Original Quality: {OriginalScore}, Issues: {IssueCount}", 
                        requirement.Item, analysis.OriginalQualityScore, analysis.Issues?.Count ?? 0);
                    
                    progressCallback?.Invoke($"Extracting|Analysis complete. Your requirement quality: {analysis.OriginalQualityScore}/10|90");

                    // Store the result on the requirement
                    requirement.Analysis = analysis;
                }
                else
                {
                    _logger.LogWarning("[AnalysisEngine] Analysis failed for {RequirementId}: {ErrorMessage}", 
                        requirement.Item, analysis.ErrorMessage);
                    
                    progressCallback?.Invoke($"Extracting|Analysis failed: {analysis.ErrorMessage}|85");
                }

                return analysis;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[AnalysisEngine] Analysis cancelled for {RequirementId}", requirement.Item);
                progressCallback?.Invoke("Extracting|Analysis cancelled|0");
                
                return new RequirementAnalysis
                {
                    IsAnalyzed = false,
                    ErrorMessage = "Analysis was cancelled",
                    OriginalQualityScore = 0,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisEngine] Unexpected error analyzing {RequirementId}", requirement.Item);
                progressCallback?.Invoke("Extracting|Analysis failed due to unexpected error|0");
                
                return new RequirementAnalysis
                {
                    IsAnalyzed = false,
                    ErrorMessage = $"Analysis failed: {ex.Message}",
                    OriginalQualityScore = 0,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<bool> ValidateEngineHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("[AnalysisEngine] Validating engine health");
                
                // Validate that the underlying analysis service is responsive
                var testRequirement = new Requirement 
                { 
                    Item = "TEST", 
                    Name = "Health Check", 
                    Description = "Simple test requirement for health validation" 
                };

                // Quick validation without full analysis
                var isHealthy = _analysisService.GeneratePromptForInspection(testRequirement) != null;
                await Task.CompletedTask; // Ensure proper async behavior

                _logger.LogDebug("[AnalysisEngine] Health check result: {IsHealthy}", isHealthy);
                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisEngine] Health check failed");
                return false;
            }
        }

        public AnalysisEngineStatus GetEngineStatus()
        {
            try
            {
                // Get cache statistics from the underlying service
                var cacheStats = _analysisService.CacheStatistics;

                var status = new AnalysisEngineStatus
                {
                    IsHealthy = true, // TODO: Implement proper health checks
                    StatusMessage = "Analysis engine operational",
                    CacheStatistics = cacheStats != null ? $"Cache Hits: {cacheStats.CacheHits}, Cache Entries: {cacheStats.TotalEntries}" : "Cache statistics unavailable",
                    LLMHealthStatus = "LLM service connected", // TODO: Get actual LLM health
                    PerformanceMetrics = "Average response time: <1s", // TODO: Get actual metrics
                    LastUpdated = DateTime.UtcNow
                };

                _logger.LogDebug("[AnalysisEngine] Status retrieved: {StatusMessage}", status.StatusMessage);
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisEngine] Failed to get engine status");
                
                return new AnalysisEngineStatus
                {
                    IsHealthy = false,
                    StatusMessage = $"Engine status check failed: {ex.Message}",
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        public string GeneratePromptForInspection(Requirement requirement)
        {
            if (requirement == null)
                return "ERROR: Requirement is null";

            try
            {
                _logger.LogDebug("[AnalysisEngine] Generating inspection prompt for {RequirementId}", requirement.Item);

                // Delegate to the underlying service for prompt generation
                var prompt = _analysisService.GeneratePromptForInspection(requirement);

                // Add engine-specific debugging information
                var inspectionData = $"=== ANALYSIS ENGINE INSPECTION ===\n" +
                                   $"Engine: RequirementAnalysisEngine (Requirements Domain)\n" +
                                   $"Requirement ID: {requirement.Item}\n" +
                                   $"Requirement Name: {requirement.Name}\n" +
                                   $"Description Length: {requirement.Description?.Length ?? 0} characters\n" +
                                   $"Has Tables: {(requirement.Tables?.Count > 0 ? "Yes" : "No")}\n" +
                                   $"Has Loose Content: {(requirement.LooseContent != null ? "Yes" : "No")}\n" +
                                   $"Generated At: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n\n" +
                                   $"=== ACTUAL LLM PROMPT ===\n" +
                                   $"{prompt}\n" +
                                   $"=== END PROMPT ===";

                return inspectionData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisEngine] Failed to generate inspection prompt for {RequirementId}", requirement.Item);
                return $"ERROR: Failed to generate prompt - {ex.Message}";
            }
        }
    }
}