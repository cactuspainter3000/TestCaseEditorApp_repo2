using System;
using System.Collections.Generic;
using System.Linq;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Concrete implementations of Template Form Architecture interfaces
    /// ARCHITECTURAL COMPLIANCE: Updated to use factory pattern following existing service patterns
    /// </summary>

    public class FormTemplate : IFormTemplate
    {
        public string TemplateName { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public List<IFormField> Fields { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Version { get; set; } = "1.0";

        public FormTemplate()
        {
        }

        public FormTemplate(string templateName, string instructions)
        {
            TemplateName = templateName;
            Instructions = instructions;
        }
    }

    public class FormField : IFormField
    {
        public string FieldName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public FieldCriticality Criticality { get; set; }
        public FieldType Type { get; set; }
        public FieldConstraintType ConstraintType { get; set; }
        public string Instructions { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public IValidationRule ValidationRule { get; set; } = new ValidationRule();
        public string[] ValidOptions { get; set; } = Array.Empty<string>();

        public FormField()
        {
        }

        public FormField(string fieldName, string displayName, FieldCriticality criticality)
        {
            FieldName = fieldName;
            DisplayName = displayName;
            Criticality = criticality;
            Instructions = "Enter value for " + displayName;
            Placeholder = displayName + "...";
        }
    }

    public class CriticalityScore : ICriticalityScore
    {
        public double RequiredFieldsScore { get; set; } = 1.0;
        public double OptionalFieldsScore { get; set; } = 1.0;
        public double EnhancementFieldsScore { get; set; } = 1.0;
        public double WeightedOverallScore { get; set; } = 1.0;
    }

    /// <summary>
    /// Factory for validation rules - follows architectural pattern like LlmFactory
    /// ARCHITECTURAL COMPLIANCE: Replaced static StandardValidationRules with factory pattern
    /// </summary>
    public static class ValidationRuleFactory
    {
        public static IValidationRule CreateRequiredFieldRule() => new ValidationRule
        {
            RuleName = "RequiredField",
            ErrorMessage = "This field is required",
            Severity = ValidationSeverity.Error,
            ValidateFunc = (value, field) =>
            {
                var stringValue = value?.ToString() ?? string.Empty;
                return new ValidationResult
                {
                    IsValid = !string.IsNullOrWhiteSpace(stringValue),
                    Severity = ValidationSeverity.Error
                };
            }
        };

        public static IValidationRule CreateTaxonomyRule() => new ValidationRule
        {
            RuleName = "TaxonomyCategory",
            ErrorMessage = "Must be valid taxonomy category A-N",
            Severity = ValidationSeverity.Error,
            ValidateFunc = (value, field) =>
            {
                var category = value?.ToString()?.ToUpper() ?? string.Empty;
                var validCategories = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N" };
                return new ValidationResult
                {
                    IsValid = validCategories.Contains(category),
                    Severity = ValidationSeverity.Error,
                    SuggestedFix = validCategories.Contains(category) ? null : "Choose from: A, B, C, D, E, F, G, H, I, J, K, L, M, N"
                };
            }
        };

        public static IValidationRule CreateCapabilityFormatRule() => new ValidationRule
        {
            RuleName = "CapabilityFormat",
            ErrorMessage = "Must start with 'The system shall provide...' or 'The system shall be capable of...'",
            Severity = ValidationSeverity.Error,
            ValidateFunc = (value, field) =>
            {
                var capability = value?.ToString() ?? string.Empty;
                var isValid = capability.StartsWith("The system shall provide", StringComparison.OrdinalIgnoreCase) ||
                             capability.StartsWith("The system shall be capable of", StringComparison.OrdinalIgnoreCase);
                
                return new ValidationResult
                {
                    IsValid = isValid,
                    Severity = ValidationSeverity.Error,
                    SuggestedFix = isValid ? null : "Start with 'The system shall provide...' or 'The system shall be capable of...'"
                };
            }
        };

        public static IValidationRule CreateConfidenceRangeRule() => new ValidationRule
        {
            RuleName = "ConfidenceRange",
            ErrorMessage = "Confidence must be between 0.0 and 1.0",
            Severity = ValidationSeverity.Error,
            ValidateFunc = (value, field) =>
            {
                if (double.TryParse(value?.ToString(), out var confidence))
                {
                    return new ValidationResult
                    {
                        IsValid = confidence >= 0.0 && confidence <= 1.0,
                        Severity = ValidationSeverity.Error
                    };
                }
                return new ValidationResult { IsValid = false, ErrorMessage = "Must be a valid number", Severity = ValidationSeverity.Error };
            }
        };
    }
}
