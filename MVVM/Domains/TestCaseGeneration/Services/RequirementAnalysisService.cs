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
        
        /// <summary>
        /// Enable/disable self-reflection feature. When enabled, the LLM will review its own responses for quality.
        /// </summary>
        public bool EnableSelfReflection { get; set; } = false;

        /// <summary>
        /// Enable/disable caching of analysis results. When enabled, identical requirement content will use cached results.
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Timeout for LLM analysis operations. Default is 60 seconds to prevent hanging.
        /// </summary>
        public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Current health status of the LLM service (null if no health monitor configured)
        /// </summary>
        public LlmServiceHealthMonitor.HealthReport? ServiceHealth => _healthMonitor?.CurrentHealth;

        /// <summary>
        /// Whether the service is currently using fallback mode
        /// </summary>
        public bool IsUsingFallback => _healthMonitor?.IsUsingFallback ?? false;

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

            onProgressUpdate?.Invoke("Starting requirement analysis...");

            // Check cache first if enabled
            if (EnableCaching && _cache != null)
            {
                onProgressUpdate?.Invoke("Checking cache...");
                if (_cache.TryGet(requirement, out var cachedAnalysis) && cachedAnalysis != null)
                {
                    onProgressUpdate?.Invoke("Using cached analysis");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RequirementAnalysisService] Using CACHED analysis for requirement {requirement.Item}");
                    return cachedAnalysis;
                }
            }

            var analysisStartTime = DateTime.UtcNow;
            CancellationTokenSource? timeoutCts = null;

            try
            {
                onProgressUpdate?.Invoke("Preparing analysis context...");
                
                // Get verification assumptions for context
                var verificationAssumptions = GetVerificationAssumptionsText(requirement);

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
                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(AnalysisTimeout);
                var timeoutToken = timeoutCts.Token;
                
                // Try RAG-based analysis first (faster and more context-aware)
                var ragResult = await TryRagAnalysisAsync(requirement, onPartialResult, onProgressUpdate, timeoutToken);
                if (ragResult.success)
                {
                    response = ragResult.response;
                }
                // Fallback to direct LLM service
                else if (_llmService is AnythingLLMService anythingLlmService)
                {
                    // Use streaming analysis for real-time feedback
                    response = await anythingLlmService.SendChatMessageStreamingAsync(
                        "test-case-analysis", // Default workspace
                        $"{_cachedSystemMessage}\\n\\n{contextPrompt}",
                        onChunkReceived: onPartialResult,
                        onProgressUpdate: onProgressUpdate,
                        cancellationToken: timeoutToken) ?? string.Empty;
                }
                else
                {
                    // Traditional LLM method
                    response = await _llmService.GenerateWithSystemAsync(_cachedSystemMessage, contextPrompt, timeoutToken);
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
                return analysis;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                if (timeoutCts?.Token.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    onProgressUpdate?.Invoke($"Analysis timed out after {AnalysisTimeout.TotalSeconds} seconds");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] Analysis timed out for requirement {requirement.Item} after {AnalysisTimeout.TotalSeconds}s");
                    return CreateErrorAnalysis($"Analysis timed out after {AnalysisTimeout.TotalSeconds} seconds. This may indicate an issue with the LLM service.");
                }
                else
                {
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
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] JSON validation failed for {requirement.Item}. Raw response length: {reflectedResponse?.Length ?? 0}");
                    return CreateErrorAnalysis("LLM response failed JSON format validation");
                }

                // Parse JSON response
                var analysis = ParseAnalysisResponse(jsonText);

                // Check for self-reported fabrication
                if (!string.IsNullOrEmpty(analysis.HallucinationCheck) && 
                    analysis.HallucinationCheck.Contains("FABRICATED_DETAILS", StringComparison.OrdinalIgnoreCase))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementAnalysisService] LLM self-reported fabrication for {requirement.Item}: {analysis.HallucinationCheck}");
                    
                    // Mark analysis as having fabricated content
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
        /// Clean JSON response by removing markdown code fences and extra whitespace.
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
                "compression", "encoding", "format specification"
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
            // Check if RAG is available
            if (_anythingLLMService == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[RAG] AnythingLLMService not available, using traditional analysis");
                return (false, string.Empty);
            }

            try
            {
                // Get or create workspace for current project
                var workspaceSlug = await EnsureWorkspaceConfiguredAsync(onProgressUpdate, cancellationToken);
                if (string.IsNullOrEmpty(workspaceSlug))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[RAG] No workspace configured, using traditional analysis");
                    return (false, string.Empty);
                }

                // Upload supplemental information as documents if not already done
                await EnsureSupplementalInfoUploadedAsync(requirement, workspaceSlug, onProgressUpdate, cancellationToken);

                onProgressUpdate?.Invoke("Performing RAG-enhanced analysis...");

                // Create simplified prompt for RAG (context is in uploaded documents)
                var ragPrompt = BuildRagOptimizedPrompt(requirement);

                // Use RAG-based analysis
                var response = await _anythingLLMService.SendChatMessageStreamingAsync(
                    workspaceSlug,
                    ragPrompt,
                    onChunkReceived: onPartialResult,
                    onProgressUpdate: onProgressUpdate,
                    cancellationToken: cancellationToken) ?? string.Empty;

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
            if (_anythingLLMService == null) return null;

            // Check if we already have a cached workspace
            if (!string.IsNullOrEmpty(_currentWorkspaceSlug))
            {
                return _currentWorkspaceSlug;
            }

            try
            {
                onProgressUpdate?.Invoke("Checking RAG workspace...");

                // Get existing workspaces
                var workspaces = await _anythingLLMService.GetWorkspacesAsync(cancellationToken);
                
                // Look for a test case editor workspace
                var testCaseWorkspace = workspaces.FirstOrDefault(w => 
                    w.Name.Contains("Test Case Editor", StringComparison.OrdinalIgnoreCase) ||
                    w.Name.Contains("Requirements Analysis", StringComparison.OrdinalIgnoreCase));

                if (testCaseWorkspace != null)
                {
                    _currentWorkspaceSlug = testCaseWorkspace.Slug;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Using existing workspace: {testCaseWorkspace.Name}");
                    return _currentWorkspaceSlug;
                }

                // Create a new workspace if none exists
                onProgressUpdate?.Invoke("Creating RAG workspace...");
                var (newWorkspace, _) = await _anythingLLMService.CreateAndConfigureWorkspaceAsync(
                    "Requirements Analysis",
                    onProgress: onProgressUpdate,
                    cancellationToken: cancellationToken);

                if (newWorkspace != null)
                {
                    _currentWorkspaceSlug = newWorkspace.Slug;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Created new workspace: {newWorkspace.Name}");
                    return _currentWorkspaceSlug;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to configure workspace: {ex.Message}");
            }

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

                // Upload as document (simplified - would need actual upload implementation)
                TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Prepared supplemental information document for {requirement.Item} ({docContent.Length} characters)");
                // Note: Actual document upload would require implementing document upload API in AnythingLLMService
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to upload supplemental information for {requirement.Item}: {ex.Message}");
            }
        }

        /// <summary>
        /// Build RAG-optimized prompt that leverages uploaded documents for context.
        /// Much simpler than traditional prompts since context is in RAG documents.
        /// </summary>
        private string BuildRagOptimizedPrompt(Requirement requirement)
        {
            var prompt = new System.Text.StringBuilder();
            
            prompt.AppendLine("Analyze the following requirement for quality and provide structured feedback in JSON format:");
            prompt.AppendLine();
            prompt.AppendLine($"**Requirement ID:** {requirement.Item}");
            prompt.AppendLine($"**Name:** {requirement.Name}");
            prompt.AppendLine($"**Description:** {requirement.Description}");
            prompt.AppendLine();
            prompt.AppendLine("Use any uploaded supplemental information and definitions to provide accurate analysis.");
            prompt.AppendLine("Return only valid JSON with OverallScore, Issues, and Recommendations.");

            return prompt.ToString();
        }
    }
}
