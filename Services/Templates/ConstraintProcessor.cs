using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Interface for Hard/Soft Constraint Processing System
    /// Handles graceful degradation based on field constraint types
    /// </summary>
    public interface IConstraintProcessor
    {
        /// <summary>
        /// Process constraints and determine system action based on violations
        /// </summary>
        Task<ConstraintProcessingResult> ProcessConstraintsAsync(IFilledForm filledForm, IFormTemplate template);
        
        /// <summary>
        /// Execute retry logic for SoftRetry constraints
        /// </summary>
        Task<RetryResult> ExecuteRetryLogicAsync(IFilledForm originalForm, IFormTemplate template, List<ConstraintViolation> softViolations);
        
        /// <summary>
        /// Generate constraint violation report
        /// </summary>
        ConstraintViolationReport GenerateViolationReport(List<ConstraintViolation> violations);
    }

    /// <summary>
    /// Hard/Soft Constraint Processing Service
    /// Implements graceful degradation strategy based on constraint severity
    /// </summary>
    public sealed class ConstraintProcessor : IConstraintProcessor
    {
        private readonly IConstraintRuleEngine _ruleEngine;

        public ConstraintProcessor(IConstraintRuleEngine ruleEngine)
        {
            _ruleEngine = ruleEngine;
        }

        public async Task<ConstraintProcessingResult> ProcessConstraintsAsync(IFilledForm filledForm, IFormTemplate template)
        {
            var result = new ConstraintProcessingResult
            {
                ProcessingId = Guid.NewGuid().ToString(),
                ProcessedAt = DateTime.UtcNow,
                OriginalForm = filledForm,
                Template = template
            };

            // Analyze all field constraints
            var violations = new List<ConstraintViolation>();

            foreach (var field in template.Fields)
            {
                var fieldValue = filledForm.FieldValues.ContainsKey(field.FieldName) 
                    ? filledForm.FieldValues[field.FieldName] 
                    : null;

                var violation = await _ruleEngine.EvaluateConstraintAsync(field, fieldValue);
                if (violation != null)
                {
                    violations.Add(violation);
                }
            }

            // Categorize violations by constraint type
            var hardRejects = violations.Where(v => v.ConstraintType == FieldConstraintType.HardReject).ToList();
            var softRetries = violations.Where(v => v.ConstraintType == FieldConstraintType.SoftRetry).ToList();
            var flaggedIssues = violations.Where(v => v.ConstraintType == FieldConstraintType.FlagOnly).ToList();

            result.HardViolations = hardRejects;
            result.SoftViolations = softRetries;
            result.FlaggedIssues = flaggedIssues;

            // Determine processing action
            if (hardRejects.Any())
            {
                result.ProcessingAction = ConstraintProcessingAction.Reject;
                result.ProcessingStatus = ConstraintProcessingStatus.Rejected;
                result.ActionReason = $"Hard constraint violations: {string.Join(", ", hardRejects.Select(h => h.FieldName))}";
            }
            else if (softRetries.Any())
            {
                result.ProcessingAction = ConstraintProcessingAction.Retry;
                result.ProcessingStatus = ConstraintProcessingStatus.RequiresRetry;
                result.ActionReason = $"Soft constraint violations requiring retry: {string.Join(", ", softRetries.Select(s => s.FieldName))}";
            }
            else
            {
                result.ProcessingAction = ConstraintProcessingAction.Accept;
                result.ProcessingStatus = flaggedIssues.Any() 
                    ? ConstraintProcessingStatus.AcceptedWithFlags 
                    : ConstraintProcessingStatus.Accepted;
                result.ActionReason = flaggedIssues.Any() 
                    ? $"Accepted with flagged issues: {string.Join(", ", flaggedIssues.Select(f => f.FieldName))}"
                    : "All constraints satisfied";
            }

            // Generate violation report
            result.ViolationReport = GenerateViolationReport(violations);

            return result;
        }

        public async Task<RetryResult> ExecuteRetryLogicAsync(IFilledForm originalForm, IFormTemplate template, List<ConstraintViolation> softViolations)
        {
            var retryResult = new RetryResult
            {
                RetryId = Guid.NewGuid().ToString(),
                InitiatedAt = DateTime.UtcNow,
                OriginalForm = originalForm,
                ViolationsToResolve = softViolations
            };

            // Generate enhanced prompt for LLM retry
            var retryPrompt = GenerateRetryPrompt(originalForm, template, softViolations);
            retryResult.RetryPrompt = retryPrompt;

            // Create retry recommendations
            var recommendations = new List<RetryRecommendation>();
            
            foreach (var violation in softViolations)
            {
                recommendations.Add(new RetryRecommendation
                {
                    FieldName = violation.FieldName,
                    Issue = violation.Description,
                    RecommendedAction = GenerateRetryRecommendation(violation),
                    Priority = violation.Severity == ValidationSeverity.Error ? RetryPriority.High : RetryPriority.Medium
                });
            }

            retryResult.Recommendations = recommendations;
            retryResult.Status = RetryStatus.Ready;

            return retryResult;
        }

        public ConstraintViolationReport GenerateViolationReport(List<ConstraintViolation> violations)
        {
            return new ConstraintViolationReport
            {
                ReportId = Guid.NewGuid().ToString(),
                GeneratedAt = DateTime.UtcNow,
                TotalViolations = violations.Count,
                HardRejectCount = violations.Count(v => v.ConstraintType == FieldConstraintType.HardReject),
                SoftRetryCount = violations.Count(v => v.ConstraintType == FieldConstraintType.SoftRetry),
                FlagOnlyCount = violations.Count(v => v.ConstraintType == FieldConstraintType.FlagOnly),
                Violations = violations,
                Summary = GenerateViolationSummary(violations)
            };
        }

        private string GenerateRetryPrompt(IFilledForm originalForm, IFormTemplate template, List<ConstraintViolation> violations)
        {
            var prompt = new System.Text.StringBuilder();
            prompt.AppendLine("CONSTRAINT VIOLATION RETRY");
            prompt.AppendLine("========================");
            prompt.AppendLine();
            prompt.AppendLine("Your previous response had constraint violations that require correction:");
            prompt.AppendLine();

            foreach (var violation in violations)
            {
                prompt.AppendLine($"❌ {violation.FieldName}: {violation.Description}");
                prompt.AppendLine($"   Recommendation: {GenerateRetryRecommendation(violation)}");
                prompt.AppendLine();
            }

            prompt.AppendLine("Please provide corrected values for the flagged fields while maintaining all other field values.");
            prompt.AppendLine();
            prompt.AppendLine("ORIGINAL RESPONSE FOR REFERENCE:");
            prompt.AppendLine("================================");

            foreach (var field in template.Fields.Where(f => originalForm.FieldValues.ContainsKey(f.FieldName)))
            {
                var value = originalForm.FieldValues[field.FieldName];
                var hasViolation = violations.Any(v => v.FieldName == field.FieldName);
                var status = hasViolation ? "❌ NEEDS CORRECTION" : "✅ Keep as-is";
                
                prompt.AppendLine($"{field.DisplayName}: {value} ({status})");
            }

            return prompt.ToString();
        }

        private string GenerateRetryRecommendation(ConstraintViolation violation)
        {
            return violation.ViolationType switch
            {
                ConstraintViolationType.MissingRequired => "Provide a complete response for this required field",
                ConstraintViolationType.FormatValidation => $"Follow the expected format: {violation.ExpectedFormat}",
                ConstraintViolationType.ValueOutOfRange => $"Provide a value within the valid range: {violation.ExpectedRange}",
                ConstraintViolationType.InvalidOption => $"Select from valid options: {string.Join(", ", violation.ValidOptions ?? new string[0])}",
                ConstraintViolationType.QualityThreshold => "Improve response quality and completeness",
                _ => "Review and correct the field value based on field requirements"
            };
        }

        private string GenerateViolationSummary(List<ConstraintViolation> violations)
        {
            var summary = new System.Text.StringBuilder();
            
            var grouped = violations.GroupBy(v => v.ConstraintType).ToList();
            
            foreach (var group in grouped)
            {
                var constraintType = group.Key;
                var count = group.Count();
                var fields = string.Join(", ", group.Select(v => v.FieldName));
                
                summary.AppendLine($"{constraintType}: {count} violation(s) in fields [{fields}]");
            }

            return summary.ToString();
        }
    }

    /// <summary>
    /// Constraint rule evaluation engine
    /// </summary>
    public interface IConstraintRuleEngine
    {
        Task<ConstraintViolation?> EvaluateConstraintAsync(IFormField field, object? fieldValue);
    }

    public class ConstraintRuleEngine : IConstraintRuleEngine
    {
        public async Task<ConstraintViolation?> EvaluateConstraintAsync(IFormField field, object? fieldValue)
        {
            // Check field criticality vs value presence
            if (field.Criticality == FieldCriticality.Required && IsEmpty(fieldValue))
            {
                return new ConstraintViolation
                {
                    FieldName = field.FieldName,
                    ConstraintType = field.ConstraintType,
                    ViolationType = ConstraintViolationType.MissingRequired,
                    Description = $"Required field '{field.DisplayName}' is missing or empty",
                    Severity = ValidationSeverity.Error
                };
            }

            // Run field validation rule if value exists
            if (!IsEmpty(fieldValue))
            {
                var validationResult = field.ValidationRule.Validate(fieldValue!, field);
                if (!validationResult.IsValid)
                {
                    var violationType = DetermineViolationType(validationResult);
                    
                    return new ConstraintViolation
                    {
                        FieldName = field.FieldName,
                        ConstraintType = field.ConstraintType,
                        ViolationType = violationType,
                        Description = validationResult.ErrorMessage ?? $"Validation failed for field '{field.DisplayName}'",
                        Severity = validationResult.Severity,
                        ExpectedFormat = validationResult.SuggestedFix,
                        ValidOptions = field.ValidOptions?.Length > 0 ? field.ValidOptions : null
                    };
                }
            }

            return null; // No violation
        }

        private bool IsEmpty(object? value)
        {
            return value == null || string.IsNullOrWhiteSpace(value.ToString());
        }

        private ConstraintViolationType DetermineViolationType(ValidationResult validationResult)
        {
            var errorMessage = validationResult.ErrorMessage?.ToLower() ?? "";
            
            if (errorMessage.Contains("format") || errorMessage.Contains("pattern"))
                return ConstraintViolationType.FormatValidation;
            if (errorMessage.Contains("range") || errorMessage.Contains("between"))
                return ConstraintViolationType.ValueOutOfRange;
            if (errorMessage.Contains("option") || errorMessage.Contains("valid"))
                return ConstraintViolationType.InvalidOption;
            if (errorMessage.Contains("quality") || errorMessage.Contains("threshold"))
                return ConstraintViolationType.QualityThreshold;
                
            return ConstraintViolationType.GeneralValidation;
        }
    }
}