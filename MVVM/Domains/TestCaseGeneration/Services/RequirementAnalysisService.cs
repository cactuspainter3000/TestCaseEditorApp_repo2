using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services
{
    /// <summary>
    /// Service for analyzing requirement quality using LLM.
    /// Generates structured analysis with quality scores, issues, and recommendations.
    /// </summary>
    public sealed class RequirementAnalysisService
    {
        private readonly ITextGenerationService _llmService;
        private readonly RequirementAnalysisPromptBuilder _promptBuilder;
        private readonly LlmServiceHealthMonitor? _healthMonitor;
        private readonly RequirementAnalysisCache? _cache;
        private readonly AnythingLLMService? _anythingLLMService;
        private string? _cachedSystemMessage;
        private string? _currentWorkspaceSlug;
        private string? _projectWorkspaceName;
        
        // Cache for workspace prompt validation to avoid repeated checks
        private static bool? _workspaceSystemPromptConfigured;
        private static DateTime _lastWorkspaceValidation = DateTime.MinValue;
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
            _projectWorkspaceName = workspaceName;
            // Clear cached workspace slug when context changes
            _currentWorkspaceSlug = null;
            TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Workspace context set to: {workspaceName ?? "<none>"}");
        }

        /// <summary>
        /// Current cache statistics (null if no cache configured)
        /// </summary>
        public RequirementAnalysisCache.CacheStatistics? CacheStatistics => _cache?.GetStatistics();

        public RequirementAnalysisService(ITextGenerationService llmService, AnythingLLMService? anythingLLMService = null)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _anythingLLMService = anythingLLMService;
            _promptBuilder = new RequirementAnalysisPromptBuilder();
        }

        public RequirementAnalysisService(ITextGenerationService llmService, LlmServiceHealthMonitor healthMonitor, RequirementAnalysisCache? cache = null, AnythingLLMService? anythingLLMService = null)
        {
            _llmService = healthMonitor?.GetHealthyService() ?? throw new ArgumentNullException(nameof(llmService));
            _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
            _cache = cache;
            _anythingLLMService = anythingLLMService;
            _promptBuilder = new RequirementAnalysisPromptBuilder();
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
                
                // Create timeout cancellation token to prevent hanging
                var timeoutSetupStart = DateTime.UtcNow;
                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(AnalysisTimeout);
                var timeoutToken = timeoutCts.Token;
                var timeoutSetupTime = DateTime.UtcNow - timeoutSetupStart;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Timeout setup completed in {timeoutSetupTime.TotalMilliseconds}ms, timeout: {AnalysisTimeout.TotalSeconds}s");
                
                // Try RAG-based analysis first (faster and more context-aware)
                var ragAttemptStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Attempting RAG analysis at {ragAttemptStart:HH:mm:ss.fff}");
                var ragResult = await TryRagAnalysisAsync(requirement, onPartialResult, onProgressUpdate, timeoutToken);
                var ragAttemptTime = DateTime.UtcNow - ragAttemptStart;
                System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] RAG attempt completed in {ragAttemptTime.TotalMilliseconds}ms, success: {ragResult.success}");
                
                if (ragResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Using RAG response, length: {ragResult.response?.Length ?? 0}");
                    response = ragResult.response;
                }
                // Use AnythingLLM with workspace-configured system prompt (avoids sending ~793 lines per request)
                else if (_llmService is AnythingLLMService anythingLlmService)
                {
                    var streamingStart = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Starting AnythingLLM streaming at {streamingStart:HH:mm:ss.fff}");
                    
                    // Check if workspace has system prompt configured to avoid duplication
                    var promptToSend = await GetOptimizedPromptForWorkspaceAsync(anythingLlmService, "test-case-analysis", contextPrompt, cancellationToken);
                    
                    // Use streaming analysis with optimized prompt
                    response = await anythingLlmService.SendChatMessageStreamingAsync(
                        "test-case-analysis", // Default workspace with potentially pre-configured system prompt
                        promptToSend, // Optimized based on workspace configuration
                        onChunkReceived: onPartialResult,
                        onProgressUpdate: onProgressUpdate,
                        cancellationToken: timeoutToken) ?? string.Empty;
                    var streamingTime = DateTime.UtcNow - streamingStart;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] AnythingLLM streaming completed in {streamingTime.TotalMilliseconds}ms, response length: {response?.Length ?? 0}");
                }
                else
                {
                    var traditionalStart = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Starting traditional LLM at {traditionalStart:HH:mm:ss.fff}");
                    // Traditional LLM method
                    response = await _llmService.GenerateWithSystemAsync(_cachedSystemMessage, contextPrompt, timeoutToken);
                    var traditionalTime = DateTime.UtcNow - traditionalStart;
                    System.Diagnostics.Debug.WriteLine($"[ANALYSIS DEBUG] Traditional LLM completed in {traditionalTime.TotalMilliseconds}ms, response length: {response?.Length ?? 0}");
                }

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
                
                // Clean and parse response
                var jsonText = CleanJsonResponse(reflectedResponse);
                var analysis = ParseAnalysisResponse(jsonText);

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
                    
                    // Parse natural language response into structured analysis  
                    var ragAnalysis = ParseNaturalLanguageResponse(ragResult.response ?? string.Empty, requirement.Item ?? "UNKNOWN");
                    
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
                
                // Clean response (remove markdown code fences if present)
                var jsonText = CleanJsonResponse(reflectedResponse ?? string.Empty);

                // Validate JSON format before parsing
                if (!ValidateJsonFormat(jsonText, requirement.Item ?? "UNKNOWN"))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] LLM JSON validation failed for {requirement.Item}, attempting JSON repair...");
                    
                    var repairedResult = await TryJsonRepairAsync(reflectedResponse ?? string.Empty, requirement.Item ?? "UNKNOWN", cancellationToken);
                    
                    if (repairedResult.success && ValidateJsonFormat(repairedResult.repairedJson, requirement.Item ?? "UNKNOWN"))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] LLM JSON repair successful for {requirement.Item}");
                        jsonText = repairedResult.repairedJson;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] LLM JSON repair also failed for {requirement.Item}. Raw response length: {reflectedResponse?.Length ?? 0}");
                        return CreateErrorAnalysis("LLM response failed JSON format validation even after repair attempt");
                    }
                }

                // Parse JSON response
                var analysis = ParseAnalysisResponse(jsonText);

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
                    analysis.QualityScore = Math.Max(1, analysis.QualityScore - 3); // Reduce quality score as penalty
                    
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
                        analysis.QualityScore = Math.Max(1, analysis.QualityScore - 2); // Smaller penalty for suspected fabrication
                    }
                }

                // Log what we got from the LLM for debugging
                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] LLM response for {requirement.Item}: QualityScore={analysis.QualityScore}, Issues={analysis.Issues?.Count ?? 0}, Recommendations={analysis.Recommendations?.Count ?? 0}, HallucinationCheck={analysis.HallucinationCheck}");
                
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
        /// Clean JSON response by removing markdown code fences, extra whitespace, and common formatting issues.
        /// </summary>
        private string CleanJsonResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return response;

            var cleaned = response.Trim();

            // Remove markdown code fences
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(7); // Remove ```json
                var endIndex = cleaned.LastIndexOf("```");
                if (endIndex > 0)
                    cleaned = cleaned.Substring(0, endIndex);
            }
            else if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3); // Remove ```
                var endIndex = cleaned.LastIndexOf("```");
                if (endIndex > 0)
                    cleaned = cleaned.Substring(0, endIndex);
            }

            // Clean up common JSON formatting issues
            cleaned = cleaned.Trim();
            
            // Remove any trailing commas before closing braces/brackets (common AI mistake)
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @",(\s*[}\]])", "$1");
            
            // Remove any duplicate commas
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @",\s*,", ",");
            
            // Fix common character encoding issues that might cause 'u' errors
            cleaned = cleaned.Replace("'", "\""); // Replace single quotes with double quotes
            cleaned = cleaned.Replace(""", "\"").Replace(""", "\""); // Replace smart quotes
            cleaned = cleaned.Replace("'", "\"").Replace("'", "\""); // Replace smart single quotes
            
            // Remove any control characters that might break JSON parsing
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[\x00-\x1F\x7F]", "");

            return cleaned.Trim();
        }

        /// <summary>
        /// Clean template markers like &lt;Category&gt; from category names.
        /// </summary>
        private string CleanTemplateMarkers(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return category;

            // Remove angle brackets and template content
            var cleaned = category.Trim();
            if (cleaned.StartsWith("<") && cleaned.EndsWith(">"))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
                
                // If it contains pipe-separated options, take the first one
                var pipeIndex = cleaned.IndexOf('|');
                if (pipeIndex >= 0)
                {
                    cleaned = cleaned.Substring(0, pipeIndex).Trim();
                }
            }

            return cleaned;
        }

        /// <summary>
        /// Parse the JSON response into a RequirementAnalysis object.
        /// </summary>
        private RequirementAnalysis ParseAnalysisResponse(string jsonText)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var analysis = JsonSerializer.Deserialize<RequirementAnalysis>(jsonText, options);

            if (analysis == null)
            {
                return CreateErrorAnalysis("Deserialized analysis was null");
            }

            // Validate quality score is in valid range
            if (analysis.QualityScore < 1 || analysis.QualityScore > 10)
            {
                analysis.QualityScore = Math.Clamp(analysis.QualityScore, 1, 10);
            }

            // Ensure collections are initialized
            analysis.Issues ??= new System.Collections.Generic.List<AnalysisIssue>();
            analysis.Recommendations ??= new System.Collections.Generic.List<AnalysisRecommendation>();

            // Clean up template markers from categories
            foreach (var issue in analysis.Issues)
            {
                issue.Category = CleanTemplateMarkers(issue.Category);
            }
            foreach (var recommendation in analysis.Recommendations)
            {
                recommendation.Category = CleanTemplateMarkers(recommendation.Category);
            }

            // Enforce maximum recommendations policy - consolidate if LLM provided too many
            if (analysis.Recommendations != null && analysis.Recommendations.Count > 2)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] LLM provided {analysis.Recommendations.Count} recommendations, consolidating to maximum 2");
                
                // Keep the first 2 recommendations and log what we're dropping
                var droppedCount = analysis.Recommendations.Count - 2;
                for (int i = 2; i < analysis.Recommendations.Count; i++)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Dropping recommendation #{i + 1}: {analysis.Recommendations[i].Category} - {analysis.Recommendations[i].Description}");
                }
                
                analysis.Recommendations = analysis.Recommendations.Take(2).ToList();
                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Consolidated {analysis.Recommendations.Count + droppedCount} recommendations down to {analysis.Recommendations.Count}");
            }

            // Debug logging to track field contents
            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Parsed {analysis.Recommendations?.Count ?? 0} recommendations");
            if (analysis.Recommendations != null)
            {
                for (int i = 0; i < analysis.Recommendations.Count; i++)
                {
                    var rec = analysis.Recommendations[i];
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Rec {i+1}: Category='{rec.Category}', HasSuggestedEdit={!string.IsNullOrEmpty(rec.SuggestedEdit)}");
                    if (!string.IsNullOrEmpty(rec.SuggestedEdit))
                    {
                        var suggestedEditPreview = rec.SuggestedEdit.Length > 100 ? rec.SuggestedEdit.Substring(0, 100) + "..." : rec.SuggestedEdit;
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Rec {i+1} SuggestedEdit: '{suggestedEditPreview}'");
                    }
                }
            }

            // Mark as successfully analyzed
            analysis.IsAnalyzed = true;
            analysis.ErrorMessage = null;

            return analysis;
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
                QualityScore = 0,
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
                if (!root.TryGetProperty("QualityScore", out _))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] JSON validation failed for {requirementItem}: Missing QualityScore");
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
        private async Task<(bool success, string response)> TryRagAnalysisAsync(
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
                TestCaseEditorApp.Services.Logging.Log.Debug("[RAG] AnythingLLMService not available, using traditional analysis");
                return (false, string.Empty);
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
                    TestCaseEditorApp.Services.Logging.Log.Debug("[RAG] No workspace configured, using traditional analysis");
                    return (false, string.Empty);
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
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to create thread for requirement {requirement.Item}, using default workspace chat");
                }

                // Use RAG-based analysis
                var ragRequestStart = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Starting RAG request at {ragRequestStart:HH:mm:ss.fff}");
                var response = await _anythingLLMService.SendChatMessageStreamingAsync(
                    workspaceSlug,
                    ragPrompt,
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
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Successfully analyzed requirement {requirement.Item} using workspace '{workspaceSlug}'");
                    return (true, response);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to perform RAG analysis for requirement {requirement.Item}, falling back to traditional method: {ex.Message}");
            }

            return (false, string.Empty);
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
                
                // Look for project-specific workspace
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Looking for project workspace: '{_projectWorkspaceName ?? "<none>"}'...");
                
                AnythingLLMService.Workspace? targetWorkspace = null;
                
                if (!string.IsNullOrEmpty(_projectWorkspaceName))
                {
                    // Look for exact project workspace match first
                    targetWorkspace = workspaces.FirstOrDefault(w => 
                        string.Equals(w.Name, _projectWorkspaceName, StringComparison.OrdinalIgnoreCase));
                    
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Exact match found: {targetWorkspace != null}");
                    
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
                        if (targetWorkspace == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No exact fuzzy match, trying partial matching...");
                            targetWorkspace = workspaces.FirstOrDefault(w => 
                            {
                                var normalizedWorkspaceName = w.Name.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
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
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] No project workspace match, looking for Test Case Editor workspace...");
                    targetWorkspace = workspaces
                        .Where(w => w.Name.Contains("Test Case Editor", StringComparison.OrdinalIgnoreCase) ||
                                    w.Name.Contains("Requirements Analysis", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(w => w.CreatedAt)
                        .FirstOrDefault();
                }
                
                var testCaseWorkspace = targetWorkspace;

                if (testCaseWorkspace != null)
                {
                    _currentWorkspaceSlug = testCaseWorkspace.Slug;
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Found existing workspace: '{testCaseWorkspace.Name}' with slug '{_currentWorkspaceSlug}'");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Using existing workspace: {testCaseWorkspace.Name}");
                    
                    // Try to configure workspace settings to ensure optimal system prompt
                    onProgressUpdate?.Invoke("Configuring workspace settings...");
                    try
                    {
                        var configResult = await _anythingLLMService.ConfigureWorkspaceSettingsAsync(_currentWorkspaceSlug, cancellationToken);
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
                    System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] Failed to create new workspace - returned null");
                }
            }
            catch (Exception ex)
            {
                var workspaceMethodTime = DateTime.UtcNow - workspaceMethodStart;
                System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] EnsureWorkspaceConfiguredAsync failed after {workspaceMethodTime.TotalMilliseconds}ms: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to configure workspace: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine($"[RAG DEBUG] EnsureWorkspaceConfiguredAsync returning null");
            return null;
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
                    var retryAnalysis = ParseNaturalLanguageResponse(retryResponse, requirement.Item ?? "RETRY");
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
            prompt.AppendLine("Format your response exactly as follows:");
            prompt.AppendLine();
            prompt.AppendLine("**QUALITY SCORE:** [1-10]");
            prompt.AppendLine();
            prompt.AppendLine("**ISSUES FOUND:**");
            prompt.AppendLine("- [List specific problems with this requirement]");
            prompt.AppendLine("- [Focus on clarity, testability, completeness issues]");
            prompt.AppendLine();
            prompt.AppendLine("**RECOMMENDATIONS:**");
            prompt.AppendLine("- **Category:** [Issue type] | **Description:** [What to fix] | **Suggested Edit:** [Rewritten requirement text]");
            prompt.AppendLine();
            prompt.AppendLine("**HALLUCINATION CHECK:**");
            prompt.AppendLine("- You MUST respond with 'NO_FABRICATION' for this corrective attempt");
            prompt.AppendLine("- If you still need to invent details, respond with 'FABRICATED_DETAILS' and we'll try a different approach");
            prompt.AppendLine();
            prompt.AppendLine("Remember: It's better to have fewer, accurate recommendations than many fabricated ones!");

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
                    var cleanedRepair = CleanJsonResponse(repairedResponse);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] JSON repair attempt for {requirementId}: {cleanedRepair}");
                    return (true, cleanedRepair);
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
        /// Parse natural language analysis response into structured RequirementAnalysis object
        /// </summary>
        private RequirementAnalysis? ParseNaturalLanguageResponse(string response, string requirementId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Empty natural language response for {requirementId}");
                    return null;
                }

                // Debug logging: Show the actual response content
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Parsing RAG response for {requirementId}:");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Response length: {response.Length} characters");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] First 500 chars: {response.Substring(0, Math.Min(500, response.Length))}");

                var analysis = new RequirementAnalysis
                {
                    Timestamp = DateTime.Now,
                    Issues = new List<AnalysisIssue>(),
                    Recommendations = new List<AnalysisRecommendation>(),
                    HallucinationCheck = ""
                };

                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var currentSection = "";

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Parse quality score
                    if (trimmed.ToUpper().StartsWith("QUALITY") || trimmed.ToUpper().StartsWith("SCORE"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)");
                        if (match.Success && int.TryParse(match.Value, out int score))
                        {
                            analysis.QualityScore = Math.Max(1, Math.Min(10, score));
                        }
                    }
                    // Parse section headers
                    else if (trimmed.ToUpper().Contains("ISSUE") && trimmed.ToUpper().Contains("FOUND"))
                    {
                        currentSection = "issues";
                    }
                    else if (trimmed.ToUpper().Contains("IMPROVED REQUIREMENT") || 
                             trimmed.ToUpper().Contains("REWRITTEN REQUIREMENT"))
                    {
                        currentSection = "improved";
                        continue; // Skip the section header line itself
                    }
                    else if (trimmed.ToUpper().Contains("RECOMMENDATION"))
                    {
                        currentSection = "recommendations";
                    }
                    else if (trimmed.ToUpper().Contains("FABRICATION") || trimmed.ToUpper().Contains("HALLUCINATION"))
                    {
                        if (trimmed.ToUpper().Contains("NO_FABRICATION"))
                        {
                            analysis.HallucinationCheck = "<NO_FABRICATION>";
                        }
                        else if (trimmed.ToUpper().Contains("FABRICATED"))
                        {
                            analysis.HallucinationCheck = "FABRICATED_DETAILS";
                        }
                        currentSection = "";
                    }
                    // Handle improved requirement content (may not be in list format)
                    else if (currentSection == "improved" && !string.IsNullOrWhiteSpace(trimmed) && 
                             !trimmed.StartsWith("-") && !trimmed.StartsWith("•") &&
                             !trimmed.ToUpper().Contains("RECOMMENDATION") &&
                             !trimmed.ToUpper().Contains("HALLUCINATION") &&
                             !trimmed.ToUpper().Contains("OVERALL ASSESSMENT") &&
                             !trimmed.Trim().Equals("[REQUIRED:", StringComparison.OrdinalIgnoreCase) &&
                             !trimmed.StartsWith("[") && !trimmed.EndsWith("]"))
                    {
                        // Accumulate the improved requirement text
                        if (string.IsNullOrWhiteSpace(analysis.ImprovedRequirement))
                        {
                            analysis.ImprovedRequirement = trimmed;
                        }
                        else
                        {
                            analysis.ImprovedRequirement += " " + trimmed;
                        }
                    }
                    // Parse list items
                    else if (trimmed.StartsWith("-") || trimmed.StartsWith("•"))
                    {
                        var content = trimmed.Substring(1).Trim();
                        
                        if (currentSection == "issues" && !string.IsNullOrWhiteSpace(content))
                        {
                            // Parse enhanced format: "Clarity Issue (Medium): Description | Fix: Solution"
                            var parts = content.Split('|', StringSplitOptions.RemoveEmptyEntries);
                            var mainPart = parts.Length > 0 ? parts[0].Trim() : content;
                            var fixPart = parts.Length > 1 ? parts[1].Trim() : "";
                            
                            // Extract issue type, severity, and description
                            var category = "Quality"; // Default
                            var severity = "Medium";  // Default
                            var description = mainPart;
                            
                            // Look for specific issue types
                            if (mainPart.ToUpper().Contains("CLARITY"))
                            {
                                category = "Clarity";
                            }
                            else if (mainPart.ToUpper().Contains("COMPLETENESS"))
                            {
                                category = "Completeness";
                            }
                            else if (mainPart.ToUpper().Contains("TESTABILITY"))
                            {
                                category = "Testability";
                            }
                            else if (mainPart.ToUpper().Contains("CONSISTENCY"))
                            {
                                category = "Consistency";
                            }
                            else if (mainPart.ToUpper().Contains("FEASIBILITY"))
                            {
                                category = "Feasibility";
                            }
                            
                            // Extract severity if present
                            if (mainPart.ToUpper().Contains("(HIGH)"))
                            {
                                severity = "High";
                            }
                            else if (mainPart.ToUpper().Contains("(LOW)"))
                            {
                                severity = "Low";
                            }
                            
                            // Clean up description by removing the category and severity parts
                            if (mainPart.Contains(":"))
                            {
                                var colonIndex = mainPart.IndexOf(":");
                                description = mainPart.Substring(colonIndex + 1).Trim();
                            }
                            
                            // Add fix information to description if present
                            if (fixPart.ToUpper().StartsWith("FIX:"))
                            {
                                var fix = fixPart.Substring(4).Trim();
                                description += $" | Fix: {fix}";
                            }
                            
                            analysis.Issues.Add(new AnalysisIssue
                            {
                                Category = category,
                                Description = description,
                                Severity = severity
                            });
                        }
                        else if (currentSection == "recommendations" && !string.IsNullOrWhiteSpace(content))
                        {
                            // Parse structured recommendation format: Category: X | Description: Y | Suggested Edit: Z
                            var parts = content.Split('|', StringSplitOptions.RemoveEmptyEntries);
                            var recommendation = new AnalysisRecommendation
                            {
                                Category = "Improvement",
                                Description = content, // Default to full content
                                SuggestedEdit = ""
                            };

                            foreach (var part in parts)
                            {
                                var keyValue = part.Trim();
                                if (keyValue.ToUpper().StartsWith("CATEGORY:"))
                                {
                                    recommendation.Category = keyValue.Substring(9).Trim();
                                }
                                else if (keyValue.ToUpper().StartsWith("DESCRIPTION:"))
                                {
                                    recommendation.Description = keyValue.Substring(12).Trim();
                                }
                                else if (keyValue.ToUpper().StartsWith("SUGGESTED EDIT:") || 
                                        keyValue.ToUpper().StartsWith("EDIT:") || 
                                        keyValue.ToUpper().StartsWith("FIX:") ||
                                        keyValue.ToUpper().StartsWith("RATIONALE:"))
                                {
                                    int startIndex;
                                    if (keyValue.ToUpper().StartsWith("SUGGESTED EDIT:"))
                                        startIndex = 15;
                                    else if (keyValue.ToUpper().StartsWith("RATIONALE:"))
                                        startIndex = 10;
                                    else if (keyValue.ToUpper().StartsWith("EDIT:"))
                                        startIndex = 5;
                                    else // FIX:
                                        startIndex = 4;
                                    
                                    recommendation.SuggestedEdit = keyValue.Substring(startIndex).Trim();
                                }
                            }

                            analysis.Recommendations.Add(recommendation);
                        }
                    }
                }

                // Set default quality score if not found
                if (analysis.QualityScore == 0)
                {
                    analysis.QualityScore = analysis.Issues.Count > 3 ? 4 : 6; // Reasonable default based on issues found
                }

                // Set default hallucination check if not found
                if (string.IsNullOrWhiteSpace(analysis.HallucinationCheck))
                {
                    analysis.HallucinationCheck = "<NO_FABRICATION>";
                }

                // Validate that improved requirement was provided
                if (string.IsNullOrWhiteSpace(analysis.ImprovedRequirement))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] No improved requirement provided for {requirementId}, adding to freeform feedback");
                    if (!string.IsNullOrWhiteSpace(analysis.FreeformFeedback))
                    {
                        analysis.FreeformFeedback += "\n\n[Note: LLM did not provide an improved requirement rewrite]";
                    }
                    else
                    {
                        analysis.FreeformFeedback = "[Note: LLM did not provide an improved requirement rewrite]";
                    }
                }

                // If no structured content was parsed, store the full response as freeform feedback
                if (analysis.Issues.Count == 0 && analysis.Recommendations.Count == 0 && string.IsNullOrWhiteSpace(analysis.FreeformFeedback))
                {
                    analysis.FreeformFeedback = response.Trim();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] No structured content found, storing full response as freeform feedback (length: {response.Length})");
                }

                // Mark as successfully analyzed
                analysis.IsAnalyzed = true;
                analysis.ErrorMessage = null;
                analysis.Timestamp = DateTime.Now;

                TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Natural language parsing successful for {requirementId}: Score={analysis.QualityScore}, Issues={analysis.Issues.Count}, Recommendations={analysis.Recommendations.Count}, ImprovedReq={!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement)}, Freeform={!string.IsNullOrWhiteSpace(analysis.FreeformFeedback)}");
                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[RequirementAnalysisService] Natural language parsing failed for {requirementId}");
                return null;
            }
        }
    }
}
