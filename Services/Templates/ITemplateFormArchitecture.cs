using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Core interfaces for Template Form Architecture
    /// Defines contracts for structured LLM interaction with guaranteed format compliance
    /// </summary>
    
    /// <summary>
    /// Service interface for capability derivation using template forms - follows existing service patterns
    /// </summary>
    public interface ICapabilityDerivationTemplateService
    {
        /// <summary>
        /// Get the standard capability derivation template
        /// </summary>
        IFormTemplate GetStandardCapabilityTemplate();
        
        /// <summary>
        /// Generate form prompt for LLM
        /// </summary>
        string GenerateFormPrompt(IFormTemplate template, string atpStep, string? ragContext = null);
        
        /// <summary>
        /// Parse LLM response that filled out the form
        /// </summary>
        IFilledForm ParseFormResponse(string llmResponse, IFormTemplate template);
    }
    
    /// <summary>
    /// Service interface for self-auditing capabilities - follows existing service patterns
    /// </summary>
    public interface ISelfAuditingTemplateService 
    {
        /// <summary>
        /// Generate self-audit prompt for LLM validation
        /// </summary>
        string GenerateSelfAuditPrompt(string originalResponse, IFormTemplate template, string atpStep);
        
        /// <summary>
        /// Parse LLM's self-audit response
        /// </summary>
        ISelfAuditResult ParseAuditResponse(string auditResponse);
        
        /// <summary>
        /// Full two-phase process: generate response then self-audit
        /// </summary>
        Task<(string Response, ISelfAuditResult Audit)> GenerateWithSelfAuditAsync(
            string prompt, IFormTemplate template, string atpStep, 
            Func<string, Task<string>> llmCallAsync);
    }

    
    public interface IFormTemplate
    {
        string TemplateName { get; set; }
        string Instructions { get; set; }
        List<IFormField> Fields { get; set; }
        DateTime CreatedAt { get; set; }
        string Version { get; set; }
    }

    public interface IFormField
    {
        string FieldName { get; set; }
        string DisplayName { get; set; }
        FieldCriticality Criticality { get; set; }
        FieldType Type { get; set; }
        FieldConstraintType ConstraintType { get; set; }
        string Instructions { get; set; }
        string Placeholder { get; set; }
        IValidationRule ValidationRule { get; set; }
        string[] ValidOptions { get; set; }
    }

    public interface IValidationRule
    {
        string RuleName { get; set; }
        ValidationResult Validate(object value, IFormField field);
        string ErrorMessage { get; set; }
        ValidationSeverity Severity { get; set; }
    }

    public interface IFilledForm
    {
        Dictionary<string, object> FieldValues { get; set; }
        IFormValidationResult ValidationResult { get; set; }
        DateTime CompletedAt { get; set; }
        string SourceContext { get; set; }
        double CompletionScore { get; set; }
    }

    public interface IFormValidationResult
    {
        bool IsValid { get; set; }
        List<string> FieldViolations { get; set; }
        List<string> WarningMessages { get; set; }
        double ComplianceScore { get; set; }
        ICriticalityScore CriticalityScore { get; set; }
        bool RequiresRetry { get; set; }
        bool RequiresManualReview { get; set; }
    }

    public interface ICriticalityScore
    {
        double RequiredFieldsScore { get; set; }
        double OptionalFieldsScore { get; set; }
        double EnhancementFieldsScore { get; set; }
        double WeightedOverallScore { get; set; }
    }

    public interface ISelfAuditResult
    {
        bool PassedAudit { get; set; }
        double ConfidenceScore { get; set; }
        List<string> IdentifiedIssues { get; set; }
        string RecommendedAction { get; set; }
        Dictionary<string, object> Metadata { get; set; }
    }

    /// <summary>
    /// Template Form Architecture Service interface (Task 6.5 placeholder)
    /// Future implementation planned for comprehensive template form management
    /// </summary>
    public interface ITemplateFormArchitectureService
    {
        // Placeholder interface for future Template Form Architecture integration
    }

    /// <summary>
    /// Constraint Validation Service interface (Task 6.5 placeholder)
    /// Future implementation planned for constraint validation integration
    /// </summary>
    public interface IConstraintValidationService
    {
        /// <summary>
        /// Validates all constraints for a template form
        /// </summary>
        Task<ConstraintValidationResult> ValidateAllConstraintsAsync(IFormTemplate formTemplate, Dictionary<string, object> formData);
    }
}