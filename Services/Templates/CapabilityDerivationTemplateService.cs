using System;
using System.Collections.Generic;
using System.Linq;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Template-based capability derivation using structured forms instead of free-form JSON
    /// Provides guaranteed format compliance and field-level validation
    /// ARCHITECTURAL COMPLIANCE: Implements proper service interface following existing patterns
    /// </summary>
    public sealed class CapabilityDerivationTemplateService : ICapabilityDerivationTemplateService
    {
        /// <summary>
        /// Generate the standard system capability derivation form template
        /// </summary>
        public IFormTemplate GetStandardCapabilityTemplate()
        {
            return new FormTemplate
            {
                TemplateName = "System Capability Derivation Form",
                Instructions = @"
Fill out this form to derive system capabilities from ATP steps.
- [REQUIRED] fields must be completed for valid derivation
- [OPTIONAL] fields enhance analysis quality  
- [ENHANCEMENT] fields provide additional context
Follow the specific instructions for each field.",

                Fields = new List<IFormField>
                {
                    new FormField
                    {
                        FieldName = "systemCapability",
                        DisplayName = "[REQUIRED] System Capability",
                        Criticality = FieldCriticality.Required,
                        Type = FieldType.LongText,
                        Instructions = "What must the system be capable of? Start with 'The system shall provide...' or 'The system shall be capable of...'",
                        Placeholder = "The system shall provide..."
                    },

                    new FormField
                    {
                        FieldName = "taxonomyCategory", 
                        DisplayName = "[REQUIRED] Taxonomy Category",
                        Criticality = FieldCriticality.Required,
                        Type = FieldType.Dropdown,
                        Instructions = "Select the A-N taxonomy category that best fits this capability",
                        ValidOptions = new[] 
                        { 
                            "A1", "A2", "A3", "A4", "B1", "B2", "B3", 
                            "C1", "C2", "C3", "D1", "D2", "D3",
                            "E1", "E2", "E3", "F1", "F2", "F3",
                            "G1", "G2", "G3", "H1", "H2", "I1", "I2",
                            "J1", "J2", "K1", "N1"
                        }
                    },

                    new FormField
                    {
                        FieldName = "derivationRationale",
                        DisplayName = "[REQUIRED] Derivation Rationale", 
                        Criticality = FieldCriticality.Required,
                        Type = FieldType.LongText,
                        Instructions = "Explain why this ATP step requires this system capability. Be specific about the logical connection.",
                        Placeholder = "This capability is needed because the ATP step requires..."
                    },

                    new FormField
                    {
                        FieldName = "confidenceLevel",
                        DisplayName = "[REQUIRED] Confidence Level",
                        Criticality = FieldCriticality.Required, 
                        Type = FieldType.Dropdown,
                        Instructions = "How confident are you that this capability is required?",
                        ValidOptions = new[] { "High (0.8-1.0)", "Medium (0.5-0.8)", "Low (0.2-0.5)" }
                    },

                    new FormField
                    {
                        FieldName = "technicalDetails",
                        DisplayName = "[OPTIONAL] Technical Details",
                        Criticality = FieldCriticality.Optional,
                        Type = FieldType.LongText, 
                        Instructions = "Specific parameters, interfaces, performance requirements, or technical constraints",
                        Placeholder = "Technical specifications, interfaces, performance criteria..."
                    },

                    new FormField
                    {
                        FieldName = "verificationMethod",
                        DisplayName = "[OPTIONAL] Verification Method",
                        Criticality = FieldCriticality.Optional,
                        Type = FieldType.Text,
                        Instructions = "How would you test or verify this capability?",
                        Placeholder = "Verification approach..."
                    },

                    new FormField
                    {
                        FieldName = "standardsReferences", 
                        DisplayName = "[ENHANCEMENT] Standards References",
                        Criticality = FieldCriticality.Enhancement,
                        Type = FieldType.Text,
                        Instructions = "Applicable industry standards, protocols, or regulations",
                        Placeholder = "IEEE, IEC, MIL-STD, etc."
                    }
                }
            };
        }

        /// <summary>
        /// Generate a prompt that presents the form template to the LLM
        /// </summary>
        public string GenerateFormPrompt(IFormTemplate template, string atpStep, string? ragContext = null)
        {
            var prompt = new System.Text.StringBuilder();

            prompt.AppendLine(template.TemplateName);
            prompt.AppendLine(new string('=', template.TemplateName.Length));
            prompt.AppendLine();

            if (!string.IsNullOrEmpty(ragContext))
            {
                prompt.AppendLine("DOCUMENT CONTEXT:");
                prompt.AppendLine("Base your analysis on this documentation:");
                prompt.AppendLine(ragContext);
                prompt.AppendLine();
            }

            prompt.AppendLine("ATP STEP TO ANALYZE:");
            prompt.AppendLine(atpStep);
            prompt.AppendLine();

            prompt.AppendLine(template.Instructions);
            prompt.AppendLine();

            // Generate form fields
            foreach (var field in template.Fields)
            {
                prompt.AppendLine($"{field.DisplayName}:");
                prompt.AppendLine($"Instructions: {field.Instructions}");
                
                if (field.Type == FieldType.Dropdown && field.ValidOptions.Length > 0)
                {
                    prompt.AppendLine("Valid Options: " + string.Join(" | ", field.ValidOptions));
                }
                
                prompt.AppendLine($"Your Response: ________________");
                prompt.AppendLine();
            }

            prompt.AppendLine("RESPONSE FORMAT:");
            prompt.AppendLine("Fill out each field above with your response on the 'Your Response:' line.");
            prompt.AppendLine("Do not change the field names or structure.");
            prompt.AppendLine("If you cannot determine a field value, write 'INSUFFICIENT_INFO'.");

            return prompt.ToString();
        }

        /// <summary>
        /// Parse LLM response that filled out the form template
        /// </summary>
        public IFilledForm ParseFormResponse(string llmResponse, IFormTemplate template)
        {
            var filledForm = new FilledForm
            {
                CompletedAt = DateTime.Now,
                FieldValues = new Dictionary<string, object>()
            };

            // Parse field values from response
            foreach (var field in template.Fields)
            {
                var fieldValue = ExtractFieldValue(llmResponse, field.DisplayName);
                if (!string.IsNullOrEmpty(fieldValue) && fieldValue != "INSUFFICIENT_INFO")
                {
                    filledForm.FieldValues[field.FieldName] = fieldValue;
                }
            }

            // Validate the form
            filledForm.ValidationResult = ValidateForm(filledForm, template);

            return filledForm;
        }

        private string ExtractFieldValue(string response, string fieldDisplayName)
        {
            // Look for pattern: "DisplayName:" followed by "Your Response: value"
            var fieldStart = response.IndexOf(fieldDisplayName + ":");
            if (fieldStart == -1) return string.Empty;

            var responseStart = response.IndexOf("Your Response:", fieldStart);
            if (responseStart == -1) return string.Empty;

            responseStart += "Your Response:".Length;
            var lineEnd = response.IndexOf('\n', responseStart);
            if (lineEnd == -1) lineEnd = response.Length;

            return response.Substring(responseStart, lineEnd - responseStart)
                          .Replace("________________", "")
                          .Trim();
        }

        private IFormValidationResult ValidateForm(IFilledForm form, IFormTemplate template)
        {
            var result = new FormValidationResult();
            var totalFields = template.Fields.Count;
            var completedFields = form.FieldValues.Count;

            // Check required fields
            var requiredFields = template.Fields.Where(f => f.Criticality == FieldCriticality.Required).ToList();
            var missingRequired = requiredFields.Where(f => !form.FieldValues.ContainsKey(f.FieldName)).Select(f => f.DisplayName).ToList();
            
            // Use interface properties
            result.FieldViolations.AddRange(missingRequired.Select(field => $"Missing required field: {field}"));
            result.IsValid = missingRequired.Count == 0;

            // Check optional fields  
            var optionalFields = template.Fields.Where(f => f.Criticality == FieldCriticality.Optional).ToList();
            var missingOptional = optionalFields.Where(f => !form.FieldValues.ContainsKey(f.FieldName)).Select(f => f.DisplayName).ToList();
            result.WarningMessages.AddRange(missingOptional.Select(field => $"Missing optional field: {field}"));

            // Calculate compliance score (percentage of total fields completed)
            result.ComplianceScore = totalFields > 0 ? (double)completedFields / totalFields : 1.0;

            // Calculate criticality scores
            result.CriticalityScore = new CriticalityScore
            {
                RequiredFieldsScore = requiredFields.Count > 0 ? 
                    (double)(requiredFields.Count - missingRequired.Count) / requiredFields.Count : 1.0,
                OptionalFieldsScore = optionalFields.Count > 0 ?
                    (double)(optionalFields.Count - missingOptional.Count) / optionalFields.Count : 1.0,
                EnhancementFieldsScore = 1.0  // Enhancement fields don't affect validity
            };

            // Weighted overall: Required=70%, Optional=20%, Enhancement=10%
            result.CriticalityScore.WeightedOverallScore = 
                (result.CriticalityScore.RequiredFieldsScore * 0.7) +
                (result.CriticalityScore.OptionalFieldsScore * 0.2) +
                (result.CriticalityScore.EnhancementFieldsScore * 0.1);

            // Set retry and manual review flags
            result.RequiresRetry = !result.IsValid;
            result.RequiresManualReview = result.ComplianceScore < 0.8; // Less than 80% completion

            return result;
        }
    }
}