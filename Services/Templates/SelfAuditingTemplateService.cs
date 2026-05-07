using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Self-auditing extension for template-based capability derivation
    /// Enables LLMs to validate their own responses against form requirements
    /// ARCHITECTURAL COMPLIANCE: Implements proper service interface following existing patterns
    /// </summary>
    public sealed class SelfAuditingTemplateService : ISelfAuditingTemplateService
    {
        public class AuditResult : ISelfAuditResult
        {
            public bool PassedAudit { get; set; }
            public double ConfidenceScore { get; set; }        // Mapped from QualityScore/ComplianceScore
            public List<string> IdentifiedIssues { get; set; } = new();  // Combined FieldViolations + QualityIssues
            public string RecommendedAction { get; set; } = string.Empty;  // Mapped from RevisionRecommendation
            public Dictionary<string, object> Metadata { get; set; } = new();
            
            // Legacy properties for backwards compatibility
            public double ComplianceScore { get; set; }        // % of required fields completed correctly
            public double QualityScore { get; set; }          // Self-assessed quality (1-10)
            public List<string> FieldViolations { get; set; } = new();
            public List<string> QualityIssues { get; set; } = new();
            public string RevisionRecommendation { get; set; } = string.Empty;
            public bool RecommendRevision { get; set; }
        }

        public class SelfAuditPrompt
        {
            public string OriginalResponse { get; set; } = string.Empty;
            public IFormTemplate Template { get; set; } = new FormTemplate();
            public string AuditInstructions { get; set; } = string.Empty;
        }

        /// <summary>
        /// Generate a self-audit prompt for the LLM to validate its own response
        /// </summary>
        public string GenerateSelfAuditPrompt(
            string originalResponse, 
            IFormTemplate template,
            string atpStep)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine("SELF-AUDIT: CAPABILITY DERIVATION RESPONSE");
            prompt.AppendLine("=========================================");
            prompt.AppendLine();
            prompt.AppendLine("Your task: Review your previous response and audit it for compliance and quality.");
            prompt.AppendLine();

            prompt.AppendLine("ORIGINAL ATP STEP:");
            prompt.AppendLine(atpStep);
            prompt.AppendLine();

            prompt.AppendLine("YOUR PREVIOUS RESPONSE:");
            prompt.AppendLine("----------------------");
            prompt.AppendLine(originalResponse);
            prompt.AppendLine("----------------------");
            prompt.AppendLine();

            prompt.AppendLine("AUDIT CHECKLIST:");
            prompt.AppendLine("================");
            prompt.AppendLine();

            // Generate field-specific audit questions
            foreach (var field in template.Fields.OrderBy(f => f.Criticality))
            {
                var criticalityLabel = field.Criticality == FieldCriticality.Required ? "REQUIRED" : 
                                     field.Criticality == FieldCriticality.Optional ? "OPTIONAL" : "ENHANCEMENT";

                prompt.AppendLine($"□ [{criticalityLabel}] {field.DisplayName}:");
                
                switch (field.FieldName)
                {
                    case "systemCapability":
                        prompt.AppendLine("  - Did I start with 'The system shall provide...' or 'The system shall be capable of...'?");
                        prompt.AppendLine("  - Is the capability specific and actionable (not vague)?");
                        prompt.AppendLine("  - Does it describe what the SYSTEM must do (not test procedures)?");
                        break;

                    case "taxonomyCategory":
                        prompt.AppendLine("  - Is my selection from the valid A-N taxonomy list?");
                        prompt.AppendLine("  - Does the category actually match the capability type?");
                        break;

                    case "derivationRationale":
                        prompt.AppendLine("  - Did I explain WHY this ATP step requires this capability?");
                        prompt.AppendLine("  - Is the logical connection clear and specific?");
                        break;

                    case "confidenceLevel":
                        prompt.AppendLine("  - Does my confidence level honestly reflect the analysis quality?");
                        prompt.AppendLine("  - Is it from the valid options (High/Medium/Low)?");
                        break;

                    case "technicalDetails":
                        if (field.Criticality == FieldCriticality.Optional)
                        {
                            prompt.AppendLine("  - If provided, are the technical details specific and relevant?");
                            prompt.AppendLine("  - Do they add meaningful context beyond the basic capability?");
                        }
                        break;
                }
                
                prompt.AppendLine("  Audit Status: _______________");
                prompt.AppendLine();
            }

            prompt.AppendLine("QUALITY ASSESSMENT:");
            prompt.AppendLine("==================");
            prompt.AppendLine("Rate each aspect (1-10 scale):");
            prompt.AppendLine();
            prompt.AppendLine("□ Specificity: How specific vs. vague is the derived capability? ___/10");
            prompt.AppendLine("□ Relevance: How directly related to the ATP step? ___/10");
            prompt.AppendLine("□ Completeness: How well did I address all aspects? ___/10");
            prompt.AppendLine("□ Technical Accuracy: How accurate are technical details? ___/10");
            prompt.AppendLine("□ System Focus: Did I focus on system capabilities vs. test procedures? ___/10");
            prompt.AppendLine();

            prompt.AppendLine("FINAL AUDIT RESULTS:");
            prompt.AppendLine("===================");
            prompt.AppendLine();
            prompt.AppendLine("Compliance Score: ___/5 (required fields completed correctly)");
            prompt.AppendLine("Average Quality Score: ___/10 (average of quality ratings above)");
            prompt.AppendLine();
            prompt.AppendLine("Field Violations Found: ________________");
            prompt.AppendLine("(List any fields that don't meet requirements)");
            prompt.AppendLine();
            prompt.AppendLine("Quality Issues Found: ________________");
            prompt.AppendLine("(List any quality concerns or improvements needed)");
            prompt.AppendLine();
            prompt.AppendLine("Recommendation: ________________");
            prompt.AppendLine("(Should response be: APPROVED / REVISED / REJECTED)");
            prompt.AppendLine();
            prompt.AppendLine("If REVISED recommended, suggest specific improvements: ________________");
            prompt.AppendLine();

            prompt.AppendLine("RESPONSE FORMAT:");
            prompt.AppendLine("===============");
            prompt.AppendLine("Fill out the audit checklist above with honest self-assessment.");
            prompt.AppendLine("Be critical of your own work - identify real issues that need fixing.");
            prompt.AppendLine("Focus on compliance first, then quality improvements.");

            return prompt.ToString();
        }

        /// <summary>
        /// Parse the LLM's self-audit response
        /// </summary>
        public ISelfAuditResult ParseAuditResponse(string auditResponse)
        {
            var result = new AuditResult();

            try
            {
                // Extract compliance score
                var complianceMatch = System.Text.RegularExpressions.Regex.Match(
                    auditResponse, @"Compliance Score:\s*(\d+)/(\d+)");
                if (complianceMatch.Success)
                {
                    var score = int.Parse(complianceMatch.Groups[1].Value);
                    var total = int.Parse(complianceMatch.Groups[2].Value);
                    result.ComplianceScore = (double)score / total * 100;
                }

                // Extract quality score
                var qualityMatch = System.Text.RegularExpressions.Regex.Match(
                    auditResponse, @"Average Quality Score:\s*(\d+(?:\.\d+)?)/10");
                if (qualityMatch.Success)
                {
                    result.QualityScore = double.Parse(qualityMatch.Groups[1].Value);
                }

                // Extract violations
                result.FieldViolations = ExtractListValue(auditResponse, "Field Violations Found:");
                result.QualityIssues = ExtractListValue(auditResponse, "Quality Issues Found:");

                // Extract recommendation
                var recommendationMatch = System.Text.RegularExpressions.Regex.Match(
                    auditResponse, @"Recommendation:\s*(APPROVED|REVISED|REJECTED)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (recommendationMatch.Success)
                {
                    var recommendation = recommendationMatch.Groups[1].Value.ToUpper();
                    result.RecommendRevision = recommendation == "REVISED";
                    result.PassedAudit = recommendation == "APPROVED";
                }

                // Extract revision suggestions if any
                result.RevisionRecommendation = ExtractSingleValue(auditResponse, "If REVISED recommended, suggest specific improvements:");

                // Map legacy properties to interface properties for compliance
                result.ConfidenceScore = Math.Max(result.QualityScore, result.ComplianceScore);
                result.IdentifiedIssues.Clear();
                result.IdentifiedIssues.AddRange(result.FieldViolations);
                result.IdentifiedIssues.AddRange(result.QualityIssues);
                result.RecommendedAction = result.RevisionRecommendation;
                result.Metadata["ComplianceScore"] = result.ComplianceScore;
                result.Metadata["QualityScore"] = result.QualityScore;
                result.Metadata["RecommendRevision"] = result.RecommendRevision;

                return result;
            }
            catch (Exception ex)
            {
                // If parsing fails, assume audit failed
                result.PassedAudit = false;
                result.QualityIssues.Add($"Audit parsing failed: {ex.Message}");
                
                // Map to interface properties
                result.ConfidenceScore = 0.0;
                result.IdentifiedIssues.Clear();
                result.IdentifiedIssues.Add($"Audit parsing failed: {ex.Message}");
                result.RecommendedAction = "Retry audit with clearer response format";
                result.Metadata["ParseException"] = ex.Message;
                
                return result;
            }
        }

        private List<string> ExtractListValue(string text, string fieldLabel)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text, $@"{Regex.Escape(fieldLabel)}\s*([^\n]+)");
            
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                if (value != "________________" && !string.IsNullOrEmpty(value))
                {
                    return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim())
                              .Where(s => !string.IsNullOrEmpty(s))
                              .ToList();
                }
            }
            return new List<string>();
        }

        private string ExtractSingleValue(string text, string fieldLabel)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                text, $@"{Regex.Escape(fieldLabel)}\s*([^\n]+)");
            
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();
                return value != "________________" ? value : string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// Full two-phase process: Generate response, then self-audit
        /// </summary>
        public async Task<(string Response, ISelfAuditResult Audit)> GenerateWithSelfAuditAsync(
            string prompt, 
            IFormTemplate template,
            string atpStep,
            Func<string, Task<string>> llmCallAsync)
        {
            // Phase 1: Generate initial response
            var initialResponse = await llmCallAsync(prompt);

            // Phase 2: Self-audit the response
            var auditPrompt = GenerateSelfAuditPrompt(initialResponse, template, atpStep);
            var auditResponse = await llmCallAsync(auditPrompt);
            var auditResult = ParseAuditResponse(auditResponse);

            return (initialResponse, auditResult);
        }
    }
}