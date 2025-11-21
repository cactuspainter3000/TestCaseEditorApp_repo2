using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for analyzing requirement quality using LLM.
    /// Generates structured analysis with quality scores, issues, and recommendations.
    /// </summary>
    public sealed class RequirementAnalysisService
    {
        private readonly ITextGenerationService _llmService;
        private readonly RequirementAnalysisPromptBuilder _promptBuilder;

        public RequirementAnalysisService(ITextGenerationService llmService)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _promptBuilder = new RequirementAnalysisPromptBuilder();
        }

        /// <summary>
        /// Analyze a single requirement's quality.
        /// </summary>
        /// <param name="requirement">The requirement to analyze</param>
        /// <param name="useFastMode">If true, uses simplified prompt for faster analysis</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>RequirementAnalysis with quality score, issues, and recommendations</returns>
        public async Task<RequirementAnalysis> AnalyzeRequirementAsync(
            Requirement requirement,
            bool useFastMode = false,
            CancellationToken cancellationToken = default)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            try
            {
                // Build the prompt
                var prompt = useFastMode
                    ? _promptBuilder.BuildFastAnalysisPrompt(
                        requirement.Item ?? "UNKNOWN",
                        requirement.Name ?? string.Empty,
                        requirement.Description ?? string.Empty)
                    : _promptBuilder.BuildAnalysisPrompt(
                        requirement.Item ?? "UNKNOWN",
                        requirement.Name ?? string.Empty,
                        requirement.Description ?? string.Empty,
                        requirement.Tables,
                        requirement.LooseContent);

                // Call LLM
                var response = await _llmService.GenerateAsync(prompt, cancellationToken);

                if (string.IsNullOrWhiteSpace(response))
                {
                    return CreateErrorAnalysis("LLM returned empty response");
                }

                // Clean response (remove markdown code fences if present)
                var jsonText = CleanJsonResponse(response);

                // Parse JSON response
                var analysis = ParseAnalysisResponse(jsonText);

                // Set timestamp
                analysis.Timestamp = DateTime.Now;

                return analysis;
            }
            catch (OperationCanceledException)
            {
                return CreateErrorAnalysis("Analysis was cancelled");
            }
            catch (JsonException ex)
            {
                return CreateErrorAnalysis($"Failed to parse LLM response as JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
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
        /// Validate that the LLM service is available and responding.
        /// </summary>
        public async Task<bool> ValidateServiceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var testPrompt = "Return only: {\"test\":\"ok\"}";
                var response = await _llmService.GenerateAsync(testPrompt, cancellationToken);
                return !string.IsNullOrWhiteSpace(response);
            }
            catch
            {
                return false;
            }
        }
    }
}
