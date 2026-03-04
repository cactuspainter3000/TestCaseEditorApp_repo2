using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Core enums and models for Template Form Architecture
    /// Based on colleague feedback: formalizing LLM interaction as systems interface
    /// </summary>
    /// 
    // CORE ENUMS
    
    /// <summary>
    /// Field criticality levels - determines system behavior on missing/invalid fields
    /// </summary>
    public enum FieldCriticality
    {
        Required,      // Form invalid without this field - hard failure
        Optional,      // Enhances quality when present - soft failure 
        Enhancement    // Nice-to-have context - flag only
    }

    /// <summary>
    /// Field constraint handling - colleague suggestion for hard vs soft constraints
    /// </summary>
    public enum FieldConstraintType
    {
        HardReject,    // Wrong format = immediate failure and rejection
        SoftRetry,     // Low confidence = automatic retry with feedback
        FlagOnly       // Issues noted but processing continues
    }

    /// <summary>
    /// Supported field input types for template forms
    /// </summary>
    public enum FieldType
    {
        Text,          // Single-line text input
        LongText,      // Multi-line text area  
        Dropdown,      // Single selection from predefined options
        MultiSelect,   // Multiple selections from options
        Scale,         // Numeric scale (e.g. confidence 1-10)
        Boolean,       // Yes/No checkbox
        Date,          // Date picker
        Number         // Numeric input with validation
    }

    /// <summary>
    /// Validation rule severity levels
    /// </summary>
    public enum ValidationSeverity
    {
        Error,         // Blocks form submission
        Warning,       // Flags for review but allows processing 
        Info           // Informational feedback only
    }

    // VALIDATION MODELS

    /// <summary>
    /// Result of field validation with detailed feedback
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public ValidationSeverity Severity { get; set; }
        public string? SuggestedFix { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Validation rule with configurable logic
    /// </summary>
    public class ValidationRule : IValidationRule
    {
        public string RuleName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
        public Func<object, IFormField, ValidationResult>? ValidateFunc { get; set; }

        public ValidationResult Validate(object value, IFormField field)
        {
            if (ValidateFunc == null)
            {
                return new ValidationResult { IsValid = true, Severity = ValidationSeverity.Info };
            }

            try
            {
                return ValidateFunc(value, field);
            }
            catch (Exception ex)
            {
                return new ValidationResult 
                {
                    IsValid = false,
                    ErrorMessage = $"Validation failed: {ex.Message}", 
                    Severity = ValidationSeverity.Error
                };
            }
        }
    }

    // FIELD PERFORMANCE TRACKING
    
    /// <summary>
    /// Tracks LLM performance metrics per field type - colleague suggestion
    /// </summary>
    public class FieldPerformanceMetrics
    {
        public string FieldName { get; set; } = string.Empty;
        public FieldType FieldType { get; set; }
        public double CompletionRate { get; set; }        // % of times field completed
        public double RetryRate { get; set; }             // % requiring retry
        public double LowConfidenceRate { get; set; }     // % flagged as uncertain
        public double AverageConfidence { get; set; }     // Mean confidence score
        public string[] CommonFailurePatterns { get; set; } = Array.Empty<string>();
        public double QualityTrend { get; set; }          // Improving/degrading over time
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public int SampleSize { get; set; }               // Number of observations
    }

    /// <summary>
    /// Aggregated metrics across all template forms
    /// </summary>
    public class TemplateSystemMetrics
    {
        public double OverallComplianceRate { get; set; }
        public double AverageRetryRate { get; set; } 
        public double SelfAuditAccuracy { get; set; }     // How often self-audit matches validation
        public string MostProblematicField { get; set; } = string.Empty;
        public string HighestPerformingField { get; set; } = string.Empty;
        public Dictionary<FieldCriticality, double> CompletionByTier { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    // COMPLIANCE WRAPPER MODELS
    
    /// <summary>
    /// Wraps LLM interaction as engineered system component - key architectural insight
    /// </summary>
    public class LLMSystemInterface
    {
        public string InterfaceName { get; set; } = string.Empty;
        public string ContractVersion { get; set; } = "1.0";
        public Dictionary<string, object> Capabilities { get; set; } = new();
        public List<string> SupportedFormats { get; set; } = new();
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetries { get; set; } = 3;
        public bool EnableSelfAudit { get; set; } = true;
        public double MinimumConfidenceThreshold { get; set; } = 0.7;
    }

    /// <summary>
    /// Circuit breaker pattern for LLM reliability
    /// </summary>
    public class LLMCircuitBreakerState
    {
        public bool IsOpen { get; set; }
        public DateTime LastFailureTime { get; set; }
        public int ConsecutiveFailures { get; set; }
        public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(5);
        public int FailureThreshold { get; set; } = 5;
        public string LastError { get; set; } = string.Empty;
    }
}