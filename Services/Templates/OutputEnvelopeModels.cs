using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Types of output envelopes for different LLM response categories
    /// </summary>
    public enum EnvelopeType
    {
        RequirementGeneration,  // Generated requirement responses
        TestCaseGeneration,     // Test case generation responses
        AnalysisResponse,       // Analysis and evaluation responses
        GeneralStructured,      // General structured data responses
        ErrorResponse          // Error and fallback responses
    }

    /// <summary>
    /// Validation severity levels for envelope parsing
    /// </summary>
    public enum EnvelopeValidationSeverity
    {
        Critical,              // Structure completely invalid
        Major,                 // Missing required fields
        Minor,                 // Optional field issues
        Warning,              // Format suggestions
        Info                  // Informational feedback
    }

    /// <summary>
    /// Repair strategies for malformed envelopes
    /// </summary>
    public enum EnvelopeRepairStrategy
    {
        StrictValidation,     // Fail on any format deviation
        GracefulDegradation,  // Attempt to extract valid portions
        BestEffortRecovery,   // Try multiple parsing strategies
        FallbackToRaw        // Use raw response if parsing fails
    }

    /// <summary>
    /// Models for Deterministic Output Envelope System
    /// Provides structured data containers for standardized LLM output parsing
    /// </summary>
    /// 
    /// <summary>
    /// Structured envelope containing LLM response with metadata
    /// </summary>
    public class OutputEnvelope
    {
        public string EnvelopeId { get; set; } = string.Empty;
        public EnvelopeType Type { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0";
        
        // Core response data
        public string ResponseId { get; set; } = string.Empty;
        public JsonDocument? StructuredData { get; set; }
        public string RawResponse { get; set; } = string.Empty;
        
        // Quality and validation metadata
        public EnvelopeValidationResult ValidationResult { get; set; } = new();
        public double ConfidenceScore { get; set; }
        public double CompletenessScore { get; set; }
        
        // Processing metadata
        public List<string> ProcessingWarnings { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string ProcessingContext { get; set; } = string.Empty;
    }

    /// <summary>
    /// Schema definition for envelope structure validation
    /// </summary>
    public class EnvelopeSchema
    {
        public string SchemaName { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0";
        public string Description { get; set; } = string.Empty;
                public EnvelopeType TargetEnvelopeType { get; set; } = EnvelopeType.GeneralStructured;
        public bool AllowCustomFields { get; set; } = false;
                public List<EnvelopeField> RequiredFields { get; set; } = new();
        public List<EnvelopeField> OptionalFields { get; set; } = new();
        public List<EnvelopeValidation> ValidationRules { get; set; } = new();
        
        public EnvelopeRepairStrategy DefaultRepairStrategy { get; set; } = EnvelopeRepairStrategy.GracefulDegradation;
        public Dictionary<string, object> SchemaMetadata { get; set; } = new();
    }

    /// <summary>
    /// Field definition within envelope schema
    /// </summary>
    public class EnvelopeField
    {
        public string FieldName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string DataType { get; set; } = "string"; // String representation of data type
        public Type ExpectedType { get; set; } = typeof(string);
        public bool IsRequired { get; set; }
        
        public string Description { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        public List<string> AllowedValues { get; set; } = new();
        
        // Validation constraints
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public string? RegexPattern { get; set; }
        public Dictionary<string, object> FieldMetadata { get; set; } = new();
    }

    /// <summary>
    /// Validation rule for envelope content
    /// </summary>
    public class EnvelopeValidation
    {
        public string RuleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public EnvelopeValidationSeverity Severity { get; set; }
        
        public Func<OutputEnvelope, ValidationResult> ValidationFunction { get; set; } = _ => new ValidationResult { IsValid = true };
        public string ErrorMessage { get; set; } = string.Empty;
        public string SuggestedFix { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of envelope parsing operation
    /// </summary>
    public class EnvelopeParseResult
    {
        public bool IsSuccessful { get; set; }
        public OutputEnvelope? ParsedEnvelope { get; set; }
        public string? ErrorMessage { get; set; }
        
        public List<EnvelopeParseWarning> Warnings { get; set; } = new();
        public EnvelopeParseMetrics Metrics { get; set; } = new();
        
        public string? FallbackData { get; set; } // Raw data if structured parsing fails
        public EnvelopeRepairStrategy UsedStrategy { get; set; }
    }

    /// <summary>
    /// Warning during envelope parsing
    /// </summary>
    public class EnvelopeParseWarning
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public EnvelopeValidationSeverity Severity { get; set; }
        public string? SuggestedFix { get; set; }
    }

    /// <summary>
    /// Metrics for envelope parsing performance
    /// </summary>
    public class EnvelopeParseMetrics
    {
        public TimeSpan ParseTime { get; set; }
        public int FieldsParsed { get; set; }
        public int FieldsSkipped { get; set; }
        public int ValidationErrors { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Result of envelope validation against schema
    /// </summary>
    public class EnvelopeValidationResult
    {
        public bool IsValid { get; set; }
        public double ComplianceScore { get; set; } // 0.0 to 1.0
        
        public List<EnvelopeValidationError> Errors { get; set; } = new();
        public List<EnvelopeValidationWarning> Warnings { get; set; } = new();
        
        public Dictionary<string, object> ValidationMetadata { get; set; } = new();
        public EnvelopeValidationSummary Summary { get; set; } = new();
    }

    /// <summary>
    /// Validation error details
    /// </summary>
    public class EnvelopeValidationError
    {
        public string Field { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public EnvelopeValidationSeverity Severity { get; set; }
        public object? ActualValue { get; set; }
        public object? ExpectedValue { get; set; }
        public string? SuggestedFix { get; set; }
    }

    /// <summary>
    /// Validation warning details
    /// </summary>
    public class EnvelopeValidationWarning
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Recommendation { get; set; }
    }

    /// <summary>
    /// Summary of validation results
    /// </summary>
    public class EnvelopeValidationSummary
    {
        public int TotalFields { get; set; }
        public int ValidFields { get; set; }
        public int RequiredFieldsMissing { get; set; }
        public int OptionalFieldsMissing { get; set; }
        public int CriticalErrors { get; set; }
        public int MajorErrors { get; set; }
        public int MinorErrors { get; set; }
        public int Warnings { get; set; }
    }

    /// <summary>
    /// Result of envelope repair operation
    /// </summary>
    public class EnvelopeRepairResult
    {
        public bool RepairSuccessful { get; set; }
        public OutputEnvelope? RepairedEnvelope { get; set; }
        public string? OriginalResponse { get; set; }
        
        public List<EnvelopeRepairAction> ActionsPerformed { get; set; } = new();
        public EnvelopeRepairStrategy StrategyUsed { get; set; }
        public double RepairConfidence { get; set; } // 0.0 to 1.0
        
        public string? FailureReason { get; set; }
        public List<string> RepairWarnings { get; set; } = new();
    }

    /// <summary>
    /// Action performed during envelope repair
    /// </summary>
    public class EnvelopeRepairAction
    {
        public string ActionType { get; set; } = string.Empty; // "FieldExtracted", "DefaultApplied", etc.
        public string Field { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public object? OriginalValue { get; set; }
        public object? RepairedValue { get; set; }
        public double Confidence { get; set; }
    }
}