using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Parser for legacy requirement extraction responses that use REQ-ID/Text/Category/Priority/Verification fields.
    /// This keeps scrape-derived extraction output isolated from the generic delimited parser.
    /// </summary>
    public sealed class LegacyRequirementExtractionParser : IResponseParser
    {
        public string ParserName => "LegacyRequirementExtraction";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var trimmed = response.Trim();
            var isLegacyExtraction = trimmed.Contains("REQ-ID:", StringComparison.OrdinalIgnoreCase) &&
                                     (trimmed.Contains("Text:", StringComparison.OrdinalIgnoreCase) ||
                                      trimmed.Contains("Category:", StringComparison.OrdinalIgnoreCase) ||
                                      trimmed.Contains("Priority:", StringComparison.OrdinalIgnoreCase) ||
                                      trimmed.Contains("Verification:", StringComparison.OrdinalIgnoreCase));

            var isProseAnalysis = IsProseAnalysisResponse(trimmed);
            var canParse = isLegacyExtraction || isProseAnalysis;

            var preview = trimmed.Substring(0, Math.Min(150, trimmed.Length)).Replace("\n", " ").Replace("\r", " ");
            TestCaseEditorApp.Services.Logging.Log.Info($"[PARSER_CANPARSE_CHECK] LegacyRequirementExtractionParser length={response.Length}, has_req_id={trimmed.Contains("REQ-ID:", StringComparison.OrdinalIgnoreCase)}, prose_variant={isProseAnalysis}, result={canParse}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[PARSER_RESPONSE_PREVIEW] First 150 chars: {preview}");

            return canParse;
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

                if (IsProseAnalysisResponse(response))
                {
                    return ParseProseAnalysisResponse(response, requirementId);
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Parsing legacy extraction response for {requirementId}");

                var analysis = new RequirementAnalysis
                {
                    Timestamp = DateTime.Now,
                    Issues = new List<AnalysisIssue>(),
                    Recommendations = new List<AnalysisRecommendation>(),
                    IsAnalyzed = true,
                    ErrorMessage = null,
                    HallucinationCheck = "NO_FABRICATION"
                };

                var blocks = response.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
                var lines = (blocks.Length > 0 ? blocks : new[] { response })
                    .SelectMany(block => block.Split('\n', StringSplitOptions.RemoveEmptyEntries));

                string extractedText = string.Empty;
                string category = string.Empty;
                string priority = string.Empty;
                string verification = string.Empty;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    if (trimmed.StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
                    {
                        extractedText = trimmed.Substring(5).Trim();
                    }
                    else if (trimmed.StartsWith("Category:", StringComparison.OrdinalIgnoreCase))
                    {
                        category = trimmed.Substring(9).Trim();
                    }
                    else if (trimmed.StartsWith("Priority:", StringComparison.OrdinalIgnoreCase))
                    {
                        priority = trimmed.Substring(9).Trim();
                    }
                    else if (trimmed.StartsWith("Verification:", StringComparison.OrdinalIgnoreCase))
                    {
                        verification = trimmed.Substring(13).Trim();
                    }
                }

                analysis.OriginalQualityScore = DetermineQualityScore(extractedText, category, priority, verification);
                analysis.ImprovedRequirement = extractedText;
                CreateAnalysisFromExtraction(analysis, extractedText, category, priority, verification);

                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Successfully parsed legacy extraction for {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues?.Count ?? 0}, Recommendations={analysis.Recommendations?.Count ?? 0}");

                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[{ParserName}Parser] Failed to parse legacy extraction response for {requirementId}");
                return null;
            }
        }

        private int DetermineQualityScore(string text, string category, string priority, string verification)
        {
            var score = 5;

            if (!string.IsNullOrWhiteSpace(text) && text.Length > 20)
                score++;

            if (text.Contains("shall", StringComparison.OrdinalIgnoreCase))
                score++;

            if (text.Contains("%", StringComparison.Ordinal) ||
                text.Contains("seconds", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("within", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d+\b"))
            {
                score++;
            }

            if (priority.Contains("high", StringComparison.OrdinalIgnoreCase))
                score++;

            if (category.Contains("functional", StringComparison.OrdinalIgnoreCase))
                score++;

            if (verification.Contains("test", StringComparison.OrdinalIgnoreCase))
                score++;

            return Math.Max(1, Math.Min(10, score));
        }

        private static bool IsProseAnalysisResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var trimmed = response.Trim();
            return trimmed.Contains("Analysis of Requirement Quality", StringComparison.OrdinalIgnoreCase) &&
                   (trimmed.Contains("Requirement ID:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("IMPROVED REQUIREMENT:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("ISSUES FOUND:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("RECOMMENDATIONS:", StringComparison.OrdinalIgnoreCase));
        }

        private RequirementAnalysis? ParseProseAnalysisResponse(string response, string requirementId)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Parsing prose analysis fallback response for {requirementId}");

            var analysis = new RequirementAnalysis
            {
                Timestamp = DateTime.Now,
                Issues = new List<AnalysisIssue>(),
                Recommendations = new List<AnalysisRecommendation>(),
                IsAnalyzed = true,
                ErrorMessage = null,
                HallucinationCheck = "NO_FABRICATION",
                OriginalQualityScore = 5
            };

            var lines = response
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n', StringSplitOptions.None)
                .Select(l => l.Trim())
                .ToList();

            string section = string.Empty;
            var improvedBuilder = new List<string>();
            var feedbackBuilder = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("QUALITY SCORE:", StringComparison.OrdinalIgnoreCase))
                {
                    section = string.Empty;
                    var scoreText = line.Substring("QUALITY SCORE:".Length).Trim();
                    if (int.TryParse(new string(scoreText.TakeWhile(c => char.IsDigit(c)).ToArray()), out var rawScore))
                    {
                        // Some prose variants use 0-100; normalize to 1-10.
                        analysis.OriginalQualityScore = rawScore > 10
                            ? Math.Max(1, Math.Min(10, (int)Math.Round(rawScore / 10.0)))
                            : Math.Max(1, Math.Min(10, rawScore));
                    }
                    continue;
                }

                if (line.StartsWith("HALLUCINATION CHECK", StringComparison.OrdinalIgnoreCase))
                {
                    section = "hallucination";
                    continue;
                }

                if (line.StartsWith("ISSUES FOUND:", StringComparison.OrdinalIgnoreCase))
                {
                    section = "issues";
                    continue;
                }

                if (line.StartsWith("IMPROVED REQUIREMENT:", StringComparison.OrdinalIgnoreCase))
                {
                    section = "improved";
                    continue;
                }

                if (line.StartsWith("RECOMMENDATIONS:", StringComparison.OrdinalIgnoreCase))
                {
                    section = "recommendations";
                    continue;
                }

                if (line.StartsWith("OVERALL ASSESSMENT:", StringComparison.OrdinalIgnoreCase))
                {
                    section = "feedback";
                    continue;
                }

                if (section == "hallucination")
                {
                    if (line.Contains("HELPFUL_ELABORATION", StringComparison.OrdinalIgnoreCase))
                        analysis.HallucinationCheck = "HELPFUL_ELABORATION";
                    else if (line.Contains("FABRICATED_DETAILS", StringComparison.OrdinalIgnoreCase))
                        analysis.HallucinationCheck = "FABRICATED_DETAILS";
                    else if (line.Contains("NO_FABRICATION", StringComparison.OrdinalIgnoreCase))
                        analysis.HallucinationCheck = "NO_FABRICATION";
                    continue;
                }

                if (section == "issues" && (line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal)))
                {
                    var content = line.Substring(1).Trim();
                    var issue = ParseIssueLine(content);
                    if (issue != null)
                    {
                        analysis.Issues.Add(issue);
                    }
                    continue;
                }

                if (section == "improved")
                {
                    improvedBuilder.Add(line.TrimStart('-', '*', ' '));
                    continue;
                }

                if (section == "recommendations")
                {
                    var content = line.TrimStart('-', '*', ' ');
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        analysis.Recommendations.Add(new AnalysisRecommendation
                        {
                            Category = "Completeness",
                            Description = content,
                            SuggestedEdit = null
                        });
                    }
                    continue;
                }

                if (section == "feedback")
                {
                    feedbackBuilder.Add(line.TrimStart('-', '*', ' '));
                }
            }

            if (improvedBuilder.Count > 0)
            {
                analysis.ImprovedRequirement = string.Join(" ", improvedBuilder).Trim();
            }

            if (feedbackBuilder.Count > 0)
            {
                analysis.FreeformFeedback = string.Join(" ", feedbackBuilder).Trim();
            }

            // If recommendations are present but no SuggestedEdit was supplied, reuse the rewritten requirement when available.
            if (!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement))
            {
                foreach (var recommendation in analysis.Recommendations)
                {
                    if (string.IsNullOrWhiteSpace(recommendation.SuggestedEdit))
                    {
                        recommendation.SuggestedEdit = analysis.ImprovedRequirement;
                    }
                }
            }

            var hasSubstantiveContent =
                (analysis.Issues?.Count ?? 0) > 0 ||
                (analysis.Recommendations?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(analysis.ImprovedRequirement);

            if (!hasSubstantiveContent)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] Prose fallback parse produced no substantive content for {requirementId}");
                return null;
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Prose fallback parse succeeded for {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues.Count}, Recommendations={analysis.Recommendations.Count}, HasRewrite={!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement)}");
            return analysis;
        }

        private static AnalysisIssue? ParseIssueLine(string issueLine)
        {
            if (string.IsNullOrWhiteSpace(issueLine))
                return null;

            var category = "Clarity";
            var severity = "Medium";
            var description = issueLine;
            var fix = "Clarified requirement wording";

            var openParen = issueLine.IndexOf('(');
            var closeParen = issueLine.IndexOf(')');
            var colon = issueLine.IndexOf(':');
            if (openParen > 0 && closeParen > openParen && colon > closeParen)
            {
                category = issueLine.Substring(0, openParen).Trim();
                severity = issueLine.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                description = issueLine.Substring(colon + 1).Trim();
            }

            var fixSeparator = description.IndexOf("| Fix:", StringComparison.OrdinalIgnoreCase);
            if (fixSeparator >= 0)
            {
                fix = description.Substring(fixSeparator + "| Fix:".Length).Trim();
                description = description.Substring(0, fixSeparator).Trim();
            }

            return new AnalysisIssue
            {
                Category = string.IsNullOrWhiteSpace(category) ? "Clarity" : category,
                Severity = string.IsNullOrWhiteSpace(severity) ? "Medium" : severity,
                Description = string.IsNullOrWhiteSpace(description) ? issueLine : description,
                Fix = string.IsNullOrWhiteSpace(fix) ? "Clarified requirement wording" : fix
            };
        }

        private void CreateAnalysisFromExtraction(RequirementAnalysis analysis, string text, string category, string priority, string verification)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                analysis.Recommendations.Add(new AnalysisRecommendation
                {
                    Category = string.IsNullOrWhiteSpace(category) ? "Completeness" : category,
                    Description = "Enhance requirement with specific acceptance criteria and verification details",
                    SuggestedEdit = $"{text} The system shall meet [specify measurable criteria] and be verified through [specify verification method]."
                });
            }

            if (string.IsNullOrWhiteSpace(text) || text.Length < 20)
            {
                analysis.Issues.Add(new AnalysisIssue
                {
                    Category = "Clarity",
                    Severity = "High",
                    Description = "Requirement text is too brief or unclear",
                    Fix = "Expanded requirement text with specific details"
                });
            }
            else if (!text.Contains("shall", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Issues.Add(new AnalysisIssue
                {
                    Category = "Consistency",
                    Severity = "Medium",
                    Description = "Requirement should use 'shall' language for mandatory requirements",
                    Fix = "Updated requirement to use proper 'shall' language"
                });
            }

            analysis.FreeformFeedback = $"This requirement was extracted from document content and appears to be {category?.ToLowerInvariant() ?? "functional"} in nature with {priority?.ToLowerInvariant() ?? "medium"} priority. Verification guidance: {verification ?? "not specified"}. Consider adding specific acceptance criteria, measurable parameters, and verification methods to improve testability.";
        }
    }
}