using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Focused orchestration service for requirement quality analysis.
    /// This class is responsible for:
    /// 1. Building prompts
    /// 2. Sending prompts to the LLM
    /// 3. Parsing responses into RequirementAnalysis
    /// 4. Managing cache / health / validation helpers
    ///
    /// It is intentionally narrower than the old all-in-one implementation.
    /// </summary>
    public sealed class RequirementAnalysisService : IRequirementAnalysisService
    {
        private readonly ITextGenerationService _llmService;
        private readonly RequirementAnalysisPromptBuilder _promptBuilder;
        private readonly ResponseParserManager _parserManager;
        private readonly LlmServiceHealthMonitor? _healthMonitor;
        private readonly RequirementAnalysisCache? _cache;
        private readonly ILogger<RequirementAnalysisService> _logger;
        private readonly IAnythingLLMService _anythingLlmService;

        private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

        // Optional advanced services
        private readonly ISystemCapabilityDerivationService? _derivationService;
        private readonly IRequirementGapAnalyzer? _gapAnalyzer;

        private string? _cachedSystemPrompt;
        private string? _projectWorkspaceName;
        private string? _anythingLlmWorkspaceSlug;

        /// <summary>
        /// Enable/disable self-reflection feature.
        /// </summary>
        public bool EnableSelfReflection { get; set; } = false;

        /// <summary>
        /// Enable/disable caching of analysis results.
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Retained for interface compatibility. Not used in this focused version.
        /// </summary>
        public bool EnableThreadCleanup { get; set; } = true;

        /// <summary>
        /// Timeout for requirement analysis operations.
        /// </summary>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromSeconds(240);

        /// <summary>
        /// Current health report from the underlying LLM monitor, if present.
        /// </summary>
        public LlmServiceHealthMonitor.HealthReport? ServiceHealth => _healthMonitor?.CurrentHealth;

        /// <summary>
        /// Whether the health monitor indicates fallback mode.
        /// </summary>
        public bool IsUsingFallback => _healthMonitor?.IsUsingFallback ?? false;

        /// <summary>
        /// Current cache statistics if caching is configured.
        /// </summary>
        public RequirementAnalysisCache.CacheStatistics? CacheStatistics => _cache?.GetStatistics();

        /// <summary>
        /// Initializes the requirement analysis service.
        /// </summary>
        public RequirementAnalysisService(
            ITextGenerationService llmService,
            RequirementAnalysisPromptBuilder promptBuilder,
            ResponseParserManager parserManager,
            ILogger<RequirementAnalysisService> logger,
            IAnythingLLMService anythingLlmService,
            LlmServiceHealthMonitor? healthMonitor = null,
            RequirementAnalysisCache? cache = null,
            ISystemCapabilityDerivationService? derivationService = null,
            IRequirementGapAnalyzer? gapAnalyzer = null)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _parserManager = parserManager ?? throw new ArgumentNullException(nameof(parserManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _healthMonitor = healthMonitor;
            _cache = cache;
            _derivationService = derivationService;
            _gapAnalyzer = gapAnalyzer;
            _anythingLlmService = anythingLlmService ?? throw new ArgumentNullException(nameof(anythingLlmService));
        }

        /// <summary>
        /// Sets workspace/project context for analysis logging and AnythingLLM routing.
        /// </summary>
        public void SetWorkspaceContext(string? workspaceName, string? anythingLlmWorkspaceSlug = null)
        {
            _projectWorkspaceName = workspaceName;
            _anythingLlmWorkspaceSlug = anythingLlmWorkspaceSlug;

            _logger.LogInformation(
                "[RequirementAnalysisService:{InstanceId}] Workspace context set. WorkspaceName={Workspace}, AnythingLLMSlug={Slug}",
                _instanceId,
                workspaceName ?? "<none>",
                anythingLlmWorkspaceSlug ?? "<none>");
        }

        /// <summary>
        /// Analyze a requirement with the standard request path.
        /// </summary>
        public async Task<RequirementAnalysis> AnalyzeRequirementAsync(
            Requirement requirement,
            CancellationToken cancellationToken = default)
        {
            return await AnalyzeRequirementWithStreamingAsync(
                requirement,
                onPartialResult: null,
                onProgressUpdate: null,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Analyze a requirement with progress callbacks.
        /// </summary>
        /// <summary>
        /// Analyze a requirement with progress callbacks.
        /// </summary>
        public async Task<RequirementAnalysis> AnalyzeRequirementWithStreamingAsync(
            Requirement requirement,
            Action<string>? onPartialResult = null,
            Action<string>? onProgressUpdate = null,
            CancellationToken cancellationToken = default)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            var startedUtc = DateTime.UtcNow;

            try
            {
                onProgressUpdate?.Invoke("Starting requirement analysis...");
                _logger.LogInformation("[RequirementAnalysisService] Starting analysis for requirement {RequirementId}", requirement.Item);

                _logger.LogInformation(
                    "[RequirementAnalysisService:{InstanceId}] Analyze start. Stored WorkspaceName={Workspace}, Stored AnythingLLMSlug={Slug}",
                    _instanceId,
                    _projectWorkspaceName ?? "<none>",
                    _anythingLlmWorkspaceSlug ?? "<none>");

                // Cache check first
                if (EnableCaching && _cache != null)
                {
                    onProgressUpdate?.Invoke("Checking cache...");
                    if (_cache.TryGet(requirement, out var cachedAnalysis) && cachedAnalysis != null)
                    {
                        _logger.LogInformation("[RequirementAnalysisService] Using cached analysis for requirement {RequirementId}", requirement.Item);
                        onProgressUpdate?.Invoke("Using cached analysis");
                        return cachedAnalysis;
                    }
                }

                onProgressUpdate?.Invoke("Preparing analysis prompt...");

                _cachedSystemPrompt ??= _promptBuilder.GetSystemPrompt();

                var verificationAssumptions = GetVerificationAssumptionsText(requirement);

                var contextPrompt = _promptBuilder.BuildContextPrompt(
                    requirement.Item ?? "UNKNOWN",
                    requirement.Name ?? string.Empty,
                    requirement.Description ?? string.Empty,
                    requirement.Tables,
                    requirement.LooseContent,
                    verificationAssumptions);

                string finalPromptContext = contextPrompt;

                if (!string.IsNullOrWhiteSpace(_projectWorkspaceName))
                {
                    finalPromptContext =
                        $"Project Context: {_projectWorkspaceName}{Environment.NewLine}{Environment.NewLine}{contextPrompt}";
                }

                onProgressUpdate?.Invoke("Sending analysis request to AI...");

                _logger.LogInformation(
    "[RequirementAnalysisService] Runtime AnalysisTimeout for {RequirementId}: {TimeoutSeconds} seconds",
    requirement.Item ?? "UNKNOWN",
    AnalysisTimeout.TotalSeconds);




                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(AnalysisTimeout);

                var textGenService = GetEffectiveTextGenerationService();

                _logger.LogInformation(
    "[RequirementAnalysisService] Prompt sizes for {RequirementId}: System={SystemLength}, Context={ContextLength}, Total={TotalLength}",
    requirement.Item ?? "UNKNOWN",
    _cachedSystemPrompt?.Length ?? 0,
    finalPromptContext?.Length ?? 0,
    (_cachedSystemPrompt?.Length ?? 0) + (finalPromptContext?.Length ?? 0));

                var response = await textGenService.GenerateWithSystemAsync(
                    _cachedSystemPrompt,
                    finalPromptContext,
                    timeoutCts.Token);

                if (string.IsNullOrWhiteSpace(response))
                {
                    return CreateErrorAnalysis("LLM returned empty response", startedUtc);
                }

                onPartialResult?.Invoke(response);

                // Optional second-pass review
                if (EnableSelfReflection)
                {
                    onProgressUpdate?.Invoke("Reviewing response quality...");
                    response = await ApplySelfReflectionAsync(
                        initialResponse: response,
                        originalContextPrompt: finalPromptContext,
                        requirementId: requirement.Item ?? "UNKNOWN",
                        cancellationToken: timeoutCts.Token);
                }

                onProgressUpdate?.Invoke("Parsing analysis response...");

                var analysis = _parserManager.ParseResponse(response, requirement.Item ?? "UNKNOWN");
                if (analysis == null)
                {
                    _logger.LogWarning("[RequirementAnalysisService] Failed to parse response for requirement {RequirementId}", requirement.Item);
                    return CreateErrorAnalysis("Failed to parse LLM response", startedUtc);
                }

                analysis.Timestamp = DateTime.UtcNow;
                analysis.AnalysisDurationSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds;
                analysis.IsAnalyzed = true;
                analysis.ErrorMessage = null;

                ValidateRecommendationQuality(analysis, requirement.Item ?? "UNKNOWN");

                if (EnableCaching && _cache != null && analysis.IsAnalyzed)
                {
                    _cache.Set(requirement, analysis, TimeSpan.FromSeconds(analysis.AnalysisDurationSeconds));
                }

                onProgressUpdate?.Invoke("Analysis complete");
                _logger.LogInformation(
                    "[RequirementAnalysisService] Analysis completed for {RequirementId}. Score={Score}, Issues={IssueCount}, Recommendations={RecommendationCount}",
                    requirement.Item,
                    analysis.OriginalQualityScore,
                    analysis.Issues?.Count ?? 0,
                    analysis.Recommendations?.Count ?? 0);

                return analysis;
            }

            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "[RequirementAnalysisService] Upstream cancellation token triggered for requirement {RequirementId}",
                    requirement.Item);

                return CreateErrorAnalysis("Analysis was cancelled", startedUtc);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "[RequirementAnalysisService] Local analysis timeout triggered after {TimeoutSeconds}s for requirement {RequirementId}",
                    AnalysisTimeout.TotalSeconds,
                    requirement.Item);

                return CreateErrorAnalysis($"Analysis timed out after {AnalysisTimeout.TotalSeconds:F0} seconds", startedUtc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisService] Unexpected analysis error for requirement {RequirementId}", requirement.Item);
                return CreateErrorAnalysis($"Analysis failed: {ex.Message}", startedUtc);
            }
        }

        /// <summary>
        /// Generates the actual prompt sent to the LLM for inspection/debugging.
        /// </summary>
        public string GeneratePromptForInspection(Requirement requirement)
        {
            if (requirement == null)
                return "ERROR: Requirement is null";

            var verificationAssumptions = GetVerificationAssumptionsText(requirement);

            var systemPrompt = _promptBuilder.GetSystemPrompt();
            var contextPrompt = _promptBuilder.BuildContextPrompt(
                requirement.Item ?? "UNKNOWN",
                requirement.Name ?? string.Empty,
                requirement.Description ?? string.Empty,
                requirement.Tables,
                requirement.LooseContent,
                verificationAssumptions);

            if (!string.IsNullOrWhiteSpace(_projectWorkspaceName))
            {
                contextPrompt =
                    $"Project Context: {_projectWorkspaceName}{Environment.NewLine}{Environment.NewLine}{contextPrompt}";
            }

            return $"{systemPrompt}{Environment.NewLine}{Environment.NewLine}{contextPrompt}";
        }

        /// <summary>
        /// Returns detailed health information, if a monitor is configured.
        /// </summary>
        public async Task<LlmServiceHealthMonitor.HealthReport?> GetDetailedHealthAsync(CancellationToken cancellationToken = default)
        {
            if (_healthMonitor == null)
                return null;

            return await _healthMonitor.CheckHealthAsync(cancellationToken);
        }

        /// <summary>
        /// Invalidates cache for a specific requirement ID.
        /// </summary>
        public void InvalidateCache(string requirementGlobalId)
        {
            if (_cache == null || !EnableCaching || string.IsNullOrWhiteSpace(requirementGlobalId))
                return;

            _cache.Invalidate(requirementGlobalId);
            _logger.LogInformation("[RequirementAnalysisService] Invalidated cache for requirement {RequirementId}", requirementGlobalId);
        }

        /// <summary>
        /// Clears all analysis cache entries.
        /// </summary>
        public void ClearAnalysisCache()
        {
            if (_cache == null)
                return;

            _cache.Clear();
            _logger.LogInformation("[RequirementAnalysisService] Cleared analysis cache");
        }

        // =====================================================
        // TASK 4.4: ENHANCED DERIVATION ANALYSIS CAPABILITIES
        // =====================================================

        /// <summary>
        /// Analyze a requirement for ATP-derived capabilities.
        /// </summary>
        public async Task<RequirementDerivationAnalysis> AnalyzeRequirementDerivationAsync(
            Requirement requirement,
            CancellationToken cancellationToken = default)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            var result = new RequirementDerivationAnalysis
            {
                AnalyzedRequirement = requirement,
                AnalyzedAt = DateTime.UtcNow
            };

            try
            {
                if (_derivationService == null)
                {
                    result.DerivationIssues.Add("System capability derivation service not configured");
                    result.Recommendations.Add("Configure ISystemCapabilityDerivationService to enable ATP derivation analysis.");
                    return result;
                }

                result.HasATPContent = DetectATPContentFallback(requirement);
                result.ATPDetectionConfidence = result.HasATPContent ? 0.7 : 0.3;

                if (!result.HasATPContent)
                    return result;

                var derivationOptions = new DerivationOptions
                {
                    EnableQualityScoring = true,
                    IncludeRejectionAnalysis = true
                };

                var derivationResult = await _derivationService.DeriveCapabilitiesAsync(
                    requirement.Description ?? requirement.Name ?? string.Empty,
                    derivationOptions);

                if (derivationResult != null && derivationResult.IsSuccessful)
                {
                    result.DerivedCapabilities.AddRange(derivationResult.DerivedCapabilities);
                    result.DerivationQuality = derivationResult.QualityScore;

                    if (derivationResult.ProcessingWarnings?.Any() == true)
                    {
                        result.DerivationIssues.AddRange(derivationResult.ProcessingWarnings);
                    }
                }
                else
                {
                    result.DerivationIssues.Add("Failed to derive capabilities from requirement.");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisService] Derivation analysis failed for requirement {RequirementId}", requirement.Item);
                result.DerivationIssues.Add($"Analysis failed: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Run gap analysis between derived capabilities and current requirements.
        /// </summary>
        public async Task<RequirementGapAnalysisResult> AnalyzeRequirementGapAsync(
            IEnumerable<DerivedCapability> derivedCapabilities,
            IEnumerable<Requirement> existingRequirements,
            CancellationToken cancellationToken = default)
        {
            if (derivedCapabilities == null)
                throw new ArgumentNullException(nameof(derivedCapabilities));
            if (existingRequirements == null)
                throw new ArgumentNullException(nameof(existingRequirements));

            try
            {
                if (_gapAnalyzer == null)
                {
                    return new RequirementGapAnalysisResult
                    {
                        IsSuccessful = false,
                        ErrorMessage = "RequirementGapAnalyzer service not configured",
                        AnalyzedAt = DateTime.UtcNow
                    };
                }

                var result = await _gapAnalyzer.AnalyzeGapsAsync(
                    derivedCapabilities.ToList(),
                    existingRequirements.ToList());

                return new RequirementGapAnalysisResult
                {
                    IsSuccessful = true,
                    GapAnalysisResult = result,
                    AnalyzedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisService] Gap analysis failed");
                return new RequirementGapAnalysisResult
                {
                    IsSuccessful = false,
                    ErrorMessage = ex.Message,
                    AnalyzedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Validate a testing workflow across a set of requirements.
        /// </summary>
        public async Task<TestingWorkflowValidationResult> ValidateTestingWorkflowAsync(
            IEnumerable<Requirement> requirements,
            TestingValidationContext? testingContext = null,
            CancellationToken cancellationToken = default)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));

            var requirementList = requirements.ToList();

            var result = new TestingWorkflowValidationResult
            {
                ValidatedAt = DateTime.UtcNow
            };

            try
            {
                var derivationResults = new List<RequirementDerivationAnalysis>();
                var allDerivedCapabilities = new List<DerivedCapability>();

                foreach (var requirement in requirementList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var derivation = await AnalyzeRequirementDerivationAsync(requirement, cancellationToken);
                    derivationResults.Add(derivation);
                    allDerivedCapabilities.AddRange(derivation.DerivedCapabilities);
                }

                var gapAnalysis = await AnalyzeRequirementGapAsync(
                    allDerivedCapabilities,
                    requirementList,
                    cancellationToken);

                result.CoverageAnalysis = AnalyzeTestingCoverage(requirementList, allDerivedCapabilities);
                result.Issues.AddRange(AnalyzeValidationIssues(derivationResults, gapAnalysis));
                result.Recommendations.AddRange(GenerateTestingWorkflowRecommendations(derivationResults, gapAnalysis));

                result.OverallScore = CalculateOverallValidationScore(result);
                result.IsValid = result.OverallScore >= 0.7;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisService] Testing workflow validation failed");
                result.Issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Critical,
                    Description = $"Validation failed: {ex.Message}",
                    Category = "System Error"
                });
                return result;
            }
        }

        /// <summary>
        /// Perform batch derivation analysis across requirements.
        /// </summary>
        public async Task<IEnumerable<RequirementDerivationAnalysis>> AnalyzeBatchDerivationAsync(
            IEnumerable<Requirement> requirements,
            BatchAnalysisOptions? batchOptions = null,
            Action<BatchAnalysisProgress>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));

            var requirementList = requirements.ToList();
            var options = batchOptions ?? new BatchAnalysisOptions();
            var results = new List<RequirementDerivationAnalysis>();

            var progress = new BatchAnalysisProgress
            {
                TotalCount = requirementList.Count
            };

            foreach (var requirement in requirementList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    progress.CurrentRequirement = requirement.Item;
                    onProgress?.Invoke(progress);

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(options.AnalysisTimeout);

                    var derivation = await AnalyzeRequirementDerivationAsync(requirement, timeoutCts.Token);
                    results.Add(derivation);

                    progress.CompletedCount++;
                    onProgress?.Invoke(progress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RequirementAnalysisService] Batch derivation failed for requirement {RequirementId}", requirement.Item);

                    progress.CompletedCount++;
                    progress.FailedCount++;
                    onProgress?.Invoke(progress);

                    if (!options.ContinueOnFailure)
                        throw;

                    results.Add(new RequirementDerivationAnalysis
                    {
                        AnalyzedRequirement = requirement,
                        AnalyzedAt = DateTime.UtcNow,
                        DerivationIssues = new List<string> { $"Batch analysis failed: {ex.Message}" }
                    });
                }
            }

            return results;
        }

        // =====================================================
        // Private helpers
        // =====================================================

        /// <summary>
        /// Creates a standard failed analysis result.
        /// </summary>
        private static RequirementAnalysis CreateErrorAnalysis(string errorMessage, DateTime startedUtc)
        {
            return new RequirementAnalysis
            {
                IsAnalyzed = false,
                ErrorMessage = errorMessage,
                OriginalQualityScore = 0,
                ImprovedQualityScore = null,
                Issues = new List<AnalysisIssue>(),
                Recommendations = new List<AnalysisRecommendation>(),
                FreeformFeedback = string.Empty,
                ImprovedRequirement = string.Empty,
                HallucinationCheck = "UNKNOWN",
                Timestamp = DateTime.UtcNow,
                AnalysisDurationSeconds = (DateTime.UtcNow - startedUtc).TotalSeconds
            };
        }

        /// <summary>
        /// Applies an optional self-reflection pass to improve response quality.
        /// </summary>
        private async Task<string> ApplySelfReflectionAsync(
            string initialResponse,
            string originalContextPrompt,
            string requirementId,
            CancellationToken cancellationToken)
        {
            try
            {
                var reflectionPrompt =
                    "Review the analysis below against the original task. " +
                    "If the analysis already follows the requested format and remains grounded in the provided text, return EXACTLY: APPROVED. " +
                    "Otherwise return a corrected version in the same required output format.\n\n" +
                    "ORIGINAL TASK:\n" + originalContextPrompt + "\n\n" +
                    "CURRENT ANALYSIS:\n" + initialResponse;

                var reflectionResponse = await _llmService.GenerateAsync(reflectionPrompt, cancellationToken);

                if (string.IsNullOrWhiteSpace(reflectionResponse))
                    return initialResponse;

                if (reflectionResponse.Trim().Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
                    return initialResponse;

                _logger.LogInformation("[RequirementAnalysisService] Self-reflection returned a revised response for {RequirementId}", requirementId);
                return reflectionResponse;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementAnalysisService] Self-reflection failed for {RequirementId}. Using original response.", requirementId);
                return initialResponse;
            }
        }

        /// <summary>
        /// Build a text description of the active verification assumptions for a requirement.
        /// </summary>
        private string? GetVerificationAssumptionsText(Requirement requirement)
        {
            if (requirement?.SelectedAssumptionKeys == null || !requirement.SelectedAssumptionKeys.Any())
                return null;

            try
            {
                var catalog = DefaultsHelper.LoadProjectDefaultsTemplate();
                if (catalog?.Items == null)
                    return null;

                var selectedAssumptions = catalog.Items
                    .Where(item => requirement.SelectedAssumptionKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (!selectedAssumptions.Any())
                    return null;

                var lines = new List<string>
                {
                    $"Verification Method: {requirement.Method}",
                    "Selected Assumptions:"
                };

                foreach (var assumption in selectedAssumptions)
                {
                    var promptText = !string.IsNullOrWhiteSpace(assumption.ContentLine)
                        ? assumption.ContentLine
                        : assumption.Name;

                    lines.Add($"• {promptText}");
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[RequirementAnalysisService] Failed to load verification assumptions");
                return null;
            }
        }

        /// <summary>
        /// Removes malformed or unusable recommendations rather than failing the whole analysis.
        /// </summary>
        private void ValidateRecommendationQuality(RequirementAnalysis analysis, string requirementId)
        {
            if (analysis.Recommendations == null || analysis.Recommendations.Count == 0)
                return;

            for (int i = analysis.Recommendations.Count - 1; i >= 0; i--)
            {
                var recommendation = analysis.Recommendations[i];
                bool remove =
                    string.IsNullOrWhiteSpace(recommendation.Category) ||
                    string.IsNullOrWhiteSpace(recommendation.Description) ||
                    string.IsNullOrWhiteSpace(recommendation.SuggestedEdit);

                if (remove)
                {
                    _logger.LogWarning(
                        "[RequirementAnalysisService] Removing invalid recommendation {Index} for {RequirementId}",
                        i,
                        requirementId);

                    analysis.Recommendations.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Simple fallback ATP detection for requirements that look procedure-driven.
        /// </summary>
        private static bool DetectATPContentFallback(Requirement requirement)
        {
            var content = $"{requirement.Name} {requirement.Description}".ToLowerInvariant();

            string[] indicators =
            {
                "test procedure",
                "automated test",
                "test step",
                "verify",
                "validate",
                "connect",
                "apply",
                "measure",
                "check",
                "ensure",
                "configure"
            };

            return indicators.Any(indicator => content.Contains(indicator));
        }

        /// <summary>
        /// Analyze overall testing coverage.
        /// </summary>
        private static TestingCoverageAnalysis AnalyzeTestingCoverage(
            IList<Requirement> requirements,
            IList<DerivedCapability> derivedCapabilities)
        {
            var analysis = new TestingCoverageAnalysis();

            if (requirements.Count == 0)
            {
                analysis.CoveragePercentage = 1.0;
                return analysis;
            }

            var coveredIds = new HashSet<string>(
                derivedCapabilities
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .Select(c => c.Id!));

            foreach (var requirement in requirements)
            {
                if (!coveredIds.Contains(requirement.Item ?? string.Empty))
                {
                    analysis.UncoveredRequirements.Add(requirement.Item ?? "Unknown");
                }
            }

            analysis.CoveragePercentage = (double)(requirements.Count - analysis.UncoveredRequirements.Count) / requirements.Count;

            if (analysis.CoveragePercentage < 0.8)
            {
                analysis.TestingGaps.Add("Less than 80% of requirements appear to have derived testing coverage.");
            }

            if (derivedCapabilities.Count == 0)
            {
                analysis.TestingGaps.Add("No derived capabilities were found from the current requirement set.");
            }

            return analysis;
        }

        /// <summary>
        /// Convert derivation and gap-analysis results into UI-friendly validation issues.
        /// </summary>
        private static List<ValidationIssue> AnalyzeValidationIssues(
            IList<RequirementDerivationAnalysis> derivationResults,
            RequirementGapAnalysisResult gapAnalysis)
        {
            var issues = new List<ValidationIssue>();

            foreach (var derivation in derivationResults)
            {
                if (derivation.DerivationQuality < 0.5)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Description = $"Low derivation quality ({derivation.DerivationQuality:F2}) for requirement {derivation.AnalyzedRequirement.Item}",
                        RequirementId = derivation.AnalyzedRequirement.Item,
                        Category = "Quality"
                    });
                }

                foreach (var issue in derivation.DerivationIssues)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Description = issue,
                        RequirementId = derivation.AnalyzedRequirement.Item,
                        Category = "Derivation"
                    });
                }
            }

            if (gapAnalysis.IsSuccessful && gapAnalysis.GapAnalysisResult != null)
            {
                var severeGaps = gapAnalysis.GapAnalysisResult.UncoveredCapabilities?
                    .Where(g => g.Severity >= GapSeverity.High)
                    .ToList();

                if (severeGaps != null)
                {
                    foreach (var gap in severeGaps)
                    {
                        issues.Add(new ValidationIssue
                        {
                            Severity = gap.Severity == GapSeverity.High ? ValidationSeverity.Critical : ValidationSeverity.Error,
                            Description = gap.Recommendation,
                            Category = "Gap Analysis"
                        });
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Generate workflow recommendations from derivation and gap analysis.
        /// </summary>
        private static List<string> GenerateTestingWorkflowRecommendations(
            IList<RequirementDerivationAnalysis> derivationResults,
            RequirementGapAnalysisResult gapAnalysis)
        {
            var recommendations = new List<string>();

            foreach (var derivation in derivationResults)
            {
                recommendations.AddRange(derivation.Recommendations);
            }

            if (gapAnalysis.IsSuccessful && gapAnalysis.GapAnalysisResult?.UncoveredCapabilities?.Any() == true)
            {
                recommendations.AddRange(
                    gapAnalysis.GapAnalysisResult.UncoveredCapabilities.Select(c => c.Recommendation));
            }

            var lowQualityCount = derivationResults.Count(d => d.DerivationQuality < 0.7);
            if (lowQualityCount > 0)
            {
                recommendations.Add($"Review {lowQualityCount} requirement(s) with low derivation quality.");
            }

            return recommendations.Distinct().ToList();
        }

        /// <summary>
        /// Calculate a single validation score for the workflow.
        /// </summary>
        private static double CalculateOverallValidationScore(TestingWorkflowValidationResult result)
        {
            double score = 1.0;

            var coverageScore = result.CoverageAnalysis?.CoveragePercentage ?? 0.0;
            score *= (0.4 + 0.6 * coverageScore);

            int criticalIssues = result.Issues.Count(i => i.Severity == ValidationSeverity.Critical);
            int errorIssues = result.Issues.Count(i => i.Severity == ValidationSeverity.Error);

            score *= Math.Max(0.0, 1.0 - (criticalIssues * 0.2 + errorIssues * 0.1));

            return Math.Max(0.0, Math.Min(1.0, score));
        }
        private ITextGenerationService GetEffectiveTextGenerationService()
        {
            _logger.LogInformation(
                "[RequirementAnalysisService:{InstanceId}] GetEffectiveTextGenerationService called. Stored WorkspaceName={Workspace}, Stored AnythingLLMSlug={Slug}, AnythingLlmServiceNull={ServiceNull}",
                _instanceId,
                _projectWorkspaceName ?? "<none>",
                _anythingLlmWorkspaceSlug ?? "<none>",
                _anythingLlmService == null);

            if (!string.IsNullOrWhiteSpace(_anythingLlmWorkspaceSlug) && _anythingLlmService != null)
            {
                _logger.LogInformation(
                    "[RequirementAnalysisService] Using slug-aware AnythingLLMTextGenerationService for workspace slug '{Slug}'",
                    _anythingLlmWorkspaceSlug);

                return new AnythingLLMTextGenerationService(_anythingLlmService, _anythingLlmWorkspaceSlug);
            }

            _logger.LogInformation(
                "[RequirementAnalysisService] Using default injected text generation service (slug not available)");

            return _llmService;
        }
    }
}