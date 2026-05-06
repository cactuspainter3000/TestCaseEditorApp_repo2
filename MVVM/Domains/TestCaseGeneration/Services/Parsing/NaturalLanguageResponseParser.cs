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
                    HallucinationCheck = ""
                };

                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var currentSection = "";

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    // Parse quality score - handle markdown formatting like **QUALITY SCORE: **55**
                    if (trimmed.ToUpper().StartsWith("QUALITY") || trimmed.ToUpper().StartsWith("SCORE") || 
                        (trimmed.ToUpper().Contains("QUALITY") && trimmed.ToUpper().Contains("SCORE")))
                    {
                        // Match patterns like "55", "**55**", "SCORE: 55", etc.
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"\**(\d+)\**");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int score))
                        {
                            analysis.QualityScore = Math.Max(1, Math.Min(10, score));
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Parsed quality score: {score} from line: '{trimmed}'");
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
                             !trimmed.ToUpper().Contains("FORMATTING EXAMPLES") &&
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
                    // Parse list items - handle various bullet formats including markdown asterisks
                    else if (trimmed.StartsWith("-") || trimmed.StartsWith("•") || trimmed.StartsWith("*"))
                    {
                        var content = trimmed.Substring(1).Trim();
                        
                        if (currentSection == "issues" && !string.IsNullOrWhiteSpace(content))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[{ParserName}Parser] Processing issue item: '{content}'");
                            ParseIssueItem(content, analysis.Issues);
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
            TestCaseEditorApp.Services.Logging.Log.Info($"[ParseIssue] Raw issue content: '{content}'");

            // Remove markdown formatting (bold asterisks) from content
            var cleanContent = content.Replace("**", "").Trim();
            
            // Parse enhanced format: "Clarity Issue (Medium): Description | Fix: Solution"
            var parts = cleanContent.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var mainPart = parts.Length > 0 ? parts[0].Trim() : cleanContent;
            var fixPart = parts.Length > 1 ? parts[1].Trim() : "";
            
            // Extract issue type, severity, and description
            var category = "Quality"; // Default
            var severity = "Medium";  // Default
            var description = mainPart;
            
            // Look for specific issue types in cleaned content
            var upperMain = mainPart.ToUpper();
            if (upperMain.Contains("CLARITY"))
                category = "Clarity";
            else if (upperMain.Contains("COMPLETENESS"))
                category = "Completeness";
            else if (upperMain.Contains("TESTABILITY"))
                category = "Testability";
            else if (upperMain.Contains("CONSISTENCY"))
                category = "Consistency";
            else if (upperMain.Contains("FEASIBILITY"))
                category = "Feasibility";
                
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ParseIssue] Detected category: '{category}' from: '{mainPart}'");
            
            // Extract severity if present - check for patterns like (Medium), (High), (Low)
            if (upperMain.Contains("(HIGH)") || upperMain.Contains("HIGH"))
                severity = "High";
            else if (upperMain.Contains("(LOW)") || upperMain.Contains("LOW"))
                severity = "Low";
            else if (upperMain.Contains("(MEDIUM)") || upperMain.Contains("MEDIUM"))
                severity = "Medium";
                
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ParseIssue] Detected severity: '{severity}' from: '{mainPart}'");
            
            // Clean up description by removing the category and severity parts
            if (mainPart.Contains(":"))
            {
                var colonIndex = mainPart.IndexOf(":");
                description = mainPart.Substring(colonIndex + 1).Trim();
            }

            // Remove unnecessary brackets that sometimes appear in LLM responses
            description = description.Trim('[', ']').Trim();

            // Simple extraction - LLM should provide properly formatted responses
            string fix = "";
            if (fixPart.ToUpper().StartsWith("FIX:"))
            {
                fix = fixPart.Substring(4).Trim();
            }
            
            var issue = new AnalysisIssue
            {
                Category = category,
                Description = description,
                Severity = severity,
                Fix = fix
            };
            
            issues.Add(issue);
            TestCaseEditorApp.Services.Logging.Log.Info($"[ParseIssue] Created issue: Category='{category}', Severity='{severity}', Description='{description}', Fix='{fix}'");
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

            TestCaseEditorApp.Services.Logging.Log.Info($"[{ParserName}Parser] Natural language parsing successful for {requirementId}: Score={analysis.QualityScore}, Issues={analysis.Issues.Count}, ImprovedReq={!string.IsNullOrWhiteSpace(analysis.ImprovedRequirement)}, Freeform={!string.IsNullOrWhiteSpace(analysis.FreeformFeedback)}");
        }
    }
}