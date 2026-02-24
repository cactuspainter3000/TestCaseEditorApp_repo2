using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services; // For Requirements domain interface

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Categorizes why RAG analysis failed
    /// </summary>
    public enum RAGFailureReason
    {
        ServiceNotAvailable,
        WorkspaceNotConfigured,
        WorkspaceCreationFailed,
        ThreadCreationFailed,
        LLMRequestFailed,
        Timeout,
        EmptyResponse,
        ConfigurationTimeout,
        UnknownError
    }

    /// <summary>
    /// Service for analyzing requirement quality using LLM.
    /// Generates structured analysis with quality scores, issues, and recommendations.
    /// </summary>
    public sealed class RequirementAnalysisService : 
        TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService
    {
        private readonly ITextGenerationService _llmService;
        private readonly RequirementAnalysisPromptBuilder _promptBuilder;
        private readonly LlmServiceHealthMonitor? _healthMonitor;
        private readonly RequirementAnalysisCache? _cache;
        private readonly AnythingLLMService? _anythingLLMService;
        private readonly ResponseParserManager _parserManager;
        
        // TASK 4.4: Enhanced derivation analysis services
        private readonly ISystemCapabilityDerivationService? _derivationService;
        private readonly IRequirementGapAnalyzer? _gapAnalyzer;
        
        private string? _cachedSystemMessage;
        private string? _currentWorkspaceSlug;
        private string? _projectWorkspaceName;
        private bool _ragSyncInProgress = false;
        
        // Instance-based cache for workspace prompt validation to avoid repeated checks
        private bool? _workspaceSystemPromptConfigured;
        private DateTime _lastWorkspaceValidation = DateTime.MinValue;
        private static readonly TimeSpan _workspaceValidationCooldown = TimeSpan.FromMinutes(5);
        
        /// <summary>
        /// Enable/disable self-reflection feature. When enabled, the LLM will review its own responses for quality.
        /// </summary>
        public bool EnableSelfReflection { get; set; } = false;

        /// <summary>
        /// Enable/disable caching of analysis results. When enabled, identical requirement content will use cached results.
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Enable/disable thread cleanup after analysis. When enabled, analysis threads are deleted after completion.
        /// </summary>
        public bool EnableThreadCleanup { get; set; } = true;

        /// <summary>
        /// Timeout for LLM analysis operations. Default is 90 seconds to allow for RAG processing.
        /// </summary>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>
        /// Current health status of the LLM service (null if no health monitor configured)
        /// </summary>
        public LlmServiceHealthMonitor.HealthReport? ServiceHealth => _healthMonitor?.CurrentHealth;

        /// <summary>
        /// Whether the service is currently using fallback mode
        /// </summary>
        public bool IsUsingFallback => _healthMonitor?.IsUsingFallback ?? false;

        /// <summary>
        /// Sets the workspace context for project-specific analysis
        /// </summary>
        /// <param name="workspaceName">Name of the project workspace to use for analysis</param>
        public void SetWorkspaceContext(string? workspaceName)
        {
            // Skip if workspace context hasn't changed to avoid unnecessary work
            if (string.Equals(_projectWorkspaceName, workspaceName, StringComparison.OrdinalIgnoreCase))
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Workspace context unchanged: {workspaceName ?? "<none>"}");
                return;
            }
            
            _projectWorkspaceName = workspaceName;
            // Clear cached workspace slug when context actually changes
            _currentWorkspaceSlug = null;
            TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Workspace context set to: {workspaceName ?? "<none>"}");
            
            // Auto-sync RAG documents if workspace context is set (project opened)
            if (!string.IsNullOrEmpty(workspaceName))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EnsureRagDocumentsAreSyncedAsync();
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Failed to auto-sync RAG documents: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Current cache statistics (null if no cache configured)
        /// </summary>
        public RequirementAnalysisCache.CacheStatistics? CacheStatistics => _cache?.GetStatistics();

        /// <summary>
        /// Initializes a new instance of RequirementAnalysisService with proper dependency injection.
        /// Task 4.4: Extended with derivation analysis capabilities for enhanced testing workflow validation.
        /// </summary>
        /// <param name="llmService">Text generation service for LLM communication</param>
        /// <param name="promptBuilder">Prompt builder for requirement analysis prompts</param>
        /// <param name="parserManager">Parser manager for response parsing</param>
        /// <param name="healthMonitor">Optional health monitor for service reliability</param>
        /// <param name="cache">Optional cache for analysis results</param>
        /// <param name="anythingLLMService">Optional AnythingLLM service for enhanced features</param>
        /// <param name="derivationService">Optional system capability derivation service for ATP analysis</param>
        /// <param name="gapAnalyzer">Optional gap analyzer for capability vs requirements comparison</param>
        public RequirementAnalysisService(
            ITextGenerationService llmService,
            RequirementAnalysisPromptBuilder promptBuilder,
            ResponseParserManager parserManager,
            LlmServiceHealthMonitor? healthMonitor = null,
            RequirementAnalysisCache? cache = null,
            AnythingLLMService? anythingLLMService = null,
            ISystemCapabilityDerivationService? derivationService = null,
            IRequirementGapAnalyzer? gapAnalyzer = null)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _parserManager = parserManager ?? throw new ArgumentNullException(nameof(parserManager));
            _healthMonitor = healthMonitor;
            _cache = cache;
            _anythingLLMService = anythingLLMService;
            _derivationService = derivationService;
            _gapAnalyzer = gapAnalyzer;
        }

        /// <summary>
        /// Analyze a single requirement's quality with streaming response and progress updates.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <param name="onPartialResult">Callback for partial analysis results as they arrive</param>
        /// <param name="onProgressUpdate">Callback for progress status updates</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>RequirementAnalysis with quality score, issues, and recommendations</returns>
        public async Task<RequirementAnalysis> AnalyzeRequirementWithStreamingAsync(
            Requirement requirement,
            Action<string>? onPartialResult = null,
            Action<string>? onProgressUpdate = null,
            CancellationToken cancellationToken = default)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            var startTime = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Starting analysis for requirement {requirement.Item} at {startTime:HH:mm:ss.fff}");
            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] RAG Service Available: {_anythingLLMService != null}");
            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Timeout Setting: {AnalysisTimeout.TotalSeconds}s");

            onProgressUpdate?.Invoke("Starting requirement analysis...");

            // Check cache first if enabled
            if (EnableCaching && _cache != null)
            {
                var cacheCheckStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Checking cache at {cacheCheckStart:HH:mm:ss.fff}");
                onProgressUpdate?.Invoke("Checking cache...");
                if (_cache.TryGet(requirement, out var cachedAnalysis) && cachedAnalysis != null)
                {
                    var cacheTime = DateTime.UtcNow - cacheCheckStart;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Cache HIT - returned in {cacheTime.TotalMilliseconds}ms");
                    onProgressUpdate?.Invoke("Using cached analysis");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Using CACHED analysis for requirement {requirement.Item}");
                    return cachedAnalysis;
                }
                var cacheMissTime = DateTime.UtcNow - cacheCheckStart;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Cache MISS - check took {cacheMissTime.TotalMilliseconds}ms");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Cache disabled or unavailable");
            }

            var analysisStartTime = DateTime.UtcNow;
            CancellationTokenSource? timeoutCts = null;

            try
            {
                var prepStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Starting preparation at {prepStart:HH:mm:ss.fff}");
                onProgressUpdate?.Invoke("Preparing analysis context...");
                
                // Get verification assumptions for context
                var verificationAssumptions = GetVerificationAssumptionsText(requirement);
                var prepTime = DateTime.UtcNow - prepStart;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Preparation completed in {prepTime.TotalMilliseconds}ms");

                // Build the prompt using optimized system+context approach for better performance
                string response;
                
                // Cache system message for reuse across multiple requirements
                if (_cachedSystemMessage == null)
                {
                    _cachedSystemMessage = _promptBuilder.GetSystemPrompt();
                    TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementAnalysis] Cached system message for session reuse");
                }

                var contextPrompt = _promptBuilder.BuildContextPrompt(
                    requirement.Item ?? "UNKNOWN",
                    requirement.Name ?? string.Empty,
                    requirement.Description ?? string.Empty,
                    requirement.Tables,
                    requirement.LooseContent,
                    verificationAssumptions);

                onProgressUpdate?.Invoke("Sending analysis request to AI...");
                
                // Send to LLM with interactive timeout handling
                response = await SendLLMWithInteractiveTimeoutAsync(
                    requirement, 
                    contextPrompt, 
                    cancellationToken, 
                    onPartialResult, 
                    onProgressUpdate);

                onProgressUpdate?.Invoke("Processing analysis results...");

                if (string.IsNullOrWhiteSpace(response))
                {
                    return CreateErrorAnalysis("LLM returned empty response");
                }

                // Apply self-reflection to improve response quality (if enabled)
                var reflectedResponse = response;
                if (EnableSelfReflection)
                {
                    onProgressUpdate?.Invoke("Applying self-reflection for quality improvement...");
                    reflectedResponse = await ApplySelfReflectionAsync(response, contextPrompt, requirement.Item ?? "UNKNOWN", cancellationToken);
                }
                
                // Parse response using parser manager
                var analysis = _parserManager.ParseResponse(reflectedResponse, requirement.Item ?? "UNKNOWN");
                
                // Check if parsing was successful
                if (analysis == null)
                {
                    return CreateErrorAnalysis("Failed to parse LLM response");
                }

                // Set timestamp and cache if enabled
                analysis.Timestamp = DateTime.Now;
                // analysis.AnalysisDuration = DateTime.UtcNow - analysisStartTime; // Property doesn't exist yet

                if (EnableCaching && _cache != null)
                {
                    onProgressUpdate?.Invoke("Caching analysis results...");
                    // _cache.Store(requirement, analysis); // Method doesn't exist yet
                }

                onProgressUpdate?.Invoke("Analysis complete");
                var totalAnalysisTime = DateTime.UtcNow - startTime;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Analysis completed successfully in {totalAnalysisTime.TotalSeconds:F1}s");
                return analysis;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                var totalTime = DateTime.UtcNow - startTime;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Operation cancelled after {totalTime.TotalSeconds:F1}s");
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Timeout triggered: {timeoutCts?.Token.IsCancellationRequested == true}");
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] User cancelled: {cancellationToken.IsCancellationRequested}");
                
                if (timeoutCts?.Token.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    var timeoutMessage = $"Analysis timed out after {AnalysisTimeout.TotalSeconds} seconds";
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] TIMEOUT: {timeoutMessage}");
                    onProgressUpdate?.Invoke(timeoutMessage);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Analysis timed out for requirement {requirement.Item} after {AnalysisTimeout.TotalSeconds}s");
                    return CreateErrorAnalysis($"Analysis timed out after {AnalysisTimeout.TotalSeconds} seconds. This may indicate an issue with the LLM service.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] USER CANCELLED");
                    onProgressUpdate?.Invoke("Analysis cancelled");
                    return CreateErrorAnalysis("Analysis was cancelled");
                }
            }
            catch (JsonException ex)
            {
                onProgressUpdate?.Invoke("Error parsing analysis results");
                return CreateErrorAnalysis($"Failed to parse LLM response as JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                onProgressUpdate?.Invoke($"Analysis failed: {ex.Message}");
                return CreateErrorAnalysis($"Analysis failed: {ex.Message}");
            }
            finally
            {
                // Dispose timeout cancellation token
                timeoutCts?.Dispose();
            }
        }

        /// <summary>
        /// Analyze a single requirement's quality.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>RequirementAnalysis with quality score, issues, and recommendations</returns>
        public async Task<RequirementAnalysis> AnalyzeRequirementAsync(
            Requirement requirement,
            CancellationToken cancellationToken = default)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] ENTERING analysis for requirement {requirement.Item}");

            // Check cache first if enabled
            if (EnableCaching && _cache != null)
            {
                if (_cache.TryGet(requirement, out var cachedAnalysis) && cachedAnalysis != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Using CACHED analysis for requirement {requirement.Item}");
                    return cachedAnalysis;
                }
            }

            var analysisStartTime = DateTime.UtcNow;

            try
            {
                // Try RAG-based analysis first (faster and more context-aware)
                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Attempting RAG analysis for requirement {requirement.Item}");
                var ragResult = await TryRagAnalysisAsync(requirement, null, null, cancellationToken);
                
                if (ragResult.success)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Using RAG response for requirement {requirement.Item}, length: {ragResult.response?.Length ?? 0}");
                    
                    // Log raw RAG response for debugging (visible level for parsing debug)
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG RESPONSE DEBUG] Raw response for {requirement.Item}:\n--- START RAG RESPONSE ---\n{ragResult.response}\n--- END RAG RESPONSE ---");
                    
                    // Parse response using appropriate parser (JSON or Natural Language)
                    var ragAnalysis = _parserManager.ParseResponse(ragResult.response ?? string.Empty, requirement.Item ?? "UNKNOWN");
                    
                    if (ragAnalysis != null)
                    {
                        ragAnalysis.Timestamp = DateTime.Now;
                        
                        // Validate that recommendations have required fields  
                        ValidateRecommendationQuality(ragAnalysis, requirement.Item ?? "UNKNOWN");
                        
                        // Cache the successful RAG analysis result
                        if (EnableCaching && _cache != null && ragAnalysis.IsAnalyzed)
                        {
                            var analysisDuration = DateTime.UtcNow - analysisStartTime;
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Would cache RAG analysis result for {requirement.Item} (duration: {analysisDuration.TotalMilliseconds:F0}ms)");
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] RAG natural language parsing successful for {requirement.Item}");
                        return ragAnalysis;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] RAG natural language parsing failed for {requirement.Item}, falling back to LLM");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] RAG analysis failed for requirement {requirement.Item}, falling back to direct LLM");
                }
                
                // Fallback to direct LLM analysis
                // Get verification assumptions for context
                var verificationAssumptions = GetVerificationAssumptionsText(requirement);

                // Build the prompt using optimized system+context approach for better performance
                string response;
                
                // Use optimized system+context approach for full analysis
                // Cache system message for reuse across multiple requirements
                if (_cachedSystemMessage == null)
                {
                    _cachedSystemMessage = _promptBuilder.GetSystemPrompt();
                    TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementAnalysis] Cached system message for session reuse");
                }

                var contextPrompt = _promptBuilder.BuildContextPrompt(
                    requirement.Item ?? "UNKNOWN",
                    requirement.Name ?? string.Empty,
                    requirement.Description ?? string.Empty,
                    requirement.Tables,
                    requirement.LooseContent,
                    verificationAssumptions);

                // Use optimized system+context call - much faster for multiple requirements
                response = await _llmService.GenerateWithSystemAsync(_cachedSystemMessage, contextPrompt, cancellationToken);

                // Debug logging to verify what's being included
                var tablesCount = requirement.Tables?.Count ?? 0;
                var looseParasCount = requirement.LooseContent?.Paragraphs?.Count ?? 0;
                var looseTablesCount = requirement.LooseContent?.Tables?.Count ?? 0;
                var hasAssumptions = !string.IsNullOrWhiteSpace(verificationAssumptions);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysis] Requirement {requirement.Item}: Tables={tablesCount}, LooseParas={looseParasCount}, LooseTables={looseTablesCount}, Assumptions={hasAssumptions}, OptimizedMode=true");

                // Call LLM
                if (string.IsNullOrWhiteSpace(response))
                {
                    return CreateErrorAnalysis("LLM returned empty response");
                }

                // Apply self-reflection to improve response quality (if enabled)
                var reflectedResponse = EnableSelfReflection 
                    ? await ApplySelfReflectionAsync(response, contextPrompt, requirement.Item ?? "UNKNOWN", cancellationToken)
                    : response;
                    
                if (EnableSelfReflection)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Self-reflection enabled for {requirement.Item}. Original length: {response?.Length ?? 0}, Reflected length: {reflectedResponse?.Length ?? 0}");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Self-reflection disabled for {requirement.Item}, using original response");
                }
                
                // Parse response using parser manager
                var analysis = _parserManager.ParseResponse(reflectedResponse ?? string.Empty, requirement.Item ?? "UNKNOWN");
                
                // Check if parsing was successful
                if (analysis == null)
                {
                    return CreateErrorAnalysis("Failed to parse LLM response");
                }

                // Check for self-reported fabrication
                if (!string.IsNullOrEmpty(analysis.HallucinationCheck) && 
                    analysis.HallucinationCheck.Contains("FABRICATED_DETAILS", StringComparison.OrdinalIgnoreCase))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] LLM self-reported fabrication for {requirement.Item}: {analysis.HallucinationCheck}");
                    
                    // TODO: Add retry logic for JSON path (requires streaming method with progress updates)
                    // For now, use fallback warning mode
                    analysis.ErrorMessage = "🚨 CRITICAL WARNING: AI FABRICATED TECHNICAL DETAILS NOT IN ORIGINAL REQUIREMENT 🚨\n\n" +
                                           "This analysis contains invented specifications that could mislead engineers. " +
                                           "All recommendations have been removed for safety. Manual review required.";
                    analysis.OriginalQualityScore = Math.Max(1, analysis.OriginalQualityScore - 3); // Reduce quality score as penalty
                    
                    // Clear all recommendations to prevent misleading guidance
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Removing {analysis.Recommendations?.Count ?? 0} recommendations due to fabrication");
                    analysis.Recommendations?.Clear();
                    analysis.Recommendations = new List<AnalysisRecommendation>
                    {
                        new AnalysisRecommendation
                        {
                            Category = "Reliability",
                            Description = "AI analysis detected fabricated content. Manual review recommended.",
                            SuggestedEdit = $"[MANUAL REVIEW REQUIRED] The original requirement '{requirement.Description?.Trim()}' may need clarification, but AI-generated suggestions contained fabricated details."
                        }
                    };
                }
                else
                {
                    // Even if LLM claims no fabrication, scan for common fabrication patterns
                    var fabricationDetected = DetectLikelyFabrication(analysis, requirement.Description ?? "");
                    if (fabricationDetected)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Pattern-based fabrication detection triggered for {requirement.Item}");
                        
                        // TODO: Add retry logic for pattern-detected fabrication (requires streaming method)
                        // For now, use warning mode
                        analysis.ErrorMessage = "⚠️  CAUTION: POSSIBLE AI FABRICATION DETECTED  ⚠️\n\n" +
                                               "AI analysis may contain technical details not in the original requirement. " +
                                               "Please verify all recommendations against the source material.";
                        analysis.OriginalQualityScore = Math.Max(1, analysis.OriginalQualityScore - 2); // Smaller penalty for suspected fabrication
                    }
                }

                // Log what we got from the LLM for debugging
                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] LLM response for {requirement.Item}: OriginalQualityScore={analysis.OriginalQualityScore}, Issues={analysis.Issues?.Count ?? 0}, Recommendations={analysis.Recommendations?.Count ?? 0}, HallucinationCheck={analysis.HallucinationCheck}");
                
                // Validate that recommendations have required fields (now cleans up invalid ones)
                ValidateRecommendationQuality(analysis, requirement.Item ?? "UNKNOWN");

                // Set timestamp
                analysis.Timestamp = DateTime.Now;

                // Cache the successful analysis result if caching is enabled
                if (EnableCaching && _cache != null && analysis.IsAnalyzed)
                {
                    var analysisDuration = DateTime.UtcNow - analysisStartTime;
                    // _cache.Set(requirement, analysis, analysisDuration); // Method doesn't exist yet
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Would cache analysis result for {requirement.Item} (duration: {analysisDuration.TotalMilliseconds:F0}ms)");
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] EXITING analysis for requirement {requirement.Item} - Success: {analysis.IsAnalyzed}");

                return analysis;
            }
            catch (OperationCanceledException)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Analysis CANCELLED for requirement {requirement.Item}");
                return CreateErrorAnalysis("Analysis was cancelled");
            }
            catch (JsonException ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RequirementAnalysisService] JSON PARSE ERROR for requirement {requirement.Item}");
                return CreateErrorAnalysis($"Failed to parse LLM response as JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RequirementAnalysisService] GENERAL ERROR for requirement {requirement.Item}");
                return CreateErrorAnalysis($"Analysis failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Create an error analysis result when analysis fails.
        /// </summary>
        private RequirementAnalysis CreateErrorAnalysis(string errorMessage)
        {
            return new RequirementAnalysis
            {
                IsAnalyzed = false,
                ErrorMessage = errorMessage,
                OriginalQualityScore = 0,
                Issues = new System.Collections.Generic.List<AnalysisIssue>(),
                Recommendations = new System.Collections.Generic.List<AnalysisRecommendation>(),
                FreeformFeedback = string.Empty,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Generate the analysis prompt for a requirement (for debugging/inspection).
        /// This lets you see exactly what text is being sent to the LLM.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <returns>The exact prompt that would be sent to the LLM</returns>
        /// <summary>
        /// Optimizes prompt sending based on workspace configuration to reduce message size.
        /// If workspace has system prompt configured, sends only context. Otherwise sends full prompt.
        /// </summary>
        private async Task<string> GetOptimizedPromptForWorkspaceAsync(AnythingLLMService anythingLlmService, string workspaceSlug, string contextPrompt, CancellationToken cancellationToken)
        {
            try
            {
                // Check if we need to validate workspace prompt configuration (with cooldown)
                if (!_workspaceSystemPromptConfigured.HasValue || DateTime.UtcNow - _lastWorkspaceValidation > _workspaceValidationCooldown)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysis] Validating workspace system prompt configuration for '{workspaceSlug}'");
                    _workspaceSystemPromptConfigured = await anythingLlmService.ValidateWorkspaceSystemPromptAsync(workspaceSlug, cancellationToken);
                    _lastWorkspaceValidation = DateTime.UtcNow;
                }
                
                if (_workspaceSystemPromptConfigured == true)
                {
                    // Workspace has system prompt configured - send only context (saves ~793 lines per request)
                    var optimizedLength = contextPrompt.Length;
                    var savedBytes = (_cachedSystemMessage?.Length ?? 0) + 4; // +4 for "\\n\\n"
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysis] Using optimized messaging - sending {optimizedLength} chars instead of {optimizedLength + savedBytes} (saved {savedBytes} chars)");
                    return contextPrompt;
                }
                else
                {
                    // Workspace doesn't have system prompt - send full prompt for compatibility
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysis] Using full prompt - workspace system prompt not configured");
                    return $"{_cachedSystemMessage}\\n\\n{contextPrompt}";
                }
            }
            catch (Exception ex)
            {
                // Fallback to full prompt on any error
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysis] Error optimizing prompt for workspace '{workspaceSlug}' - falling back to full prompt: {ex.Message}");
                return $"{_cachedSystemMessage}\\n\\n{contextPrompt}";
            }
        }

        public string GeneratePromptForInspection(Requirement requirement)
        {
            if (requirement == null)
                return "ERROR: Requirement is null";

            // Get verification assumptions for context
            var verificationAssumptions = GetVerificationAssumptionsText(requirement);

            var systemPrompt = _promptBuilder.GetSystemPrompt();
            var contextPrompt = _promptBuilder.BuildContextPrompt(
                requirement.Item ?? "UNKNOWN",
                requirement.Name ?? string.Empty,
                requirement.Description ?? string.Empty,
                requirement.Tables,
                requirement.LooseContent,
                verificationAssumptions);
            
            var prompt = systemPrompt + "\n\n" + contextPrompt;

            return prompt;
        }

        /// <summary>
        /// Apply self-reflection to improve the initial LLM response quality.
        /// Sends the response back to the LLM asking it to review against the original criteria.
        /// </summary>
        private async Task<string> ApplySelfReflectionAsync(string initialResponse, string originalPrompt, string requirementItem, CancellationToken cancellationToken)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[RequirementAnalysisService] Applying self-reflection to improve response quality");
                
                // Write detailed debugging to file
                await WriteDebugToFileAsync(requirementItem, "ORIGINAL_PROMPT", originalPrompt);
                await WriteDebugToFileAsync(requirementItem, "FIRST_RESPONSE", initialResponse);

                // Build self-reflection prompt
                var reflectionPrompt = BuildSelfReflectionPrompt(initialResponse, originalPrompt);
                await WriteDebugToFileAsync(requirementItem, "REFLECTION_PROMPT", reflectionPrompt);

                // Get LLM's self-assessment and potential improvements
                var reflectionResponse = await _llmService.GenerateAsync(reflectionPrompt, cancellationToken);

                if (string.IsNullOrWhiteSpace(reflectionResponse))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[RequirementAnalysisService] Self-reflection returned empty response, using original");
                    return initialResponse;
                }

                await WriteDebugToFileAsync(requirementItem, "REFLECTION_RESPONSE", reflectionResponse);

                // Parse the reflection response to see if LLM suggested improvements
                var shouldImprove = ShouldImproveBasedOnReflection(reflectionResponse);
                
                if (shouldImprove)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[RequirementAnalysisService] LLM identified improvements needed, generating revised analysis");
                    
                    // Ask for improved version
                    var improvementPrompt = BuildImprovementPrompt(originalPrompt, reflectionResponse);
                    await WriteDebugToFileAsync(requirementItem, "IMPROVEMENT_PROMPT", improvementPrompt);
                    
                    var improvedResponse = await _llmService.GenerateAsync(improvementPrompt, cancellationToken);
                    
                    if (!string.IsNullOrWhiteSpace(improvedResponse))
                    {
                        await WriteDebugToFileAsync(requirementItem, "SECOND_RESPONSE", improvedResponse);
                        
                        TestCaseEditorApp.Services.Logging.Log.Info("[RequirementAnalysisService] Using improved analysis from self-reflection");
                        return improvedResponse;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info("[RequirementAnalysisService] Self-reflection complete, using original response");
                return initialResponse;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Self-reflection failed: {ex.Message}, using original response");
                return initialResponse;
            }
        }

        /// <summary>
        /// Build the self-reflection prompt asking LLM to review its own work.
        /// </summary>
        private string BuildSelfReflectionPrompt(string initialResponse, string originalPrompt)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("Please review the analysis you just provided against the original criteria.");
            sb.AppendLine();
            sb.AppendLine("ORIGINAL TASK:");
            sb.AppendLine(originalPrompt);
            sb.AppendLine();
            sb.AppendLine("YOUR ANALYSIS:");
            sb.AppendLine(initialResponse);
            sb.AppendLine();
            sb.AppendLine("SELF-REVIEW QUESTIONS:");
            sb.AppendLine("1. Does the analysis follow the exact JSON format required?");
            sb.AppendLine("2. Are all recommendations specific and actionable (not vague or placeholder text)?");
            sb.AppendLine("3. Does each recommendation include a complete 'SuggestedEdit' with actual improved requirement text?");
            sb.AppendLine("4. Are the issues and recommendations based on the actual requirement content (not example terms)?");
            sb.AppendLine("5. Did I properly consider all tables and supplemental information provided?");
            sb.AppendLine("6. Are there any placeholder texts like '[enter details]' or 'example text' that should be replaced?");
            sb.AppendLine();
            sb.AppendLine("Respond with either:");
            sb.AppendLine("- 'APPROVED' if the analysis meets all criteria and is ready to use");
            sb.AppendLine("- 'NEEDS_IMPROVEMENT: [specific issues found]' if improvements are needed");
            
            return sb.ToString();
        }

        /// <summary>
        /// Analyze the reflection response to determine if improvements are needed.
        /// </summary>
        private bool ShouldImproveBasedOnReflection(string reflectionResponse)
        {
            if (string.IsNullOrWhiteSpace(reflectionResponse))
                return false;

            var response = reflectionResponse.Trim().ToUpperInvariant();
            
            // Look for improvement indicators
            return response.Contains("NEEDS_IMPROVEMENT") || 
                   response.Contains("PLACEHOLDER") ||
                   response.Contains("IMPROVE") ||
                   response.Contains("MISSING") ||
                   response.Contains("INCOMPLETE") ||
                   response.Contains("VAGUE") ||
                   (!response.Contains("APPROVED") && response.Length > 50); // Long response likely indicates issues
        }

        /// <summary>
        /// Build prompt asking for improved analysis based on self-reflection feedback.
        /// </summary>
        private string BuildImprovementPrompt(string originalPrompt, string reflectionFeedback)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("Based on your self-review, please provide an improved analysis that addresses the issues you identified.");
            sb.AppendLine();
            sb.AppendLine("ORIGINAL TASK:");
            sb.AppendLine(originalPrompt);
            sb.AppendLine();
            sb.AppendLine("YOUR FEEDBACK:");
            sb.AppendLine(reflectionFeedback);
            sb.AppendLine();
            sb.AppendLine("Please provide the corrected analysis in the exact same JSON format, ensuring:");
            sb.AppendLine("- All recommendations have specific, actionable SuggestedEdit text");
            sb.AppendLine("- No placeholder text or generic examples");
            sb.AppendLine("- Proper consideration of all provided context");
            sb.AppendLine("- Clear, specific improvements based on the actual requirement");
            
            return sb.ToString();
        }

        /// <summary>
        /// Build a text description of the active verification method assumptions for a requirement.
        /// </summary>
        private string? GetVerificationAssumptionsText(Requirement requirement)
        {
            if (requirement?.SelectedAssumptionKeys == null || !requirement.SelectedAssumptionKeys.Any())
                return null;

            try
            {
                // Load the defaults catalog to get assumption details
                var catalog = DefaultsHelper.LoadProjectDefaultsTemplate();
                if (catalog?.Items == null) return null;

                // Find assumptions that match the selected keys
                var selectedAssumptions = catalog.Items
                    .Where(item => requirement.SelectedAssumptionKeys.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (!selectedAssumptions.Any()) return null;

                // Build text description
                var lines = new List<string>();
                lines.Add($"Verification Method: {requirement.Method}");
                lines.Add("Selected Assumptions:");
                
                foreach (var assumption in selectedAssumptions)
                {
                    var promptText = !string.IsNullOrWhiteSpace(assumption.ContentLine) ? assumption.ContentLine : assumption.Name;
                    lines.Add($"• {promptText}");
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysis] Error getting assumptions: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validate that the LLM service is available and responding.
        /// </summary>
        public async Task<bool> ValidateServiceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Use health monitor if available
                if (_healthMonitor != null)
                {
                    var health = await _healthMonitor.CheckHealthAsync(cancellationToken);
                    return health.Status == LlmServiceHealthMonitor.HealthStatus.Healthy || 
                           health.Status == LlmServiceHealthMonitor.HealthStatus.Degraded;
                }

                // Fallback to direct service test
                var testPrompt = "Return only: {\"test\":\"ok\"}";
                var response = await _llmService.GenerateAsync(testPrompt, cancellationToken);
                return !string.IsNullOrWhiteSpace(response);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get detailed service health information including response times and error details.
        /// </summary>
        public async Task<LlmServiceHealthMonitor.HealthReport?> GetDetailedHealthAsync(CancellationToken cancellationToken = default)
        {
            if (_healthMonitor == null)
                return null;

            return await _healthMonitor.CheckHealthAsync(cancellationToken);
        }

        /// <summary>
        /// Invalidate cached analysis for a specific requirement (e.g., when content changes).
        /// </summary>
        public void InvalidateCache(string requirementGlobalId)
        {
            if (_cache == null || !EnableCaching)
                return;

            _cache.Invalidate(requirementGlobalId);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Invalidated cache for requirement {requirementGlobalId}");
        }

        /// <summary>
        /// Clear all cached analysis results.
        /// </summary>
        public void ClearAnalysisCache()
        {
            if (_cache == null)
                return;

            _cache.Clear();
            TestCaseEditorApp.Services.Logging.Log.Info("[RequirementAnalysisService] Cleared all analysis cache");
        }

        /// <summary>
        /// Get comprehensive cache performance statistics.
        /// </summary>
        public RequirementAnalysisCache.CacheStatistics? GetCacheStatistics()
        {
            return _cache?.GetStatistics();
        }

        /// <summary>
        /// Clear the cached system message to force regeneration on next analysis.
        /// Call this if analysis criteria or output format changes.
        /// </summary>
        public void ClearSystemMessageCache()
        {
            _cachedSystemMessage = null;
            TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementAnalysis] Cleared cached system message");
        }

        /// <summary>
        /// Write debugging information to external files for analysis.
        /// </summary>
        private async Task WriteDebugToFileAsync(string requirementItem, string step, string content)
        {
            try
            {
                // Create debug folder on desktop
                var debugFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LLM_Debug");
                Directory.CreateDirectory(debugFolder);
                
                // Clean requirement item for filename
                var cleanItem = string.IsNullOrWhiteSpace(requirementItem) ? "UNKNOWN" : requirementItem.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                var fileName = $"{timestamp}_{cleanItem}_{step}.txt";
                var filePath = Path.Combine(debugFolder, fileName);
                
                var fileContent = $"Requirement: {requirementItem}\nStep: {step}\nTimestamp: {DateTime.Now}\n\n{content}";
                
                await File.WriteAllTextAsync(filePath, fileContent);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Debug file written: {fileName}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Failed to write debug file: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the optimized system+context approach is being used.
        /// </summary>
        public bool IsOptimizedModeAvailable => _llmService.GetType().GetMethod("GenerateWithSystemAsync") != null;

        /// <summary>
        /// Validate that the response contains valid JSON structure.
        /// </summary>
        private bool ValidateJsonFormat(string jsonText, string requirementItem)
        {
            try
            {
                // Attempt to parse as basic JSON first
                using var document = JsonDocument.Parse(jsonText);
                var root = document.RootElement;

                // Check for required top-level fields
                if (!root.TryGetProperty("OriginalQualityScore", out _))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] JSON validation failed for {requirementItem}: Missing OriginalQualityScore");
                    return false;
                }

                if (!root.TryGetProperty("Recommendations", out var recsElement))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] JSON validation failed for {requirementItem}: Missing Recommendations");
                    return false;
                }

                // Ensure Recommendations is an array
                if (recsElement.ValueKind != JsonValueKind.Array)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] JSON validation failed for {requirementItem}: Recommendations is not an array");
                    return false;
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] JSON format validation passed for {requirementItem}");
                return true;
            }
            catch (JsonException ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] JSON validation failed for {requirementItem}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validate that recommendations contain the required fields for blue border display.
        /// </summary>
        private bool ValidateRecommendationQuality(RequirementAnalysis analysis, string requirementItem)
        {
            if (analysis?.Recommendations == null || !analysis.Recommendations.Any())
            {
                // No recommendations is valid - requirement might be perfect or have only issues
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] No recommendations for {requirementItem} - this is valid (may have issues only)");
                return true;
            }

            for (int i = 0; i < analysis.Recommendations.Count; i++)
            {
                var rec = analysis.Recommendations[i];
                bool isRecValid = true;
                
                // Validate required fields
                if (string.IsNullOrWhiteSpace(rec.Category))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Recommendation {i+1} for {requirementItem} missing Category");
                    isRecValid = false;
                }

                if (string.IsNullOrWhiteSpace(rec.Description))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Recommendation {i+1} for {requirementItem} missing Description");
                    isRecValid = false;
                }

                if (string.IsNullOrWhiteSpace(rec.SuggestedEdit))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Recommendation {i+1} for {requirementItem} missing SuggestedEdit - removing from recommendations to prevent display issues");
                    isRecValid = false;
                }

                // Check for placeholder text that indicates LLM didn't provide actual content
                if (!string.IsNullOrEmpty(rec.SuggestedEdit) &&
                    (rec.SuggestedEdit.Contains("[Define this]") || 
                     rec.SuggestedEdit.Contains("[Enter details]") ||
                     rec.SuggestedEdit.StartsWith("EXAMPLE:") ||
                     rec.Description.Contains("example text")))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Recommendation {i+1} for {requirementItem} contains placeholder text - removing from recommendations");
                    isRecValid = false;
                }

                if (!isRecValid)
                {
                    // Remove invalid recommendation instead of failing the whole analysis
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Removing invalid recommendation {i+1} for {requirementItem}");
                    analysis.Recommendations.RemoveAt(i);
                    i--; // Adjust index since we removed an item
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Recommendation quality validation completed for {requirementItem}. Kept {analysis.Recommendations.Count} valid recommendations");
            return true; // Always return true now - we clean up invalid recommendations instead of failing
        }

        /// <summary>
        /// Detect likely fabrication by scanning for technical terms not present in the original requirement.
        /// </summary>
        private bool DetectLikelyFabrication(RequirementAnalysis analysis, string originalRequirement)
        {
            if (analysis?.Recommendations == null || !analysis.Recommendations.Any())
                return false;

            var originalLower = originalRequirement.ToLowerInvariant();
            
            // Common fabricated terms that LLMs often add
            var suspiciousTerms = new[]
            {
                "waveform", "snapshot", "mes", "manufacturing execution",
                "timestamp", "metadata", "protocol", "interface",
                "tcp/ip", "usb", "ethernet", "jtag", "encryption",
                "security protocol", "authentication", "authorization",
                "one bit", "tolerance level", "precision", "accuracy",
                "portable storage", "network storage", "backup",
                "compression", "encoding", "format specification",
                // IEEE standards fabrication detection
                "ieee", "ieee-", "ieee ", "standard ", "specification ",
                "iso ", "ansi ", "mil-std", "jedec", "arinc"
            };

            foreach (var recommendation in analysis.Recommendations)
            {
                var suggestedEdit = recommendation.SuggestedEdit?.ToLowerInvariant() ?? "";
                
                foreach (var term in suspiciousTerms)
                {
                    if (suggestedEdit.Contains(term) && !originalLower.Contains(term))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[FabricationDetection] Found suspicious term '{term}' in suggestion but not in original requirement");
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Try RAG-based analysis using AnythingLLM workspace with uploaded documents.
        /// This method provides faster, more context-aware analysis than traditional prompting.
        /// </summary>
        private async Task<(bool success, string response, RAGFailureReason failureReason, string failureDetails)> TryRagAnalysisAsync(
            Requirement requirement, 
            Action<string>? onPartialResult,
            Action<string>? onProgressUpdate,
            CancellationToken cancellationToken)
        {
            var ragStart = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] TryRagAnalysisAsync started at {ragStart:HH:mm:ss.fff}");
            
            // Check if RAG is available
            if (_anythingLLMService == null)
            {
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] AnythingLLMService is null, returning false");
                var llmProvider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
                var llmEndpoint = Environment.GetEnvironmentVariable("ANYTHINGLLM_ENDPOINT");
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG DIAGNOSTICS] AnythingLLMService not available. LLM_PROVIDER='{llmProvider}', ANYTHINGLLM_ENDPOINT='{llmEndpoint}'");
                return (false, string.Empty, RAGFailureReason.ServiceNotAvailable, "AnythingLLM service not initialized. Verify LLM_PROVIDER is set to 'AnythingLLM'.");
            }

            try
            {
                // Get or create workspace for current project
                var workspaceStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Getting workspace at {workspaceStart:HH:mm:ss.fff}");
                var workspaceSlug = await EnsureWorkspaceConfiguredAsync(onProgressUpdate, cancellationToken);
                var workspaceTime = DateTime.UtcNow - workspaceStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Workspace check completed in {workspaceTime.TotalMilliseconds}ms, slug: '{workspaceSlug}'");
                
                if (string.IsNullOrEmpty(workspaceSlug))
                {
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No workspace slug returned, returning false");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to find or create workspace for project '{_projectWorkspaceName ?? "<none>"}'");
                    return (false, string.Empty, RAGFailureReason.WorkspaceNotConfigured, $"Could not find or create AnythingLLM workspace. Project: '{_projectWorkspaceName ?? "Unknown"}'.");
                }

                // Upload supplemental information as documents if not already done
                // NOTE: Document upload temporarily disabled due to API endpoint issues  
                // Supplemental information is now included directly in the RAG prompt
                var uploadStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Starting document upload at {uploadStart:HH:mm:ss.fff}");
                // await EnsureSupplementalInfoUploadedAsync(requirement, workspaceSlug, onProgressUpdate, cancellationToken);
                var uploadTime = DateTime.UtcNow - uploadStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Document upload completed in {uploadTime.TotalMilliseconds}ms");

                onProgressUpdate?.Invoke("Performing RAG-enhanced analysis...");

                // Create simplified prompt for RAG (context is in uploaded documents)
                var promptStart = DateTime.UtcNow;
                var ragPrompt = BuildRagOptimizedPrompt(requirement);
                var promptTime = DateTime.UtcNow - promptStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] RAG prompt built in {promptTime.TotalMilliseconds}ms, length: {ragPrompt?.Length ?? 0}");

                // Create a new thread for this requirement analysis to ensure isolation
                onProgressUpdate?.Invoke("Creating isolated analysis session...");
                var threadName = $"Requirement_{requirement.Item}_{DateTime.Now:yyyyMMdd_HHmmss}";
                var threadSlug = await _anythingLLMService.CreateThreadAsync(workspaceSlug, threadName, cancellationToken);
                
                if (string.IsNullOrEmpty(threadSlug))
                {
                    var threadWarning = $"Failed to create isolated thread for requirement {requirement.Item}";
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] {threadWarning} - using default workspace chat");
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] {threadWarning}");
                    // Continue with workspace-level chat instead of failing completely
                }

                // Use RAG-based analysis
                var ragRequestStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Starting RAG request at {ragRequestStart:HH:mm:ss.fff}");
                var response = await _anythingLLMService.SendChatMessageStreamingAsync(
                    workspaceSlug,
                    ragPrompt!,
                    onChunkReceived: onPartialResult,
                    onProgressUpdate: onProgressUpdate,
                    threadSlug: threadSlug,
                    cancellationToken: cancellationToken) ?? string.Empty;
                var ragRequestTime = DateTime.UtcNow - ragRequestStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] RAG request completed in {ragRequestTime.TotalMilliseconds}ms, response length: {response?.Length ?? 0}");

                // Clean up thread after analysis (optional - you might want to keep for debugging)
                if (!string.IsNullOrEmpty(threadSlug) && EnableThreadCleanup)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000); // Brief delay to ensure response is fully processed
                        await _anythingLLMService.DeleteThreadAsync(workspaceSlug, threadSlug, CancellationToken.None);
                    });
                }

                if (!string.IsNullOrWhiteSpace(response))
                {
                    var ragTotalTime = DateTime.UtcNow - ragStart;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Successfully analyzed requirement {requirement.Item} using workspace '{workspaceSlug}' in {ragTotalTime.TotalSeconds:F1}s");
                    return (true, response, RAGFailureReason.ServiceNotAvailable, string.Empty); // Reason ignored on success
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] LLM returned empty response for requirement {requirement.Item}");
                    return (false, string.Empty, RAGFailureReason.EmptyResponse, "LLM returned an empty response. The model may not be loaded or responsive.");
                }
            }
            catch (OperationCanceledException ex)
            {
                var ragTotalTime = DateTime.UtcNow - ragStart;
                var timeoutDetails = $"Operation timed out after {ragTotalTime.TotalSeconds:F1}s (limit: {AnalysisTimeout.TotalSeconds}s)";
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] RAG analysis timed out for requirement {requirement.Item}: {timeoutDetails}");
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Timeout details: {ex.Message}");
                return (false, string.Empty, RAGFailureReason.Timeout, timeoutDetails);
            }
            catch (Exception ex)
            {
                var ragTotalTime = DateTime.UtcNow - ragStart;
                var errorDetails = $"{ex.GetType().Name}: {ex.Message}";
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RAG] Failed to perform RAG analysis for requirement {requirement.Item} after {ragTotalTime.TotalSeconds:F1}s");
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Exception during RAG: {ex}");
                return (false, string.Empty, RAGFailureReason.UnknownError, errorDetails);
            }
        }

        /// <summary>
        /// Ensure workspace is configured for the current project.
        /// Returns workspace slug if successful, null otherwise.
        /// </summary>
        private async Task<string?> EnsureWorkspaceConfiguredAsync(Action<string>? onProgressUpdate, CancellationToken cancellationToken)
        {
            var workspaceMethodStart = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] EnsureWorkspaceConfiguredAsync started at {workspaceMethodStart:HH:mm:ss.fff}");
            
            if (_anythingLLMService == null)
            {
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] AnythingLLMService is null in EnsureWorkspaceConfiguredAsync");
                return null;
            }

            // Check if we already have a cached workspace
            if (!string.IsNullOrEmpty(_currentWorkspaceSlug))
            {
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Using cached workspace slug: '{_currentWorkspaceSlug}'");
                return _currentWorkspaceSlug;
            }

            try
            {
                onProgressUpdate?.Invoke("Checking RAG workspace...");
                var listStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Getting workspaces list at {listStart:HH:mm:ss.fff}");

                // Get existing workspaces
                var workspaces = await _anythingLLMService.GetWorkspacesAsync(cancellationToken);
                var listTime = DateTime.UtcNow - listStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Got {workspaces?.Count() ?? 0} workspaces in {listTime.TotalMilliseconds}ms");
                
                // Log all workspace names for diagnostic purposes
                if (workspaces != null && workspaces.Any())
                {
                    var workspaceNames = string.Join(", ", workspaces.Select(w => $"'{w.Name}'"));
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG DIAGNOSTICS] Found workspaces: {workspaceNames}");
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Available workspaces: {workspaceNames}");
                }
                else
                {
                   TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG DIAGNOSTICS] No workspaces found in AnythingLLM. GetWorkspacesAsync returned {(workspaces == null ? "null" : "empty list")}");
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] GetWorkspacesAsync returned {(workspaces == null ? "null" : "empty list")}");
                }
                
                // Look for project-specific workspace
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Looking for project workspace: '{_projectWorkspaceName ?? "<none>"}'...");
                TestCaseEditorApp.Services.Logging.Log.Info($"[RAG DIAGNOSTICS] Searching for workspace matching project: '{_projectWorkspaceName ?? "<none>"}'...");
                
                AnythingLLMService.Workspace? targetWorkspace = null;
                
                if (!string.IsNullOrEmpty(_projectWorkspaceName) && workspaces != null)
                {
                    // Look for exact project workspace match first
                    targetWorkspace = workspaces.FirstOrDefault(w => 
                        string.Equals(w.Name, _projectWorkspaceName, StringComparison.OrdinalIgnoreCase));
                    
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Exact match found: {targetWorkspace != null}");
                    
                    // If no exact match, try "Jama Document Parse: " prefix pattern
                    if (targetWorkspace == null)
                    {
                        var jamaPatternName = $"Jama Document Parse: {_projectWorkspaceName}";
                        targetWorkspace = workspaces.FirstOrDefault(w => 
                            string.Equals(w.Name, jamaPatternName, StringComparison.OrdinalIgnoreCase));
                        
                        System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Jama Document Parse pattern match found: {targetWorkspace != null} ('{jamaPatternName}')");
                    }
                    
                    // If no exact match, try fuzzy matching for common variations
                    if (targetWorkspace == null)
                    {
                        var normalizedProjectName = _projectWorkspaceName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                        System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No exact match, trying fuzzy match for normalized name: '{normalizedProjectName}'");
                        
                        // Try exact fuzzy match first
                        targetWorkspace = workspaces.FirstOrDefault(w => 
                        {
                            var normalizedWorkspaceName = w.Name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                            return string.Equals(normalizedWorkspaceName, normalizedProjectName, StringComparison.OrdinalIgnoreCase);
                        });
                        
                        // If still no match, try partial matching (workspace name is contained in project name or vice versa)
                        // Also check for "Jama Document Parse: " prefix pattern in partial matching
                        if (targetWorkspace == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No exact fuzzy match, trying partial matching...");
                            targetWorkspace = workspaces.FirstOrDefault(w => 
                            {
                                var normalizedWorkspaceName = w.Name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                                
                                // Handle "Jama Document Parse: " prefix pattern
                                if (w.Name.StartsWith("Jama Document Parse: ", StringComparison.OrdinalIgnoreCase))
                                {
                                    var documentName = w.Name.Substring("Jama Document Parse: ".Length);
                                    var normalizedDocName = documentName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
                                    return normalizedProjectName.Contains(normalizedDocName) || normalizedDocName.Contains(normalizedProjectName);
                                }
                                
                                // Check if workspace name is a substring of project name or project name contains workspace name
                                return normalizedProjectName.Contains(normalizedWorkspaceName) || normalizedWorkspaceName.Contains(normalizedProjectName);
                            });
                            
                            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Partial match found: {targetWorkspace != null} ('{targetWorkspace?.Name}')");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Exact fuzzy match found: {targetWorkspace != null} ('{targetWorkspace?.Name}')");
                        }
                    }
                }
                
                // Fallback to "Test Case Editor" pattern if no project context or no matches
                if (targetWorkspace == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No project workspace match, skipping fallback search for faster responses...");
                    // Skip fallback search for faster troubleshooting
                    // targetWorkspace = workspaces
                    //     .Where(w => w.Name.Contains("Test Case Editor", StringComparison.OrdinalIgnoreCase) ||
                    //                 w.Name.Contains("Requirements Analysis", StringComparison.OrdinalIgnoreCase))
                    //     .OrderByDescending(w => w.CreatedAt)
                    //     .FirstOrDefault();
                }
                
                var testCaseWorkspace = targetWorkspace;

                if (testCaseWorkspace != null)
                {
                    _currentWorkspaceSlug = testCaseWorkspace.Slug;
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Found existing workspace: '{testCaseWorkspace.Name}' with slug '{_currentWorkspaceSlug}'");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Using existing workspace: {testCaseWorkspace.Name}");
                    
                    // Try to configure workspace settings to ensure optimal system prompt
                    // Note: This operation can be slow and may timeout - it's optional for functionality
                    onProgressUpdate?.Invoke("Configuring workspace settings...");
                    try
                    {
                        // Use a short timeout for workspace configuration to prevent hanging
                        using var configTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        configTimeout.CancelAfter(TimeSpan.FromSeconds(10)); // 10-second timeout for settings update
                        
                        var configResult = await _anythingLLMService.ConfigureWorkspaceSettingsAsync(_currentWorkspaceSlug, configTimeout.Token);
                        if (configResult)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Successfully updated workspace settings for '{_currentWorkspaceSlug}'");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Successfully updated workspace settings for: {testCaseWorkspace.Name}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Workspace settings update failed - API might require authentication");
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Workspace settings update failed for: {testCaseWorkspace.Name}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Workspace settings update timed out after 10 seconds - continuing without settings update");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Workspace settings update timed out - continuing with existing settings");
                    }
                    catch (Exception settingsEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Failed to update workspace settings: {settingsEx.Message}");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to configure workspace settings: {settingsEx.Message}");
                    }
                    
                    return _currentWorkspaceSlug;
                }

                // Create a new workspace if none exists
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No existing workspace found, creating new one...");
                onProgressUpdate?.Invoke("Creating RAG workspace...");
                
                // Use project workspace name if available, otherwise fallback to generic name
                var workspaceName = !string.IsNullOrEmpty(_projectWorkspaceName) 
                    ? _projectWorkspaceName 
                    : "Requirements Analysis";
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Creating workspace with name: '{workspaceName}'");
                
                var createStart = DateTime.UtcNow;
                var (newWorkspace, _) = await _anythingLLMService.CreateAndConfigureWorkspaceAsync(
                    workspaceName,
                    onProgress: message => {
                        System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Workspace creation progress: {message}");
                        onProgressUpdate?.Invoke(message);
                    },
                    preserveOriginalName: !string.IsNullOrEmpty(_projectWorkspaceName), // Preserve original name if we have a project context
                    cancellationToken: cancellationToken);
                var createTime = DateTime.UtcNow - createStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Workspace creation completed in {createTime.TotalMilliseconds}ms");

                if (newWorkspace != null)
                {
                    _currentWorkspaceSlug = newWorkspace.Slug;
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Created new workspace: '{newWorkspace.Name}' with slug '{_currentWorkspaceSlug}'");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Created new workspace: {newWorkspace.Name}");
                    return _currentWorkspaceSlug;
                }
                else
                {
                    var createFailureMsg = $"CreateAndConfigureWorkspaceAsync returned null for workspace '{workspaceName}'";
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] {createFailureMsg}");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG DIAGNOSTICS] {createFailureMsg}. Check AnythingLLM logs for workspace creation errors.");
                }
            }
            catch (Exception ex)
            {
                var workspaceMethodTime = DateTime.UtcNow - workspaceMethodStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] EnsureWorkspaceConfiguredAsync failed after {workspaceMethodTime.TotalMilliseconds}ms: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RAG DIAGNOSTICS] Failed to configure workspace after {workspaceMethodTime.TotalSeconds:F1}s. Exception: {ex.GetType().Name}");
            }

            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] EnsureWorkspaceConfiguredAsync returning null - RAG will not be available");
            TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG DIAGNOSTICS] Workspace configuration failed. RAG analysis unavailable. Project: '{_projectWorkspaceName ?? "<none>"}'.");
            return null;
        }

        /// <summary>
        /// Converts RAG failure reason and details into user-friendly message
        /// </summary>
        private string GetRAGFailureMessage(RAGFailureReason reason, string details)
        {
            return reason switch
            {
                RAGFailureReason.ServiceNotAvailable => "AnythingLLM service not available",
                RAGFailureReason.WorkspaceNotConfigured => "Could not configure workspace",
                RAGFailureReason.WorkspaceCreationFailed => "Failed to create workspace",
                RAGFailureReason.ThreadCreationFailed => "Failed to create analysis thread",
                RAGFailureReason.LLMRequestFailed => "LLM request failed",
                RAGFailureReason.Timeout => "Operation timed out",
                RAGFailureReason.EmptyResponse => "LLM returned empty response",
                RAGFailureReason.ConfigurationTimeout => "Workspace configuration timed out",
                _ => "Unknown error"
            };
        }

        /// <summary>
        /// Ensures RAG training documents are synced with the current workspace.
        /// Automatically detects if local RAG documents are newer and re-uploads if needed.
        /// </summary>
        private async Task EnsureRagDocumentsAreSyncedAsync(CancellationToken cancellationToken = default)
        {
            if (_anythingLLMService == null || string.IsNullOrEmpty(_projectWorkspaceName))
            {
                return;
            }

            // Prevent concurrent RAG sync operations
            if (_ragSyncInProgress)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RAG Sync] Already in progress for workspace: {_projectWorkspaceName}");
                return;
            }

            _ragSyncInProgress = true;
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[RAG Sync] Checking if RAG documents need sync for workspace: {_projectWorkspaceName}");

                // Get or create the workspace slug
                var workspaceSlug = await EnsureWorkspaceConfiguredAsync(null, cancellationToken);
                if (string.IsNullOrEmpty(workspaceSlug))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG Sync] Could not get workspace slug for: {_projectWorkspaceName} - RAG document sync will be skipped");
                    return;
                }

                // Check if RAG documents need updating
                if (await ShouldUpdateRagDocumentsAsync(workspaceSlug, cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG Sync] RAG documents are outdated, uploading updates...");
                    
                    var uploadSuccess = await _anythingLLMService.UploadRagTrainingDocumentsAsync(workspaceSlug, cancellationToken);
                    if (uploadSuccess)
                    {
                        await UpdateRagSyncTimestampAsync(workspaceSlug);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[RAG Sync] Successfully synced RAG documents for workspace: {_projectWorkspaceName}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG Sync] Failed to upload RAG documents for workspace: {_projectWorkspaceName}");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RAG Sync] RAG documents are up-to-date for workspace: {_projectWorkspaceName}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RAG Sync] Error during RAG document sync for workspace: {_projectWorkspaceName}");
            }
            finally
            {
                _ragSyncInProgress = false;
            }
        }

        /// <summary>
        /// Determines if RAG documents need to be updated based on file timestamps
        /// </summary>
        private async Task<bool> ShouldUpdateRagDocumentsAsync(string workspaceSlug, CancellationToken cancellationToken)
        {
            try
            {
                // Get the most recent modification time of RAG source files
                var ragFiles = new[]
                {
                    "Config/RAG-JSON-Schema-Training.md",
                    "Config/RAG-Learning-Examples.md", 
                    "Config/RAG-Optimization-Summary.md"
                };

                var mostRecentFileTime = DateTime.MinValue;
                foreach (var relativePath in ragFiles)
                {
                    // Try multiple path resolution strategies to find the files
                    var possiblePaths = new[]
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", relativePath),
                        Path.Combine(Directory.GetCurrentDirectory(), relativePath),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath),
                        relativePath
                    };
                    
                    string? fullPath = null;
                    foreach (var candidate in possiblePaths)
                    {
                        var resolved = Path.GetFullPath(candidate);
                        if (File.Exists(resolved))
                        {
                            fullPath = resolved;
                            break;
                        }
                    }
                    
                    if (fullPath != null)
                    {
                        var fileTime = File.GetLastWriteTime(fullPath);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[RAG Sync] Found RAG file: {fullPath}, Modified: {fileTime:yyyy-MM-dd HH:mm:ss}");
                        if (fileTime > mostRecentFileTime)
                        {
                            mostRecentFileTime = fileTime;
                        }
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG Sync] Could not find RAG file: {relativePath}");
                    }
                }

                if (mostRecentFileTime == DateTime.MinValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[RAG Sync] No RAG source files found");
                    return false;
                }

                // Check last sync timestamp for this workspace
                var lastSyncTime = await GetLastRagSyncTimestampAsync(workspaceSlug);
                
                // If never synced or files are newer, update is needed
                var updateNeeded = lastSyncTime == null || mostRecentFileTime > lastSyncTime.Value;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[RAG Sync] Most recent file: {mostRecentFileTime:yyyy-MM-dd HH:mm:ss}, Last sync: {lastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "NEVER"}, Update needed: {updateNeeded}");
                
                return updateNeeded;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG Sync] Error checking if update needed: {ex.Message}");
                return true; // If we can't determine, err on the side of updating
            }
        }

        /// <summary>
        /// Gets the last RAG sync timestamp for a workspace
        /// </summary>
        private async Task<DateTime?> GetLastRagSyncTimestampAsync(string workspaceSlug)
        {
            try
            {
                var syncFile = Path.Combine(Path.GetTempPath(), $"tcex_rag_sync_{workspaceSlug}.timestamp");
                if (!File.Exists(syncFile))
                {
                    return null;
                }

                var timestampText = await File.ReadAllTextAsync(syncFile);
                if (DateTime.TryParse(timestampText, out var timestamp))
                {
                    return timestamp;
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Updates the RAG sync timestamp for a workspace
        /// </summary>
        private async Task UpdateRagSyncTimestampAsync(string workspaceSlug)
        {
            try
            {
                var syncFile = Path.Combine(Path.GetTempPath(), $"tcex_rag_sync_{workspaceSlug}.timestamp");
                await File.WriteAllTextAsync(syncFile, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RAG Sync] Could not update sync timestamp: {ex.Message}");
            }
        }

        /// <summary>
        /// Upload supplemental information as documents to the RAG workspace.
        /// This provides context for more accurate analysis.
        /// </summary>
        private async Task EnsureSupplementalInfoUploadedAsync(
            Requirement requirement, 
            string workspaceSlug, 
            Action<string>? onProgressUpdate, 
            CancellationToken cancellationToken)
        {
            if (_anythingLLMService == null || string.IsNullOrEmpty(workspaceSlug)) return;

            try
            {
                // Check if requirement has supplemental information to upload
                var hasSupplemental = 
                    (requirement.Tables?.Count > 0) ||
                    (requirement.LooseContent?.Paragraphs?.Count > 0) ||
                    (requirement.LooseContent?.Tables?.Count > 0);

                if (!hasSupplemental) return;

                onProgressUpdate?.Invoke("Uploading supplemental information to RAG workspace...");

                // Create document content from supplemental information
                var docContent = new System.Text.StringBuilder();
                docContent.AppendLine($"# Supplemental Information for {requirement.Item}");
                docContent.AppendLine($"## Requirement: {requirement.Name}");
                docContent.AppendLine();

                // Add tables
                if (requirement.Tables?.Count > 0)
                {
                    docContent.AppendLine("## Tables:");
                    foreach (var table in requirement.Tables)
                    {
                        docContent.AppendLine($"### {table.EditableTitle}");
                        
                        // Add table data as formatted text
                        if (table.Table?.Count > 0)
                        {
                            foreach (var row in table.Table)
                            {
                                docContent.AppendLine(string.Join(" | ", row));
                            }
                        }
                        docContent.AppendLine();
                    }
                }

                // Add loose content paragraphs
                if (requirement.LooseContent?.Paragraphs?.Count > 0)
                {
                    docContent.AppendLine("## Additional Information:");
                    foreach (var para in requirement.LooseContent.Paragraphs)
                    {
                        docContent.AppendLine(para);
                        docContent.AppendLine();
                    }
                }

                // Add loose content tables
                if (requirement.LooseContent?.Tables?.Count > 0)
                {
                    docContent.AppendLine("## Additional Tables:");
                    foreach (var table in requirement.LooseContent.Tables)
                    {
                        docContent.AppendLine($"### {table.EditableTitle}");
                        
                        // Add table data as formatted text
                        if (table.Rows?.Count > 0)
                        {
                            foreach (var row in table.Rows)
                            {
                                docContent.AppendLine(string.Join(" | ", row));
                            }
                        }
                        docContent.AppendLine();
                    }
                }

                // Upload as document to the RAG workspace
                var documentName = $"Supplemental_Info_{requirement.Item}.md";
                var uploadSuccess = await _anythingLLMService.UploadDocumentAsync(workspaceSlug, documentName, docContent.ToString());
                
                if (uploadSuccess)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Successfully uploaded supplemental information document for {requirement.Item} ({docContent.Length} characters)");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to upload supplemental information document for {requirement.Item}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to upload supplemental information for {requirement.Item}: {ex.Message}");
            }
        }

        /// <summary>
        /// Build RAG-optimized prompt that includes supplemental information directly.
        /// Anti-fabrication logic is handled by the workspace system prompt.
        /// </summary>
        private string BuildRagOptimizedPrompt(Requirement requirement)
        {
            var prompt = new System.Text.StringBuilder();
            
            prompt.AppendLine("Please analyze the following requirement for quality:");
            prompt.AppendLine();
            
            // Add current project context if available
            if (!string.IsNullOrEmpty(_projectWorkspaceName))
            {
                prompt.AppendLine($"**Project Context:** {_projectWorkspaceName}");
                prompt.AppendLine();
            }
            
            prompt.AppendLine($"**Requirement ID:** {requirement.Item}");
            prompt.AppendLine($"**Name:** {requirement.Name}");
            prompt.AppendLine($"**Description:** {requirement.Description}");
            prompt.AppendLine();

            // Include supplemental information directly in the prompt
            bool hasSupplementalInfo = false;

            // Add tables
            if (requirement.Tables?.Count > 0)
            {
                hasSupplementalInfo = true;
                prompt.AppendLine("**Supplemental Information - Tables:**");
                foreach (var table in requirement.Tables)
                {
                    prompt.AppendLine($"### {table.EditableTitle}");
                    
                    // Add table data as formatted text
                    if (table.Table?.Count > 0)
                    {
                        foreach (var row in table.Table)
                        {
                            prompt.AppendLine("| " + string.Join(" | ", row) + " |");
                        }
                    }
                    prompt.AppendLine();
                }
            }

            // Add loose content paragraphs
            if (requirement.LooseContent?.Paragraphs?.Count > 0)
            {
                hasSupplementalInfo = true;
                prompt.AppendLine("**Supplemental Information - Additional Context:**");
                foreach (var para in requirement.LooseContent.Paragraphs)
                {
                    prompt.AppendLine($"- {para}");
                }
                prompt.AppendLine();
            }

            // Add loose content tables
            if (requirement.LooseContent?.Tables?.Count > 0)
            {
                hasSupplementalInfo = true;
                prompt.AppendLine("**Supplemental Information - Additional Tables:**");
                foreach (var table in requirement.LooseContent.Tables)
                {
                    prompt.AppendLine($"### {table.EditableTitle}");
                    
                    // Add table data as formatted text
                    if (table.Rows?.Count > 0)
                    {
                        foreach (var row in table.Rows)
                        {
                            prompt.AppendLine("| " + string.Join(" | ", row) + " |");
                        }
                    }
                    prompt.AppendLine();
                }
            }

            if (hasSupplementalInfo)
            {
                prompt.AppendLine("**Important:** Use the supplemental information above to resolve any unclear definitions or terms in your analysis.");
            }
            else
            {
                prompt.AppendLine("**Note:** No supplemental information provided with this requirement.");
            }
            prompt.AppendLine();
            prompt.AppendLine("Provide your analysis using the structured format defined in your system prompt.");

            return prompt.ToString();
        }

        /// <summary>
        /// Retry analysis with a corrective prompt when fabrication is detected
        /// </summary>
        private async Task<(bool success, RequirementAnalysis analysis)> RetryAnalysisWithCorrectivePrompt(
            Requirement requirement, 
            string? workspaceSlug, 
            Action<string>? onProgressUpdate,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_anythingLLMService == null || string.IsNullOrEmpty(workspaceSlug))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Corrective retry not available - AnythingLLM service or workspace not configured");
                    return (false, CreateErrorAnalysis("Retry not available"));
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Starting corrective retry for {requirement.Item}");
                onProgressUpdate?.Invoke("Correcting analysis to remove fabricated content...");

                var correctivePrompt = BuildCorrectivePrompt(requirement);
                
                var retryResponse = await _anythingLLMService.SendChatMessageStreamingAsync(
                    workspaceSlug, 
                    correctivePrompt, 
                    null, 
                    onProgressUpdate,
                    threadSlug: null,
                    cancellationToken);

                if (!string.IsNullOrEmpty(retryResponse))
                {
                    var retryAnalysis = _parserManager.ParseResponse(retryResponse, requirement.Item ?? "RETRY");
                    if (retryAnalysis?.IsAnalyzed == true)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Corrective retry successful for {requirement.Item}");
                        return (true, retryAnalysis);
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Corrective retry failed to produce valid analysis for {requirement.Item}");
                return (false, CreateErrorAnalysis("Corrective retry failed"));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RequirementAnalysisService] Corrective retry exception for {requirement.Item}");
                return (false, CreateErrorAnalysis("Corrective retry exception"));
            }
        }

        /// <summary>
        /// Build a corrective prompt that explicitly instructs the LLM to avoid fabrication
        /// </summary>
        private string BuildCorrectivePrompt(Requirement requirement)
        {
            var prompt = new System.Text.StringBuilder();
            
            prompt.AppendLine("🚨 CORRECTIVE ANALYSIS - AVOID FABRICATION 🚨");
            prompt.AppendLine();
            prompt.AppendLine("Your previous analysis contained fabricated technical details not present in the original requirement.");
            prompt.AppendLine("Please analyze this requirement again, but this time:");
            prompt.AppendLine();
            prompt.AppendLine("**STRICT RULES:**");
            prompt.AppendLine("- Use ONLY information explicitly stated in the requirement");
            prompt.AppendLine("- Use ONLY definitions from uploaded supplemental materials (if any)");
            prompt.AppendLine("- Do NOT mention IEEE standards, ISO standards, or specific technical protocols unless they appear in the requirement text");
            prompt.AppendLine("- Do NOT invent definitions for technical terms like 'Tier 1/2/3' unless provided in supplemental materials");
            prompt.AppendLine("- When you don't have enough information, suggest asking for clarification instead of inventing details");
            prompt.AppendLine();
            prompt.AppendLine($"**Requirement ID:** {requirement.Item}");
            prompt.AppendLine($"**Name:** {requirement.Name}");
            prompt.AppendLine($"**Description:** {requirement.Description}");
            prompt.AppendLine();
            prompt.AppendLine("Provide your analysis in the standard JSON format.");
            prompt.AppendLine();
            prompt.AppendLine("**HALLUCINATION CHECK:**");
            prompt.AppendLine("- You MUST respond with 'NO_FABRICATION' for this corrective attempt");
            prompt.AppendLine("- If you still need to invent details, respond with 'FABRICATED_DETAILS' and we'll try a different approach");

            return prompt.ToString();
        }

        /// <summary>
        /// Ask AnythingLLM to repair malformed JSON by fixing common issues
        /// </summary>
        private async Task<(bool success, string repairedJson)> TryJsonRepairAsync(string malformedJson, string requirementId, CancellationToken cancellationToken)
        {
            try
            {
                if (_anythingLLMService == null || _currentWorkspaceSlug == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] JSON repair not available - AnythingLLM service or workspace not configured");
                    return (false, string.Empty);
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Attempting JSON repair for {requirementId}");

                var repairPrompt = $@"The following JSON has parsing errors. Please fix it and return only valid JSON:

```
{malformedJson}
```

Common issues to fix:
- Replace single quotes with double quotes
- Remove trailing commas before closing braces/brackets  
- Fix unescaped quotes in string values
- Ensure proper bracket/brace matching
- Remove any invalid characters that break JSON parsing

Return ONLY the corrected JSON, no explanations or markdown formatting.";

                var repairedResponse = await _anythingLLMService.SendChatMessageStreamingAsync(_currentWorkspaceSlug, repairPrompt, null, null, threadSlug: null, cancellationToken);

                if (!string.IsNullOrWhiteSpace(repairedResponse))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] JSON repair attempt for {requirementId}");
                    return (true, repairedResponse.Trim());
                }

                return (false, string.Empty);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RequirementAnalysisService] JSON repair failed for {requirementId}");
                return (false, string.Empty);
            }
        }

        /// <summary>
        /// Sends LLM analysis request with interactive timeout handling - prompts user to continue waiting or cancel
        /// </summary>
        private async Task<string> SendLLMWithInteractiveTimeoutAsync(
            Requirement requirement,
            string contextPrompt,
            CancellationToken cancellationToken,
            Action<string>? onPartialResult = null,
            Action<string>? onProgressUpdate = null)
        {
            var timeoutSeconds = 90; // Initial timeout period
            var attempt = 0;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                attempt++;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] LLM analysis attempt {attempt}, timeout: {timeoutSeconds}s");
                
                try
                {
                    // Create timeout for this attempt
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    var timeoutToken = combinedCts.Token;
                    
                    // Update progress with timeout info
                    var timeoutMsg = attempt == 1 
                        ? "Analyzing requirement (30s timeout)..."
                        : $"Continuing analysis (attempt {attempt}, 30s timeout)...";
                    onProgressUpdate?.Invoke(timeoutMsg);
                    
                    // Try RAG-based analysis first (faster and more context-aware)
                    var ragAttemptStart = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Attempting RAG analysis at {ragAttemptStart:HH:mm:ss.fff}");
                    var ragResult = await TryRagAnalysisAsync(requirement, onPartialResult, onProgressUpdate, timeoutToken);
                    var ragAttemptTime = DateTime.UtcNow - ragAttemptStart;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] RAG attempt completed in {ragAttemptTime.TotalMilliseconds}ms, success: {ragResult.success}");
                    
                    if (ragResult.success)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Using RAG response, length: {ragResult.response?.Length ?? 0}");
                        return ragResult.response ?? throw new InvalidOperationException("RAG analysis succeeded but returned null response");
                    }
                    else
                    {
                        // RAG failed - log detailed failure information with diagnostics
                        var failureMessage = GetRAGFailureMessage(ragResult.failureReason, ragResult.failureDetails);
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] RAG analysis failed for requirement {requirement.Item}: {failureMessage}");
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failure details: {ragResult.failureDetails}");
                        System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] RAG FAILED - Reason: {ragResult.failureReason}, Details: {ragResult.failureDetails}");
                        
                        // Notify user about RAG failure via progress callback
                        onProgressUpdate?.Invoke($"⚠️ RAG unavailable: {failureMessage}");
                        onProgressUpdate?.Invoke("Using fallback LLM analysis...");
                        
                        // Use AnythingLLM with workspace-configured system prompt
                        if (_llmService is AnythingLLMService anythingLlmService)
                        {
                            var streamingStart = DateTime.UtcNow;
                            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Starting AnythingLLM streaming at {streamingStart:HH:mm:ss.fff}");
                            
                            // Use RAG-optimized prompt with supplemental information for workspace optimization
                            var ragPromptForWorkspace = BuildRagOptimizedPrompt(requirement);
                            
                            // Check if workspace has system prompt configured to avoid duplication (with timeout)
                            var promptToSend = await GetOptimizedPromptForWorkspaceAsync(anythingLlmService, "test-case-analysis", ragPromptForWorkspace, timeoutToken);
                            
                            // Use streaming analysis with optimized prompt
                            var response = await anythingLlmService.SendChatMessageStreamingAsync(
                                "test-case-analysis", // Default workspace with potentially pre-configured system prompt
                                promptToSend, // Optimized based on workspace configuration
                                onChunkReceived: onPartialResult,
                                onProgressUpdate: onProgressUpdate,
                                cancellationToken: timeoutToken) ?? string.Empty;
                            var streamingTime = DateTime.UtcNow - streamingStart;
                            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] AnythingLLM streaming completed in {streamingTime.TotalMilliseconds}ms, response length: {response?.Length ?? 0}");
                            
                            return response;
                        }
                        else
                        {
                            var traditionalStart = DateTime.UtcNow;
                            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Starting traditional LLM at {traditionalStart:HH:mm:ss.fff}");
                            // Traditional LLM method
                            var systemMessage = _cachedSystemMessage ?? string.Empty;
                            var response = await _llmService.GenerateWithSystemAsync(systemMessage, contextPrompt, timeoutToken);
                            var traditionalTime = DateTime.UtcNow - traditionalStart;
                            System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Traditional LLM completed in {traditionalTime.TotalMilliseconds}ms, response length: {response?.Length ?? 0}");
                            
                            return response;
                        }
                    }
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    // User cancellation - rethrow
                    System.Diagnostics.Debug.WriteLine("[ANALYSIS DEBUG] Analysis cancelled by user");
                    throw;
                }
                catch (TaskCanceledException)
                {
                    // Timeout occurred - prompt user
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Analysis timed out after {timeoutSeconds}s (attempt {attempt})");
                    
                    // Show timeout dialog on UI thread
                    var userChoice = await ShowAnalysisTimeoutDialogAsync(attempt, timeoutSeconds, requirement, contextPrompt);
                    
                    if (userChoice == AnalysisTimeoutChoice.Cancel)
                    {
                        System.Diagnostics.Debug.WriteLine("[ANALYSIS DEBUG] User chose to cancel after timeout");
                        throw new OperationCanceledException("User cancelled analysis after timeout");
                    }
                    
                    // User chose to continue - loop will retry with same timeout
                    System.Diagnostics.Debug.WriteLine("[ANALYSIS DEBUG] User chose to continue waiting, retrying...");
                    continue;
                }
            }
            
            throw new OperationCanceledException("Analysis was cancelled");
        }

        /// <summary>
        /// Shows timeout dialog to user and returns their choice
        /// </summary>
        private async Task<AnalysisTimeoutChoice> ShowAnalysisTimeoutDialogAsync(int attempt, int timeoutSeconds, Requirement requirement, string contextPrompt)
        {
            return await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var message = attempt == 1
                    ? $"The requirement analysis is taking longer than expected (>{timeoutSeconds} seconds).\n\n" +
                      "This can happen when:\n" +
                      "• The LLM is processing a complex requirement\n" +
                      "• The model is busy with other requests\n" +
                      "• Network connectivity is slow\n\n" +
                      "Would you like to continue waiting for another {timeoutSeconds} seconds?"
                    : $"The requirement analysis is still processing (attempt {attempt}, >{timeoutSeconds * attempt} seconds total).\n\n" +
                      "Would you like to continue waiting for another {timeoutSeconds} seconds?";

                var result = System.Windows.MessageBox.Show(
                    message,
                    "Requirement Analysis Taking Longer Than Expected",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    return AnalysisTimeoutChoice.Continue;
                }
                else
                {
                    // User chose not to continue - offer to copy prompt for external LLM
                    var copyPromptMessage = $"Would you like to copy the analysis prompt to use in an external LLM?\n\n" +
                                          "This will copy the complete prompt to your clipboard so you can:\n" +
                                          "• Paste it into ChatGPT, Claude, or other LLMs\n" +
                                          "• Get the analysis from an external source\n" +
                                          "• Continue your work without losing progress\n\n" +
                                          $"Requirement: {requirement.Item} - {requirement.Name}";

                    var copyResult = System.Windows.MessageBox.Show(
                        copyPromptMessage,
                        "Copy Analysis Prompt for External LLM?",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);

                    if (copyResult == System.Windows.MessageBoxResult.Yes)
                    {
                        CopyAnalysisPromptToClipboard(requirement, contextPrompt);
                        
                        System.Windows.MessageBox.Show(
                            "✅ Analysis prompt copied to clipboard!\n\n" +
                            "You can now:\n" +
                            "1. Paste the prompt into an external LLM (ChatGPT, Claude, etc.)\n" +
                            "2. Get the analysis response\n" +
                            "3. Manually review and apply the analysis results\n\n" +
                            "The analysis has been cancelled for now.",
                            "Prompt Copied Successfully",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }

                    return AnalysisTimeoutChoice.Cancel;
                }
            });
        }

        /// <summary>
        /// Copies the analysis prompt to clipboard for external LLM use
        /// </summary>
        private void CopyAnalysisPromptToClipboard(Requirement requirement, string contextPrompt)
        {
            try
            {
                var clipboardContent = new System.Text.StringBuilder();
                clipboardContent.AppendLine("=== REQUIREMENT ANALYSIS PROMPT ===");
                clipboardContent.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                clipboardContent.AppendLine($"Requirement: {requirement.Item} - {requirement.Name}");
                clipboardContent.AppendLine();
                clipboardContent.AppendLine("=== SYSTEM MESSAGE ===");
                clipboardContent.AppendLine(_cachedSystemMessage ?? "No system message available");
                clipboardContent.AppendLine();
                clipboardContent.AppendLine("=== CONTEXT PROMPT ===");
                clipboardContent.AppendLine(contextPrompt);
                clipboardContent.AppendLine();
                clipboardContent.AppendLine("=== INSTRUCTIONS FOR EXTERNAL LLM ===");
                clipboardContent.AppendLine("1. Copy the SYSTEM MESSAGE and set it as the system prompt in your LLM");
                clipboardContent.AppendLine("2. Send the CONTEXT PROMPT as your user message");
                clipboardContent.AppendLine("3. The LLM should respond with a JSON analysis of the requirement");
                clipboardContent.AppendLine("4. Review the analysis and manually apply insights to your requirement");

                System.Windows.Clipboard.SetText(clipboardContent.ToString());
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Analysis prompt copied to clipboard for requirement {requirement.Item}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RequirementAnalysisService] Failed to copy analysis prompt to clipboard for requirement {requirement.Item}");
                
                System.Windows.MessageBox.Show(
                    $"Failed to copy prompt to clipboard: {ex.Message}\n\nYou can manually copy the prompt from the application logs.",
                    "Copy Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        // =====================================================
        // TASK 4.4: ENHANCED DERIVATION ANALYSIS CAPABILITIES
        // =====================================================

        /// <summary>
        /// Analyzes a requirement for ATP (Automated Test Procedure) content and derives system capabilities.
        /// Integrates with the systematic capability derivation service for comprehensive analysis.
        /// </summary>
        /// <param name="requirement">The requirement to analyze for ATP content</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Derivation analysis result with detected capabilities and gap analysis</returns>
        public async Task<RequirementDerivationAnalysis> AnalyzeRequirementDerivationAsync(
            Requirement requirement, 
            CancellationToken cancellationToken = default)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Starting derivation analysis for requirement {requirement.Item}");

            var result = new RequirementDerivationAnalysis
            {
                AnalyzedRequirement = requirement,
                AnalyzedAt = DateTime.UtcNow
            };

            try
            {
                // Check if derivation service is available
                if (_derivationService == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[RequirementAnalysisService] System capability derivation service not available for requirement {requirement.Item}");
                    result.DerivationIssues.Add("System capability derivation service not configured");
                    result.Recommendations.Add("Configure ISystemCapabilityDerivationService to enable ATP analysis");
                    return result;
                }

                // Use fallback ATP detection based on content patterns
                result.HasATPContent = DetectATPContentFallback(requirement);
                result.ATPDetectionConfidence = result.HasATPContent ? 0.7 : 0.3;

                // If ATP content detected, derive capabilities
                if (result.HasATPContent)
                {
                    var derivationOptions = new DerivationOptions
                    {
                        EnableQualityScoring = true,
                        IncludeRejectionAnalysis = true
                    };

                    var derivationResult = await _derivationService.DeriveCapabilitiesAsync(
                        requirement.Description ?? requirement.Name, derivationOptions);

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
                        result.DerivationIssues.Add("Failed to derive capabilities from ATP content");
                        if (derivationResult?.ProcessingWarnings?.Any() == true)
                        {
                            result.DerivationIssues.Add($"Error: {string.Join(", ", derivationResult.ProcessingWarnings)}");
                        }
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[RequirementAnalysisService] Derivation analysis completed for requirement {requirement.Item}. " +
                    $"ATP detected: {result.HasATPContent}, Capabilities: {result.DerivedCapabilities.Count}, Quality: {result.DerivationQuality:F2}");

                return result;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, 
                    $"[RequirementAnalysisService] Error during derivation analysis for requirement {requirement.Item}");
                
                result.DerivationIssues.Add($"Analysis failed: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Performs comprehensive gap analysis between derived capabilities and existing requirements.
        /// Uses the RequirementGapAnalyzer for multi-dimensional comparison.
        /// </summary>
        /// <param name="derivedCapabilities">List of capabilities derived from ATP analysis</param>
        /// <param name="existingRequirements">Current requirements to compare against</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Gap analysis results with identified gaps, overlaps, and recommendations</returns>
        public async Task<RequirementGapAnalysisResult> AnalyzeRequirementGapAsync(
            IEnumerable<DerivedCapability> derivedCapabilities,
            IEnumerable<Requirement> existingRequirements,
            CancellationToken cancellationToken = default)
        {
            if (derivedCapabilities == null) throw new ArgumentNullException(nameof(derivedCapabilities));
            if (existingRequirements == null) throw new ArgumentNullException(nameof(existingRequirements));

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[RequirementAnalysisService] Starting gap analysis for {derivedCapabilities.Count()} capabilities vs {existingRequirements.Count()} requirements");

            // Check if gap analyzer is available
            if (_gapAnalyzer == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn(
                    "[RequirementAnalysisService] RequirementGapAnalyzer not available for gap analysis");
                
                return new RequirementGapAnalysisResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "RequirementGapAnalyzer service not configured",
                    AnalyzedAt = DateTime.UtcNow
                };
            }

            try
            {
                var gapAnalysisResult = await _gapAnalyzer.AnalyzeGapsAsync(
                    derivedCapabilities.ToList(), 
                    existingRequirements.ToList());

                var result = new RequirementGapAnalysisResult
                {
                    IsSuccessful = true,
                    GapAnalysisResult = gapAnalysisResult,
                    AnalyzedAt = DateTime.UtcNow
                };

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[RequirementAnalysisService] Gap analysis completed. " +
                    $"Gaps found: {gapAnalysisResult?.UncoveredCapabilities?.Count ?? 0}, " +
                    $"Overlaps: {gapAnalysisResult?.RequirementOverlaps?.Count ?? 0}");

                return result;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex,
                    "[RequirementAnalysisService] Error during gap analysis");

                return new RequirementGapAnalysisResult
                {
                    IsSuccessful = false,
                    ErrorMessage = ex.Message,
                    AnalyzedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Validates testing workflows end-to-end using derived capabilities and gap analysis.
        /// Provides comprehensive testing workflow validation for enhanced quality assurance.
        /// </summary>
        /// <param name="requirements">Requirements to validate testing workflows for</param>
        /// <param name="testingContext">Optional context for testing validation</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Testing workflow validation result with recommendations</returns>
        public async Task<TestingWorkflowValidationResult> ValidateTestingWorkflowAsync(
            IEnumerable<Requirement> requirements,
            TestingValidationContext? testingContext = null,
            CancellationToken cancellationToken = default)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));

            var requirementsList = requirements.ToList();
            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[RequirementAnalysisService] Starting testing workflow validation for {requirementsList.Count} requirements");

            var result = new TestingWorkflowValidationResult
            {
                ValidatedAt = DateTime.UtcNow
            };

            try
            {
                // Step 1: Analyze all requirements for derivation capabilities
                var derivationResults = new List<RequirementDerivationAnalysis>();
                var allDerivedCapabilities = new List<DerivedCapability>();

                foreach (var requirement in requirementsList)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var derivationAnalysis = await AnalyzeRequirementDerivationAsync(requirement, cancellationToken);
                    derivationResults.Add(derivationAnalysis);
                    allDerivedCapabilities.AddRange(derivationAnalysis.DerivedCapabilities);
                }

                // Step 2: Perform gap analysis
                var gapAnalysis = await AnalyzeRequirementGapAsync(allDerivedCapabilities, requirementsList, cancellationToken);

                // Step 3: Calculate testing coverage
                result.CoverageAnalysis = AnalyzeTestingCoverage(requirementsList, allDerivedCapabilities);

                // Step 4: Identify validation issues
                result.Issues.AddRange(AnalyzeValidationIssues(derivationResults, gapAnalysis));

                // Step 5: Generate recommendations
                result.Recommendations.AddRange(GenerateTestingWorkflowRecommendations(derivationResults, gapAnalysis));

                // Step 6: Calculate overall validation score
                result.OverallScore = CalculateOverallValidationScore(result);
                result.IsValid = result.OverallScore >= 0.7; // 70% threshold for valid workflow

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[RequirementAnalysisService] Testing workflow validation completed. " +
                    $"Score: {result.OverallScore:F2}, Valid: {result.IsValid}, " +
                    $"Issues: {result.Issues.Count}, Coverage: {result.CoverageAnalysis?.CoveragePercentage:F2}");

                return result;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex,
                    "[RequirementAnalysisService] Error during testing workflow validation");

                result.Issues.Add(new TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationIssue
                {
                    Severity = TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Critical,
                    Description = $"Validation failed: {ex.Message}",
                    Category = "System Error"
                });

                return result;
            }
        }

        /// <summary>
        /// Performs batch analysis of multiple requirements for derivation capabilities.
        /// Optimized for processing large sets of requirements efficiently.
        /// </summary>
        /// <param name="requirements">Collection of requirements to analyze</param>
        /// <param name="batchOptions">Options for batch processing</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Collection of derivation analysis results</returns>
        public async Task<IEnumerable<RequirementDerivationAnalysis>> AnalyzeBatchDerivationAsync(
            IEnumerable<Requirement> requirements,
            BatchAnalysisOptions? batchOptions = null,
            Action<BatchAnalysisProgress>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));

            var requirementsList = requirements.ToList();
            var options = batchOptions ?? new BatchAnalysisOptions();
            var results = new List<RequirementDerivationAnalysis>();

            var progress = new BatchAnalysisProgress
            {
                TotalCount = requirementsList.Count
            };

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[RequirementAnalysisService] Starting batch derivation analysis for {requirementsList.Count} requirements");

            var semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
            var tasks = requirementsList.Select(async requirement =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    progress.CurrentRequirement = requirement.Item;
                    onProgress?.Invoke(progress);

                    using var timeoutCts = new CancellationTokenSource(options.AnalysisTimeout);
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, timeoutCts.Token);

                    var analysisResult = await AnalyzeRequirementDerivationAsync(requirement, combinedCts.Token);
                    
                    lock (progress)
                    {
                        progress.CompletedCount++;
                        results.Add(analysisResult);
                    }

                    onProgress?.Invoke(progress);
                    return analysisResult;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex,
                        $"[RequirementAnalysisService] Batch analysis failed for requirement {requirement.Item}");

                    var failedResult = new RequirementDerivationAnalysis
                    {
                        AnalyzedRequirement = requirement,
                        DerivationIssues = { $"Batch analysis failed: {ex.Message}" },
                        AnalyzedAt = DateTime.UtcNow
                    };

                    lock (progress)
                    {
                        progress.FailedCount++;
                        progress.CompletedCount++;
                        if (options.ContinueOnFailure)
                        {
                            results.Add(failedResult);
                        }
                    }

                    onProgress?.Invoke(progress);

                    if (!options.ContinueOnFailure)
                        throw;

                    return failedResult;
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[RequirementAnalysisService] Batch derivation analysis completed. " +
                $"Processed: {progress.CompletedCount}, Failed: {progress.FailedCount}");

            return results.OrderBy(r => r.AnalyzedRequirement.Item).ToList();
        }

        #region Private Helper Methods for Task 4.4

        /// <summary>
        /// Fallback ATP detection using simple content pattern matching.
        /// </summary>
        private static bool DetectATPContentFallback(Requirement requirement)
        {
            var content = $"{requirement.Name} {requirement.Description}".ToLowerInvariant();
            
            // Simple pattern matching for ATP indicators
            var atpIndicators = new[]
            {
                "test procedure", "automated test", "test step", "verify", "validate",
                "connect", "apply", "measure", "check", "ensure", "configure"
            };

            return atpIndicators.Any(indicator => content.Contains(indicator));
        }

        /// <summary>
        /// Analyzes testing coverage based on requirements and derived capabilities.
        /// </summary>
        private static TestingCoverageAnalysis AnalyzeTestingCoverage(
            IList<Requirement> requirements, 
            IList<DerivedCapability> derivedCapabilities)
        {
            var analysis = new TestingCoverageAnalysis();

            if (requirements.Count == 0)
            {
                analysis.CoveragePercentage = 1.0; // No requirements means 100% coverage
                return analysis;
            }

            var coveredRequirements = new HashSet<string>();
            
            // Check which requirements have corresponding derived capabilities
            foreach (var capability in derivedCapabilities)
            {
                if (!string.IsNullOrEmpty(capability.Id))
                {
                    coveredRequirements.Add(capability.Id);
                }
            }

            analysis.CoveragePercentage = (double)coveredRequirements.Count / requirements.Count;

            // Identify uncovered requirements
            foreach (var requirement in requirements)
            {
                if (!coveredRequirements.Contains(requirement.Item ?? string.Empty))
                {
                    analysis.UncoveredRequirements.Add(requirement.Item ?? "Unknown");
                }
            }

            // Identify testing gaps
            if (analysis.CoveragePercentage < 0.8)
            {
                analysis.TestingGaps.Add("Low requirement coverage - less than 80% of requirements have derived test capabilities");
            }

            if (derivedCapabilities.Count == 0)
            {
                analysis.TestingGaps.Add("No testing capabilities derived - requirements may lack testable content");
            }

            return analysis;
        }

        /// <summary>
        /// Analyzes validation issues from derivation and gap analysis results.
        /// </summary>
        private static List<TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationIssue> AnalyzeValidationIssues(
            IList<RequirementDerivationAnalysis> derivationResults,
            RequirementGapAnalysisResult gapAnalysis)
        {
            var issues = new List<TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationIssue>();

            // Check derivation quality issues
            foreach (var derivation in derivationResults)
            {
                if (derivation.DerivationQuality < 0.5)
                {
                    issues.Add(new TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationIssue
                    {
                        Severity = TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Warning,
                        Description = $"Low derivation quality ({derivation.DerivationQuality:F2}) for requirement {derivation.AnalyzedRequirement.Item}",
                        RequirementId = derivation.AnalyzedRequirement.Item,
                        Category = "Quality"
                    });
                }

                foreach (var issue in derivation.DerivationIssues)
                {
                    issues.Add(new TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationIssue
                    {
                        Severity = TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Error,
                        Description = issue,
                        RequirementId = derivation.AnalyzedRequirement.Item,
                        Category = "Derivation"
                    });
                }
            }

            // Check gap analysis issues
            if (gapAnalysis.IsSuccessful && gapAnalysis.GapAnalysisResult != null)
            {
                var gaps = gapAnalysis.GapAnalysisResult.UncoveredCapabilities?.Where(g => g.Severity >= GapSeverity.High) ?? Enumerable.Empty<UncoveredCapability>();
                foreach (var gap in gaps)
                {
                    issues.Add(new TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationIssue
                    {
                        Severity = gap.Severity == GapSeverity.High ? TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Critical : TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Error,
                        Description = gap.Recommendation,
                        Category = "Gap Analysis"
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Generates testing workflow recommendations based on analysis results.
        /// </summary>
        private static List<string> GenerateTestingWorkflowRecommendations(
            IList<RequirementDerivationAnalysis> derivationResults,
            RequirementGapAnalysisResult gapAnalysis)
        {
            var recommendations = new List<string>();

            // Collect recommendations from derivation analysis
            foreach (var derivation in derivationResults)
            {
                recommendations.AddRange(derivation.Recommendations);
            }

            // Add gap analysis recommendations
            if (gapAnalysis.IsSuccessful && gapAnalysis.GapAnalysisResult?.UncoveredCapabilities?.Any() == true)
            {
                recommendations.AddRange(gapAnalysis.GapAnalysisResult.UncoveredCapabilities.Select(r => r.Recommendation));
            }

            // Add general workflow recommendations
            var lowQualityCount = derivationResults.Count(d => d.DerivationQuality < 0.7);
            if (lowQualityCount > 0)
            {
                recommendations.Add($"Consider improving {lowQualityCount} requirements with low derivation quality for better testing coverage");
            }

            var noAtpCount = derivationResults.Count(d => !d.HasATPContent);
            if (noAtpCount > derivationResults.Count * 0.5)
            {
                recommendations.Add("Consider adding more testable content (ATP procedures) to requirements for better test automation");
            }

            return recommendations.Distinct().ToList();
        }

        /// <summary>
        /// Calculates overall validation score based on all analysis results.
        /// </summary>
        private static double CalculateOverallValidationScore(TestingWorkflowValidationResult result)
        {
            double score = 1.0;

            // Reduce score based on coverage
            var coverageScore = result.CoverageAnalysis?.CoveragePercentage ?? 0.0;
            score *= (0.4 + 0.6 * coverageScore); // Coverage contributes 60% of score

            // Reduce score based on critical issues
            var criticalIssues = result.Issues.Count(i => i.Severity == TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Critical);
            var errorIssues = result.Issues.Count(i => i.Severity == TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Error);
            
            score *= Math.Max(0.0, 1.0 - (criticalIssues * 0.2 + errorIssues * 0.1));

            return Math.Max(0.0, Math.Min(1.0, score));
        }

        #endregion

        /// <summary>
        /// User choice when analysis timeout occurs
        /// </summary>
        private enum AnalysisTimeoutChoice
        {
            Continue,
            Cancel
        }
    }
}
