using System;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Parser for JSON or fenced-JSON requirement analysis responses.
    /// Handles responses shaped like RequirementAnalysis objects.
    /// </summary>
    public class JsonRequirementAnalysisParser : IResponseParser
    {
        public string ParserName => "JsonRequirementAnalysis";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var cleaned = CleanupResponse(response);

            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            var trimmed = cleaned.Trim();

            bool looksLikeJson =
                (trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
                (trimmed.StartsWith("[") && trimmed.EndsWith("]"));

            TestCaseEditorApp.Services.Logging.Log.Debug(
                $"[{ParserName}Parser] CanParse result: {looksLikeJson}");

            return looksLikeJson;
        }

        public RequirementAnalysis? ParseResponse(string response, string requirementId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(response))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[{ParserName}Parser] Empty response for {requirementId}");
                    return null;
                }

                var cleaned = CleanupResponse(response);

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[{ParserName}Parser] Parsing JSON response for {requirementId}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var analysis = JsonSerializer.Deserialize<RequirementAnalysis>(cleaned, options);

                if (analysis == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[{ParserName}Parser] Deserialized RequirementAnalysis is null for {requirementId}");
                    return null;
                }

                analysis.Issues ??= new System.Collections.Generic.List<AnalysisIssue>();
                analysis.Recommendations ??= new System.Collections.Generic.List<AnalysisRecommendation>();
                analysis.Timestamp = DateTime.Now;
                analysis.IsAnalyzed = true;
                analysis.ErrorMessage ??= string.Empty;

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[{ParserName}Parser] Successfully parsed {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues.Count}, Recommendations={analysis.Recommendations.Count}");

                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(
                    ex,
                    $"[{ParserName}Parser] Failed to parse JSON response for {requirementId}");
                return null;
            }
        }

        /// <summary>
        /// Removes markdown code fences and trims surrounding whitespace.
        /// </summary>
        private static string CleanupResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            var cleaned = response.Trim();

            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(7).Trim();
            }
            else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(3).Trim();
            }

            if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
            }

            return cleaned;
        }
    }
}