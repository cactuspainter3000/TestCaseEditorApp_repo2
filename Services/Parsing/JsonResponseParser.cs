using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Parser for JSON-based requirement analysis responses.
    /// Supports raw JSON and markdown-fenced JSON.
    /// </summary>
    public class JsonResponseParser : IResponseParser
    {
        public string ParserName => "JSON";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var cleaned = ExtractJsonPayload(response);
            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[JsonParser] CanParse=false (root is not object)");
                    return false;
                }

                // Accept the known schema variants currently produced by prompts.
                var canParse = HasProperty(root, "QualityScore") ||
                               HasProperty(root, "OriginalQualityScore") ||
                               HasProperty(root, "Recommendations") ||
                               HasProperty(root, "Analysis") ||
                               HasProperty(root, "FreeformFeedback");

                TestCaseEditorApp.Services.Logging.Log.Info($"[JsonParser] CanParse={canParse}, payloadLength={cleaned.Length}");
                return canParse;
            }
            catch (JsonException ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JsonParser] CanParse JSON parse failed: {ex.Message}");
                return false;
            }
        }

        public RequirementAnalysis? ParseResponse(string response, string requirementId)
        {
            try
            {
                var cleaned = ExtractJsonPayload(response);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Empty JSON payload for {requirementId}");
                    return null;
                }

                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;

                var analysis = new RequirementAnalysis
                {
                    Timestamp = DateTime.Now,
                    IsAnalyzed = true,
                    ErrorMessage = null,
                    HallucinationCheck = GetString(root, "HallucinationCheck") ?? "NO_FABRICATION",
                    FreeformFeedback = GetString(root, "FreeformFeedback") ?? GetString(root, "Analysis"),
                    ImprovedRequirement = GetString(root, "ImprovedRequirement") ?? GetString(root, "RewrittenRequirement"),
                    Issues = ParseIssues(root),
                    Recommendations = ParseRecommendations(root)
                };

                analysis.OriginalQualityScore = GetInt(root, "OriginalQualityScore")
                    ?? GetInt(root, "QualityScore")
                    ?? 0;

                analysis.ImprovedQualityScore = GetInt(root, "ImprovedQualityScore");

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[{ParserName}Parser] Parsed JSON response for {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues.Count}, Recommendations={analysis.Recommendations.Count}");

                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[{ParserName}Parser] Failed to parse JSON response for {requirementId}");
                return null;
            }
        }

        private static string ExtractJsonPayload(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            var cleaned = response.Trim();

            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(7);
            else if (cleaned.StartsWith("```", StringComparison.Ordinal))
                cleaned = cleaned.Substring(3);

            if (cleaned.EndsWith("```", StringComparison.Ordinal))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);

            cleaned = cleaned.Trim();

            var firstBrace = cleaned.IndexOf('{');
            var lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            cleaned = cleaned.Trim();

            // Best-effort cleanup for common LLM JSON defects.
            cleaned = cleaned.Replace("\u201c", "\"").Replace("\u201d", "\"");
            cleaned = cleaned.Replace("\u2018", "'").Replace("\u2019", "'");
            cleaned = Regex.Replace(cleaned, @",\s*([}\]])", "$1");

            return cleaned;
        }

        private static bool HasProperty(JsonElement root, string propertyName)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static JsonElement? GetProperty(JsonElement root, string propertyName)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return prop.Value;
            }

            return null;
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            var value = GetProperty(root, propertyName);
            if (value == null)
                return null;

            if (value.Value.ValueKind == JsonValueKind.String)
                return value.Value.GetString();

            return value.Value.ToString();
        }

        private static int? GetInt(JsonElement root, string propertyName)
        {
            var value = GetProperty(root, propertyName);
            if (value == null)
                return null;

            if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var intValue))
                return intValue;

            if (value.Value.ValueKind == JsonValueKind.String && int.TryParse(value.Value.GetString(), out var parsed))
                return parsed;

            return null;
        }

        private static List<AnalysisIssue> ParseIssues(JsonElement root)
        {
            var issues = new List<AnalysisIssue>();
            var issuesElement = GetProperty(root, "Issues");
            if (issuesElement == null || issuesElement.Value.ValueKind != JsonValueKind.Array)
                return issues;

            foreach (var issueEl in issuesElement.Value.EnumerateArray())
            {
                if (issueEl.ValueKind != JsonValueKind.Object)
                    continue;

                issues.Add(new AnalysisIssue
                {
                    Category = GetString(issueEl, "Category") ?? "Unknown",
                    Severity = GetString(issueEl, "Severity") ?? "Medium",
                    Description = GetString(issueEl, "Description") ?? string.Empty,
                    Fix = GetString(issueEl, "Fix") ?? string.Empty
                });
            }

            return issues;
        }

        private static List<AnalysisRecommendation> ParseRecommendations(JsonElement root)
        {
            var recommendations = new List<AnalysisRecommendation>();
            var recsElement = GetProperty(root, "Recommendations");
            if (recsElement == null || recsElement.Value.ValueKind != JsonValueKind.Array)
                return recommendations;

            foreach (var recEl in recsElement.Value.EnumerateArray())
            {
                if (recEl.ValueKind != JsonValueKind.Object)
                    continue;

                recommendations.Add(new AnalysisRecommendation
                {
                    Category = GetString(recEl, "Category") ?? "General",
                    Description = GetString(recEl, "Description") ?? string.Empty,
                    SuggestedEdit = GetString(recEl, "SuggestedEdit")
                });
            }

            return recommendations;
        }
    }
}