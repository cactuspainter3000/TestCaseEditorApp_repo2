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
                // If the payload looks like our expected schema but is slightly malformed/truncated,
                // allow ParseResponse to run a repair attempt.
                var likelyJsonSchema = LooksLikeExpectedSchema(cleaned);
                if (likelyJsonSchema)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JsonParser] CanParse JSON parse failed, but schema hints detected. Will attempt repair in ParseResponse. Error: {ex.Message}");
                    return true;
                }

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

                JsonElement root;
                JsonDocument? parsedDoc = null;

                try
                {
                    parsedDoc = JsonDocument.Parse(cleaned);
                    root = parsedDoc.RootElement;
                }
                catch (JsonException firstEx)
                {
                    var repaired = TryRepairMalformedJson(cleaned);
                    if (string.IsNullOrWhiteSpace(repaired))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Failed initial parse and no repair candidate available for {requirementId}: {firstEx.Message}");
                        return null;
                    }

                    try
                    {
                        parsedDoc = JsonDocument.Parse(repaired);
                        root = parsedDoc.RootElement;
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Parsed repaired JSON payload for {requirementId} (originalLength={cleaned.Length}, repairedLength={repaired.Length})");
                    }
                    catch (JsonException secondEx)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Repair parse failed for {requirementId}: {secondEx.Message}");
                        return null;
                    }
                }

                using (parsedDoc)
                {
                    var analysis = new RequirementAnalysis
                    {
                        Timestamp = DateTime.Now,
                        IsAnalyzed = true,
                        ErrorMessage = null,
                        HallucinationCheck = GetString(root, "HallucinationCheck") ?? "NO_FABRICATION",
                        FreeformFeedback = GetStringValue(root, "FreeformFeedback")
                            ?? GetStringValue(root, "AnalysisSummary")
                            ?? GetStringValue(root, "Summary")
                            ?? GetStringValue(root, "AdditionalFeedback")
                            ?? GetStringValue(root, "Analysis"),
                        Issues = ParseIssues(root),
                        Recommendations = ParseRecommendations(root)
                    };

                    analysis.ImprovedRequirement = GetFirstNonEmptyString(root,
                            "ImprovedRequirement",
                            "RewrittenRequirement",
                            "RefinedRequirement",
                            "RevisedRequirement",
                            "RequirementRewrite",
                            "RewriteRequirement",
                            "RewriteText",
                            "SuggestedRequirement")
                        ?? analysis.Recommendations?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r?.SuggestedEdit))?.SuggestedEdit;

                    analysis.OriginalQualityScore = GetInt(root, "OriginalQualityScore")
                        ?? GetInt(root, "QualityScore")
                        ?? 0;

                    TestCaseEditorApp.Services.Logging.Log.Info(
                        $"[{ParserName}Parser] Parsed JSON response for {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues.Count}, Recommendations={analysis.Recommendations.Count}, HasRewrite={!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement)}");

                    return analysis;
                }
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

        private static bool LooksLikeExpectedSchema(string cleaned)
        {
            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            return cleaned.Contains("QualityScore", StringComparison.OrdinalIgnoreCase)
                || cleaned.Contains("OriginalQualityScore", StringComparison.OrdinalIgnoreCase)
                || cleaned.Contains("Recommendations", StringComparison.OrdinalIgnoreCase)
                || cleaned.Contains("Analysis", StringComparison.OrdinalIgnoreCase)
                || cleaned.Contains("FreeformFeedback", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryRepairMalformedJson(string cleaned)
        {
            if (string.IsNullOrWhiteSpace(cleaned))
                return string.Empty;

            var repaired = cleaned;

            // Close an unterminated string if quote count is odd.
            int quoteCount = 0;
            bool escaped = false;
            foreach (var ch in repaired)
            {
                if (ch == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"' && !escaped)
                {
                    quoteCount++;
                }

                escaped = false;
            }

            if (quoteCount % 2 != 0)
            {
                repaired += "\"";
            }

            // Close missing braces/brackets while respecting quoted text.
            int openBraces = 0;
            int openBrackets = 0;
            bool inString = false;
            escaped = false;

            foreach (var ch in repaired)
            {
                if (ch == '\\' && !escaped)
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"' && !escaped)
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    if (ch == '{') openBraces++;
                    else if (ch == '}') openBraces--;
                    else if (ch == '[') openBrackets++;
                    else if (ch == ']') openBrackets--;
                }

                escaped = false;
            }

            if (openBrackets > 0)
            {
                repaired += new string(']', openBrackets);
            }

            if (openBraces > 0)
            {
                repaired += new string('}', openBraces);
            }

            // Remove trailing commas before closing tokens one final time.
            repaired = Regex.Replace(repaired, @",\s*([}\]])", "$1");
            return repaired;
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

        private static string? GetStringValue(JsonElement root, string propertyName)
        {
            var value = GetProperty(root, propertyName);
            if (value == null)
                return null;

            return value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString()
                : null;
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

        private static string? GetFirstNonEmptyString(JsonElement root, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var value = GetString(root, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static List<AnalysisIssue> ParseIssues(JsonElement root)
        {
            var issues = new List<AnalysisIssue>();

            // Standard schema: Issues: [{ Category, Severity, Description, Fix }]
            var issuesElement = GetProperty(root, "Issues");
            if (issuesElement != null && issuesElement.Value.ValueKind == JsonValueKind.Array)
            {
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
            }

            // Some model variants emit legacy issue buckets at the top level.
            AddLegacyIssueArray(issues, root, "ClarityIssues", "Clarity");
            AddLegacyIssueArray(issues, root, "CompletenessIssues", "Completeness");
            AddLegacyIssueArray(issues, root, "TestabilityIssues", "Testability");
            AddLegacyIssueArray(issues, root, "AmbiguityIssues", "Ambiguity");

            // Legacy schema often appears as AdditionalFeedback/Analysis object with arrays:
            // ClarityIssues, CompletenessIssues, etc.
            JsonElement feedbackRoot;
            if (TryGetLegacyFeedbackRoot(root, out feedbackRoot))
            {
                AddLegacyIssueArray(issues, feedbackRoot, "ClarityIssues", "Clarity");
                AddLegacyIssueArray(issues, feedbackRoot, "CompletenessIssues", "Completeness");
                AddLegacyIssueArray(issues, feedbackRoot, "TestabilityIssues", "Testability");
                AddLegacyIssueArray(issues, feedbackRoot, "AmbiguityIssues", "Ambiguity");
            }

            return issues;
        }

        private static List<AnalysisRecommendation> ParseRecommendations(JsonElement root)
        {
            var recommendations = new List<AnalysisRecommendation>();

            // Standard schema: Recommendations: [{ Category, Description, SuggestedEdit }]
            var recsElement = GetProperty(root, "Recommendations");
            if (recsElement != null && recsElement.Value.ValueKind == JsonValueKind.Array)
            {
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
            }

            // Some model variants emit actionable improvements at the top level.
            AddLegacyActionableImprovements(recommendations, root);

            // Legacy schema: ActionableImprovements array in AdditionalFeedback/Analysis object
            JsonElement feedbackRoot;
            if (TryGetLegacyFeedbackRoot(root, out feedbackRoot))
            {
                AddLegacyActionableImprovements(recommendations, feedbackRoot);
            }

            return recommendations;
        }

        private static void AddLegacyActionableImprovements(List<AnalysisRecommendation> target, JsonElement root)
        {
            if (!root.TryGetProperty("ActionableImprovements", out var improvements) ||
                improvements.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in improvements.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var suggestedEdit = GetString(item, "SuggestedEdit") ?? string.Empty;
                    var description = GetString(item, "Description") ?? suggestedEdit;
                    target.Add(new AnalysisRecommendation
                    {
                        Category = GetString(item, "Category") ?? "Actionable Improvement",
                        Description = description,
                        SuggestedEdit = string.IsNullOrWhiteSpace(suggestedEdit) ? null : suggestedEdit
                    });
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        target.Add(new AnalysisRecommendation
                        {
                            Category = "Actionable Improvement",
                            Description = text,
                            SuggestedEdit = text
                        });
                    }
                }
            }
        }

        private static bool TryGetLegacyFeedbackRoot(JsonElement root, out JsonElement feedbackRoot)
        {
            var additional = GetProperty(root, "AdditionalFeedback");
            if (additional != null && additional.Value.ValueKind == JsonValueKind.Object)
            {
                feedbackRoot = additional.Value;
                return true;
            }

            var analysis = GetProperty(root, "Analysis");
            if (analysis != null && analysis.Value.ValueKind == JsonValueKind.Object)
            {
                feedbackRoot = analysis.Value;
                return true;
            }

            feedbackRoot = default;
            return false;
        }

        private static void AddLegacyIssueArray(List<AnalysisIssue> target, JsonElement root, string propertyName, string category)
        {
            if (!root.TryGetProperty(propertyName, out var issuesArray) || issuesArray.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in issuesArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        target.Add(new AnalysisIssue
                        {
                            Category = category,
                            Severity = "Medium",
                            Description = text,
                            Fix = string.Empty
                        });
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var description = GetString(item, "Description") ?? GetString(item, "Issue") ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        target.Add(new AnalysisIssue
                        {
                            Category = GetString(item, "Category") ?? category,
                            Severity = GetString(item, "Severity") ?? "Medium",
                            Description = description,
                            Fix = GetString(item, "Fix") ?? string.Empty
                        });
                    }
                }
            }
        }
    }
}