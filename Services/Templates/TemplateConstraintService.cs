using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Interface for Template Form Constraint Integration Service
    /// Orchestrates template processing with hard/soft constraint handling
    /// </summary>
    public interface ITemplateConstraintService
    {
        /// <summary>
        /// Process template form with integrated constraint validation and graceful degradation
        /// </summary>
        Task<TemplateConstraintResult> ProcessWithConstraintsAsync(
            string llmResponse, 
            IFormTemplate template, 
            DegradationStrategy? strategy = null);
        
        /// <summary>
        /// Execute retry cycle for soft constraint violations
        /// </summary>
        Task<TemplateConstraintResult> ExecuteRetryAsync(
            TemplateConstraintResult previousResult, 
            Func<string, Task<string>> llmCallAsync);
        
        /// <summary>
        /// Apply graceful degradation based on constraint violations
        /// </summary>
        Task<TemplateConstraintResult> ApplyGracefulDegradationAsync(
            TemplateConstraintResult result, 
            DegradationStrategy strategy);
    }

    /// <summary>
    /// Template Form Constraint Integration Service
    /// Coordinates template processing, constraint validation, and graceful degradation
    /// </summary>
    public sealed class TemplateConstraintService : ITemplateConstraintService
    {
        private readonly ICapabilityDerivationTemplateService _templateService;
        private readonly IConstraintProcessor _constraintProcessor;
        private readonly IConstraintMetricsCollector _metricsCollector;

        public TemplateConstraintService(
            ICapabilityDerivationTemplateService templateService,
            IConstraintProcessor constraintProcessor,
            IConstraintMetricsCollector metricsCollector)
        {
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _constraintProcessor = constraintProcessor ?? throw new ArgumentNullException(nameof(constraintProcessor));
            _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        }

        public async Task<TemplateConstraintResult> ProcessWithConstraintsAsync(
            string llmResponse, 
            IFormTemplate template, 
            DegradationStrategy? strategy = null)
        {
            var startTime = DateTime.UtcNow;
            var processingId = Guid.NewGuid().ToString();
            
            strategy ??= GetDefaultDegradationStrategy();

            var result = new TemplateConstraintResult
            {
                ProcessingId = processingId,
                StartedAt = startTime,
                Template = template,
                Strategy = strategy,
                OriginalLlmResponse = llmResponse
            };

            try
            {
                // Step 1: Parse LLM response using standard template service
                var filledForm = _templateService.ParseFormResponse(llmResponse, template);
                result.ParsedForm = filledForm;
                result.ParsingSucceeded = true;

                // Step 2: Process constraints 
                var constraintResult = await _constraintProcessor.ProcessConstraintsAsync(filledForm, template);
                result.ConstraintProcessing = constraintResult;

                // Step 3: Determine final processing decision
                await DetermineProcessingDecisionAsync(result);

                // Step 4: Apply degradation strategy if needed
                if (result.RequiresDegradation)
                {
                    result = await ApplyGracefulDegradationAsync(result, strategy);
                }

                result.ProcessingStatus = GetFinalProcessingStatus(result);
                
            }
            catch (Exception ex)
            {
                result.ProcessingStatus = TemplateProcessingStatus.Failed;
                result.ErrorMessage = ex.Message;
                result.Exception = ex;
            }
            finally
            {
                result.CompletedAt = DateTime.UtcNow;
                result.TotalProcessingTime = result.CompletedAt - startTime;
                
                // Collect metrics
                await _metricsCollector.CollectMetricsAsync(result);
            }

            return result;
        }

        public async Task<TemplateConstraintResult> ExecuteRetryAsync(
            TemplateConstraintResult previousResult, 
            Func<string, Task<string>> llmCallAsync)
        {
            if (previousResult.ConstraintProcessing?.SoftViolations == null || 
                !previousResult.ConstraintProcessing.SoftViolations.Any())
            {
                throw new InvalidOperationException("No soft violations to retry");
            }

            var retryCount = previousResult.RetryHistory.Count + 1;
            var maxRetries = previousResult.Strategy?.MaxRetryAttempts ?? 3;

            if (retryCount > maxRetries)
            {
                throw new InvalidOperationException($"Maximum retry attempts ({maxRetries}) exceeded");
            }

            // Generate retry prompt
            var retryResult = await _constraintProcessor.ExecuteRetryLogicAsync(
                previousResult.ParsedForm!, 
                previousResult.Template, 
                previousResult.ConstraintProcessing.SoftViolations);

            // Execute LLM call with retry prompt
            var retryResponse = await llmCallAsync(retryResult.RetryPrompt);
            
            // Record retry attempt
            var retryRecord = new RetryRecord
            {
                AttemptNumber = retryCount,
                InitiatedAt = DateTime.UtcNow,
                ViolationsAddressed = previousResult.ConstraintProcessing.SoftViolations.Count,
                RetryPrompt = retryResult.RetryPrompt,
                RetryResponse = retryResponse
            };

            // Process the retry response
            var newResult = await ProcessWithConstraintsAsync(retryResponse, previousResult.Template, previousResult.Strategy);
            
            // Link to previous result
            newResult.PreviousResult = previousResult;
            newResult.IsRetryAttempt = true;
            newResult.RetryHistory.AddRange(previousResult.RetryHistory);
            newResult.RetryHistory.Add(retryRecord);

            return newResult;
        }

        public async Task<TemplateConstraintResult> ApplyGracefulDegradationAsync(
            TemplateConstraintResult result, 
            DegradationStrategy strategy)
        {
            var degradationStartTime = DateTime.UtcNow;
            
            result.DegradationApplied = true;
            result.DegradationStartedAt = degradationStartTime;

            switch (strategy.FallbackBehavior)
            {
                case ConstraintFallbackBehavior.AcceptPartial:
                    await ApplyPartialAcceptanceAsync(result);
                    break;
                    
                case ConstraintFallbackBehavior.UseDefaults:
                    await ApplyDefaultValuesAsync(result);
                    break;
                    
                case ConstraintFallbackBehavior.EscalateToHuman:
                    await EscalateToHumanAsync(result);
                    break;
                    
                case ConstraintFallbackBehavior.Reject:
                    result.ProcessingStatus = TemplateProcessingStatus.Rejected;
                    break;
            }

            result.DegradationCompletedAt = DateTime.UtcNow;
            result.DegradationTime = result.DegradationCompletedAt.Value - degradationStartTime;

            return result;
        }

        private async Task DetermineProcessingDecisionAsync(TemplateConstraintResult result)
        {
            var constraintResult = result.ConstraintProcessing!;
            
            if (constraintResult.HardViolations.Any())
            {
                result.ProcessingDecision = TemplateProcessingDecision.Reject;
                result.RequiresDegradation = true;
                result.DecisionReason = "Hard constraint violations detected";
            }
            else if (constraintResult.SoftViolations.Any())
            {
                var strategy = result.Strategy!;
                var shouldRetry = result.RetryHistory.Count < strategy.MaxRetryAttempts;
                
                if (shouldRetry)
                {
                    result.ProcessingDecision = TemplateProcessingDecision.Retry;
                    result.RequiresDegradation = false;
                    result.DecisionReason = "Soft violations detected - retry recommended";
                }
                else
                {
                    result.ProcessingDecision = TemplateProcessingDecision.AcceptWithDegradation;
                    result.RequiresDegradation = true;
                    result.DecisionReason = "Maximum retries reached - applying degradation";
                }
            }
            else
            {
                result.ProcessingDecision = TemplateProcessingDecision.Accept;
                result.RequiresDegradation = false;
                result.DecisionReason = constraintResult.FlaggedIssues.Any() 
                    ? "Accepted with minor flagged issues" 
                    : "All constraints satisfied";
            }
        }

        private async Task ApplyPartialAcceptanceAsync(TemplateConstraintResult result)
        {
            // Accept the form as-is but flag issues for monitoring
            result.ProcessingStatus = TemplateProcessingStatus.AcceptedWithDegradation;
            result.DegradationActions.Add("Accepted partial completion with flagged constraint violations");
            
            // Generate quality report for monitoring
            var qualityReport = GenerateQualityReport(result);
            result.QualityReport = qualityReport;
        }

        private async Task ApplyDefaultValuesAsync(TemplateConstraintResult result)
        {
            // Fill missing required fields with defaults where possible
            var filledWithDefaults = 0;
            
            foreach (var field in result.Template.Fields.Where(f => f.Criticality == FieldCriticality.Required))
            {
                if (!result.ParsedForm!.FieldValues.ContainsKey(field.FieldName))
                {
                    var defaultValue = GetDefaultValueForField(field);
                    if (defaultValue != null)
                    {
                        result.ParsedForm.FieldValues[field.FieldName] = defaultValue;
                        filledWithDefaults++;
                    }
                }
            }
            
            result.ProcessingStatus = TemplateProcessingStatus.AcceptedWithDegradation;
            result.DegradationActions.Add($"Applied default values to {filledWithDefaults} fields");
        }

        private async Task EscalateToHumanAsync(TemplateConstraintResult result)
        {
            result.ProcessingStatus = TemplateProcessingStatus.EscalatedToHuman;
            result.DegradationActions.Add("Escalated to human review due to constraint violations");
            
            // Generate human-readable summary of issues
            var escalationSummary = GenerateEscalationSummary(result);
            result.EscalationSummary = escalationSummary;
        }

        private string GenerateQualityReport(TemplateConstraintResult result)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("QUALITY ASSESSMENT REPORT");
            report.AppendLine("========================");
            report.AppendLine();
            
            var violations = result.ConstraintProcessing?.HardViolations.Concat(
                result.ConstraintProcessing?.SoftViolations ?? new List<ConstraintViolation>()).ToList() ?? new();
                
            foreach (var violation in violations)
            {
                report.AppendLine($"⚠️  {violation.FieldName}: {violation.Description}");
            }
            
            return report.ToString();
        }

        private object? GetDefaultValueForField(IFormField field)
        {
            return field.Type switch
            {
                FieldType.Text => field.Placeholder.StartsWith("Enter") ? "Not specified" : field.Placeholder,
                FieldType.LongText => "Information not available",
                FieldType.Boolean => false,
                FieldType.Number => 0,
                FieldType.Scale => 1,
                FieldType.Date => DateTime.Today,
                _ => null
            };
        }

        private string GenerateEscalationSummary(TemplateConstraintResult result)
        {
            var summary = new System.Text.StringBuilder();
            summary.AppendLine("HUMAN REVIEW REQUIRED");
            summary.AppendLine("====================");
            summary.AppendLine();
            summary.AppendLine($"Processing ID: {result.ProcessingId}");
            summary.AppendLine($"Template: {result.Template.TemplateName}");
            summary.AppendLine();
            summary.AppendLine("ISSUES REQUIRING ATTENTION:");
            
            var allViolations = new List<ConstraintViolation>();
            if (result.ConstraintProcessing != null)
            {
                allViolations.AddRange(result.ConstraintProcessing.HardViolations);
                allViolations.AddRange(result.ConstraintProcessing.SoftViolations);
            }
            
            foreach (var violation in allViolations)
            {
                summary.AppendLine($"- {violation.FieldName}: {violation.Description}");
            }
            
            return summary.ToString();
        }

        private DegradationStrategy GetDefaultDegradationStrategy()
        {
            return new DegradationStrategy
            {
                StrategyName = "Default",
                MinAcceptableQuality = 0.7,
                RetryQualityThreshold = 0.5,
                MaxRetryAttempts = 3,
                FallbackBehavior = ConstraintFallbackBehavior.AcceptPartial
            };
        }

        private TemplateProcessingStatus GetFinalProcessingStatus(TemplateConstraintResult result)
        {
            if (result.Exception != null)
                return TemplateProcessingStatus.Failed;
                
            if (!result.ParsingSucceeded)
                return TemplateProcessingStatus.ParsingFailed;
                
            return result.ProcessingDecision switch
            {
                TemplateProcessingDecision.Accept => TemplateProcessingStatus.Completed,
                TemplateProcessingDecision.Retry => TemplateProcessingStatus.RequiresRetry,
                TemplateProcessingDecision.Reject => TemplateProcessingStatus.Rejected,
                TemplateProcessingDecision.AcceptWithDegradation => TemplateProcessingStatus.AcceptedWithDegradation,
                _ => TemplateProcessingStatus.InProgress
            };
        }
    }

    /// <summary>
    /// Metrics collection service for constraint processing performance
    /// </summary>
    public interface IConstraintMetricsCollector
    {
        Task CollectMetricsAsync(TemplateConstraintResult result);
        Task<ConstraintProcessingMetrics> GetMetricsAsync(string processingId);
        Task<List<ConstraintProcessingMetrics>> GetMetricsSummaryAsync(DateTime fromDate, DateTime toDate);
    }

    public class ConstraintMetricsCollector : IConstraintMetricsCollector
    {
        private readonly Dictionary<string, ConstraintProcessingMetrics> _metricsCache = new();

        public async Task CollectMetricsAsync(TemplateConstraintResult result)
        {
            var metrics = new ConstraintProcessingMetrics
            {
                MetricsId = result.ProcessingId,
                TotalProcessingTime = result.TotalProcessingTime,
                TotalFieldsEvaluated = result.Template.Fields.Count,
                RetryAttemptsRequired = result.RetryHistory.Count
            };

            if (result.ConstraintProcessing != null)
            {
                var allViolations = result.ConstraintProcessing.HardViolations
                    .Concat(result.ConstraintProcessing.SoftViolations)
                    .Concat(result.ConstraintProcessing.FlaggedIssues);
                
                metrics.TotalViolationsFound = allViolations.Count();
                metrics.ViolationTypeBreakdown = allViolations
                    .GroupBy(v => v.ViolationType)
                    .ToDictionary(g => g.Key, g => g.Count());
                    
                var totalFields = result.Template.Fields.Count;
                var violatedFields = allViolations.Select(v => v.FieldName).Distinct().Count();
                metrics.OverallComplianceRate = totalFields > 0 ? (double)(totalFields - violatedFields) / totalFields : 1.0;
            }

            _metricsCache[result.ProcessingId] = metrics;
        }

        public async Task<ConstraintProcessingMetrics> GetMetricsAsync(string processingId)
        {
            return _metricsCache.TryGetValue(processingId, out var metrics) 
                ? metrics 
                : throw new KeyNotFoundException($"No metrics found for processing ID: {processingId}");
        }

        public async Task<List<ConstraintProcessingMetrics>> GetMetricsSummaryAsync(DateTime fromDate, DateTime toDate)
        {
            return _metricsCache.Values
                .Where(m => m.CollectedAt >= fromDate && m.CollectedAt <= toDate)
                .ToList();
        }
    }
}