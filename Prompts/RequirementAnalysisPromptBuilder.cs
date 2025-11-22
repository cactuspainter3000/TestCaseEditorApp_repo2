using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Prompts
{
    /// <summary>
    /// Builds prompts for LLM-powered requirement quality analysis.
    /// Evaluates requirements for quality, testability, clarity, completeness, and actionability.
    /// Returns structured JSON with quality score, categorized issues, recommendations, and freeform feedback.
    /// </summary>
    public sealed class RequirementAnalysisPromptBuilder
    {
        /// <summary>
        /// Build a prompt to analyze a single requirement's quality.
        /// </summary>
        /// <param name="requirementId">The requirement ID (e.g., "REQ-001")</param>
        /// <param name="requirementName">The requirement name/title</param>
        /// <param name="requirementText">The full requirement text to analyze</param>
        /// <param name="tables">Tables associated with the requirement</param>
        /// <param name="looseContent">Supplemental paragraphs and tables</param>
        /// <returns>Complete prompt string for LLM analysis</returns>
        public string BuildAnalysisPrompt(string requirementId, string requirementName, string requirementText, 
            List<RequirementTable>? tables = null, RequirementLooseContent? looseContent = null)
        {
            var sb = new StringBuilder();

            // System role and task definition
            sb.AppendLine("You are a systems engineering expert specializing in requirements quality analysis.");
            sb.AppendLine("Your task is to evaluate the quality of a single system-level requirement.");
            sb.AppendLine();

            // Evaluation criteria
            sb.AppendLine("EVALUATION CRITERIA:");
            sb.AppendLine();
            sb.AppendLine("Assess the requirement across these dimensions:");
            sb.AppendLine();
            sb.AppendLine("1. **Clarity & Precision**");
            sb.AppendLine("   - Is the requirement unambiguous and free of vague, subjective, or undefined terms?");
            sb.AppendLine("   - Are all technical terms properly defined or industry-standard?");
            sb.AppendLine("   - Is there exactly one interpretation, or could different readers understand it differently?");
            sb.AppendLine();
            sb.AppendLine("2. **Testability & Verifiability**");
            sb.AppendLine("   - Can this requirement be verified through test, demonstration, inspection, analysis, or simulation?");
            sb.AppendLine("   - Are success criteria measurable and observable?");
            sb.AppendLine("   - Are there specific values, tolerances, thresholds, or acceptance criteria?");
            sb.AppendLine("   - Could you write concrete test cases with clear pass/fail outcomes?");
            sb.AppendLine();
            sb.AppendLine("3. **Completeness**");
            sb.AppendLine("   - Does the requirement specify WHAT must be done without prescribing HOW (avoiding design constraints)?");
            sb.AppendLine("   - Are all necessary conditions, modes, or operational contexts specified?");
            sb.AppendLine("   - Are there missing prerequisites, dependencies, or environmental assumptions?");
            sb.AppendLine();
            sb.AppendLine("4. **Atomicity & Focus**");
            sb.AppendLine("   - Does this requirement express a single, cohesive need?");
            sb.AppendLine("   - Should it be split into multiple independent requirements?");
            sb.AppendLine("   - Are there hidden sub-requirements that need separation?");
            sb.AppendLine();
            sb.AppendLine("5. **Actionability**");
            sb.AppendLine("   - Is it clear what the system must DO or what state it must achieve?");
            sb.AppendLine("   - Is it implementable given typical system constraints?");
            sb.AppendLine("   - Does it avoid being purely aspirational or high-level intent?");
            sb.AppendLine();
            sb.AppendLine("6. **Consistency & Traceability**");
            sb.AppendLine("   - Does the requirement ID and name match the content?");
            sb.AppendLine("   - Are units, terminology, and naming conventions consistent?");
            sb.AppendLine();

            // Requirement to analyze
            sb.AppendLine("REQUIREMENT TO ANALYZE:");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: Analyze ONLY the text provided below. Do not reference examples from the criteria.");
            sb.AppendLine();
            sb.AppendLine($"ID: {requirementId}");
            sb.AppendLine($"Name: {requirementName}");
            sb.AppendLine($"Text: {requirementText}");
            
            // Add tables if present
            if (tables != null && tables.Any())
            {
                sb.AppendLine();
                sb.AppendLine("ASSOCIATED TABLES:");
                sb.AppendLine("(These tables are part of the requirement and should be included in your analysis)");
                sb.AppendLine();
                
                for (int i = 0; i < tables.Count; i++)
                {
                    var table = tables[i];
                    sb.AppendLine($"Table {i + 1}: {table.EditableTitle}");
                    
                    if (table.Table != null && table.Table.Any())
                    {
                        // Format table with clear header separation
                        bool hasHeader = table.FirstRowLooksLikeHeader && table.Table.Count > 1;
                        
                        for (int rowIdx = 0; rowIdx < table.Table.Count; rowIdx++)
                        {
                            var row = table.Table[rowIdx] ?? new List<string>();
                            sb.AppendLine("| " + string.Join(" | ", row) + " |");
                            
                            // Add separator after header row
                            if (hasHeader && rowIdx == 0)
                            {
                                var separatorCells = row.Select(_ => "---");
                                sb.AppendLine("| " + string.Join(" | ", separatorCells) + " |");
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("(empty table)");
                    }
                    sb.AppendLine();
                }
            }
            
            // Add loose content if present
            if (looseContent != null)
            {
                if (looseContent.Paragraphs != null && looseContent.Paragraphs.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("SUPPLEMENTAL PARAGRAPHS:");
                    foreach (var para in looseContent.Paragraphs)
                    {
                        sb.AppendLine($"  {para}");
                    }
                }
                
                if (looseContent.Tables != null && looseContent.Tables.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("SUPPLEMENTAL TABLES:");
                    sb.AppendLine("(These tables are part of the requirement and should be included in your analysis)");
                    sb.AppendLine();
                    
                    for (int i = 0; i < looseContent.Tables.Count; i++)
                    {
                        var looseTable = looseContent.Tables[i];
                        sb.AppendLine($"Supplemental Table {i + 1}: {looseTable.EditableTitle}");
                        
                        if (looseTable.Rows != null && looseTable.Rows.Any())
                        {
                            // Check if we have column headers to display
                            bool hasHeaders = looseTable.ColumnHeaders != null && looseTable.ColumnHeaders.Any();
                            
                            // If headers exist, display them first
                            if (hasHeaders)
                            {
                                sb.AppendLine("| " + string.Join(" | ", looseTable.ColumnHeaders) + " |");
                                var separatorCells = looseTable.ColumnHeaders.Select(_ => "---");
                                sb.AppendLine("| " + string.Join(" | ", separatorCells) + " |");
                            }
                            
                            // Display all data rows
                            foreach (var row in looseTable.Rows)
                            {
                                sb.AppendLine("| " + string.Join(" | ", row ?? new List<string>()) + " |");
                            }
                        }
                        else
                        {
                            sb.AppendLine("(empty table)");
                        }
                        sb.AppendLine();
                    }
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("====================================");
            sb.AppendLine("CRITICAL REMINDER:");
            sb.AppendLine("====================================");
            sb.AppendLine("1. Your analysis MUST consider ALL information provided above:");
            sb.AppendLine("   - The requirement text");
            sb.AppendLine("   - ALL associated tables (if any)");
            sb.AppendLine("   - ALL supplemental content (if any)");
            sb.AppendLine();
            sb.AppendLine("2. When the requirement references tables (e.g., 'per the following table', 'as shown in the table'),");
            sb.AppendLine("   you MUST look at the actual table data provided above. If a table is provided, treat it as part");
            sb.AppendLine("   of the requirement text itself.");
            sb.AppendLine();
            sb.AppendLine("3. Do NOT mention example terms from the criteria (like 'adequate', 'user-friendly', 'reasonable')");
            sb.AppendLine("   UNLESS they actually appear in this specific requirement.");
            sb.AppendLine();
            sb.AppendLine("4. Provide concrete rewrite examples in the 'Example' field of your recommendations.");
            sb.AppendLine("   Use [brackets] to indicate placeholders: 'The system shall respond within [X] seconds'");
            sb.AppendLine("====================================");
            sb.AppendLine();

            // Output instructions
            sb.AppendLine("OUTPUT INSTRUCTIONS:");
            sb.AppendLine();
            sb.AppendLine("Return ONLY valid JSON with no markdown, code fences, or extra commentary.");
            sb.AppendLine("Use this exact structure:");
            sb.AppendLine();
            sb.AppendLine(@"{");
            sb.AppendLine(@"  ""QualityScore"": <integer from 1-10>,");
            sb.AppendLine(@"  ""Issues"": [");
            sb.AppendLine(@"    {");
            sb.AppendLine(@"      ""Category"": ""<Clarity|Testability|Completeness|Atomicity|Actionability|Consistency>"",");
            sb.AppendLine(@"      ""Severity"": ""<High|Medium|Low>"",");
            sb.AppendLine(@"      ""Description"": ""<concise description of the issue>""");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"  ],");
            sb.AppendLine(@"  ""Recommendations"": [");
            sb.AppendLine(@"    {");
            sb.AppendLine(@"      ""Category"": ""<Clarity|Testability|Completeness|Atomicity|Actionability|Consistency>"",");
            sb.AppendLine(@"      ""Description"": ""<actionable recommendation>"",");
            sb.AppendLine(@"      ""Example"": ""<STRONGLY RECOMMENDED: provide a concrete rewrite showing how to fix the issue. Use [brackets] for parts that need specific values.>""");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"  ],");
            sb.AppendLine(@"  ""FreeformFeedback"": ""<optional: additional insights, context, or observations not captured above>""");
            sb.AppendLine(@"}");
            sb.AppendLine();

            // Scoring guidance
            sb.AppendLine("QUALITY SCORE GUIDANCE:");
            sb.AppendLine();
            sb.AppendLine("Score 1-3:   Poor quality. Multiple critical issues. Not testable or actionable. Needs major revision.");
            sb.AppendLine("Score 4-5:   Fair quality. Some clarity or completeness issues. Testability is unclear or limited.");
            sb.AppendLine("Score 6-7:   Good quality. Minor issues present. Generally testable but could be more precise.");
            sb.AppendLine("Score 8-9:   Very good quality. Clear, testable, complete. Only minor improvements needed.");
            sb.AppendLine("Score 10:    Excellent quality. Exemplary requirement. Clear, atomic, testable, complete, actionable.");
            sb.AppendLine();

            // Issue severity guidance
            sb.AppendLine("SEVERITY GUIDANCE:");
            sb.AppendLine();
            sb.AppendLine("High:   Critical issue that prevents verification or creates ambiguity that could lead to incorrect implementation.");
            sb.AppendLine("Medium: Significant issue that makes verification difficult or creates potential for misinterpretation.");
            sb.AppendLine("Low:    Minor issue or stylistic improvement that would enhance clarity or consistency.");
            sb.AppendLine();

            // Category guidance
            sb.AppendLine("CATEGORY GUIDANCE:");
            sb.AppendLine();
            sb.AppendLine("Clarity:       Ambiguous terms, vague language, unclear intent, poor wording");
            sb.AppendLine("Testability:   Missing acceptance criteria, no measurable outcomes, unverifiable claims");
            sb.AppendLine("Completeness:  Missing context, undefined terms, missing conditions or constraints");
            sb.AppendLine("Atomicity:     Multiple requirements combined, should be split, contains hidden sub-requirements");
            sb.AppendLine("Actionability: Too abstract, not implementable, unclear action or outcome");
            sb.AppendLine("Consistency:   ID/name mismatch, inconsistent terminology, unit inconsistencies");
            sb.AppendLine();

            // Final reminders
            sb.AppendLine("REMINDERS:");
            sb.AppendLine("- Be constructive and specific in your feedback");
            sb.AppendLine("- Focus on issues that materially impact testability, implementation, or clarity");
            sb.AppendLine("- Provide actionable recommendations with concrete examples when possible");
            sb.AppendLine("- If the requirement is excellent, say so (high score, minimal issues)");
            sb.AppendLine("- Empty arrays are valid if there are no issues or recommendations");
            sb.AppendLine("- FreeformFeedback can be empty string if nothing additional to say");
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object. No preamble, no markdown formatting, no code fences.");

            return sb.ToString();
        }

        /// <summary>
        /// Build a simplified prompt for fast analysis (fewer details, quicker evaluation).
        /// Useful for batch analysis where speed is important.
        /// </summary>
        public string BuildFastAnalysisPrompt(string requirementId, string requirementName, string requirementText)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Quickly evaluate this requirement's quality (1-10 scale).");
            sb.AppendLine();
            sb.AppendLine($"ID: {requirementId}");
            sb.AppendLine($"Name: {requirementName}");
            sb.AppendLine($"Text: {requirementText}");
            sb.AppendLine();
            sb.AppendLine("Focus on: clarity, testability, completeness, atomicity.");
            sb.AppendLine();
            sb.AppendLine("Return ONLY JSON (no markdown, no code fences):");
            sb.AppendLine(@"{");
            sb.AppendLine(@"  ""QualityScore"": <1-10>,");
            sb.AppendLine(@"  ""Issues"": [{ ""Category"": ""<category>"", ""Severity"": ""<High|Medium|Low>"", ""Description"": ""..."" }],");
            sb.AppendLine(@"  ""Recommendations"": [{ ""Category"": ""<category>"", ""Description"": ""..."", ""Example"": ""..."" }],");
            sb.AppendLine(@"  ""FreeformFeedback"": ""<brief additional notes>""");
            sb.AppendLine(@"}");
            sb.AppendLine();
            sb.AppendLine("Be concise. Provide 0-3 issues, 0-2 recommendations.");

            return sb.ToString();
        }

        /// <summary>
        /// Build an example prompt with sample JSON to help test/validate the service.
        /// </summary>
        public string BuildExamplePrompt()
        {
            return BuildAnalysisPrompt(
                "REQ-001",
                "Display Brightness Control",
                "The system shall provide user-friendly brightness adjustment."
            );
        }

        /// <summary>
        /// Parse the expected JSON structure from LLM response.
        /// This is a simple validation helper - actual parsing should be done by the service.
        /// </summary>
        public static bool IsValidJsonStructure(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check for required properties
                if (!root.TryGetProperty("QualityScore", out var score)) return false;
                if (!root.TryGetProperty("Issues", out var issues)) return false;
                if (!root.TryGetProperty("Recommendations", out var recs)) return false;
                if (!root.TryGetProperty("FreeformFeedback", out _)) return false;

                // Validate score is in range
                if (score.GetInt32() < 1 || score.GetInt32() > 10) return false;

                // Validate Issues array structure
                if (issues.ValueKind != JsonValueKind.Array) return false;
                foreach (var issue in issues.EnumerateArray())
                {
                    if (!issue.TryGetProperty("Category", out _)) return false;
                    if (!issue.TryGetProperty("Severity", out _)) return false;
                    if (!issue.TryGetProperty("Description", out _)) return false;
                }

                // Validate Recommendations array structure
                if (recs.ValueKind != JsonValueKind.Array) return false;
                foreach (var rec in recs.EnumerateArray())
                {
                    if (!rec.TryGetProperty("Category", out _)) return false;
                    if (!rec.TryGetProperty("Description", out _)) return false;
                    // Example is optional
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
