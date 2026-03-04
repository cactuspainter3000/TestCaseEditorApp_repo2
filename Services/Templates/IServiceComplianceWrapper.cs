using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services.Templates
{
    /// <summary>
    /// Universal Compliance Wrapper Interface - enforces Template Form Architecture standards
    /// across all service interactions regardless of underlying implementation
    /// 
    /// PURPOSE: Treat LLM as engineered system component with contract, validation, telemetry
    /// PATTERN: Adapter pattern for gradual system-wide architectural compliance
    /// 
    /// Task 6.8: Build Compliance Wrapper Interface
    /// </summary>
    public interface IServiceComplianceWrapper
    {
        /// <summary>
        /// Execute service call with full Template Form Architecture compliance enforcement
        /// </summary>
        /// <typeparam name="TResult">Expected result type from service call</typeparam>
        /// <param name="serviceCall">The actual service operation to execute</param>
        /// <param name="config">Compliance configuration with templates, schemas, metrics</param>
        /// <returns>ComplianceResult with validated output and quality metrics</returns>
        Task<ComplianceResult<TResult>> ExecuteWithComplianceAsync<TResult>(
            Func<Task<TResult>> serviceCall,
            ComplianceConfig config) where TResult : class;

        /// <summary>
        /// Execute service call with input validation against template
        /// </summary>
        Task<ComplianceResult<TResult>> ExecuteWithInputValidationAsync<TInput, TResult>(
            Func<TInput, Task<TResult>> serviceCall,
            TInput input,
            ComplianceConfig config) where TResult : class;

        /// <summary>
        /// Wrap legacy service with automatic compliance enforcement
        /// </summary>
        IComplianceAdapter<TService> WrapLegacyService<TService>(
            TService legacyService,
            LegacyServiceConfig<TService> adapterConfig) where TService : class;

        /// <summary>
        /// Validate service output against expected schema without executing call
        /// </summary>
        Task<OutputValidationResult> ValidateOutputAsync<TOutput>(
            TOutput output,
            EnvelopeSchema expectedSchema);

        /// <summary>
        /// Register compliance metrics observer for real-time monitoring
        /// </summary>
        void RegisterMetricsObserver(IComplianceMetricsObserver observer);
    }

    /// <summary>
    /// Adapter interface for wrapping legacy services with compliance enforcement
    /// </summary>
    public interface IComplianceAdapter<TService> where TService : class
    {
        /// <summary>
        /// Execute method on wrapped service with compliance checks
        /// </summary>
        Task<ComplianceResult<TResult>> ExecuteMethodAsync<TResult>(
            Func<TService, Task<TResult>> method,
            string methodName,
            Dictionary<string, object>? parameters = null) where TResult : class;

        /// <summary>
        /// Get compliance statistics for this adapter
        /// </summary>
        AdapterComplianceStats GetStats();
    }

    /// <summary>
    /// Configuration for compliance enforcement
    /// </summary>
    public class ComplianceConfig
    {
        /// <summary>
        /// Unique identifier for this service call operation
        /// </summary>
        public string OperationName { get; set; } = "UnnamedOperation";

        /// <summary>
        /// Input validation template (optional - for input validation)
        /// </summary>
        public IFormTemplate? InputTemplate { get; set; }

        /// <summary>
        /// Output envelope schema for validation
        /// </summary>
        public EnvelopeSchema? OutputSchema { get; set; }

        /// <summary>
        /// Expected result template for parsing LLM responses
        /// </summary>
        public IFormTemplate? ResultTemplate { get; set; }

        /// <summary>
        /// Enable field-level quality metric tracking
        /// </summary>
        public bool TrackQualityMetrics { get; set; } = true;

        /// <summary>
        /// Enable compliance telemetry
        /// </summary>
        public bool EnableTelemetry { get; set; } = true;

        /// <summary>
        /// Minimum compliance score required (0.0 - 1.0)
        /// </summary>
        public double MinimumComplianceScore { get; set; } = 0.7;

        /// <summary>
        /// Retry policy for compliance failures
        /// </summary>
        public ComplianceRetryPolicy RetryPolicy { get; set; } = ComplianceRetryPolicy.None;

        /// <summary>
        /// Fallback strategy when compliance validation fails
        /// </summary>
        public ComplianceFallbackStrategy FallbackStrategy { get; set; } = ComplianceFallbackStrategy.ReturnError;

        /// <summary>
        /// Custom validation rules beyond template/schema
        /// </summary>
        public List<IComplianceValidationRule> CustomValidationRules { get; set; } = new();

        /// <summary>
        /// Metadata for compliance tracking
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Configuration for wrapping legacy services
    /// </summary>
    public class LegacyServiceConfig<TService> where TService : class
    {
        /// <summary>
        /// Service name for telemetry
        /// </summary>
        public string ServiceName { get; set; } = typeof(TService).Name;

        /// <summary>
        /// Method-specific compliance configurations
        /// </summary>
        public Dictionary<string, ComplianceConfig> MethodConfigs { get; set; } = new();

        /// <summary>
        /// Default compliance config for unconfigured methods
        /// </summary>
        public ComplianceConfig DefaultConfig { get; set; } = new();

        /// <summary>
        /// Enable legacy mode (relaxed compliance for gradual migration)
        /// </summary>
        public bool LegacyMode { get; set; } = true;
    }

    /// <summary>
    /// Result of compliance-enforced service call
    /// </summary>
    public class ComplianceResult<TResult> where TResult : class
    {
        /// <summary>
        /// Indicates if service call succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Result data (if successful)
        /// </summary>
        public TResult? Data { get; set; }

        /// <summary>
        /// Compliance validation details
        /// </summary>
        public ComplianceValidation Validation { get; set; } = new();

        /// <summary>
        /// Quality metrics collected during execution
        /// </summary>
        public ComplianceQualityMetrics QualityMetrics { get; set; } = new();

        /// <summary>
        /// Errors encountered (if any)
        /// </summary>
        public List<ComplianceError> Errors { get; set; } = new();

        /// <summary>
        /// Warnings (non-fatal issues)
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Execution metadata
        /// </summary>
        public ComplianceExecutionMetadata ExecutionMetadata { get; set; } = new();

        /// <summary>
        /// Original result before compliance transformation (for debugging)
        /// </summary>
        public object? OriginalResult { get; set; }
    }

    /// <summary>
    /// Validation result for service output compliance
    /// </summary>
    public class OutputValidationResult
    {
        public bool IsValid { get; set; }
        public double ComplianceScore { get; set; }
        public List<string> Violations { get; set; } = new();
        public EnvelopeValidationResult? EnvelopeValidation { get; set; }
        public IFormValidationResult? TemplateValidation { get; set; }
    }

    /// <summary>
    /// Compliance validation details
    /// </summary>
    public class ComplianceValidation
    {
        /// <summary>
        /// Overall compliance score (0.0 - 1.0)
        /// </summary>
        public double OverallScore { get; set; }

        /// <summary>
        /// Input validation passed
        /// </summary>
        public bool InputValid { get; set; } = true;

        /// <summary>
        /// Output validation passed
        /// </summary>
        public bool OutputValid { get; set; }

        /// <summary>
        /// Template form validation result
        /// </summary>
        public IFormValidationResult? TemplateValidation { get; set; }

        /// <summary>
        /// Envelope validation result
        /// </summary>
        public EnvelopeValidationResult? EnvelopeValidation { get; set; }

        /// <summary>
        /// Custom validation rule results
        /// </summary>
        public List<CustomValidationResult> CustomValidations { get; set; } = new();

        /// <summary>
        /// Compliance violations detected
        /// </summary>
        public List<ComplianceViolation> Violations { get; set; } = new();
    }

    /// <summary>
    /// Quality metrics collected during compliance execution
    /// </summary>
    public class ComplianceQualityMetrics
    {
        /// <summary>
        /// Field-level quality results (if tracked)
        /// </summary>
        public List<FieldProcessingResult>? FieldResults { get; set; }

        /// <summary>
        /// Template completion score
        /// </summary>
        public double? TemplateCompletionScore { get; set; }

        /// <summary>
        /// Confidence score from LLM
        /// </summary>
        public double? ConfidenceScore { get; set; }

        /// <summary>
        /// Retry attempts required
        /// </summary>
        public int RetryAttempts { get; set; }

        /// <summary>
        /// Performance metrics
        /// </summary>
        public PerformanceMetrics Performance { get; set; } = new();
    }

    /// <summary>
    /// Execution metadata for compliance tracking
    /// </summary>
    public class ComplianceExecutionMetadata
    {
        public string OperationName { get; set; } = "Unknown";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public bool UsedFallback { get; set; }
        public string? FallbackReason { get; set; }
        public Dictionary<string, object> CustomMetadata { get; set; } = new();
    }

    /// <summary>
    /// Compliance violation details
    /// </summary>
    public class ComplianceViolation
    {
        public string ViolationType { get; set; } = "Unknown";
        public string Description { get; set; } = "No description";
        public ValidationSeverity Severity { get; set; }
        public string? FieldName { get; set; }
        public string? SuggestedFix { get; set; }
    }

    /// <summary>
    /// Custom validation rule result
    /// </summary>
    public class CustomValidationResult
    {
        public string RuleName { get; set; } = "Unknown";
        public bool Passed { get; set; }
        public string? Message { get; set; }
        public ValidationSeverity Severity { get; set; }
    }

    /// <summary>
    /// Performance metrics for compliance operation
    /// </summary>
    public class PerformanceMetrics
    {
        public long InputValidationMs { get; set; }
        public long ServiceExecutionMs { get; set; }
        public long OutputValidationMs { get; set; }
        public long QualityTrackingMs { get; set; }
        public long TotalMs { get; set; }
    }

    /// <summary>
    /// Compliance error details
    /// </summary>
    public class ComplianceError
    {
        public string ErrorType { get; set; } = "Unknown";
        public string Message { get; set; } = "No message";
        public Exception? Exception { get; set; }
        public string? StackTrace { get; set; }
        public bool Recoverable { get; set; }
    }

    /// <summary>
    /// Adapter statistics for monitoring
    /// </summary>
    public class AdapterComplianceStats
    {
        public string ServiceName { get; set; } = "Unknown";
        public int TotalCalls { get; set; }
        public int SuccessfulCalls { get; set; }
        public int FailedCalls { get; set; }
        public double AverageComplianceScore { get; set; }
        public int TotalViolations { get; set; }
        public Dictionary<string, int> ViolationsByType { get; set; } = new();
        public TimeSpan AverageExecutionTime { get; set; }
    }

    /// <summary>
    /// Interface for custom compliance validation rules
    /// </summary>
    public interface IComplianceValidationRule
    {
        string RuleName { get; }
        ValidationSeverity Severity { get; }
        Task<CustomValidationResult> ValidateAsync(object input, object output);
    }

    /// <summary>
    /// Observer interface for compliance metrics monitoring
    /// </summary>
    public interface IComplianceMetricsObserver
    {
        Task OnComplianceResultAsync<TResult>(ComplianceResult<TResult> result) where TResult : class;
        Task OnComplianceViolationAsync(ComplianceViolation violation, string operationName);
        Task OnComplianceSuccessAsync(string operationName, double complianceScore);
    }

    /// <summary>
    /// Retry policies for compliance failures
    /// </summary>
    public enum ComplianceRetryPolicy
    {
        None,
        RetryOnce,
        RetryThreeTimes,
        ExponentialBackoff
    }

    /// <summary>
    /// Fallback strategies when compliance validation fails
    /// </summary>
    public enum ComplianceFallbackStrategy
    {
        ReturnError,
        ReturnPartialResult,
        UseLegacyParsing,
        RequestManualReview
    }
}
