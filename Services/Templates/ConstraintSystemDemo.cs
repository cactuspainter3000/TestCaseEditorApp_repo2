using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Demo service showcasing Hard/Soft Constraint System integration
    /// Provides examples of constraint processing with graceful degradation
    /// </summary>
    public class ConstraintSystemDemo
    {
        private readonly ITemplateConstraintService _templateConstraintService;
        private readonly ICapabilityDerivationTemplateService _templateService;

        public ConstraintSystemDemo(
            ITemplateConstraintService templateConstraintService,
            ICapabilityDerivationTemplateService templateService)
        {
            _templateConstraintService = templateConstraintService;
            _templateService = templateService;
        }

        /// <summary>
        /// Demonstrate hard constraint rejection scenario
        /// </summary>
        public async Task<ConstraintDemoResult> DemoHardConstraintRejectionAsync()
        {
            var template = CreateDemoTemplate();
            
            // Simulate LLM response with hard constraint violations (missing required fields)
            var hardViolationResponse = @"
System Capability: // Missing - this is REQUIRED
Your Response: [EMPTY]

Taxonomy Category: Z  // Invalid - only A-N allowed (HardReject constraint)
Your Response: Z

Derivation Rationale: [INSUFFICIENT_INFO]  // Required field missing
Your Response: [INSUFFICIENT_INFO]
";

            var result = await _templateConstraintService.ProcessWithConstraintsAsync(
                hardViolationResponse, template);

            return new ConstraintDemoResult
            {
                Scenario = "Hard Constraint Rejection",
                InputResponse = hardViolationResponse,
                ProcessingResult = result,
                ExpectedBehavior = "Should be rejected due to missing required fields and invalid taxonomy",
                ActualBehavior = $"Status: {result.ProcessingStatus}, Decision: {result.ProcessingDecision}",
                Success = result.ProcessingStatus == TemplateProcessingStatus.Rejected
            };
        }

        /// <summary>
        /// Demonstrate soft constraint retry scenario
        /// </summary>
        public async Task<ConstraintDemoResult> DemoSoftConstraintRetryAsync()
        {
            var template = CreateDemoTemplate();
            
            // Simulate LLM response with soft constraint violations
            var softViolationResponse = @"
System Capability:
Your Response: The system provides data processing

Taxonomy Category:
Your Response: A

Derivation Rationale:
Your Response: Briefly mentioned

Confidence Level:  
Your Response: 2.5   // Out of range 0-1 (SoftRetry constraint)
";

            var strategy = new DegradationStrategy
            {
                StrategyName = "Demo",
                MaxRetryAttempts = 2,
                FallbackBehavior = ConstraintFallbackBehavior.AcceptPartial
            };

            var result = await _templateConstraintService.ProcessWithConstraintsAsync(
                softViolationResponse, template, strategy);

            return new ConstraintDemoResult
            {
                Scenario = "Soft Constraint Retry",
                InputResponse = softViolationResponse,
                ProcessingResult = result,
                ExpectedBehavior = "Should trigger retry due to confidence level out of range",
                ActualBehavior = $"Status: {result.ProcessingStatus}, Violations: {result.TotalViolations}",
                Success = result.ProcessingStatus == TemplateProcessingStatus.RequiresRetry
            };
        }

        /// <summary>
        /// Demonstrate flag-only scenario with graceful degradation
        /// </summary>
        public async Task<ConstraintDemoResult> DemoFlagOnlyScenarioAsync()
        {
            var template = CreateDemoTemplateWithFlagOnly();
            
            // Response with minor quality issues that should be flagged but not rejected
            var flagOnlyResponse = @"
System Capability:
Your Response: The system shall provide basic data handling functionality

Taxonomy Category:
Your Response: B

Derivation Rationale:
Your Response: ATP step requires data management so system needs this capability

Confidence Level:
Your Response: 0.8

Technical Details:  // FlagOnly constraint - quality could be better
Your Response: Some details provided but could be more comprehensive
";

            var result = await _templateConstraintService.ProcessWithConstraintsAsync(
                flagOnlyResponse, template);

            return new ConstraintDemoResult
            {
                Scenario = "Flag-Only Graceful Degradation",
                InputResponse = flagOnlyResponse,
                ProcessingResult = result,
                ExpectedBehavior = "Should accept with flagged issues for monitoring",
                ActualBehavior = $"Status: {result.ProcessingStatus}, Flagged Issues: {result.ConstraintProcessing?.FlaggedIssues.Count ?? 0}",
                Success = result.ProcessingStatus == TemplateProcessingStatus.Completed || 
                         result.ProcessingStatus == TemplateProcessingStatus.AcceptedWithDegradation
            };
        }

        /// <summary>
        /// Demonstrate complete retry cycle with resolution
        /// </summary>
        public async Task<ConstraintDemoResult> DemoRetryCycleWithResolutionAsync()
        {
            var template = CreateDemoTemplate();
            
            // First response with issues
            var initialResponse = @"
System Capability:
Your Response: Data handling

Taxonomy Category: 
Your Response: A

Derivation Rationale:
Your Response: Brief rationale

Confidence Level:
Your Response: 2.0  // Will trigger retry
";

            var initialResult = await _templateConstraintService.ProcessWithConstraintsAsync(
                initialResponse, template);

            if (initialResult.ProcessingStatus == TemplateProcessingStatus.RequiresRetry)
            {
                // Simulate LLM correction
                Func<string, Task<string>> mockLlmCall = async (prompt) =>
                {
                    // Return a corrected response
                    return @"
System Capability:
Your Response: The system shall provide comprehensive data processing and management capabilities

Taxonomy Category:
Your Response: A

Derivation Rationale: 
Your Response: The ATP step explicitly requires data processing functionality, therefore the system must be capable of handling, transforming, and managing data efficiently

Confidence Level:
Your Response: 0.9
";
                };

                var retryResult = await _templateConstraintService.ExecuteRetryAsync(initialResult, mockLlmCall);

                return new ConstraintDemoResult
                {
                    Scenario = "Complete Retry Cycle with Resolution",
                    InputResponse = initialResponse,
                    ProcessingResult = retryResult,
                    ExpectedBehavior = "Should successfully retry and resolve constraint violations",
                    ActualBehavior = $"Initial: {initialResult.ProcessingStatus}, After Retry: {retryResult.ProcessingStatus}",
                    Success = retryResult.IsSuccessful && retryResult.TotalViolations < initialResult.TotalViolations,
                    RetryData = new RetryDemoData
                    {
                        InitialViolations = initialResult.TotalViolations,
                        FinalViolations = retryResult.TotalViolations,
                        RetryAttempts = retryResult.RetryHistory.Count
                    }
                };
            }

            return new ConstraintDemoResult
            {
                Scenario = "Retry Cycle Demo",
                Success = false,
                ActualBehavior = "Initial response did not trigger retry as expected"
            };
        }

        private IFormTemplate CreateDemoTemplate()
        {
            return new FormTemplate
            {
                TemplateName = "Constraint System Demo Template",
                Version = "1.0",
                Fields = new List<IFormField>
                {
                    new FormField
                    {
                        FieldName = "systemCapability",
                        DisplayName = "System Capability",
                        Criticality = FieldCriticality.Required,
                        Type = FieldType.LongText,
                        ConstraintType = FieldConstraintType.HardReject,  // Missing = hard rejection
                        ValidationRule = ValidationRuleFactory.CreateRequiredFieldRule()
                    },
                    new FormField
                    {
                        FieldName = "taxonomyCategory", 
                        DisplayName = "Taxonomy Category",
                        Criticality = FieldCriticality.Required,
                        Type = FieldType.Text,
                        ConstraintType = FieldConstraintType.HardReject,  // Invalid = hard rejection
                        ValidOptions = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N" },
                        ValidationRule = ValidationRuleFactory.CreateTaxonomyRule()
                    },
                    new FormField
                    {
                        FieldName = "derivationRationale",
                        DisplayName = "Derivation Rationale", 
                        Criticality = FieldCriticality.Required,
                        Type = FieldType.LongText,
                        ConstraintType = FieldConstraintType.HardReject,  // Missing = hard rejection
                        ValidationRule = ValidationRuleFactory.CreateRequiredFieldRule()
                    },
                    new FormField
                    {
                        FieldName = "confidenceLevel",
                        DisplayName = "Confidence Level",
                        Criticality = FieldCriticality.Optional,
                        Type = FieldType.Number,
                        ConstraintType = FieldConstraintType.SoftRetry,  // Out of range = retry
                        ValidationRule = ValidationRuleFactory.CreateConfidenceRangeRule()
                    }
                }
            };
        }

        private IFormTemplate CreateDemoTemplateWithFlagOnly()
        {
            var template = CreateDemoTemplate();
            
            // Add a field with FlagOnly constraint
            var flagOnlyField = new FormField
            {
                FieldName = "technicalDetails",
                DisplayName = "Technical Details",
                Criticality = FieldCriticality.Enhancement,
                Type = FieldType.LongText,
                ConstraintType = FieldConstraintType.FlagOnly,  // Quality issues = flag but continue
                ValidationRule = new ValidationRule
                {
                    RuleName = "QualityCheck",
                    ValidateFunc = (value, field) =>
                    {
                        var text = value?.ToString() ?? "";
                        var isHighQuality = text.Length > 50 && text.Contains("specific");
                        
                        return new ValidationResult
                        {
                            IsValid = isHighQuality,
                            Severity = ValidationSeverity.Warning,
                            ErrorMessage = isHighQuality ? null : "Could provide more specific technical details"
                        };
                    }
                }
            };
            
            template.Fields.Add(flagOnlyField);
            return template;
        }
    }

    /// <summary>
    /// Result of constraint system demonstration
    /// </summary>
    public class ConstraintDemoResult
    {
        public string Scenario { get; set; } = string.Empty;
        public string InputResponse { get; set; } = string.Empty;
        public TemplateConstraintResult? ProcessingResult { get; set; }
        public string ExpectedBehavior { get; set; } = string.Empty;
        public string ActualBehavior { get; set; } = string.Empty;
        public bool Success { get; set; }
        public RetryDemoData? RetryData { get; set; }
    }

    /// <summary>
    /// Additional data for retry demonstrations
    /// </summary>
    public class RetryDemoData
    {
        public int InitialViolations { get; set; }
        public int FinalViolations { get; set; }
        public int RetryAttempts { get; set; }
        public double ImprovementScore => InitialViolations > 0 ? (double)(InitialViolations - FinalViolations) / InitialViolations : 0.0;
    }
}