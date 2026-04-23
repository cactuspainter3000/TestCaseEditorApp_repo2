using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Parser for structured section-based requirement analysis responses.
    /// Handles responses with headings like:
    /// QUALITY SCORE:
    /// ISSUES FOUND:
    /// STRENGTHS:
    /// IMPROVED REQUIREMENT:
    /// RECOMMENDATIONS:
    /// OVERALL ASSESSMENT:
    /// </summary>
    public class StructuredAnalysisResponseParser : IResponseParser
    {
        public string ParserName => "StructuredAnalysis";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var normalized = response.Trim();

            bool canParse =
                normalized.Contains("QUALITY SCORE:", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains("ISSUES FOUND:", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains("IMPROVED REQUIREMENT:", StringComparison.OrdinalIgnoreCase);

            TestCaseEditorApp.Services.Logging.Log.Debug(
                $"[{ParserName}Parser] CanParse result: {canParse}");

            return canParse;
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

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[{ParserName}Parser] Parsing structured response for {requirementId}");

                var analysis = new RequirementAnalysis
                {
                    Timestamp = DateTime.Now,
                    Issues = new List<AnalysisIssue>(),
                    Recommendations = new List<AnalysisRecommendation>(),
                    IsAnalyzed = true,
                    ErrorMessage = null,
                    HallucinationCheck = "UNKNOWN"
                };

                var sections = SplitIntoSections(response);

                var rawQualityScoreText = GetSectionValue(sections, "QUALITY SCORE");
                if (!TryParseQualityScore(rawQualityScoreText, out var parsedQualityScore))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[{ParserName}Parser] Invalid QUALITY SCORE format for {requirementId}: '{rawQualityScoreText}'");
                    return null;
                }

                analysis.OriginalQualityScore = parsedQualityScore;

                var rawImprovedRequirement = GetSectionValue(sections, "IMPROVED REQUIREMENT") ?? string.Empty;
                analysis.ImprovedRequirement = SanitizeImprovedRequirement(rawImprovedRequirement);

                if (LooksLikeRequirementCommentary(analysis.ImprovedRequirement))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn(
                        $"[{ParserName}Parser] Improved requirement appears to be commentary for {requirementId}. Replacing with minimally cleaned original text.");

                    analysis.ImprovedRequirement = BuildMinimalFallbackRequirement(sections, response);
                }

                analysis.FreeformFeedback = GetSectionValue(sections, "OVERALL ASSESSMENT") ?? string.Empty;

                ParseIssues(GetSectionValue(sections, "ISSUES FOUND"), analysis);
                ParseRecommendations(GetSectionValue(sections, "RECOMMENDATIONS"), analysis);

                var strengths = GetBulletLines(GetSectionValue(sections, "STRENGTHS"));
                if (strengths.Count > 0)
                {
                    var strengthsText = "Strengths: " + string.Join(" | ", strengths);

                    if (string.IsNullOrWhiteSpace(analysis.FreeformFeedback))
                    {
                        analysis.FreeformFeedback = strengthsText;
                    }
                    else
                    {
                        analysis.FreeformFeedback += Environment.NewLine + strengthsText;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[{ParserName}Parser] Successfully parsed {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues?.Count ?? 0}, Recommendations={analysis.Recommendations?.Count ?? 0}");

                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(
                    ex,
                    $"[{ParserName}Parser] Failed to parse structured response for {requirementId}");
                return null;
            }
        }

        /// <summary>
        /// Tries to parse a whole-number quality score from 0 to 100.
        /// </summary>
        private static bool TryParseQualityScore(string? scoreText, out int score)
        {
            score = 0;

            if (string.IsNullOrWhiteSpace(scoreText))
                return false;

            var trimmed = scoreText.Trim();

            // Reject common placeholder-like formats explicitly.
            if (trimmed.Contains("<") || trimmed.Contains(">") || trimmed.Contains("[") || trimmed.Contains("]"))
                return false;

            var digits = new string(trimmed.Where(char.IsDigit).ToArray());

            if (!int.TryParse(digits, out score))
                return false;

            return score >= 0 && score <= 100;
        }

        /// <summary>
        /// Splits the structured response into named sections.
        /// </summary>
        private static Dictionary<string, string> SplitIntoSections(string response)
        {
            var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var headerAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "QUALITY SCORE", "QUALITY SCORE" },
                { "ISSUES FOUND", "ISSUES FOUND" },
                { "STRENGTH", "STRENGTHS" },
                { "STRENGTHS", "STRENGTHS" },
                { "IMPROVED REQUIREMENT", "IMPROVED REQUIREMENT" },
                { "RECOMMENDATION", "RECOMMENDATIONS" },
                { "RECOMMENDATIONS", "RECOMMENDATIONS" },
                { "OVERALL ASSESSMENT", "OVERALL ASSESSMENT" }
            };

            string? currentHeader = null;
            var currentContent = new StringBuilder();

            var lines = response.Replace("\r\n", "\n").Split('\n');

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.Trim();

                string? matchedHeader = null;
                string? inlineValue = null;

                foreach (var alias in headerAliases.Keys)
                {
                    var prefix = alias + ":";

                    if (trimmed.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedHeader = headerAliases[alias];
                        inlineValue = string.Empty;
                        break;
                    }

                    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedHeader = headerAliases[alias];
                        inlineValue = trimmed.Substring(prefix.Length).Trim();
                        break;
                    }
                }

                if (matchedHeader != null)
                {
                    if (currentHeader != null)
                    {
                        sections[currentHeader] = currentContent.ToString().Trim();
                    }

                    currentHeader = matchedHeader;
                    currentContent.Clear();

                    if (!string.IsNullOrWhiteSpace(inlineValue))
                    {
                        currentContent.AppendLine(inlineValue);
                    }

                    continue;
                }

                if (currentHeader != null)
                {
                    currentContent.AppendLine(line);
                }
            }

            if (currentHeader != null)
            {
                sections[currentHeader] = currentContent.ToString().Trim();
            }

            return sections;
        }

        /// <summary>
        /// Gets a section value by key.
        /// </summary>
        private static string? GetSectionValue(Dictionary<string, string> sections, string key)
        {
            return sections.TryGetValue(key, out var value) ? value?.Trim() : null;
        }

        /// <summary>
        /// Parses structured issue bullet lines into AnalysisIssue objects.
        /// </summary>
        private static void ParseIssues(string? issuesText, RequirementAnalysis analysis)
        {
            if (string.IsNullOrWhiteSpace(issuesText))
                return;

            var issueLines = GetBulletLines(issuesText);

            foreach (var line in issueLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string category = "Unknown";
                string severity = "Medium";
                string description = line;
                string fix = string.Empty;

                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var left = line.Substring(0, colonIndex).Trim();
                    var right = line.Substring(colonIndex + 1).Trim();

                    description = right;

                    var parenOpen = left.IndexOf('(');
                    var parenClose = left.IndexOf(')');

                    if (parenOpen > 0 && parenClose > parenOpen)
                    {
                        category = left.Substring(0, parenOpen).Trim();
                        severity = left.Substring(parenOpen + 1, parenClose - parenOpen - 1).Trim();
                    }
                    else
                    {
                        category = left.Trim();
                    }

                    var fixMarker = right.IndexOf("| Fix:", StringComparison.OrdinalIgnoreCase);
                    if (fixMarker >= 0)
                    {
                        description = right.Substring(0, fixMarker).Trim();
                        fix = right.Substring(fixMarker + 6).Trim();
                    }
                }

                analysis.Issues.Add(new AnalysisIssue
                {
                    Category = string.IsNullOrWhiteSpace(category) ? "Unknown" : category,
                    Severity = string.IsNullOrWhiteSpace(severity) ? "Medium" : severity,
                    Description = description,
                    Fix = fix
                });
            }
        }

        /// <summary>
        /// Parses recommendation lines into AnalysisRecommendation objects.
        /// </summary>
        private static void ParseRecommendations(string? recommendationsText, RequirementAnalysis analysis)
        {
            if (string.IsNullOrWhiteSpace(recommendationsText))
                return;

            var recommendationLines = GetBulletLines(recommendationsText);

            if (recommendationLines.Count == 0)
            {
                recommendationLines.Add(recommendationsText.Trim());
            }

            foreach (var line in recommendationLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                analysis.Recommendations.Add(new AnalysisRecommendation
                {
                    Category = "General",
                    Description = line,
                    SuggestedEdit = line
                });
            }
        }

        /// <summary>
        /// Extracts bullet items, including wrapped continuation lines.
        /// </summary>
        private static List<string> GetBulletLines(string? text)
        {
            var results = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return results;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            StringBuilder? currentItem = null;

            foreach (var raw in lines)
            {
                var line = raw.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                bool isBullet = line.StartsWith("- ");

                if (isBullet)
                {
                    if (currentItem != null)
                    {
                        results.Add(currentItem.ToString().Trim());
                    }

                    currentItem = new StringBuilder();
                    currentItem.Append(line.Substring(2).Trim());
                }
                else
                {
                    if (currentItem == null)
                    {
                        currentItem = new StringBuilder();
                        currentItem.Append(line);
                    }
                    else
                    {
                        currentItem.Append(' ');
                        currentItem.Append(line);
                    }
                }
            }

            if (currentItem != null)
            {
                results.Add(currentItem.ToString().Trim());
            }

            return results;
        }

        /// <summary>
        /// Cleans the improved requirement text while preserving the model's wording.
        /// </summary>
        private static string SanitizeImprovedRequirement(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var cleaned = text.Trim();

            cleaned = cleaned.Replace("\r\n", "\n").Trim();

            while (cleaned.StartsWith("- "))
            {
                cleaned = cleaned.Substring(2).Trim();
            }

            return cleaned;
        }

        /// <summary>
        /// Detects when the "improved requirement" is actually commentary or critique text.
        /// </summary>
        private static bool LooksLikeRequirementCommentary(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var normalized = text.Trim();

            var commentaryStarts = new[]
            {
                "the requirement",
                "this requirement",
                "the text",
                "it lacks",
                "it does not",
                "however,",
                "however ",
                "the statement",
                "the current requirement",
                "the requirement specifies",
                "the requirement states"
            };

            if (commentaryStarts.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return true;

            var commentaryPhrases = new[]
            {
                "lacks explicit detail",
                "does not define",
                "is ambiguous",
                "is vague",
                "what constitutes",
                "missing information",
                "should specify",
                "should clarify",
                "the requirement specifies"
            };

            if (commentaryPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        /// <summary>
        /// Builds a minimal fallback requirement from whatever source text is available.
        /// This avoids displaying critique prose as though it were a rewritten requirement.
        /// </summary>
        private static string BuildMinimalFallbackRequirement(Dictionary<string, string> sections, string fullResponse)
        {
            // If the model returned nothing useful, preserve a neutral non-empty value.
            // The parser does not have the original requirement text, so this is intentionally conservative.
            var candidate = GetSectionValue(sections, "IMPROVED REQUIREMENT");

            if (!string.IsNullOrWhiteSpace(candidate) && !LooksLikeRequirementCommentary(candidate))
                return candidate.Trim();

            // Last resort: return an empty string rather than fabricated prose.
            return string.Empty;
        }
    }
}