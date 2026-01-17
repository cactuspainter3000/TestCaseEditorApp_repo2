using System;
using System.Linq;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.Parsing
{
    /// <summary>
    /// Parser for JSON-formatted analysis responses from LLM services.
    /// Handles structured JSON with RequirementAnalysis object format.
    /// </summary>
    public class JsonResponseParser : IResponseParser
    {
        public string ParserName => "JSON";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var cleaned = CleanJsonResponse(response);
            
            // Quick JSON format detection
            var trimmed = cleaned.Trim();
            return (trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
                   (trimmed.StartsWith("[") && trimmed.EndsWith("]"));
        }

        public RequirementAnalysis? ParseResponse(string response, string requirementId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Empty response for {requirementId}");
                    return null;
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Parsing JSON response for {requirementId}");

                var cleanedResponse = CleanJsonResponse(response);
                var analysis = ParseAnalysisResponse(cleanedResponse);

                // Set timestamp
                analysis.Timestamp = DateTime.Now;

                // Mark as successfully analyzed
                analysis.IsAnalyzed = true;
                analysis.ErrorMessage = null;

                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] JSON parsing successful for {requirementId}: Score={analysis.QualityScore}, Issues={analysis.Issues?.Count ?? 0}");

                return analysis;
            }
            catch (JsonException ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[{ParserName}Parser] Failed to parse JSON response for {requirementId}");
                return null;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[{ParserName}Parser] Unexpected error parsing response for {requirementId}");
                return null;
            }
        }

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

            // Handle backward compatibility: if QualityScore is set, treat it as OriginalQualityScore
            if (analysis.OriginalQualityScore == 0 && analysis.QualityScore > 0)
            {
                analysis.OriginalQualityScore = analysis.QualityScore;
            }

            // Validate original quality score is in valid range
            if (analysis.OriginalQualityScore < 1 || analysis.OriginalQualityScore > 10)
            {
                analysis.OriginalQualityScore = Math.Clamp(analysis.OriginalQualityScore, 1, 10);
            }

            // If an improved requirement is provided, estimate improved quality score
            if (!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement) && !analysis.ImprovedQualityScore.HasValue)
            {
                // Improved version should score higher - add 1-3 points based on number of issues fixed
                var issueCount = analysis.Issues?.Count ?? 0;
                var improvement = Math.Min(3, Math.Max(1, issueCount / 2)); // 1-3 point improvement
                analysis.ImprovedQualityScore = Math.Min(10, analysis.OriginalQualityScore + improvement);
            }

            // Ensure collections are initialized
            analysis.Issues ??= new System.Collections.Generic.List<AnalysisIssue>();

            // Clean up template markers from categories
            foreach (var issue in analysis.Issues)
            {
                issue.Category = CleanTemplateMarkers(issue.Category);
            }

            return analysis;
        }

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

        private RequirementAnalysis CreateErrorAnalysis(string errorMessage)
        {
            return new RequirementAnalysis
            {
                IsAnalyzed = false,
                ErrorMessage = errorMessage,
                QualityScore = 0,
                Issues = new System.Collections.Generic.List<AnalysisIssue>(),
                FreeformFeedback = string.Empty,
                Timestamp = DateTime.Now
            };
        }
    }
}