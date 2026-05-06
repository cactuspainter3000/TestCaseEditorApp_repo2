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
        /// Get the system message that establishes analysis criteria and format.
        /// This should be sent once per session to establish context.
        /// </summary>
        /// <returns>System message with analysis criteria and output format</returns>
        public string GetSystemPrompt()
        {
            var sb = new StringBuilder();

            // System role and task definition
            sb.AppendLine("You are a systems engineering expert specializing in requirements quality analysis.");
            sb.AppendLine("Your task is to evaluate the quality of system-level requirements.");
            sb.AppendLine();
            sb.AppendLine("⚠️ CRITICAL: You will receive supplemental information (paragraphs, tables) with each requirement.");
            sb.AppendLine("DO NOT mark terms as 'ambiguous' or 'unclear' if they are defined in supplemental content.");
            sb.AppendLine("ALWAYS check supplemental information for definitions before flagging anything as unclear.");
            sb.AppendLine();

            // Evaluation criteria
            sb.AppendLine("EVALUATION CRITERIA:");
            sb.AppendLine();
            sb.AppendLine("Assess each requirement across these dimensions:");
            sb.AppendLine();
            sb.AppendLine("1. **Clarity & Precision**");
            sb.AppendLine("   - Is the requirement unambiguous and free of vague, subjective, or undefined terms?");
            sb.AppendLine("   - Are all technical terms properly defined or industry-standard?");
            sb.AppendLine("   - Is there exactly one interpretation, or could different readers understand it differently?");
            sb.AppendLine();
            sb.AppendLine("2. **Testability & Verifiability**");
            sb.AppendLine("   - Can this requirement be verified through test, demonstration, inspection, analysis, or simulation?");
            sb.AppendLine("   - Are verification criteria measurable and observable?");
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

            // Critical instructions
            sb.AppendLine("====================================");
            sb.AppendLine("🚨 CRITICAL ANTI-FABRICATION RULES:");
            sb.AppendLine("====================================");
            sb.AppendLine("YOU MUST NOT invent, assume, or add ANY technical details not explicitly present");
            sb.AppendLine("in the original requirement text or supplemental materials. This includes:");
            sb.AppendLine();
            sb.AppendLine("❌ FORBIDDEN: Adding specific technical terms (waveforms, protocols, interfaces)");
            sb.AppendLine("❌ FORBIDDEN: Inventing system names (MES, databases, networks)");
            sb.AppendLine("❌ FORBIDDEN: Making up attributes (timestamps, labels, metadata)");
            sb.AppendLine("❌ FORBIDDEN: Assuming contexts (security, networks, storage types)");
            sb.AppendLine("❌ FORBIDDEN: Specifying tolerances or values not in the source");
            sb.AppendLine();
            sb.AppendLine("✅ REQUIRED: Use [specify X] or [define Y] for missing details");
            sb.AppendLine("✅ REQUIRED: Base ALL suggestions only on words actually present in source");
            sb.AppendLine("✅ REQUIRED: When unclear, ask for specification rather than assuming");
            sb.AppendLine();
            sb.AppendLine("====================================");
            sb.AppendLine("CRITICAL INSTRUCTIONS:");
            sb.AppendLine("====================================");
            sb.AppendLine("1. Your analysis MUST consider ALL information provided:");
            sb.AppendLine("   - The requirement text");
            sb.AppendLine("   - ALL associated tables (if any)");
            sb.AppendLine("   - ALL supplemental content (if any)");
            sb.AppendLine();
            sb.AppendLine("2. When the requirement references tables (e.g., 'per the following table', 'as shown in the table'),");
            sb.AppendLine("   you MUST look at the actual table data provided. If a table is provided, treat it as part");
            sb.AppendLine("   of the requirement text itself.");
            sb.AppendLine();
            sb.AppendLine("3. Do NOT mention example terms from the criteria (like 'adequate', 'user-friendly', 'reasonable')");
            sb.AppendLine("   UNLESS they actually appear in the specific requirement being analyzed.");
            sb.AppendLine();
            sb.AppendLine("4. Provide concrete rewrites in the 'SuggestedEdit' field of your recommendations.");
            sb.AppendLine("   Use [brackets] to indicate placeholders: 'The system shall respond within [X] seconds'");
            sb.AppendLine();
            sb.AppendLine("5. ADAPTIVE REWRITING APPROACH:");
            sb.AppendLine("   - Use available information from requirement text, tables, and supplemental materials");
            sb.AppendLine("   - When specific details are missing, use [brackets] with helpful examples");
            sb.AppendLine("   - Example: 'The system shall respond within [specify time: 1 second, 500ms, etc.]'");
            sb.AppendLine("   - Set HallucinationCheck to 'HELPFUL_ELABORATION' when using [bracket] examples");
            sb.AppendLine("   - Set HallucinationCheck to 'NO_FABRICATION' when using only provided information");
            sb.AppendLine();
            sb.AppendLine("6. LEVERAGE SUPPLEMENTAL INFORMATION: If the supplemental paragraphs or tables contain");
            sb.AppendLine("   specific details that would improve the requirement (definitions, criteria, constraints),");
            sb.AppendLine("   include a 'SuggestedEdit' field with a complete rewrite that incorporates those details");
            sb.AppendLine("   directly into the requirement text. For example:");
            sb.AppendLine("   - If requirement says 'Tier 1 coverage' but supplemental defines what Tier 1 means,");
            sb.AppendLine("     suggest rewriting to include that definition in the requirement itself");
            sb.AppendLine("   - If requirement references 'standard protocols' but supplemental lists specific protocols,");
            sb.AppendLine("     suggest incorporating the specific protocol names");
            sb.AppendLine("   - Move critical details from notes/supplemental into the actual requirement text");
            sb.AppendLine();
            sb.AppendLine("7. ALWAYS PROVIDE SUGGESTED EDITS: Every recommendation should include a SuggestedEdit");
            sb.AppendLine("   with a complete rewrite of the requirement. Use [brackets] for values that need");
            sb.AppendLine("   to be filled in by the user when specific information isn't available. Examples:");
            sb.AppendLine("   - 'The test shall achieve [enter percentage here]% boundary scan coverage'");
            sb.AppendLine("   - 'The system shall respond within [specify time limit] seconds'");
            sb.AppendLine("   - 'The interface shall support [list specific protocols] communication protocols'");
            sb.AppendLine("   If supplemental information contains the specific values, use them directly instead of brackets.");
            sb.AppendLine();
            sb.AppendLine("   CRITICAL: SuggestedEdit must be ACTUAL REQUIREMENT TEXT - not instructions!");
            sb.AppendLine("   DO NOT write '[Provide definition here]' or '[Incorporate table details]' or similar instructions.");
            sb.AppendLine("   Instead, write the complete requirement with specific details incorporated:");
            sb.AppendLine("   BAD: '[Define what Tier 1 means in the requirement text]'");
            sb.AppendLine("   GOOD: 'DECAGON-REQ_RC-5: The Test System shall perform Tier 1 Boundary Scan coverage,'");
            sb.AppendLine("         'defined as direct JTAG interface access to [specify percentage]% of UUT interconnected nodes.'");
            sb.AppendLine();
            sb.AppendLine("   PROVIDE MAXIMUM 1-2 RECOMMENDATIONS TOTAL:");
            sb.AppendLine("   Do not create many separate recommendations. Consolidate related issues into ONE comprehensive");
            sb.AppendLine("   recommendation with ONE complete rewritten requirement that addresses all problems.");
            sb.AppendLine("   Each recommendation should provide exactly ONE SuggestedEdit - not multiple alternatives.");
            sb.AppendLine("   If you identify multiple issues, either:");
            sb.AppendLine("   - Create ONE recommendation that addresses all issues with ONE comprehensive rewrite, OR");
            sb.AppendLine("   - Create maximum 2 recommendations if the issues are truly unrelated");
            sb.AppendLine();
            sb.AppendLine("   IMPORTANT: When you see vague terms like 'Tier 1', 'standard', 'appropriate', or similar");
            sb.AppendLine("   language in the requirement text, and the supplemental information provides specific");
            sb.AppendLine("   definitions or details for those terms, you SHOULD provide a SuggestedEdit that");
            sb.AppendLine("   incorporates those details directly into the requirement statement.");
            sb.AppendLine();
            sb.AppendLine("   IMPORTANT: ALWAYS provide a SuggestedEdit for every recommendation, even when there's");
            sb.AppendLine("   no supplemental information. Use [brackets] to indicate where specific values need");
            sb.AppendLine("   to be filled in by the user. This gives concrete, actionable guidance for improvement.");
            sb.AppendLine("====================================");
            sb.AppendLine();
            sb.AppendLine("SELF-REVIEW PROCESS:");
            sb.AppendLine("====================================");
            sb.AppendLine("Before finalizing your analysis, STOP and verify each recommendation:");
            sb.AppendLine();
            sb.AppendLine("1. FABRICATION CHECK: For EVERY technical term, specification, or detail in your SuggestedEdit:");
            sb.AppendLine("   - Is this term present in the ORIGINAL requirement text?");
            sb.AppendLine("   - Is this detail found in the provided tables or supplemental content?");
            sb.AppendLine("   - If NO to both: REMOVE IT or replace with [specify X] placeholder");
            sb.AppendLine();
            sb.AppendLine("2. EXAMPLES OF FORBIDDEN FABRICATIONS:");
            sb.AppendLine("   - Technical terms not in source: 'waveform snapshots', 'MES', 'one bit tolerance'");
            sb.AppendLine("   - Specific protocols not mentioned: 'JTAG interface', 'TCP/IP', 'USB'");
            sb.AppendLine("   - Made-up attributes: 'time stamps, labels, metadata'");
            sb.AppendLine("   - Invented systems: 'Manufacturing Execution System', 'portable storage devices'");
            sb.AppendLine("   - Assumed contexts: 'internal network', 'security protocols', 'encryption'");
            sb.AppendLine();
            sb.AppendLine("3. MANDATORY WORD-BY-WORD CHECK:");
            sb.AppendLine("   Read your SuggestedEdit aloud. For each technical word, ask:");
            sb.AppendLine("   'Does this exact word or concept appear in the original requirement?'");
            sb.AppendLine("   If the answer is NO, either remove it or use [specify X] instead.");
            sb.AppendLine();
            sb.AppendLine("4. ACCEPTABLE vs UNACCEPTABLE IMPROVEMENTS:");
            sb.AppendLine("   ✅ GOOD: 'The system shall generate [specify type] test data outputs in TDMS format'");
            sb.AppendLine("   ❌ BAD:  'The system shall generate waveform snapshots in TDMS format'");
            sb.AppendLine("   ✅ GOOD: 'Store outputs with [define required attributes] for analysis'");
            sb.AppendLine("   ❌ BAD:  'Store outputs with time stamps, labels, etc. for analysis'");
            sb.AppendLine();
            sb.AppendLine("5. FINAL VERIFICATION: Before submitting, re-read the original requirement and confirm");
            sb.AppendLine("   your SuggestedEdit contains ONLY improvements to clarity, NOT new technical content.");
            sb.AppendLine();
            sb.AppendLine("CRITICAL: When in doubt, use [brackets] instead of guessing. It's better to have");
            sb.AppendLine("placeholders than incorrect technical specifications that could mislead engineers.");
            sb.AppendLine("====================================");
            sb.AppendLine();

            // Output format
            sb.AppendLine("OUTPUT FORMAT:");
            sb.AppendLine();
            sb.AppendLine("Return ONLY valid JSON with no markdown, code fences, or extra commentary.");
            sb.AppendLine("Use this exact structure:");
            sb.AppendLine();
            sb.AppendLine(@"{");
            sb.AppendLine(@"  ""QualityScore"": <integer from 1-10>,");
            sb.AppendLine(@"  ""HallucinationCheck"": ""<REQUIRED: Choose based on your approach - 'NO_FABRICATION' if you used ONLY terms/concepts from the original requirement/supplemental materials (complete rewrite mode), 'HELPFUL_ELABORATION' if you provided realistic examples in [brackets] to show what information is needed (coaching mode), or 'FABRICATED_DETAILS' if you added technical terms not present in the source material.>"",");
            sb.AppendLine(@"  ""Issues"": [");
            sb.AppendLine(@"    {");
            sb.AppendLine(@"      ""Category"": ""<Clarity|Testability|Completeness|Atomicity|Actionability|Consistency>"",");
            sb.AppendLine(@"      ""Severity"": ""<High|Medium|Low>"",");
            sb.AppendLine(@"      ""Description"": ""<concise description of the issue>"",");
            sb.AppendLine(@"      ""Fix"": ""<past tense description of what was addressed/improved (e.g., 'Defined specific acceptance criteria', 'Clarified technical requirements', 'Specified measurable thresholds')>""");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"  ],");
            sb.AppendLine(@"  ""FreeformFeedback"": ""<OPTIONAL: Only include if you have meaningful additional insights, context, or strategic observations not covered in the structured issues above. If no additional insights are needed, you may leave this as an empty string or omit entirely. Do not include placeholder text like 'No additional insights necessary'.>""");
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
            sb.AppendLine("ANALYSIS REMINDERS:");
            sb.AppendLine("- Be constructive and specific in your feedback");
            sb.AppendLine("- Focus on issues that materially impact testability, implementation, or clarity");
            sb.AppendLine("- Provide actionable recommendations with concrete examples when possible");
            sb.AppendLine("- CRITICAL: Provide MAXIMUM 1-2 total recommendations, not many separate ones");
            sb.AppendLine("- ABSOLUTELY NO MORE THAN 2 RECOMMENDATIONS - consolidate multiple issues into comprehensive rewrites");
            sb.AppendLine("- ALWAYS provide a SuggestedEdit for every recommendation - this is the most valuable part!");
            sb.AppendLine("- Each recommendation provides exactly ONE comprehensive SuggestedEdit - not multiple alternatives");
            sb.AppendLine("- SuggestedEdit must be ACTUAL REQUIREMENT TEXT, not instructions like '[Define this]'");
            sb.AppendLine("- Use [brackets] in SuggestedEdit for values that need to be specified by the user");
            sb.AppendLine("- When supplemental information contains valuable details, incorporate them directly into SuggestedEdit");
            sb.AppendLine("- Consolidate related improvements into ONE cohesive rewrite rather than separate versions");
            sb.AppendLine("- Write the requirement as if it's the final version someone would implement");
            sb.AppendLine("- If you see terms like 'Tier 1', 'standard protocols', 'appropriate methods', etc.");
            sb.AppendLine("  and the supplemental content defines these terms specifically, incorporate the definitions");
            sb.AppendLine("- If the requirement is excellent, say so (high score, minimal issues)");
            sb.AppendLine("- Empty arrays are valid if there are no issues or recommendations");
            sb.AppendLine("- CRITICAL: Do NOT provide 3+ recommendations - consolidate into 1-2 maximum");
            sb.AppendLine("- CRITICAL: Each recommendation gets exactly ONE SuggestedEdit, not multiple versions");
            sb.AppendLine("- REMEMBER: Every recommendation MUST include a SuggestedEdit with a complete requirement rewrite");
            sb.AppendLine("- Use [brackets] for values that need user input when specific information isn't available");
            sb.AppendLine();
            sb.AppendLine("CRITICAL FORMAT REQUIREMENTS:");
            sb.AppendLine("- MUST return valid JSON - no text outside the JSON structure");
            sb.AppendLine("- EVERY recommendation MUST include a SuggestedEdit field with actual requirement text");
            sb.AppendLine("- Do NOT provide example text, placeholder text, or instructional text");
            sb.AppendLine("- SuggestedEdit must be complete, implementable requirement language");
            sb.AppendLine("- If no recommendations needed, return empty Recommendations array");
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object. No preamble, no markdown formatting, no code fences.");

            return sb.ToString();
        }
        /// <summary>
        /// Build a context message with requirement-specific data for analysis.
        /// Use this with GetSystemPrompt() for optimal performance.
        /// </summary>
        /// <param name="requirementId">The requirement ID (e.g., "REQ-001")</param>
        /// <param name="requirementName">The requirement name/title</param>
        /// <param name="requirementText">The full requirement text to analyze</param>
        /// <param name="tables">Tables associated with the requirement</param>
        /// <param name="looseContent">Supplemental paragraphs and tables</param>
        /// <param name="verificationAssumptions">Verification method context</param>
        /// <returns>Context message with requirement data for analysis</returns>
        public string BuildContextPrompt(string requirementId, string requirementName, string requirementText, 
            List<RequirementTable>? tables = null, RequirementLooseContent? looseContent = null, string? verificationAssumptions = null)
        {
            var sb = new StringBuilder();

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
                                var headers = looseTable.ColumnHeaders ?? new System.Collections.Generic.List<string>();
                                sb.AppendLine("| " + string.Join(" | ", headers) + " |");
                                var separatorCells = headers.Select(_ => "---");
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

            // Add verification method assumptions if provided
            if (!string.IsNullOrWhiteSpace(verificationAssumptions))
            {
                sb.AppendLine();
                sb.AppendLine("VERIFICATION METHOD ASSUMPTIONS:");
                sb.AppendLine();
                sb.AppendLine("The following verification approach has been selected for this requirement:");
                sb.AppendLine(verificationAssumptions);
                sb.AppendLine();
                sb.AppendLine("Consider this verification approach when evaluating testability and providing recommendations.");
            }
            
            sb.AppendLine();
            sb.AppendLine("Please analyze this requirement and return the JSON response.");

            return sb.ToString();
        }

        /// <summary>
        /// Build a prompt to analyze a single requirement's quality.
        /// This is the legacy method - use GetSystemPrompt() + BuildContextPrompt() for better performance.
        /// </summary>
        /// <param name="requirementId">The requirement ID (e.g., "REQ-001")</param>
        /// <param name="requirementName">The requirement name/title</param>
        /// <param name="requirementText">The full requirement text to analyze</param>
        /// <param name="tables">Tables associated with the requirement</param>
        /// <param name="looseContent">Supplemental paragraphs and tables</param>
        /// <returns>Complete prompt string for LLM analysis</returns>
        [Obsolete("Use GetSystemPrompt() + BuildContextPrompt() for better performance")]
        public string BuildAnalysisPrompt(string requirementId, string requirementName, string requirementText, 
            List<RequirementTable>? tables = null, RequirementLooseContent? looseContent = null, string? verificationAssumptions = null)
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
            sb.AppendLine("   - Are verification criteria measurable and observable?");
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
                                var headers = looseTable.ColumnHeaders ?? new System.Collections.Generic.List<string>();
                                sb.AppendLine("| " + string.Join(" | ", headers) + " |");
                                var separatorCells = headers.Select(_ => "---");
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

            // Add verification method assumptions if provided
            if (!string.IsNullOrWhiteSpace(verificationAssumptions))
            {
                sb.AppendLine();
                sb.AppendLine("VERIFICATION METHOD ASSUMPTIONS:");
                sb.AppendLine();
                sb.AppendLine("The following verification approach has been selected for this requirement:");
                sb.AppendLine(verificationAssumptions);
                sb.AppendLine();
                sb.AppendLine("Consider this verification approach when evaluating testability and providing recommendations.");
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
            sb.AppendLine("4. Provide concrete rewrites in the 'SuggestedEdit' field of your recommendations.");
            sb.AppendLine("   Use [brackets] to indicate placeholders: 'The system shall respond within [X] seconds'");
            sb.AppendLine();
            sb.AppendLine("5. LEVERAGE SUPPLEMENTAL INFORMATION: If the supplemental paragraphs or tables contain");
            sb.AppendLine("   specific details that would improve the requirement (definitions, criteria, constraints),");
            sb.AppendLine("   include a 'SuggestedEdit' field with a complete rewrite that incorporates those details");
            sb.AppendLine("   directly into the requirement text. For example:");
            sb.AppendLine("   - If requirement says 'Tier 1 coverage' but supplemental defines what Tier 1 means,");
            sb.AppendLine("     suggest rewriting to include that definition in the requirement itself");
            sb.AppendLine("   - If requirement references 'standard protocols' but supplemental lists specific protocols,");
            sb.AppendLine("     suggest incorporating the specific protocol names");
            sb.AppendLine("   - Move critical details from notes/supplemental into the actual requirement text");
            sb.AppendLine();
            sb.AppendLine("6. ALWAYS PROVIDE SUGGESTED EDITS: Every recommendation should include a SuggestedEdit");
            sb.AppendLine("   with a complete rewrite of the requirement. Use [brackets] for values that need");
            sb.AppendLine("   to be filled in by the user when specific information isn't available. Examples:");
            sb.AppendLine("   - 'The test shall achieve [enter percentage here]% boundary scan coverage'");
            sb.AppendLine("   - 'The system shall respond within [specify time limit] seconds'");
            sb.AppendLine("   - 'The interface shall support [list specific protocols] communication protocols'");
            sb.AppendLine("   If supplemental information contains the specific values, use them directly instead of brackets.");
            sb.AppendLine();
            sb.AppendLine("   CRITICAL: SuggestedEdit must be ACTUAL REQUIREMENT TEXT - not instructions!");
            sb.AppendLine("   DO NOT write '[Provide definition here]' or '[Incorporate table details]' or similar instructions.");
            sb.AppendLine("   Instead, write the complete requirement with specific details incorporated:");
            sb.AppendLine("   BAD: '[Define what Tier 1 means in the requirement text]'");
            sb.AppendLine("   GOOD: 'DECAGON-REQ_RC-5: The Test System shall perform Tier 1 Boundary Scan coverage,'");
            sb.AppendLine("         'defined as direct JTAG interface access to [specify percentage]% of UUT interconnected nodes.'");
            sb.AppendLine();
            sb.AppendLine("   PROVIDE MAXIMUM 1-2 RECOMMENDATIONS TOTAL:");
            sb.AppendLine("   Do not create many separate recommendations. Consolidate related issues into ONE comprehensive");
            sb.AppendLine("   recommendation with ONE complete rewritten requirement that addresses all problems.");
            sb.AppendLine("   Each recommendation should provide exactly ONE SuggestedEdit - not multiple alternatives.");
            sb.AppendLine("   If you identify multiple issues, either:");
            sb.AppendLine("   - Create ONE recommendation that addresses all issues with ONE comprehensive rewrite, OR");
            sb.AppendLine("   - Create maximum 2 recommendations if the issues are truly unrelated");
            sb.AppendLine();
            sb.AppendLine("   IMPORTANT: When you see vague terms like 'Tier 1', 'standard', 'appropriate', or similar");
            sb.AppendLine("   language in the requirement text, and the supplemental information provides specific");
            sb.AppendLine("   definitions or details for those terms, you SHOULD provide a SuggestedEdit that");
            sb.AppendLine("   incorporates those details directly into the requirement statement.");
            sb.AppendLine();
            sb.AppendLine("   IMPORTANT: ALWAYS provide a SuggestedEdit for every recommendation, even when there's");
            sb.AppendLine("   no supplemental information. Use [brackets] to indicate where specific values need");
            sb.AppendLine("   to be filled in by the user. This gives concrete, actionable guidance for improvement.");
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

            sb.AppendLine(@"      ""SuggestedEdit"": ""<improved requirement text>""");
            sb.AppendLine(@"    }");
            sb.AppendLine(@"  ],");
            sb.AppendLine(@"  ""FreeformFeedback"": ""<OPTIONAL: Only include if you have meaningful additional insights, context, or strategic observations not covered in the structured recommendations above. If no additional insights are needed, you may leave this as an empty string or omit entirely. Do not include placeholder text like 'No additional insights necessary'.>""");
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
            sb.AppendLine("- CRITICAL: Provide maximum 1-2 total recommendations, not many separate ones");
            sb.AppendLine("- ALWAYS provide a SuggestedEdit for every recommendation - this is the most valuable part!");
            sb.AppendLine("- Each recommendation provides exactly ONE comprehensive SuggestedEdit - not multiple alternatives");
            sb.AppendLine("- SuggestedEdit must be ACTUAL REQUIREMENT TEXT, not instructions like '[Define this]'");
            sb.AppendLine("- Use [brackets] in SuggestedEdit for values that need to be specified by the user");
            sb.AppendLine("- When supplemental information contains valuable details, incorporate them directly into SuggestedEdit");
            sb.AppendLine("- Consolidate related improvements into ONE cohesive rewrite rather than separate versions");
            sb.AppendLine("- Write the requirement as if it's the final version someone would implement");
            sb.AppendLine("- If you see terms like 'Tier 1', 'standard protocols', 'appropriate methods', etc.");
            sb.AppendLine("  and the supplemental content defines these terms specifically, incorporate the definitions");
            sb.AppendLine("  For this requirement, 'Tier 1 Boundary Scan coverage' is defined in supplemental content - use it!");
            sb.AppendLine("- If the requirement is excellent, say so (high score, minimal issues)");
            sb.AppendLine("- Empty arrays are valid if there are no issues or recommendations");
            sb.AppendLine("- CRITICAL: Do NOT provide 3+ recommendations - consolidate into 1-2 maximum");
            sb.AppendLine("- CRITICAL: Each recommendation gets exactly ONE SuggestedEdit, not multiple versions");
            sb.AppendLine("- REMEMBER: Every recommendation MUST include a SuggestedEdit with a complete requirement rewrite");
            sb.AppendLine("- Use [brackets] for values that need user input when specific information isn't available");
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object. No preamble, no markdown formatting, no code fences.");

            return sb.ToString();
        }



        /// <summary>
        /// Build an example prompt with sample JSON to help test/validate the service.
        /// </summary>
        public string BuildExamplePrompt()
        {
            var systemPrompt = GetSystemPrompt();
            var contextPrompt = BuildContextPrompt(
                "REQ-001",
                "Display Brightness Control",
                "The system shall provide user-friendly brightness adjustment."
            );
            
            return systemPrompt + "\n\n" + contextPrompt;
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
                    // SuggestedEdit is optional but recommended
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
