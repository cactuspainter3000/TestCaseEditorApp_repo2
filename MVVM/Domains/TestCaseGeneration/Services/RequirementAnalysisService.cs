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
        private string? _cachedSystemMessage;
        
        /// <summary>
        /// Enable/disable self-reflection feature. When enabled, the LLM will review its own responses for quality.
        /// </summary>
        public bool EnableSelfReflection { get; set; } = false;

        /// <summary>
        /// Enable/disable caching of analysis results. When enabled, identical requirement content will use cached results.
        /// </summary>
        public bool EnableCaching { get; set; } = true;

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

        public RequirementAnalysisService(ITextGenerationService llmService)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _promptBuilder = new RequirementAnalysisPromptBuilder();
        }

        public RequirementAnalysisService(ITextGenerationService llmService, LlmServiceHealthMonitor healthMonitor, RequirementAnalysisCache? cache = null)
        {
            _llmService = healthMonitor?.GetHealthyService() ?? throw new ArgumentNullException(nameof(llmService));
            _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
            _cache = cache;
            _promptBuilder = new RequirementAnalysisPromptBuilder();
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
                    _cache.Set(requirement, analysis, analysisDuration);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementAnalysisService] Cached analysis result for {requirement.Item} (duration: {analysisDuration.TotalMilliseconds:F0}ms)");
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
    }
}
