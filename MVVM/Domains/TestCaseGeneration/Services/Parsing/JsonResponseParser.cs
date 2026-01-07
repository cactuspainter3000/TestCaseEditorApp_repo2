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

                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] JSON parsing successful for {requirementId}: Score={analysis.QualityScore}, Issues={analysis.Issues?.Count ?? 0}, Recommendations={analysis.Recommendations?.Count ?? 0}");

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
                TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] LLM provided {analysis.Recommendations.Count} recommendations, consolidating to maximum 2");
                
                // Keep the first 2 recommendations and log what we're dropping
                var droppedCount = analysis.Recommendations.Count - 2;
                for (int i = 2; i < analysis.Recommendations.Count; i++)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Dropping recommendation #{i + 1}: {analysis.Recommendations[i].Category} - {analysis.Recommendations[i].Description}");
                }
                
                analysis.Recommendations = analysis.Recommendations.Take(2).ToList();
                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Consolidated {droppedCount} recommendations to meet maximum policy of 2");
            }

            // Filter out invalid recommendations that lack SuggestedEdit
            if (analysis.Recommendations != null)
            {
                var validRecommendations = analysis.Recommendations.Where(r => !string.IsNullOrWhiteSpace(r.SuggestedEdit)).ToList();
                var removedCount = analysis.Recommendations.Count - validRecommendations.Count;
                
                if (removedCount > 0)
                {
                    for (int i = 0; i < analysis.Recommendations.Count; i++)
                    {
                        var rec = analysis.Recommendations[i];
                        if (string.IsNullOrWhiteSpace(rec.SuggestedEdit))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Recommendation {i + 1} missing SuggestedEdit - removing from recommendations to prevent display issues");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Removing invalid recommendation {i + 1}");
                        }
                    }
                    
                    analysis.Recommendations = validRecommendations;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Removed {removedCount} invalid recommendations lacking SuggestedEdit");
                }
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
                Recommendations = new System.Collections.Generic.List<AnalysisRecommendation>(),
                FreeformFeedback = string.Empty,
                Timestamp = DateTime.Now
            };
        }
    }
}