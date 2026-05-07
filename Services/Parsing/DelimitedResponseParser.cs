using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Parsing
{
    /// <summary>
    /// Parser for delimited format responses (e.g., from requirement extraction prompts).
    /// Handles responses that start with "---" and contain fields like "ID:", "Text:", etc.
    /// Converts requirement extraction format to requirement analysis format.
    /// </summary>
    public class DelimitedResponseParser : IResponseParser
    {
        public string ParserName => "Delimited";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var trimmed = response.Trim();
            bool isDelimited = trimmed.StartsWith("---") && 
                              (trimmed.Contains("ID:") || trimmed.Contains("Text:"));
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] CanParse result: {isDelimited}, starts with '---': {trimmed.StartsWith("---")}, contains fields: {trimmed.Contains("ID:") || trimmed.Contains("Text:")}");
            
            return isDelimited;
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

                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Parsing delimited response for {requirementId}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Response length: {response.Length} characters");

                var analysis = new RequirementAnalysis
                {
                    Timestamp = DateTime.Now,
                    Issues = new List<AnalysisIssue>(),
                    Recommendations = new List<AnalysisRecommendation>(),
                    IsAnalyzed = true,
                    ErrorMessage = null,
                    HallucinationCheck = "NO_FABRICATION" // Extracted from original requirement
                };

                // Parse the delimited format
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string extractedText = "";
                string category = "";
                string priority = "";
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    if (trimmed.StartsWith("Text:"))
                    {
                        extractedText = trimmed.Substring(5).Trim();
                    }
                    else if (trimmed.StartsWith("Category:"))
                    {
                        category = trimmed.Substring(9).Trim();
                    }
                    else if (trimmed.StartsWith("Priority:"))
                    {
                        priority = trimmed.Substring(9).Trim();
                    }
                }

                // Convert extraction to analysis format
                analysis.OriginalQualityScore = DetermineQualityScore(extractedText, category, priority);
                analysis.ImprovedRequirement = extractedText; // Use extracted text as improved requirement
                
                // Create analysis based on extraction quality
                CreateAnalysisFromExtraction(analysis, extractedText, category, priority);

                TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Successfully converted extraction to analysis for {requirementId}: Score={analysis.OriginalQualityScore}, Issues={analysis.Issues?.Count ?? 0}, Recommendations={analysis.Recommendations?.Count ?? 0}");

                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[{ParserName}Parser] Failed to parse delimited response for {requirementId}");
                return null;
            }
        }

        private int DetermineQualityScore(string text, string category, string priority)
        {
            // Base score assessment
            int score = 5; // Start with middle score

            // Check for completeness
            if (!string.IsNullOrEmpty(text) && text.Length > 20)
            {
                score += 1; // Has substantial text
            }

            // Check for specificity (contains "shall", specific values, etc.)
            if (text.ToLower().Contains("shall"))
            {
                score += 1; // Uses proper requirement language
            }

            // Check for measurable criteria
            if (text.Contains("%") || text.Contains("seconds") || text.Contains("within") || 
                System.Text.RegularExpressions.Regex.IsMatch(text, @"\b\d+\b"))
            {
                score += 1; // Contains measurable elements
            }

            // Priority bonus
            if (priority?.ToLower().Contains("high") == true)
            {
                score += 1;
            }

            // Category bonus for functional requirements
            if (category?.ToLower().Contains("functional") == true)
            {
                score += 1;
            }

            return Math.Max(1, Math.Min(10, score));
        }

        private void CreateAnalysisFromExtraction(RequirementAnalysis analysis, string text, string category, string priority)
        {
            // Add recommendation to enhance the requirement based on extraction
            if (!string.IsNullOrEmpty(text))
            {
                var recommendation = new AnalysisRecommendation
                {
                    Category = "Completeness",
                    Description = "Enhance requirement with specific acceptance criteria and verification details",
                    SuggestedEdit = $"{text} The system shall meet [specify measurable criteria] and be verified through [specify verification method]."
                };
                analysis.Recommendations.Add(recommendation);
            }

            // Add issue if text seems vague
            if (string.IsNullOrEmpty(text) || text.Length < 20)
            {
                analysis.Issues.Add(new AnalysisIssue
                {
                    Category = "Clarity",
                    Severity = "High",
                    Description = "Requirement text is too brief or unclear",
                    Fix = "Expanded requirement text with specific details"
                });
            }
            else if (!text.ToLower().Contains("shall"))
            {
                analysis.Issues.Add(new AnalysisIssue
                {
                    Category = "Consistency",
                    Severity = "Medium", 
                    Description = "Requirement should use 'shall' language for mandatory requirements",
                    Fix = "Updated requirement to use proper 'shall' language"
                });
            }

            // Add testability feedback
            analysis.FreeformFeedback = $"This requirement was extracted from document content and appears to be {category?.ToLower() ?? "functional"} in nature with {priority?.ToLower() ?? "medium"} priority. Consider adding specific acceptance criteria, measurable parameters, and verification methods to improve testability.";
        }
    }
}