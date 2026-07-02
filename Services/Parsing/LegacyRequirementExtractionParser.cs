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

            var preview = trimmed.Substring(0, Math.Min(150, trimmed.Length)).Replace("\n", " ").Replace("\r", " ");
            TestCaseEditorApp.Services.Logging.Log.Info($"[PARSER_CANPARSE_CHECK] LegacyRequirementExtractionParser length={response.Length}, has_req_id={trimmed.Contains("REQ-ID:", StringComparison.OrdinalIgnoreCase)}, result={isLegacyExtraction}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[PARSER_RESPONSE_PREVIEW] First 150 chars: {preview}");

            return isLegacyExtraction;
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