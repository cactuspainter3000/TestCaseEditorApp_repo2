using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for deriving system capabilities from ATP (Acceptance Test Procedure) steps.
    /// Uses LLM analysis with structured A-N taxonomy to transform test procedures into system requirements.
    /// </summary>
    public class SystemCapabilityDerivationService : ISystemCapabilityDerivationService
    {
        private readonly ITextGenerationService _llmService;
        private readonly ILogger<SystemCapabilityDerivationService> _logger;
        private readonly SystemRequirementTaxonomy _taxonomy;
        private readonly ResponseParserManager _responseParser;
        private readonly ATPStepParser _atpParser;
        private readonly CapabilityDerivationPromptBuilder _promptBuilder;
        private readonly TaxonomyValidator _taxonomyValidator;
        private readonly ICapabilityAllocator _capabilityAllocator;
        
        // Service health tracking
        private DateTime? _lastSuccessfulOperation;
        private readonly Dictionary<string, object> _performanceMetrics;

        public SystemCapabilityDerivationService(
            ITextGenerationService llmService,
            ILogger<SystemCapabilityDerivationService> logger,
            ResponseParserManager responseParser,
            ATPStepParser atpParser,
            CapabilityDerivationPromptBuilder promptBuilder,
            TaxonomyValidator taxonomyValidator,
            ICapabilityAllocator capabilityAllocator)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
            _atpParser = atpParser ?? throw new ArgumentNullException(nameof(atpParser));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _taxonomyValidator = taxonomyValidator ?? throw new ArgumentNullException(nameof(taxonomyValidator));
            _capabilityAllocator = capabilityAllocator ?? throw new ArgumentNullException(nameof(capabilityAllocator));
            _taxonomy = SystemRequirementTaxonomy.Default;
            _performanceMetrics = new Dictionary<string, object>();
            
            _logger.LogInformation("SystemCapabilityDerivationService initialized with taxonomy containing {CategoryCount} categories", 
                _taxonomy.Categories.Count);
        }

        /// <summary>
        /// Derive system capabilities from ATP content using LLM analysis and structured taxonomy
        /// </summary>
        public async Task<DerivationResult> DeriveCapabilitiesAsync(string atpContent, DerivationOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var derivationOptions = options ?? new DerivationOptions();
            var result = new DerivationResult 
            { 
                SourceATPContent = atpContent,
                AnalysisModel = GetModelName(),
                SourceMetadata = new Dictionary<string, string>(derivationOptions.SourceMetadata)
            };

            try
            {
                _logger.LogInformation("Starting capability derivation for ATP content (length: {ContentLength})", atpContent.Length);

                // Validate input
                if (string.IsNullOrWhiteSpace(atpContent))
                {
                    result.ProcessingWarnings.Add("Empty ATP content provided");
                    return result;
                }

                // Extract ATP steps using dedicated parser
                var parsedSteps = await _atpParser.ParseATPDocumentAsync(atpContent, new ATPParsingOptions
                {
                    ParseMetadata = true,
                    SkipBoilerplate = true,
                    SystemKeywords = derivationOptions.SystemType != "Generic" 
                        ? new List<string> { derivationOptions.SystemType } 
                        : new List<string>()
                });
                _logger.LogDebug("Extracted {StepCount} ATP steps from content", parsedSteps.Count);

                // Process each step for capability derivation
                foreach (var parsedStep in parsedSteps)
                {
                    try
                    {
                        var stepResult = await DeriveSingleStepAsync(parsedStep.StepText, derivationOptions);
                        
                        // Enhance results with parsing metadata
                        foreach (var capability in stepResult.DerivedCapabilities)
                        {
                            capability.SourceMetadata["StepId"] = parsedStep.StepId;
                            capability.SourceMetadata["StepNumber"] = parsedStep.StepNumber;
                            capability.SourceMetadata["StepType"] = parsedStep.StepType.ToString();
                            capability.SourceMetadata["ActionVerbs"] = string.Join(", ", parsedStep.ActionVerbs);
                            capability.SourceMetadata["SystemReferences"] = string.Join(", ", parsedStep.SystemReferences);
                        }
                        
                        // Merge results
                        result.DerivedCapabilities.AddRange(stepResult.DerivedCapabilities);
                        result.RejectedItems.AddRange(stepResult.RejectedItems);
                        result.ProcessingWarnings.AddRange(stepResult.ProcessingWarnings);
                        
                        // Add parsing warnings if any
                        result.ProcessingWarnings.AddRange(parsedStep.ParsingWarnings);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process ATP step: {Step}", parsedStep.StepText.Substring(0, Math.Min(100, parsedStep.StepText.Length)));
                        result.ProcessingWarnings.Add($"Failed to process step {parsedStep.StepNumber}: {ex.Message}");
                    }
                }

                // Calculate overall quality score
                result.QualityScore = CalculateOverallQuality(result.DerivedCapabilities);
                result.ProcessedAt = DateTime.Now;
                result.ProcessingTime = stopwatch.Elapsed;

                _lastSuccessfulOperation = DateTime.Now;
                _performanceMetrics["LastDerivationTime"] = stopwatch.ElapsedMilliseconds;
                _performanceMetrics["LastCapabilityCount"] = result.DerivedCapabilities.Count;

                _logger.LogInformation("Capability derivation completed: {CapabilityCount} capabilities derived, {RejectionCount} items rejected, quality score: {QualityScore:F2}",
                    result.DerivedCapabilities.Count, result.RejectedItems.Count, result.QualityScore);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Capability derivation failed for ATP content");
                result.ProcessingWarnings.Add($"Derivation failed: {ex.Message}");
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }
        }

        /// <summary>
        /// Derive capabilities from a single ATP step (focused analysis)
        /// </summary>
        public async Task<DerivationResult> DeriveSingleStepAsync(string atpStep, DerivationOptions? options = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var derivationOptions = options ?? new DerivationOptions();
            var result = new DerivationResult 
            { 
                SourceATPContent = atpStep,
                AnalysisModel = GetModelName()
            };

            try
            {
                _logger.LogDebug("Deriving capabilities from single ATP step: {Step}", atpStep.Substring(0, Math.Min(100, atpStep.Length)));

                // Build derivation prompt using specialized prompt builder
                var prompt = _promptBuilder.BuildDerivationPrompt(
                    atpStep, 
                    stepMetadata: null, // Single step analysis - no metadata
                    systemType: derivationOptions.SystemType,
                    derivationOptions: derivationOptions);
                
                // Generate LLM response
                var llmResponse = await _llmService.GenerateAsync(prompt);
                
                if (string.IsNullOrEmpty(llmResponse))
                {
                    result.ProcessingWarnings.Add("Empty response from LLM service");
                    return result;
                }

                // Parse structured response
                var parseResult = await ParseDerivationResponseAsync(llmResponse, atpStep);
                if (parseResult != null)
                {
                    result = parseResult;
                    result.AnalysisModel = GetModelName();
                    result.ProcessingTime = stopwatch.Elapsed;
                }
                else
                {
                    result.ProcessingWarnings.Add("Failed to parse LLM response");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Single step derivation failed for ATP step: {Step}", atpStep.Substring(0, Math.Min(50, atpStep.Length)));
                result.ProcessingWarnings.Add($"Single step derivation failed: {ex.Message}");
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }
        }

        /// <summary>
        /// Validate derived capabilities against taxonomy rules and quality standards
        /// </summary>
        public async Task<TestCaseEditorApp.MVVM.Models.ValidationResult> ValidateDerivationAsync(DerivationResult derivationResult)
        {
            try
            {
                _logger.LogDebug("Validating derivation result with {CapabilityCount} capabilities", derivationResult.DerivedCapabilities.Count);

                // Use TaxonomyValidator for comprehensive validation
                var taxonomyValidationOptions = new TaxonomyValidationOptions
                {
                    MinimumQualityThreshold = 0.6, // Slightly lower threshold for ATP derivation
                    MaxTBDSpecifications = 5,      // Allow more TBDs for initial derivation
                    RequireSpecificSubcategories = true,
                    ValidateExpectedCategories = true,
                    DetectDuplicates = true
                };

                var taxonomyValidation = _taxonomyValidator.ValidateDerivationResult(
                    derivationResult.DerivedCapabilities,
                    derivationResult.SourceATPContent,
                    taxonomyValidationOptions);

                // Convert TaxonomyValidationResult to MVVM ValidationResult
                var isValid = taxonomyValidation.IsValid;
                var reasonParts = new List<string>();

                if (!isValid)
                {
                    reasonParts.Add($"Quality score: {taxonomyValidation.QualityScore:F2}");
                    
                    var criticalIssues = taxonomyValidation.Issues.Count(i => i.Severity == TaxonomyValidationSeverity.Critical);
                    var errorIssues = taxonomyValidation.Issues.Count(i => i.Severity == TaxonomyValidationSeverity.Error);
                    
                    if (criticalIssues > 0)
                        reasonParts.Add($"{criticalIssues} critical issues");
                    if (errorIssues > 0)
                        reasonParts.Add($"{errorIssues} error issues");
                }

                var reason = isValid ? "Taxonomy validation passed" : string.Join(", ", reasonParts);
                
                return new TestCaseEditorApp.MVVM.Models.ValidationResult(isValid, reason, "TaxonomyValidation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed for derivation result");
                return new TestCaseEditorApp.MVVM.Models.ValidationResult(false, $"Validation error: {ex.Message}", "ValidationError");
            }
        }

        /// <summary>
        /// Allocate derived capabilities to subsystems using the intelligent CapabilityAllocator
        /// </summary>
        public async Task<AllocationResult> AllocateCapabilitiesAsync(List<DerivedCapability> capabilities, CapabilityAllocationOptions? allocationOptions = null)
        {
            try
            {
                _logger.LogDebug("Allocating {CapabilityCount} capabilities using CapabilityAllocator", capabilities.Count);
                
                var stopwatch = Stopwatch.StartNew();
                var result = await _capabilityAllocator.AllocateCapabilitiesAsync(capabilities, allocationOptions);
                stopwatch.Stop();
                
                _logger.LogInformation("Allocation completed: {AllocatedCount}/{TotalCount} capabilities allocated in {ElapsedMs}ms with {ConfidenceScore:F2} avg confidence", 
                    result.AllocatedCapabilities, result.TotalCapabilities, stopwatch.ElapsedMilliseconds, result.AverageConfidenceScore);
                
                // Track performance metrics
                UpdatePerformanceMetrics("allocation_operations", 1);
                UpdatePerformanceMetrics("allocation_success_rate", result.SuccessRate);
                UpdatePerformanceMetrics("allocation_avg_confidence", result.AverageConfidenceScore);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to allocate capabilities to subsystems");
                
                // Return empty result rather than throwing
                return new AllocationResult
                {
                    Timestamp = DateTime.Now,
                    Options = allocationOptions ?? new CapabilityAllocationOptions(),
                    TotalCapabilities = capabilities.Count,
                    AllocationSummary = $"Allocation failed: {ex.Message}",
                    Recommendations = new List<string> { "Review capability data quality and retry allocation" }
                };
            }
        }

        /// <summary>
        /// Updates performance metrics for monitoring and diagnostics
        /// </summary>
        private void UpdatePerformanceMetrics(string metricName, object value)
        {
            _performanceMetrics[metricName] = value;
        }

        /// <summary>
        /// Get service health and configuration status
        /// </summary>
        public async Task<ServiceStatus> GetServiceStatusAsync()
        {
            try
            {
                var isLlmAvailable = false;
                var statusMessage = "";

                // Test LLM service availability
                try
                {
                    var testResponse = await _llmService.GenerateAsync("Test connection");
                    isLlmAvailable = !string.IsNullOrEmpty(testResponse);
                    statusMessage = isLlmAvailable ? "Service operational" : "LLM service not responding";
                }
                catch (Exception ex)
                {
                    isLlmAvailable = false;
                    statusMessage = $"LLM service unavailable: {ex.Message}";
                }

                var taxonomyLoaded = _taxonomy?.Categories?.Count > 0;
                var isHealthy = taxonomyLoaded && isLlmAvailable;

                return new ServiceStatus
                {
                    ServiceName = "SystemCapabilityDerivation",
                    IsAvailable = isHealthy,
                    IsStarting = false,
                    StatusMessage = isHealthy ? "Service operational" : statusMessage,
                    LastChecked = DateTime.Now,
                    Type = ServiceType.Generic
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get service status");
                return new ServiceStatus
                {
                    ServiceName = "SystemCapabilityDerivation",
                    IsAvailable = false,
                    IsStarting = false,
                    StatusMessage = $"Status check failed: {ex.Message}",
                    LastChecked = DateTime.Now,
                    Type = ServiceType.Generic
                };
            }
        }

        // Private helper methods

        private async Task<DerivationResult?> ParseDerivationResponseAsync(string llmResponse, string sourceStep)
        {
            // This would use the ResponseParserManager, but for now we'll implement basic parsing
            // The actual implementation would depend on the specific LLM response format
            
            try
            {
                // For now, return a basic result - this would be enhanced with actual JSON parsing
                var result = new DerivationResult
                {
                    SourceATPContent = sourceStep,
                    AnalysisModel = GetModelName()
                };

                // TODO: Implement proper JSON parsing of LLM response
                // This is a placeholder that would be replaced with actual response parsing logic
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse derivation response");
                return null;
            }
        }

        private double CalculateOverallQuality(List<DerivedCapability> capabilities)
        {
            if (capabilities.Count == 0) return 0.0;
            
            return capabilities.Average(c => c.ConfidenceScore);
        }

        private async Task<CapabilityAllocation?> AllocateSingleCapabilityAsync(
            DerivedCapability capability, 
            List<string> availableSubsystems, 
            AllocationOptions options)
        {
            // Simple allocation based on taxonomy category
            var primarySubsystem = GetPrimarySubsystemForCategory(capability.TaxonomySubcategory);
            
            if (availableSubsystems.Contains(primarySubsystem))
            {
                return new CapabilityAllocation
                {
                    CapabilityId = capability.Id,
                    TargetSubsystem = primarySubsystem,
                    AllocationType = "Primary",
                    AllocationRationale = $"Category {capability.TaxonomySubcategory} typically allocated to {primarySubsystem}",
                    AllocationConfidence = 0.8
                };
            }

            return null;
        }

        private string GetPrimarySubsystemForCategory(string taxonomySubcategory)
        {
            return taxonomySubcategory.ToUpper() switch
            {
                string s when s.StartsWith("A") => "SoftwareSubsystem",
                string s when s.StartsWith("B") => "InterconnectSubsystem",  
                string s when s.StartsWith("C") => "PowerSubsystem",
                string s when s.StartsWith("D") => "InstrumentationSubsystem",
                string s when s.StartsWith("E") => "SoftwareSubsystem",
                string s when s.StartsWith("F") => "SoftwareSubsystem",
                string s when s.StartsWith("G") => "ProtectionSubsystem",
                string s when s.StartsWith("H") => "SoftwareSubsystem",
                string s when s.StartsWith("I") => "DataHandlingSubsystem",
                string s when s.StartsWith("J") => "SoftwareSubsystem",
                string s when s.StartsWith("K") => "SoftwareSubsystem",
                string s when s.StartsWith("L") => "OperatorWorkflowSubsystem",
                string s when s.StartsWith("M") => "SafetySubsystem",
                string s when s.StartsWith("N") => "SoftwareSubsystem",
                _ => "SoftwareSubsystem"
            };
        }

        private List<string> DetectAllocationConflicts(List<CapabilityAllocation> allocations)
        {
            var conflicts = new List<string>();
            
            // Check for over-allocation to single subsystems
            var allocationCounts = allocations
                .GroupBy(a => a.TargetSubsystem)
                .Where(g => g.Count() > 10) // Arbitrary threshold
                .Select(g => $"{g.Key}: {g.Count()} allocations");
            
            conflicts.AddRange(allocationCounts.Select(c => $"High allocation count: {c}"));
            
            return conflicts;
        }

        private string GetModelName()
        {
            return _llmService?.GetType()?.Name ?? "Unknown";
        }
    }
}
