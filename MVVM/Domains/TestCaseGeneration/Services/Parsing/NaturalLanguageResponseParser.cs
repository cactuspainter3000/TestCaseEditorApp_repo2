using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services.Parsing
{
    /// <summary>
    /// Parser for natural language analysis responses from AnythingLLM RAG system.
    /// Handles structured text responses with sections like "QUALITY SCORE:", "ISSUES FOUND:", etc.
    /// </summary>
    public class NaturalLanguageResponseParser : IResponseParser
    {
        public string ParserName => "NaturalLanguage";

        public bool CanParse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            // Check for natural language format indicators
            var upperResponse = response.ToUpper();
            return upperResponse.Contains("QUALITY SCORE") ||
                   upperResponse.Contains("ISSUES FOUND") ||
                   upperResponse.Contains("IMPROVED REQUIREMENT") ||
                   (upperResponse.Contains("CLARITY") && upperResponse.Contains("ISSUE"));
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

                // Debug logging
                TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Parsing response for {requirementId}:");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Response length: {response.Length} characters");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] First 500 chars: {response.Substring(0, Math.Min(500, response.Length))}");

                var analysis = new RequirementAnalysis
                {
                    Timestamp = DateTime.Now,
                    Issues = new List<AnalysisIssue>(),
                    Recommendations = new List<AnalysisRecommendation>(),
                    HallucinationCheck = ""
                };

                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var currentSection = "";

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Parse quality score
                    if (trimmed.ToUpper().StartsWith("QUALITY") || trimmed.ToUpper().StartsWith("SCORE"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"(\d+)");
                        if (match.Success && int.TryParse(match.Value, out int score))
                        {
                            analysis.QualityScore = Math.Max(1, Math.Min(10, score));
                        }
                    }
                    // Parse section headers
                    else if (trimmed.ToUpper().Contains("ISSUE") && trimmed.ToUpper().Contains("FOUND"))
                    {
                        currentSection = "issues";
                    }
                    else if (trimmed.ToUpper().Contains("IMPROVED REQUIREMENT") || 
                             trimmed.ToUpper().Contains("REWRITTEN REQUIREMENT"))
                    {
                        currentSection = "improved";
                        
                        // Check if the requirement text is on the same line after a colon
                        if (trimmed.Contains(":"))
                        {
                            var colonIndex = trimmed.IndexOf(":");
                            if (colonIndex < trimmed.Length - 1)
                            {
                                var requirementText = trimmed.Substring(colonIndex + 1).Trim();
                                if (!string.IsNullOrWhiteSpace(requirementText))
                                {
                                    analysis.ImprovedRequirement = requirementText;
                                }
                            }
                        }
                        continue; // Skip further processing of this line
                    }
                    else if (trimmed.ToUpper().Contains("RECOMMENDATION"))
                    {
                        currentSection = "recommendations";
                    }
                    else if (trimmed.ToUpper().Contains("FABRICATION") || trimmed.ToUpper().Contains("HALLUCINATION"))
                    {
                        if (trimmed.ToUpper().Contains("NO_FABRICATION"))
                        {
                            analysis.HallucinationCheck = "<NO_FABRICATION>";
                        }
                        else if (trimmed.ToUpper().Contains("FABRICATED"))
                        {
                            analysis.HallucinationCheck = "FABRICATED_DETAILS";
                        }
                        currentSection = "";
                    }
                    // Handle improved requirement content (may not be in list format)
                    else if (currentSection == "improved" && !string.IsNullOrWhiteSpace(trimmed) && 
                             !trimmed.StartsWith("-") && !trimmed.StartsWith("•") &&
                             !trimmed.ToUpper().Contains("RECOMMENDATION") &&
                             !trimmed.ToUpper().Contains("HALLUCINATION") &&
                             !trimmed.ToUpper().Contains("OVERALL ASSESSMENT") &&
                             !trimmed.Trim().Equals("[REQUIRED:", StringComparison.OrdinalIgnoreCase) &&
                             !trimmed.StartsWith("[") && !trimmed.EndsWith("]"))
                    {
                        // Accumulate the improved requirement text
                        if (string.IsNullOrWhiteSpace(analysis.ImprovedRequirement))
                        {
                            analysis.ImprovedRequirement = trimmed;
                        }
                        else
                        {
                            analysis.ImprovedRequirement += " " + trimmed;
                        }
                    }
                    // Parse list items
                    else if (trimmed.StartsWith("-") || trimmed.StartsWith("•"))
                    {
                        var content = trimmed.Substring(1).Trim();
                        
                        if (currentSection == "issues" && !string.IsNullOrWhiteSpace(content))
                        {
                            ParseIssueItem(content, analysis.Issues);
                        }
                        else if (currentSection == "recommendations" && !string.IsNullOrWhiteSpace(content))
                        {
                            ParseRecommendationItem(content, analysis.Recommendations);
                        }
                    }
                }

                PostProcessAnalysis(analysis, requirementId);
                return analysis;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[{ParserName}Parser] Failed to parse response for {requirementId}");
                return null;
            }
        }

        private void ParseIssueItem(string content, List<AnalysisIssue> issues)
        {
            // Parse enhanced format: "Clarity Issue (Medium): Description | Fix: Solution"
            var parts = content.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var mainPart = parts.Length > 0 ? parts[0].Trim() : content;
            var fixPart = parts.Length > 1 ? parts[1].Trim() : "";
            
            // Extract issue type, severity, and description
            var category = "Quality"; // Default
            var severity = "Medium";  // Default
            var description = mainPart;
            
            // Look for specific issue types
            if (mainPart.ToUpper().Contains("CLARITY"))
                category = "Clarity";
            else if (mainPart.ToUpper().Contains("COMPLETENESS"))
                category = "Completeness";
            else if (mainPart.ToUpper().Contains("TESTABILITY"))
                category = "Testability";
            else if (mainPart.ToUpper().Contains("CONSISTENCY"))
                category = "Consistency";
            else if (mainPart.ToUpper().Contains("FEASIBILITY"))
                category = "Feasibility";
            
            // Extract severity if present
            if (mainPart.ToUpper().Contains("(HIGH)"))
                severity = "High";
            else if (mainPart.ToUpper().Contains("(LOW)"))
                severity = "Low";
            
            // Clean up description by removing the category and severity parts
            if (mainPart.Contains(":"))
            {
                var colonIndex = mainPart.IndexOf(":");
                description = mainPart.Substring(colonIndex + 1).Trim();
            }
            
            // Add fix information to description if present
            if (fixPart.ToUpper().StartsWith("FIX:"))
            {
                var fix = fixPart.Substring(4).Trim();
                description += $" | Fix: {fix}";
            }
            
            issues.Add(new AnalysisIssue
            {
                Category = category,
                Description = description,
                Severity = severity
            });
        }

        private void ParseRecommendationItem(string content, List<AnalysisRecommendation> recommendations)
        {
            // Parse structured recommendation format: Category: X | Description: Y | Rationale: Z
            var parts = content.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var recommendation = new AnalysisRecommendation
            {
                Category = "Improvement",
                Description = content, // Default to full content
                SuggestedEdit = ""
            };

            foreach (var part in parts)
            {
                var keyValue = part.Trim();
                if (keyValue.ToUpper().StartsWith("CATEGORY:"))
                {
                    recommendation.Category = keyValue.Substring(9).Trim();
                }
                else if (keyValue.ToUpper().StartsWith("DESCRIPTION:"))
                {
                    recommendation.Description = keyValue.Substring(12).Trim();
                }
                else if (keyValue.ToUpper().StartsWith("SUGGESTED EDIT:") || 
                        keyValue.ToUpper().StartsWith("EDIT:") || 
                        keyValue.ToUpper().StartsWith("FIX:") ||
                        keyValue.ToUpper().StartsWith("RATIONALE:"))
                {
                    int startIndex;
                    if (keyValue.ToUpper().StartsWith("SUGGESTED EDIT:"))
                        startIndex = 15;
                    else if (keyValue.ToUpper().StartsWith("RATIONALE:"))
                        startIndex = 10;
                    else if (keyValue.ToUpper().StartsWith("EDIT:"))
                        startIndex = 5;
                    else // FIX:
                        startIndex = 4;
                    
                    recommendation.SuggestedEdit = keyValue.Substring(startIndex).Trim();
                }
            }

            recommendations.Add(recommendation);
        }

        private void PostProcessAnalysis(RequirementAnalysis analysis, string requirementId)
        {
            // Set default quality score if not found
            if (analysis.QualityScore == 0)
            {
                analysis.QualityScore = analysis.Issues.Count > 3 ? 4 : 6; // Reasonable default based on issues found
            }

            // Set default hallucination check if not found
            if (string.IsNullOrWhiteSpace(analysis.HallucinationCheck))
            {
                analysis.HallucinationCheck = "<NO_FABRICATION>";
            }

            // Validate that improved requirement was provided
            if (string.IsNullOrWhiteSpace(analysis.ImprovedRequirement))
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[{ParserName}Parser] No improved requirement provided for {requirementId}, adding to freeform feedback");
                if (!string.IsNullOrWhiteSpace(analysis.FreeformFeedback))
                {
                    analysis.FreeformFeedback += "\n\n[Note: LLM did not provide an improved requirement rewrite]";
                }
                else
                {
                    analysis.FreeformFeedback = "[Note: LLM did not provide an improved requirement rewrite]";
                }
            }

            // Mark as successfully analyzed
            analysis.IsAnalyzed = true;

            TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Natural language parsing successful for {requirementId}: Score={analysis.QualityScore}, Issues={analysis.Issues.Count}, Recommendations={analysis.Recommendations.Count}, ImprovedReq={!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement)}, Freeform={!string.IsNullOrWhiteSpace(analysis.FreeformFeedback)}");
        }
    }
}