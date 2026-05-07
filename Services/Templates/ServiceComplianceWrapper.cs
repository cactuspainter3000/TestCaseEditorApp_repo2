using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Universal Service Compliance Wrapper - enforces Template Form Architecture standards
    /// 
    /// ARCHITECTURE: Adapter pattern for gradual migration to Template Form Architecture
    /// PURPOSE: Treat LLM as engineered system component with contract, validation, telemetry
    /// 
    /// Task 6.8: Build Compliance Wrapper Interface
    /// </summary>
    public sealed class ServiceComplianceWrapper : IServiceComplianceWrapper
    {
        private readonly IOutputEnvelopeService? _envelopeService;
        private readonly IFieldLevelQualityService? _qualityService;
        private readonly ILogger<ServiceComplianceWrapper> _logger;
        private readonly List<IComplianceMetricsObserver> _observers = new();

        public ServiceComplianceWrapper(
            ILogger<ServiceComplianceWrapper> logger,
            IOutputEnvelopeService? envelopeService = null,
            IFieldLevelQualityService? qualityService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _envelopeService = envelopeService;
            _qualityService = qualityService;

            var integrationMode = _envelopeService != null && _qualityService != null
                ? "Full Template Form Architecture"
                : _envelopeService != null
                    ? "Output Validation Only"
                    : "Basic Compliance (no validation services)";

            _logger.LogInformation(
                "ServiceComplianceWrapper initialized with {IntegrationMode}",
                integrationMode);
        }

        public async Task<ComplianceResult<TResult>> ExecuteWithComplianceAsync<TResult>(
            Func<Task<TResult>> serviceCall,
            ComplianceConfig config) where TResult : class
        {
            var result = new ComplianceResult<TResult>();
            var metadata = result.ExecutionMetadata;
            metadata.OperationName = config.OperationName;
            metadata.StartTime = DateTime.UtcNow;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("🎯 Executing compliance-wrapped service call: {OperationName}", config.OperationName);

                // Execute the actual service call
                var serviceStopwatch = Stopwatch.StartNew();
                var serviceResult = await serviceCall();
                serviceStopwatch.Stop();
                result.QualityMetrics.Performance.ServiceExecutionMs = serviceStopwatch.ElapsedMilliseconds;

                result.OriginalResult = serviceResult;

                // Output validation
                if (config.OutputSchema != null && _envelopeService != null)
                {
                    var validationResult = await ValidateOutputWithSchemaAsync(serviceResult, config.OutputSchema);
                    result.Validation.OutputValid = validationResult.IsValid;
                    result.Validation.EnvelopeValidation = validationResult.EnvelopeValidation;
                    result.Validation.OverallScore = validationResult.ComplianceScore;

                    if (!validationResult.IsValid)
                    {
                        result.Validation.Violations.AddRange(
                            validationResult.Violations.Select(v => new ComplianceViolation
                            {
                                ViolationType = "OutputValidation",
                                Description = v,
                                Severity = ValidationSeverity.Error
                            }));

                        _logger.LogWarning(
                            "⚠️ Output validation failed for {OperationName}. Score: {Score:F2}",
                            config.OperationName,
                            validationResult.ComplianceScore);
                    }
                }
                else
                {
                    result.Validation.OutputValid = true; // No validation configured
                    result.Validation.OverallScore = 1.0;
                }

                // Template validation (if result template provided)
                if (config.ResultTemplate != null)
                {
                    var templateValidation = await ValidateAgainstTemplateAsync(serviceResult, config.ResultTemplate);
                    result.Validation.TemplateValidation = templateValidation;

                    if (!templateValidation.IsValid)
                    {
                        result.Validation.OutputValid = false;
                        result.Validation.OverallScore = Math.Min(result.Validation.OverallScore, templateValidation.ComplianceScore);

                        _logger.LogWarning(
                            "⚠️ Template validation failed for {OperationName}. Compliance: {Score:F2}",
                            config.OperationName,
                            templateValidation.ComplianceScore);
                    }
                }

                // Custom validation rules
                foreach (var rule in config.CustomValidationRules)
                {
                    try
                    {
                        var customResult = await rule.ValidateAsync(new object(), serviceResult);
                        result.Validation.CustomValidations.Add(customResult);

                        if (!customResult.Passed && rule.Severity == ValidationSeverity.Error)
                        {
                            result.Validation.OutputValid = false;
                            result.Validation.Violations.Add(new ComplianceViolation
                            {
                                ViolationType = "CustomValidation",
                                Description = customResult.Message ?? $"Rule '{rule.RuleName}' failed",
                                Severity = rule.Severity
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Custom validation rule '{RuleName}' threw exception", rule.RuleName);
                    }
                }

                // Quality metrics tracking
                if (config.TrackQualityMetrics && _qualityService != null)
                {
                    await TrackQualityMetricsAsync(serviceResult, config, result);
                }

                // Determine success based on compliance score
                var meetsMinimum = result.Validation.OverallScore >= config.MinimumComplianceScore;
                result.Success = meetsMinimum && result.Validation.OutputValid;

                if (result.Success)
                {
                    result.Data = serviceResult;
                    _logger.LogDebug(
                        "✅ Compliance check passed for {OperationName}. Score: {Score:F2}",
                        config.OperationName,
                        result.Validation.OverallScore);
                }
                else
                {
                    // Handle compliance failure based on fallback strategy
                    await HandleComplianceFailureAsync(result, serviceResult, config);
                }

                // Notify observers
                await NotifyObserversAsync(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during compliance-wrapped service call: {OperationName}", config.OperationName);
                result.Success = false;
                result.Errors.Add(new ComplianceError
                {
                    ErrorType = ex.GetType().Name,
                    Message = ex.Message,
                    Exception = ex,
                    StackTrace = ex.StackTrace,
                    Recoverable = false
                });
            }
            finally
            {
                stopwatch.Stop();
                metadata.EndTime = DateTime.UtcNow;
                result.QualityMetrics.Performance.TotalMs = stopwatch.ElapsedMilliseconds;
            }

            return result;
        }

        public async Task<ComplianceResult<TResult>> ExecuteWithInputValidationAsync<TInput, TResult>(
            Func<TInput, Task<TResult>> serviceCall,
            TInput input,
            ComplianceConfig config) where TResult : class
        {
            var result = new ComplianceResult<TResult>();
            var metadata = result.ExecutionMetadata;
            metadata.OperationName = config.OperationName;
            metadata.StartTime = DateTime.UtcNow;

            try
            {
                // Input validation
                if (config.InputTemplate != null)
                {
                    var inputValidationStopwatch = Stopwatch.StartNew();
                    var inputValidation = await ValidateInputAgainstTemplateAsync(input, config.InputTemplate);
                    inputValidationStopwatch.Stop();
                    result.QualityMetrics.Performance.InputValidationMs = inputValidationStopwatch.ElapsedMilliseconds;

                    result.Validation.InputValid = inputValidation.IsValid;

                    if (!inputValidation.IsValid)
                    {
                        result.Success = false;
                        result.Validation.Violations.AddRange(
                            inputValidation.FieldViolations.Select(v => new ComplianceViolation
                            {
                                ViolationType = "InputValidation",
                                Description = v,
                                Severity = ValidationSeverity.Error
                            }));

                        _logger.LogWarning(
                            "⚠️ Input validation failed for {OperationName}",
                            config.OperationName);

                        return result;
                    }
                }

                // Delegate to main compliance execution
                return await ExecuteWithComplianceAsync(() => serviceCall(input), config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during input validation for: {OperationName}", config.OperationName);
                result.Success = false;
                result.Errors.Add(new ComplianceError
                {
                    ErrorType = ex.GetType().Name,
                    Message = ex.Message,
                    Exception = ex,
                    Recoverable = false
                });
                result.ExecutionMetadata.EndTime = DateTime.UtcNow;
                return result;
            }
        }

        public IComplianceAdapter<TService> WrapLegacyService<TService>(
            TService legacyService,
            LegacyServiceConfig<TService> adapterConfig) where TService : class
        {
            _logger.LogInformation(
                "🔧 Creating compliance adapter for legacy service: {ServiceName}",
                adapterConfig.ServiceName);

            return new LegacyServiceComplianceAdapter<TService>(
                legacyService,
                adapterConfig,
                this,
                _logger);
        }

        public async Task<OutputValidationResult> ValidateOutputAsync<TOutput>(
            TOutput output,
            EnvelopeSchema expectedSchema)
        {
            return await ValidateOutputWithSchemaAsync(output, expectedSchema);
        }

        public void RegisterMetricsObserver(IComplianceMetricsObserver observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            
            _observers.Add(observer);
            _logger.LogDebug("📊 Registered compliance metrics observer: {ObserverType}", observer.GetType().Name);
        }

        // Private helper methods

        private async Task<OutputValidationResult> ValidateOutputWithSchemaAsync<TOutput>(
            TOutput output,
            EnvelopeSchema expectedSchema)
        {
            var result = new OutputValidationResult { IsValid = true, ComplianceScore = 1.0 };

            if (_envelopeService == null)
            {
                _logger.LogWarning("⚠️ No OutputEnvelopeService available for validation");
                return result;
            }

            try
            {
                var validationStopwatch = Stopwatch.StartNew();

                // Attempt to serialize output to string for envelope parsing
                var outputString = output?.ToString() ?? string.Empty;

                // Parse and validate envelope
                var parseResult = await _envelopeService.ParseEnvelopeAsync(outputString, expectedSchema);
                validationStopwatch.Stop();

                result.IsValid = parseResult.Success;
                result.EnvelopeValidation = parseResult.ValidationResult;
                result.ComplianceScore = parseResult.ValidationResult?.ComplianceScore ?? 0.0;

                if (!parseResult.Success)
                {
                    result.Violations.AddRange(parseResult.Errors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during output envelope validation");
                result.IsValid = false;
                result.ComplianceScore = 0.0;
                result.Violations.Add($"Validation exception: {ex.Message}");
            }

            return result;
        }

        private async Task<IFormValidationResult> ValidateAgainstTemplateAsync<TOutput>(
            TOutput output,
            IFormTemplate template)
        {
            // Simplified template validation - assumes output is IFilledForm or can be converted
            var result = new FormValidationResult
            {
                IsValid = true,
                ComplianceScore = 1.0
            };

            try
            {
                // If output is already a filled form, validate it
                if (output is IFilledForm filledForm)
                {
                    result = filledForm.ValidationResult as FormValidationResult ?? result;
                }
                else
                {
                    // For non-form outputs, basic validation only
                    _logger.LogDebug("Output is not IFilledForm, skipping detailed template validation");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during template validation");
                result.IsValid = false;
                result.ComplianceScore = 0.0;
            }

            return result;
        }

        private async Task<IFormValidationResult> ValidateInputAgainstTemplateAsync<TInput>(
            TInput input,
            IFormTemplate template)
        {
            // Simplified input validation
            var result = new FormValidationResult
            {
                IsValid = true,
                ComplianceScore = 1.0
            };

            // Placeholder for actual input validation logic
            // Would check if input fields match template requirements
            await Task.CompletedTask;

            return result;
        }

        private async Task TrackQualityMetricsAsync<TResult>(
            TResult serviceResult,
            ComplianceConfig config,
            ComplianceResult<TResult> complianceResult) where TResult : class
        {
            if (_qualityService == null) return;

            try
            {
                var trackingStopwatch = Stopwatch.StartNew();

                // If result is IFilledForm, track field-level metrics
                if (serviceResult is IFilledForm filledForm && config.ResultTemplate != null)
                {
                    var fieldResults = new List<FieldProcessingResult>();

                    foreach (var field in config.ResultTemplate.Fields)
                    {
                        if (filledForm.FieldValues.TryGetValue(field.FieldName, out var value))
                        {
                            var fieldResult = new FieldProcessingResult
                            {
                                FieldName = field.FieldName,
                                TemplateName = config.ResultTemplate.TemplateName,
                                Criticality = field.Criticality,
                                Success = value != null,
                                ConfidenceScore = filledForm.CompletionScore,
                                ProcessingTimeMs = 0, // Not tracked individually
                                SessionId = complianceResult.ExecutionMetadata.SessionId,
                                Timestamp = DateTime.UtcNow
                            };

                            await _qualityService.RecordFieldProcessingResultAsync(fieldResult);
                            fieldResults.Add(fieldResult);
                        }
                    }

                    complianceResult.QualityMetrics.FieldResults = fieldResults;
                    complianceResult.QualityMetrics.TemplateCompletionScore = filledForm.CompletionScore;
                }

                trackingStopwatch.Stop();
                complianceResult.QualityMetrics.Performance.QualityTrackingMs = trackingStopwatch.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception during quality metrics tracking");
            }
        }

        private async Task HandleComplianceFailureAsync<TResult>(
            ComplianceResult<TResult> result,
            TResult serviceResult,
            ComplianceConfig config) where TResult : class
        {
            result.ExecutionMetadata.UsedFallback = true;

            switch (config.FallbackStrategy)
            {
                case ComplianceFallbackStrategy.ReturnError:
                    result.ExecutionMetadata.FallbackReason = "Compliance failure - returning error";
                    _logger.LogWarning(
                        "⚠️ Compliance failure for {OperationName} - returning error (Score: {Score:F2})",
                        config.OperationName,
                        result.Validation.OverallScore);
                    break;

                case ComplianceFallbackStrategy.ReturnPartialResult:
                    result.Success = true;
                    result.Data = serviceResult;
                    result.ExecutionMetadata.FallbackReason = "Compliance failure - returning partial result";
                    result.Warnings.Add("Result does not meet compliance standards but is returned as partial");
                    _logger.LogWarning(
                        "⚠️ Compliance failure for {OperationName} - returning partial result (Score: {Score:F2})",
                        config.OperationName,
                        result.Validation.OverallScore);
                    break;

                case ComplianceFallbackStrategy.UseLegacyParsing:
                    result.Success = true;
                    result.Data = serviceResult;
                    result.ExecutionMetadata.FallbackReason = "Compliance failure - using legacy parsing";
                    result.Warnings.Add("Fell back to legacy parsing due to compliance validation failure");
                    _logger.LogWarning(
                        "⚠️ Compliance failure for {OperationName} - using legacy parsing fallback",
                        config.OperationName);
                    break;

                case ComplianceFallbackStrategy.RequestManualReview:
                    result.ExecutionMetadata.FallbackReason = "Compliance failure - manual review required";
                    result.Warnings.Add("Manual review required due to compliance validation failure");
                    _logger.LogWarning(
                        "⚠️ Compliance failure for {OperationName} - manual review requested",
                        config.OperationName);
                    break;
            }

            await Task.CompletedTask;
        }

        private async Task NotifyObserversAsync<TResult>(ComplianceResult<TResult> result) where TResult : class
        {
            foreach (var observer in _observers)
            {
                try
                {
                    await observer.OnComplianceResultAsync(result);

                    if (result.Success)
                    {
                        await observer.OnComplianceSuccessAsync(
                            result.ExecutionMetadata.OperationName,
                            result.Validation.OverallScore);
                    }
                    else
                    {
                        foreach (var violation in result.Validation.Violations)
                        {
                            await observer.OnComplianceViolationAsync(
                                violation,
                                result.ExecutionMetadata.OperationName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Exception in compliance observer notification");
                }
            }
        }
    }

    /// <summary>
    /// Legacy Service Compliance Adapter implementation
    /// Wraps legacy services and enforces compliance on method calls
    /// </summary>
    internal sealed class LegacyServiceComplianceAdapter<TService> : IComplianceAdapter<TService>
        where TService : class
    {
        private readonly TService _legacyService;
        private readonly LegacyServiceConfig<TService> _config;
        private readonly IServiceComplianceWrapper _complianceWrapper;
        private readonly ILogger _logger;
        private readonly AdapterComplianceStats _stats;

        public LegacyServiceComplianceAdapter(
            TService legacyService,
            LegacyServiceConfig<TService> config,
            IServiceComplianceWrapper complianceWrapper,
            ILogger logger)
        {
            _legacyService = legacyService ?? throw new ArgumentNullException(nameof(legacyService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _complianceWrapper = complianceWrapper ?? throw new ArgumentNullException(nameof(complianceWrapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stats = new AdapterComplianceStats { ServiceName = config.ServiceName };
        }

        public async Task<ComplianceResult<TResult>> ExecuteMethodAsync<TResult>(
            Func<TService, Task<TResult>> method,
            string methodName,
            Dictionary<string, object>? parameters = null) where TResult : class
        {
            _stats.TotalCalls++;

            // Get compliance config for this method
            var complianceConfig = _config.MethodConfigs.TryGetValue(methodName, out var config)
                ? config
                : _config.DefaultConfig;

            complianceConfig.OperationName = $"{_config.ServiceName}.{methodName}";
            complianceConfig.Metadata["LegacyService"] = true;
            complianceConfig.Metadata["MethodName"] = methodName;

            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    complianceConfig.Metadata[$"Parameter.{param.Key}"] = param.Value;
                }
            }

            _logger.LogDebug(
                "🔧 Executing legacy service method with compliance: {OperationName}",
                complianceConfig.OperationName);

            var stopwatch = Stopwatch.StartNew();

            var result = await _complianceWrapper.ExecuteWithComplianceAsync(
                () => method(_legacyService),
                complianceConfig);

            stopwatch.Stop();

            // Update statistics
            if (result.Success)
            {
                _stats.SuccessfulCalls++;
            }
            else
            {
                _stats.FailedCalls++;
            }

            _stats.TotalViolations += result.Validation.Violations.Count;

            foreach (var violation in result.Validation.Violations)
            {
                var violationType = violation.ViolationType;
                _stats.ViolationsByType.TryGetValue(violationType, out var count);
                _stats.ViolationsByType[violationType] = count + 1;
            }

            // Update average execution time
            var totalMs = (_stats.AverageExecutionTime.TotalMilliseconds * (_stats.TotalCalls - 1))
                + stopwatch.Elapsed.TotalMilliseconds;
            _stats.AverageExecutionTime = TimeSpan.FromMilliseconds(totalMs / _stats.TotalCalls);

            // Update average compliance score
            _stats.AverageComplianceScore =
                ((_stats.AverageComplianceScore * (_stats.TotalCalls - 1)) + result.Validation.OverallScore)
                / _stats.TotalCalls;

            return result;
        }

        public AdapterComplianceStats GetStats()
        {
            return _stats;
        }
    }
}
