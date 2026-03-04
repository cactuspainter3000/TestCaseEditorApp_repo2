using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.Templates; // Task 6.7: Template Form Architecture integration

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for deriving system capabilities from ATP (Acceptance Test Procedure) steps.
    /// Uses LLM analysis with structured A-N taxonomy to transform test procedures into system requirements.
    /// Task 6.7 Enhanced: Integrated with Template Form Architecture for deterministic structured output
    /// </summary>
    public class SystemCapabilityDerivationService : ISystemCapabilityDerivationService
    {
        private readonly ITextGenerationService _llmService;
        private readonly ILogger<SystemCapabilityDerivationService> _logger;
        private readonly SystemRequirementTaxonomy _taxonomy;
        private readonly ResponseParserManager _responseParser;
        private readonly ATPStepParser _atpParser;
        private readonly ICapabilityDerivationPromptBuilder _promptBuilder;
        private readonly TaxonomyValidator _taxonomyValidator;
        private readonly ICapabilityAllocator _capabilityAllocator;
        private readonly IDirectRagService? _directRagService; // RAG integration for context-aware analysis
        private readonly IMBSERequirementClassifier _mbseClassifier; // MBSE-compliant system requirement classification
        private readonly IDerivationQualityScorer _qualityScorer; // Advanced multi-dimensional quality analysis
        
        // Task 6.7: Template Form Architecture integration
        private readonly IOutputEnvelopeService? _outputEnvelopeService; // Deterministic output parsing
        private readonly ICapabilityDerivationTemplateService? _templateService; // Template form management
        private readonly IFieldLevelQualityService? _fieldLevelQualityService; // Field-level quality tracking
        
        // Service health tracking
        private DateTime? _lastSuccessfulOperation;
        private readonly Dictionary<string, object> _performanceMetrics;

        // Task 6.7: Backward-compatible constructor without Template Form Architecture services
        public SystemCapabilityDerivationService(
            ITextGenerationService llmService,
            ILogger<SystemCapabilityDerivationService> logger,
            ResponseParserManager responseParser,
            ATPStepParser atpParser,
            ICapabilityDerivationPromptBuilder promptBuilder,
            TaxonomyValidator taxonomyValidator,
            ICapabilityAllocator capabilityAllocator,
            IMBSERequirementClassifier mbseClassifier,
            IDerivationQualityScorer qualityScorer,
            IDirectRagService? directRagService = null) // Optional for RAG-enhanced analysis
            : this(llmService, logger, responseParser, atpParser, promptBuilder, taxonomyValidator, capabilityAllocator, 
                   mbseClassifier, qualityScorer, directRagService, null, null, null) // Template services nullable
        {
        }

        // Task 6.7: Enhanced constructor with Template Form Architecture integration
        public SystemCapabilityDerivationService(
            ITextGenerationService llmService,
            ILogger<SystemCapabilityDerivationService> logger,
            ResponseParserManager responseParser,
            ATPStepParser atpParser,
            ICapabilityDerivationPromptBuilder promptBuilder,
            TaxonomyValidator taxonomyValidator,
            ICapabilityAllocator capabilityAllocator,
            IMBSERequirementClassifier mbseClassifier,
            IDerivationQualityScorer qualityScorer,
            IDirectRagService? directRagService,
            IOutputEnvelopeService? outputEnvelopeService,
            ICapabilityDerivationTemplateService? templateService,
            IFieldLevelQualityService? fieldLevelQualityService)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _responseParser = responseParser ?? throw new ArgumentNullException(nameof(responseParser));
            _atpParser = atpParser ?? throw new ArgumentNullException(nameof(atpParser));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _taxonomyValidator = taxonomyValidator ?? throw new ArgumentNullException(nameof(taxonomyValidator));
            _capabilityAllocator = capabilityAllocator ?? throw new ArgumentNullException(nameof(capabilityAllocator));
            _mbseClassifier = mbseClassifier ?? throw new ArgumentNullException(nameof(mbseClassifier));
            _qualityScorer = qualityScorer ?? throw new ArgumentNullException(nameof(qualityScorer));
            _directRagService = directRagService; // Optional for enhanced analysis
            
            // Task 6.7: Template Form Architecture services (optional for backward compatibility)
            _outputEnvelopeService = outputEnvelopeService;
            _templateService = templateService;
            _fieldLevelQualityService = fieldLevelQualityService;
            
            _taxonomy = SystemRequirementTaxonomy.Default;
            _performanceMetrics = new Dictionary<string, object>();
            
            var integrationMode = templateService != null ? "with Template Form Architecture" : "in legacy JSON parsing mode";
            _logger.LogInformation("SystemCapabilityDerivationService initialized {IntegrationMode} with taxonomy containing {CategoryCount} categories", 
                integrationMode, _taxonomy.Categories.Count);
        }

        /// <summary>
        /// Derive system capabilities from ATP content using LLM analysis and structured taxonomy
        /// </summary>
        public async Task<DerivationResult> DeriveCapabilitiesAsync(string atpContent, DerivationOptions? options = null, Action<string>? progressCallback = null, Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>? retrySkippedCallback = null, Action<Requirement>? onRequirementDiscovered = null)
        {
            _logger.LogInformation("🚀 ENHANCED ATP DERIVATION SYSTEM: Starting capability derivation with intelligent MBSE processing (no legacy fallback)");
            
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
                _logger.LogInformation("Starting enhanced capability derivation for ATP content (length: {ContentLength})", atpContent.Length);

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
                progressCallback?.Invoke($"📊 ATP Parser: Extracted {parsedSteps.Count} test procedure steps from document structure");

                // Process all ATP steps - debug limit removed for full analysis
                _logger.LogInformation("🚀 FULL PROCESSING MODE: Analyzing all {StepCount} ATP steps", parsedSteps.Count);
                progressCallback?.Invoke($"🚀 Full Analysis Mode: Processing all {parsedSteps.Count} ATP steps for comprehensive requirement extraction");

                // Process each step for capability derivation with progress tracking
                int stepCounter = 0;
                foreach (var parsedStep in parsedSteps)
                {
                    stepCounter++;
                    progressCallback?.Invoke($"🤖 LLM Analysis ({stepCounter}/{parsedSteps.Count}): phi4-mini analyzing → {parsedStep.StepText.Substring(0, Math.Min(50, parsedStep.StepText.Length))}...");
                    
                    // DEBUG: Log ATP step content to understand what we're processing
                    if (stepCounter <= 3) // Log first 3 steps for analysis (reduced from 5 for full processing)
                    {
                        _logger.LogInformation("DEBUG ATP Step {Counter}: Keywords=[{Keywords}] Confidence={Confidence:F2} Text='{StepText}'", 
                            stepCounter, 
                            string.Join(", ", parsedStep.SystemReferences.Concat(parsedStep.ActionVerbs).Concat(parsedStep.MeasurementKeywords)), 
                            parsedStep.ParsingConfidence,
                            parsedStep.StepText.Length > 150 ? parsedStep.StepText.Substring(0, 150) + "..." : parsedStep.StepText);
                    }
                    // Apply per-step timeout to prevent hanging
                    using var stepCts = new CancellationTokenSource(derivationOptions.PerStepTimeout);
                    
                    try
                    {
                        var stepResult = await DeriveSingleStepWithTimeoutAsync(parsedStep.StepText, derivationOptions, stepCts.Token);
                        
                        // Enhance results with parsing metadata
                        foreach (var capability in stepResult.DerivedCapabilities)
                        {
                            capability.SourceMetadata["StepId"] = parsedStep.StepId;
                            capability.SourceMetadata["StepNumber"] = parsedStep.StepNumber;
                            capability.SourceMetadata["StepType"] = parsedStep.StepType.ToString();
                            capability.SourceMetadata["ActionVerbs"] = string.Join(", ", parsedStep.ActionVerbs);
                            capability.SourceMetadata["SystemReferences"] = string.Join(", ", parsedStep.SystemReferences);
                        }
                        
                        // Stream requirements in real-time before merging to batch collection
                        if (onRequirementDiscovered != null)
                        {
                            foreach (var capability in stepResult.DerivedCapabilities)
                            {
                                var requirement = ConvertCapabilityToRequirement(capability);
                                onRequirementDiscovered(requirement);
                            }
                        }
                        
                        // Merge results
                        result.DerivedCapabilities.AddRange(stepResult.DerivedCapabilities);
                        result.RejectedItems.AddRange(stepResult.RejectedItems);
                        result.ProcessingWarnings.AddRange(stepResult.ProcessingWarnings);
                        
                        // Add parsing warnings if any
                        result.ProcessingWarnings.AddRange(parsedStep.ParsingWarnings);
                        
                        // Update progress with running totals
                        progressCallback?.Invoke($"✅ A-N Taxonomy ({stepCounter}/{parsedSteps.Count}): Derived {stepResult.DerivedCapabilities.Count} capabilities → Total: {result.DerivedCapabilities.Count}");
                        
                        // Early exit if no capabilities found after reasonable sample
                        if (stepCounter >= 15 && result.DerivedCapabilities.Count == 0)
                        {
                            _logger.LogWarning("Early exit: Processed {ProcessedSteps} ATP steps with 0 capabilities extracted. Document likely contains no extractable requirements.", stepCounter);
                            result.ProcessingWarnings.Add($"Early exit after {stepCounter} steps: No system capabilities detected in ATP content. Document may not contain extractable requirements or may require different parsing approach.");
                            progressCallback?.Invoke($"🛑 Early Exit: No capabilities found in first {stepCounter} steps - stopping analysis to avoid wasting time");
                            break;
                        }
                    }
                    catch (OperationCanceledException) when (stepCts.IsCancellationRequested)
                    {
                        _logger.LogWarning("ATP step {StepNumber} timed out after {TimeoutSeconds} seconds", stepCounter, derivationOptions.PerStepTimeout.TotalSeconds);
                        
                        // Collect skipped step for potential retry
                        var skippedStep = new SkippedAtpStep
                        {
                            StepText = parsedStep.StepText,
                            StepNumber = stepCounter,
                            StepId = parsedStep.StepId,
                            TimeoutDuration = derivationOptions.PerStepTimeout,
                            SkipReason = $"Timed out after {derivationOptions.PerStepTimeout.TotalSeconds}s"
                        };
                        result.SkippedAtpSteps.Add(skippedStep);
                        
                        result.ProcessingWarnings.Add($"Step {stepCounter} timed out after {derivationOptions.PerStepTimeout.TotalSeconds}s - added to retry queue");
                        progressCallback?.Invoke($"⏰ Step {stepCounter}/{parsedSteps.Count} timed out (can retry later)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to process ATP step: {Step}", parsedStep.StepText.Substring(0, Math.Min(100, parsedStep.StepText.Length)));
                        result.ProcessingWarnings.Add($"Failed to process step {parsedStep.StepNumber}: {ex.Message}");
                        progressCallback?.Invoke($"❌ Step {stepCounter}/{parsedSteps.Count} failed: {ex.Message.Substring(0, Math.Min(50, ex.Message.Length))}");
                    }
                }

                // Handle retrying skipped ATP steps if user chooses to and callback is provided
                if (result.SkippedAtpSteps.Count > 0 && retrySkippedCallback != null)
                {
                    progressCallback?.Invoke($"📋 ATP Derivation Complete: {result.DerivedCapabilities.Count} capabilities derived, {result.SkippedAtpSteps.Count} ATP steps timed out");
                    
                    try
                    {
                        var retryDecision = await retrySkippedCallback(result.SkippedAtpSteps);
                        
                        if (retryDecision.ShouldRetry)
                        {
                            progressCallback?.Invoke($"🔄 Retrying {result.SkippedAtpSteps.Count} skipped steps with {retryDecision.ExtendedTimeout.TotalSeconds}s timeout...");
                            
                            await RetrySkippedStepsAsync(result, retryDecision.ExtendedTimeout, progressCallback);
                            
                            progressCallback?.Invoke($"✅ ATP Retry Complete: Final result: {result.DerivedCapabilities.Count} total capabilities via extended timeout");
                        }
                        else
                        {
                            progressCallback?.Invoke($"⏭️ Skipping retry - proceeding with {result.DerivedCapabilities.Count} capabilities");
                        }
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogWarning(retryEx, "Failed to handle retry decision for skipped ATP steps");
                        result.ProcessingWarnings.Add($"Retry handling failed: {retryEx.Message}");
                    }
                }
                else if (result.SkippedAtpSteps.Count > 0)
                {
                    progressCallback?.Invoke($"✅ ATP Derivation Complete: {result.DerivedCapabilities.Count} capabilities derived, {result.SkippedAtpSteps.Count} ATP steps skipped (no retry callback)");
                }

                // Apply MBSE system-level requirement filtering
                await ApplyMBSEFilteringAsync(result, progressCallback);

                // Calculate advanced multi-dimensional quality score
                result.QualityScore = await CalculateAdvancedQualityAsync(result, atpContent);
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
        /// Retry processing of skipped ATP steps with extended timeout
        /// </summary>
        private async Task RetrySkippedStepsAsync(DerivationResult result, TimeSpan extendedTimeout, Action<string>? progressCallback)
        {
            var skippedSteps = new List<SkippedAtpStep>(result.SkippedAtpSteps);
            result.SkippedAtpSteps.Clear(); // Clear the list - will be repopulated with any that still timeout
            
            int retryCounter = 0;
            foreach (var skippedStep in skippedSteps)
            {
                retryCounter++;
                progressCallback?.Invoke($"🔄 Retrying step {retryCounter}/{skippedSteps.Count}: {skippedStep.Preview}");
                
                // Apply extended timeout for retry 
                using var retryCts = new CancellationTokenSource(extendedTimeout);
                
                try
                {
                    var retryResult = await DeriveSingleStepWithTimeoutAsync(skippedStep.StepText, new DerivationOptions(), retryCts.Token);
                    
                    // Add successful retry results
                    result.DerivedCapabilities.AddRange(retryResult.DerivedCapabilities);
                    result.RejectedItems.AddRange(retryResult.RejectedItems);
                    result.ProcessingWarnings.AddRange(retryResult.ProcessingWarnings);
                    
                    progressCallback?.Invoke($"✅ Retry {retryCounter}/{skippedSteps.Count} succeeded - found {retryResult.DerivedCapabilities.Count} capabilities");
                }
                catch (OperationCanceledException) when (retryCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("ATP step {StepNumber} timed out again during retry with {ExtendedTimeoutSeconds}s", skippedStep.StepNumber, extendedTimeout.TotalSeconds);
                    
                    // Still timed out - update the skip reason and add back to skipped list
                    skippedStep.SkipReason = $"Timed out twice: {skippedStep.TimeoutDuration.TotalSeconds}s + {extendedTimeout.TotalSeconds}s";
                    result.SkippedAtpSteps.Add(skippedStep);
                    
                    progressCallback?.Invoke($"⏰ Retry {retryCounter}/{skippedSteps.Count} still timed out");
                }
                catch (Exception retryEx)
                {
                    _logger.LogWarning(retryEx, "Retry failed for ATP step {StepNumber}", skippedStep.StepNumber);
                    
                    // Add back to skipped list with error reason
                    skippedStep.SkipReason = $"Retry failed: {retryEx.Message}";
                    result.SkippedAtpSteps.Add(skippedStep);
                    
                    progressCallback?.Invoke($"❌ Retry {retryCounter}/{skippedSteps.Count} failed: {retryEx.Message.Substring(0, Math.Min(30, retryEx.Message.Length))}");
                }
            }
        }

        /// <summary>
        /// Derive capabilities from a single ATP step (focused analysis)
        /// </summary>
        public async Task<DerivationResult> DeriveSingleStepAsync(string atpStep, DerivationOptions? options = null)
        {
            // For backward compatibility, call with no cancellation token
            using var cts = new CancellationTokenSource((options ?? new DerivationOptions()).PerStepTimeout);
            return await DeriveSingleStepWithTimeoutAsync(atpStep, options, cts.Token);
        }

        /// <summary>
        /// Derive capabilities from a single ATP step with timeout support
        /// </summary>
        private async Task<DerivationResult> DeriveSingleStepWithTimeoutAsync(string atpStep, DerivationOptions? options = null, CancellationToken cancellationToken = default)
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

                // RAG-ENHANCED ANALYSIS: Query RAG for document context if available
                string ragContext = string.Empty;
                if (_directRagService?.IsConfigured == true && derivationOptions.SourceMetadata.ContainsKey("ProjectId"))
                {
                    var projectId = int.Parse(derivationOptions.SourceMetadata["ProjectId"]);
                    
                    // Extract keywords from ATP step for targeted RAG query
                    var stepKeywords = ExtractKeywordsFromAtpStep(atpStep);
                    var ragQuery = string.Join(" ", stepKeywords.Take(10)); // Use top 10 keywords
                    
                    try
                    {
                        _logger.LogDebug("Querying RAG for context: {Query} (Project: {ProjectId})", ragQuery, projectId);
                        ragContext = await _directRagService.GetRequirementAnalysisContextAsync(
                            ragQuery, 
                            projectId, 
                            maxContextChunks: 5, // Focused context for single step
                            cancellationToken);
                        
                        if (!string.IsNullOrEmpty(ragContext))
                        {
                            _logger.LogDebug("RAG context retrieved: {ContextLength} characters", ragContext.Length);
                            result.SourceMetadata["RAGContextLength"] = ragContext.Length.ToString();
                            result.SourceMetadata["RAGQuery"] = ragQuery;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RAG context query failed, proceeding with step-only analysis");
                        ragContext = string.Empty;
                    }
                }

                // Build derivation prompt using specialized prompt builder with RAG context
                var prompt = _promptBuilder.BuildDerivationPrompt(
                    atpStep, 
                    stepMetadata: null!, // Single step analysis - no metadata
                    systemType: derivationOptions.SystemType,
                    derivationOptions: derivationOptions,
                    ragContext: ragContext); // Enhanced with RAG context
                
                // Generate LLM response with timeout support
                var llmResponse = await _llmService.GenerateAsync(prompt, cancellationToken);
                
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
            try
            {
                var result = new DerivationResult
                {
                    SourceATPContent = sourceStep,
                    AnalysisModel = GetModelName()
                };

                // DEBUG: Log LLM responses to understand what phi4-mini is generating
                if (llmResponse.Length > 0)
                {
                    _logger.LogDebug("LLM Response Length: {Length} chars, Preview: '{Preview}'", 
                        llmResponse.Length, 
                        llmResponse.Length > 200 ? llmResponse.Substring(0, 200) + "..." : llmResponse);
                }

                // Task 6.7: Try Template Form Architecture parsing first if available
                if (_templateService != null && _outputEnvelopeService != null)
                {
                    _logger.LogDebug("🎯 Task 6.7: Using Template Form Architecture for structured parsing");
                    
                    try
                    {
                        var template = _templateService.GetStandardCapabilityTemplate();
                        var filledForm = _templateService.ParseFormResponse(llmResponse, template);
                        
                        if (filledForm.ValidationResult.IsValid)
                        {
                            // Convert filled form to DerivedCapability objects
                            var templateCapabilities = ConvertFilledFormToCapabilities(filledForm, sourceStep);
                            result.DerivedCapabilities = templateCapabilities;
                            
                            _logger.LogInformation("✅ Template Form parsing succeeded: {CapabilityCount} capabilities, validation score: {Score:F2}", 
                                templateCapabilities.Count, filledForm.ValidationResult.ComplianceScore);
                            
                            // Track field-level quality metrics
                            if (_fieldLevelQualityService != null)
                            {
                                await TrackFieldQualityMetricsAsync(filledForm, template);
                            }
                            
                            result.ProcessingWarnings.Add($"Parsed using Template Form Architecture (score: {filledForm.ValidationResult.ComplianceScore:F2})");
                            return result;
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Template Form validation failed ({ViolationCount} violations), falling back to JSON parsing", 
                                filledForm.ValidationResult.FieldViolations.Count);
                            result.ProcessingWarnings.Add($"Template Form validation failed: {string.Join(", ", filledForm.ValidationResult.FieldViolations)}");
                        }
                    }
                    catch (Exception templateEx)
                    {
                        _logger.LogWarning(templateEx, "Template Form parsing failed, falling back to legacy JSON parsing");
                        result.ProcessingWarnings.Add($"Template parsing error: {templateEx.Message}");
                    }
                }

                // Fallback to legacy JSON parsing
                _logger.LogDebug("📋 Using legacy JSON parsing");
                
                // Parse structured JSON response from LLM
                if (TryParseJsonCapabilities(llmResponse, out var capabilities))
                {
                    result.DerivedCapabilities = capabilities;
                    _logger.LogDebug("Parsed {CapabilityCount} capabilities from LLM JSON response", capabilities.Count);
                }
                else
                {
                    // Fallback: Parse freeform text response
                    var textCapabilities = ParseTextCapabilities(llmResponse, sourceStep);
                    result.DerivedCapabilities = textCapabilities;
                    _logger.LogDebug("Parsed {CapabilityCount} capabilities from LLM text response", textCapabilities.Count);
                    
                    // DEBUG: If no capabilities found, log the response for analysis
                    if (textCapabilities.Count == 0 && llmResponse.Length > 10)
                    {
                        _logger.LogWarning("❌ LLM did not follow required JSON format. Expected 'derivedCapabilities' array.");
                        _logger.LogWarning("Response preview: {Response}", 
                            llmResponse.Length > 500 ? llmResponse.Substring(0, 500) + "..." : llmResponse);
                        _logger.LogWarning("💡 The LLM should return ONLY: {{\"derivedCapabilities\": [...]}}");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse derivation response");
                return null;
            }
        }

        private bool TryParseJsonCapabilities(string llmResponse, out List<DerivedCapability> capabilities)
        {
            capabilities = new List<DerivedCapability>();
            
            try
            {
                // Remove any markdown code block formatting
                var cleanResponse = llmResponse.Trim();
                if (cleanResponse.StartsWith("```json"))
                {
                    cleanResponse = cleanResponse.Substring(7);
                }
                if (cleanResponse.StartsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(3);
                }
                if (cleanResponse.EndsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                }
                cleanResponse = cleanResponse.Trim();

                // Look for JSON structure in response
                var jsonStart = cleanResponse.IndexOf('{');
                var jsonEnd = cleanResponse.LastIndexOf('}');
                
                if (jsonStart != -1 && jsonEnd > jsonStart)
                {
                    var jsonPart = cleanResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    
                    using var doc = JsonDocument.Parse(jsonPart);
                    var root = doc.RootElement;
                    
                    // ONLY accept the exact format: "derivedCapabilities" array
                    if (root.TryGetProperty("derivedCapabilities", out var capsArray) && capsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var capElement in capsArray.EnumerateArray())
                        {
                            var capability = ParseSingleCapability(capElement);
                            if (capability != null) capabilities.Add(capability);
                        }
                        
                        _logger.LogInformation("✅ Successfully parsed {Count} capabilities from correct JSON format", capabilities.Count);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("❌ JSON response missing required 'derivedCapabilities' array. Format rejected.");
                        return false;
                    }
                }
                else
                {
                    _logger.LogWarning("❌ No valid JSON structure found in LLM response");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("❌ JSON parsing failed: {Error}", ex.Message);
                return false;
            }
        }
        
        private bool TryParseAnalysisFormat(JsonElement root, out List<DerivedCapability> capabilities)
        {
            capabilities = new List<DerivedCapability>();
            
            try
            {
                // First try parsing at root level
                ParseJsonLevel(root, capabilities);
                
                // If no capabilities found at root, search nested containers
                if (capabilities.Count == 0)
                {
                    var containerKeys = new[] { "AnalysisFocus", "analysis", "Analysis", "analysisFocus", "analysis_focus", "analysisResult", "result" };
                    foreach (var containerKey in containerKeys)
                    {
                        if (root.TryGetProperty(containerKey, out var container) && container.ValueKind == JsonValueKind.Object)
                        {
                            ParseJsonLevel(container, capabilities);
                            if (capabilities.Count > 0) break; // Found data, stop searching
                        }
                    }
                }
                
                _logger.LogDebug("Parsed {Count} capabilities from analysis format JSON", capabilities.Count);
                return capabilities.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse analysis format JSON: {Error}", ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Parse capabilities from a specific JSON level (root or nested)
        /// </summary>
        private void ParseJsonLevel(JsonElement level, List<DerivedCapability> capabilities)
        {
            // Parse systemInterfaces with multiple possible key names including RAG-enhanced format
            var interfaceKeys = new[] { 
                "systemInterfaces", "SystemInterfacesRequired", "system_interfaces_required", "SystemInterfaces", 
                "requiredInterfaces", "interfaces", "systemInterfacesRequired", "system_interfaces", 
                "1_systemInterfacesNeeded", "1_SystemInterfacesNeeded", "SystemInterfacesExistence",
                "1_required_interfaces", "1_SystemInterfacesRequired", "1_system_interfaces"
            };
            foreach (var key in interfaceKeys)
            {
                if (level.TryGetProperty(key, out var interfaces) && interfaces.ValueKind == JsonValueKind.Array)
                {
                    foreach (var interfaceElement in interfaces.EnumerateArray())
                    {
                        var interfaceName = ExtractStringFromElement(interfaceElement);
                        if (!string.IsNullOrEmpty(interfaceName))
                        {
                            capabilities.Add(new DerivedCapability
                            {
                                Id = Guid.NewGuid().ToString(),
                                RequirementText = $"The system shall provide {interfaceName} interface",
                                DerivationRationale = "Interface requirement derived from ATP step analysis",
                                TaxonomyCategory = "A2 - Communication Management",
                                ConfidenceScore = 0.8,
                                SourceATPStep = "ATP Analysis"
                            });
                        }
                    }
                    break; // Found one variant, stop looking for interfaces
                }
            }
                
                // Parse measurementOrControlFunctions with multiple possible key names
                var functionKeys = new[] { 
                    "measurementOrControlFunctions", "measurementControlFunctions", "measurement_or_control_functions", "measurement_control_functions",
                    "MeasurementControlFunctions", "measurementAndControlFunctions", "controlFunctions", "functions",
                    "2_RequiredMeasurementControlFunctions", "MeasurementAndControlFunctionsNeeded", "measurementFunctions",
                    "2_needed_functions", "2_MeasurementControlFunctions", "MeasurementFunctions", "2_measurement_control_functions"
                };
                foreach (var key in functionKeys)
                {
                    if (level.TryGetProperty(key, out var functions) && functions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var functionElement in functions.EnumerateArray())
                        {
                            var functionName = ExtractStringFromElement(functionElement);
                            if (!string.IsNullOrEmpty(functionName))
                            {
                                capabilities.Add(new DerivedCapability
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    RequirementText = $"The system shall provide {functionName}",
                                    DerivationRationale = "Measurement/control function derived from ATP step analysis",
                                    TaxonomyCategory = "A4 - Monitoring Functions",
                                    ConfidenceScore = 0.8,
                                    SourceATPStep = "ATP Analysis"
                                });
                            }
                        }
                        break; // Found one variant, stop looking for functions
                    }
                }
                
                // Parse dataHandlingCapabilities with multiple possible key names 
                var dataKeys = new[] { 
                    "dataHandlingCapabilities", "data_handling_capabilities", "dataHandlingCapability", "DataHandlingCapabilities",
                    "dataProcessingCapabilities", "dataCapabilities", "dataRequirements", "DataHandlingCapabilitiesRequired",
                    "3_NeededDataHandlingCapabilities", "3_DataHandlingCapabilities", "dataHandlingCapabilitiesRequired", "3_data_handling_capabilities"
                };
                foreach (var key in dataKeys)
                {
                    if (level.TryGetProperty(key, out var dataCapabilities) && dataCapabilities.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var capElement in dataCapabilities.EnumerateArray())
                        {
                            var capabilityName = ExtractStringFromElement(capElement);
                            if (!string.IsNullOrEmpty(capabilityName))
                            {
                                capabilities.Add(new DerivedCapability
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    RequirementText = $"The system shall support {capabilityName}",
                                    DerivationRationale = "Data handling capability derived from ATP step analysis",
                                    TaxonomyCategory = "I1 - Data Processing",
                                    ConfidenceScore = 0.75,
                                    SourceATPStep = "ATP Analysis"
                                });
                            }
                        }
                        break; // Found one variant, stop looking for data capabilities
                    }
                }
                
                // Parse performanceCharacteristics with multiple possible key names
                var perfKeys = new[] { 
                    "performanceCharacteristics", "performance_characteristics", "PerformanceCharacteristics", 
                    "performanceRequirements", "performance", "characteristics", "4_performance_characteristics"
                };
                foreach (var key in perfKeys)
                {
                    if (level.TryGetProperty(key, out var perfChars) && perfChars.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var perfElement in perfChars.EnumerateArray())
                        {
                            var perfName = ExtractStringFromElement(perfElement);
                            if (!string.IsNullOrEmpty(perfName))
                            {
                                capabilities.Add(new DerivedCapability
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    RequirementText = $"The system shall meet {perfName} performance requirement",
                                    DerivationRationale = "Performance characteristic derived from ATP step analysis",
                                    TaxonomyCategory = "A5 - General Avionics",
                                    ConfidenceScore = 0.7,
                                    SourceATPStep = "ATP Analysis"
                                });
                            }
                        }
                        break; // Found one variant, stop looking for performance
                    }
                }
            }
        
        /// <summary>
        /// Cleans JSON comments that break parsing (e.g., // comments)
        /// </summary>
        private string CleanJsonComments(string json)
        {
            try
            {
                // Remove line comments (// ...) but preserve strings
                var lines = json.Split('\n');
                var cleanedLines = new List<string>();
                bool inString = false;
                
                foreach (var line in lines)
                {
                    var cleanedLine = "";
                    for (int i = 0; i < line.Length; i++)
                    {
                        var ch = line[i];
                        
                        // Track if we're inside a string
                        if (ch == '"' && (i == 0 || line[i - 1] != '\\'))
                        {
                            inString = !inString;
                        }
                        
                        // If we find // and we're not in a string, remove the rest of the line
                        if (!inString && ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
                        {
                            break; // Remove everything from // onwards
                        }
                        
                        cleanedLine += ch;
                    }
                    cleanedLines.Add(cleanedLine.TrimEnd());
                }
                
                return string.Join("\n", cleanedLines);
            }
            catch
            {
                // If cleaning fails, return original
                return json;
            }
        }
        
        /// <summary>
        /// Extracts meaningful text from a JsonElement which can be either a simple string or an object
        /// </summary>
        private string? ExtractStringFromElement(JsonElement element)
        {
            // If it's a simple string, return it directly
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
            
            // If it's an object, try to extract meaningful text from common property names
            if (element.ValueKind == JsonValueKind.Object)
            {
                // Try common property names used by the LLM
                var propertyNames = new[] { "name", "interfaceName", "interfaceType", "functionName", "description", "purpose", "capability", "characteristic" };
                
                foreach (var propName in propertyNames)
                {
                    if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
                    {
                        var value = prop.GetString();
                        if (!string.IsNullOrEmpty(value))
                            return value;
                    }
                }
                
                // If no common property found, try to concatenate all string values
                var stringValues = new List<string>();
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrEmpty(value))
                            stringValues.Add(value);
                    }
                }
                
                if (stringValues.Count > 0)
                    return string.Join(" - ", stringValues);
            }
            
            return null;
        }
        
        private DerivedCapability? ParseSingleCapability(JsonElement element)
        {
            try
            {
                return new DerivedCapability
                {
                    Id = Guid.NewGuid().ToString(),
                    RequirementText = element.GetProperty("requirement").GetString() ?? "",
                    DerivationRationale = element.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "",
                    TaxonomyCategory = element.TryGetProperty("category", out var c) ? c.GetString() ?? "Unknown" : "Unknown",
                    ConfidenceScore = element.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.7,
                    SourceATPStep = element.TryGetProperty("source", out var src) ? src.GetString() ?? "" : ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse capability element: {Error}", ex.Message);
                return null;
            }
        }

        private List<DerivedCapability> ParseTextCapabilities(string llmResponse, string sourceStep)
        {
            var capabilities = new List<DerivedCapability>();
            
            // Look for "shall" statements as system requirements
            var shallMatches = Regex.Matches(llmResponse, @"[Tt]he\s+system\s+shall\s+[^.]{10,200}\.", RegexOptions.IgnoreCase);
            
            foreach (Match match in shallMatches)
            {
                var reqText = match.Value.Trim();
                if (reqText.Length > 20) // Minimum reasonable requirement length
                {
                    capabilities.Add(new DerivedCapability
                    {
                        Id = Guid.NewGuid().ToString(),
                        RequirementText = reqText,
                        DerivationRationale = "Extracted from ATP step using 'shall' pattern matching",
                        TaxonomyCategory = ClassifyRequirementCategory(reqText),
                        ConfidenceScore = 0.6,
                        SourceATPStep = sourceStep.Length > 100 ? sourceStep.Substring(0, 100) + "..." : sourceStep
                    });
                }
            }
            
            return capabilities;
        }
        
        private string ClassifyRequirementCategory(string requirementText)
        {
            var lowerText = requirementText.ToLowerInvariant();
            
            if (lowerText.Contains("display") || lowerText.Contains("show") || lowerText.Contains("indicate"))
                return "A1 - Information Display";
            if (lowerText.Contains("communicate") || lowerText.Contains("transmit") || lowerText.Contains("receive"))
                return "A2 - Communication Management";
            if (lowerText.Contains("navigate") || lowerText.Contains("route") || lowerText.Contains("position"))
                return "N1 - Navigation Functions";
            if (lowerText.Contains("control") || lowerText.Contains("manage") || lowerText.Contains("operate"))
                return "A3 - System Control";
            if (lowerText.Contains("monitor") || lowerText.Contains("track") || lowerText.Contains("detect"))
                return "A4 - Monitoring Functions";
                
            return "A5 - General Avionics";
        }

        /// <summary>
        /// Calculate advanced multi-dimensional quality score using sophisticated analysis
        /// </summary>
        private async Task<double> CalculateAdvancedQualityAsync(DerivationResult result, string sourceAtpContent)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                if (result.DerivedCapabilities.Count == 0) 
                {
                    _logger.LogWarning("🔍 [Quality Analysis] No capabilities to score - returning zero quality score");
                    return 0.0;
                }

                // Calculate basic score for comparison
                var basicScore = result.DerivedCapabilities.Average(c => c.ConfidenceScore);
                
                _logger.LogInformation("🔍 [Quality Analysis] Starting advanced analysis for {Count} capabilities (basic avg: {BasicScore:F2})",
                    result.DerivedCapabilities.Count, basicScore);
                
                // Log sample requirements for debugging
                var sampleReqs = result.DerivedCapabilities.Take(3).Select(c => new { 
                    Text = c.RequirementText?.Length > 80 ? c.RequirementText.Substring(0, 80) + "..." : c.RequirementText,
                    Confidence = c.ConfidenceScore,
                    Category = c.TaxonomyCategory
                }).ToList();
                
                _logger.LogDebug("🔍 [Quality Analysis] Sample requirements: {SampleReqs}", 
                    System.Text.Json.JsonSerializer.Serialize(sampleReqs, new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));

                var qualityOptions = new QualityScoringOptions
                {
                    IncludeDetailedAnalysis = true,
                    GenerateRecommendations = true,
                    AssessRelativePerformance = true,
                    ImprovementThreshold = 0.7
                };
                
                _logger.LogDebug("🔍 [Quality Analysis] Calling DerivationQualityScorer with options: DetailedAnalysis={Detailed}, Recommendations={Recs}, Threshold={Threshold}",
                    qualityOptions.IncludeDetailedAnalysis, qualityOptions.GenerateRecommendations, qualityOptions.ImprovementThreshold);

                var qualityScore = await _qualityScorer.ScoreDerivationQualityAsync(result, sourceAtpContent, qualityOptions);
                
                stopwatch.Stop();
                
                // Log comprehensive quality insights
                _logger.LogInformation("🎯 [Quality Analysis] COMPLETED in {ElapsedMs}ms: Advanced={AdvancedScore:F2} vs Basic={BasicScore:F2} (Δ{Delta:+0.00;-0.00}), Confidence={Confidence}",
                    stopwatch.ElapsedMilliseconds, qualityScore.OverallScore, basicScore, qualityScore.OverallScore - basicScore, qualityScore.ConfidenceLevel);
                
                // Log dimension scores if available
                if (qualityScore.DimensionScores?.Count > 0)
                {
                    var topDimensions = qualityScore.DimensionScores
                        .OrderByDescending(kv => kv.Value)
                        .Take(5)
                        .Select(kv => $"{kv.Key}={kv.Value:F2}")
                        .ToList();
                    
                    _logger.LogInformation("📊 [Quality Dimensions] Top scores: {TopDimensions}", string.Join(", ", topDimensions));
                }
                
                // Log improvement areas 
                if (qualityScore.ImprovementAreas?.Count > 0)
                {
                    _logger.LogWarning("⚠️  [Quality Issues] {ImprovementCount} areas need attention: {ImprovementAreas}", 
                        qualityScore.ImprovementAreas.Count, string.Join("; ", qualityScore.ImprovementAreas.Take(3)));
                }
                
                // Log actionable recommendations
                if (qualityScore.ActionableRecommendations?.Count > 0)
                {
                    _logger.LogInformation("💡 [Quality Recommendations] {RecCount} suggestions:", qualityScore.ActionableRecommendations.Count);
                    for (int i = 0; i < Math.Min(3, qualityScore.ActionableRecommendations.Count); i++)
                    {
                        _logger.LogInformation("   {Index}. {Recommendation}", i + 1, qualityScore.ActionableRecommendations[i]);
                    }
                    if (qualityScore.ActionableRecommendations.Count > 3)
                    {
                        _logger.LogInformation("   ... and {MoreCount} more recommendations", qualityScore.ActionableRecommendations.Count - 3);
                    }
                }

                // Log relative performance if available
                if (qualityScore.RelativePerformance != null)
                {
                    _logger.LogDebug("📈 [Relative Performance] Current session performance metrics available");
                }

                return qualityScore.OverallScore;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "❌ [Quality Analysis] FAILED after {ElapsedMs}ms - falling back to basic calculation", stopwatch.ElapsedMilliseconds);
                
                // Fallback to basic scoring if advanced fails
                var fallbackScore = result.DerivedCapabilities.Count > 0 ? result.DerivedCapabilities.Average(c => c.ConfidenceScore) : 0.0;
                _logger.LogWarning("🔄 [Quality Fallback] Using basic average: {FallbackScore:F2}", fallbackScore);
                
                return fallbackScore;
            }
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

        /// <summary>
        /// Extract relevant keywords from ATP step for targeted RAG queries
        /// </summary>
        private List<string> ExtractKeywordsFromAtpStep(string atpStep)
        {
            if (string.IsNullOrWhiteSpace(atpStep))
                return new List<string>();

            var keywords = new List<string>();
            
            // Remove common test step words and extract meaningful terms
            var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "test", "step", "verify", "check", "ensure", "confirm", "validate", 
                "perform", "execute", "run", "start", "stop", "set", "get", "is", 
                "are", "the", "and", "or", "but", "with", "for", "to", "from", "in", 
                "on", "at", "by", "of", "as", "if", "then", "else", "when", "while"
            };
            
            // Extract words that might be technical terms or system components
            var words = atpStep.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?' }, 
                StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                var cleanWord = word.Trim();
                
                // Include words that are likely technical terms (longer than 3 chars, not common words)
                if (cleanWord.Length > 3 && !commonWords.Contains(cleanWord) && 
                    (char.IsUpper(cleanWord[0]) || cleanWord.Contains("_") || cleanWord.All(char.IsUpper)))
                {
                    keywords.Add(cleanWord);
                }
                
                // Also include quoted strings as they often contain specific terms
                if (cleanWord.StartsWith("\"") && cleanWord.EndsWith("\"") && cleanWord.Length > 4)
                {
                    keywords.Add(cleanWord.Trim('"'));
                }
            }
            
            return keywords.Distinct().Take(15).ToList(); // Return top 15 unique keywords
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

        // Task 6.7: Template Form Architecture integration helper methods

        /// <summary>
        /// Convert filled template form to list of DerivedCapability objects
        /// </summary>
        private List<DerivedCapability> ConvertFilledFormToCapabilities(IFilledForm filledForm, string sourceStep)
        {
            var capabilities = new List<DerivedCapability>();
            
            try
            {
                // Extract capabilities array from form
                if (!filledForm.FieldValues.ContainsKey("derivedCapabilities"))
                {
                    _logger.LogWarning("Template form missing 'derivedCapabilities' field");
                    return capabilities;
                }
                
                var capabilitiesRaw = filledForm.FieldValues["derivedCapabilities"];
                var capabilityTexts = new List<string>();
                
                if (capabilitiesRaw is List<string> capList)
                {
                    capabilityTexts = capList;
                }
                else if (capabilitiesRaw is string capString)
                {
                    capabilityTexts.Add(capString);
                }
                
                // Extract additional form fields
                var taxonomyCategories = filledForm.FieldValues.ContainsKey("taxonomyCategories") && 
                                        filledForm.FieldValues["taxonomyCategories"] is List<string> taxList ? 
                                        taxList : new List<string>();
                var rationale = filledForm.FieldValues.ContainsKey("derivationRationale") ? 
                               filledForm.FieldValues["derivationRationale"]?.ToString() : "";
                var confidenceScore = filledForm.FieldValues.ContainsKey("confidenceScore") ? 
                                     Convert.ToDouble(filledForm.FieldValues["confidenceScore"]) : 0.8;
                var isSystemLevel = filledForm.FieldValues.ContainsKey("isSystemLevel") && 
                                   filledForm.FieldValues["isSystemLevel"] is bool isSys ? isSys : true;
                
                // Create DerivedCapability objects
                for (int i = 0; i < capabilityTexts.Count; i++)
                {
                    var capText = capabilityTexts[i];
                    if (string.IsNullOrWhiteSpace(capText)) continue;
                    
                    var taxCategory = i < taxonomyCategories.Count ? taxonomyCategories[i] : "Generic";
                    
                    capabilities.Add(new DerivedCapability
                    {
                        Id = Guid.NewGuid().ToString(),
                        RequirementText = capText,
                        DerivationRationale = rationale ?? "Template form derivation",
                        TaxonomyCategory = taxCategory,
                        ConfidenceScore = confidenceScore,
                        SourceATPStep = sourceStep,
                        SourceMetadata = new Dictionary<string, string>
                        {
                            ["ParseMethod"] = "TemplateForms",
                            ["FormCompletionScore"] = filledForm.CompletionScore.ToString("F2"),
                            ["FormValidationScore"] = filledForm.ValidationResult.ComplianceScore.ToString("F2"),
                            ["IsSystemLevel"] = isSystemLevel.ToString()
                        }
                    });
                }
                
                _logger.LogDebug("Converted {Count} capabilities from template form", capabilities.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert filled form to capabilities");
            }
            
            return capabilities;
        }

        /// <summary>
        /// Track field-level quality metrics for Template Form Architecture
        /// </summary>
        private async Task TrackFieldQualityMetricsAsync(IFilledForm filledForm, IFormTemplate template)
        {
            if (_fieldLevelQualityService == null) return;
            
            try
            {
                foreach (var field in template.Fields)
                {
                    var fieldResult = new FieldProcessingResult
                    {
                        FieldName = field.FieldName,
                        FieldType = field.Criticality,
                        ProcessedAt = DateTime.UtcNow,
                        IsSuccessful = filledForm.FieldValues.ContainsKey(field.FieldName) && 
                                       filledForm.FieldValues[field.FieldName] != null,
                        ConfidenceScore = filledForm.ValidationResult.ComplianceScore,
                        ProcessingTime = TimeSpan.FromMilliseconds(10), // Placeholder - could track actual time
                        RetryCount = 0,
                        TemplateId = template.TemplateName,
                        SessionId = Guid.NewGuid().ToString()
                    };
                    
                    await _fieldLevelQualityService.RecordFieldProcessingResultAsync(fieldResult);
                }
                
                _logger.LogDebug("Recorded field quality metrics for {FieldCount} fields", template.Fields.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to track field quality metrics");
            }
        }

        private string GetModelName()
        {
            return _llmService?.GetType()?.Name ?? "Unknown";
        }

        /// <summary>
        /// Applies enhanced MBSE-compliant filtering to derived capabilities, identifying system requirements,
        /// elevating derived requirements, and filtering out implementation noise. 
        /// Transforms the system from generic "requirement extraction" to intelligent "MBSE requirement elevation".
        /// </summary>
        private async Task ApplyMBSEFilteringAsync(DerivationResult result, Action<string>? progressCallback = null)
        {
            if (result.DerivedCapabilities.Count == 0)
            {
                _logger.LogDebug("No capabilities to analyze - skipping MBSE classification");
                return;
            }

            // ENHANCED SYSTEM: Validate that MBSE classifier is available (no fallback allowed)
            if (_mbseClassifier == null)
            {
                var error = "Enhanced MBSE classifier is required but not available. Fallback disabled - system must use enhanced processing.";
                _logger.LogError(error);
                progressCallback?.Invoke("❌ Enhanced MBSE system failure - no fallback available");
                throw new InvalidOperationException(error);
            }

            var originalCount = result.DerivedCapabilities.Count;
            _logger.LogInformation("🚀 ENHANCED MBSE PROCESSING: Starting intelligent requirement classification and elevation for {Count} candidates", originalCount);
            progressCallback?.Invoke($"🔍 Enhanced MBSE Analysis: Evaluating {originalCount} candidates for system-level compliance and requirement elevation...");

            try
            {
                // Apply enhanced MBSE analysis with requirement elevation (45-minute timeout)
                var enhancedResult = await _mbseClassifier.AnalyzeAndElevateRequirementsAsync(
                    result.DerivedCapabilities, 
                    minimumMBSEScore: 0.7,
                    enableRequirementElevation: true,
                    maxProcessingTimeMinutes: 45);

                // Replace capabilities with enhanced results (native + elevated system requirements)
                result.DerivedCapabilities = enhancedResult.GetAllSystemRequirements();
                
                // Store enhanced MBSE analysis results
                result.MBSEAnalysisResult = new MBSEAnalysisResult
                {
                    TotalCandidatesEvaluated = enhancedResult.Statistics.TotalCandidates,
                    SystemLevelRequirementsCount = enhancedResult.Statistics.TotalSystemRequirements,
                    ComponentLevelRequirementsCount = enhancedResult.Statistics.ComponentLevelRequirements,
                    ImplementationConstraintsCount = enhancedResult.Statistics.ImplementationConstraints,
                    AverageMBSEScore = enhancedResult.Statistics.AverageMBSEScore,
                    SystemLevelPercentage = enhancedResult.Statistics.SystemRequirementPercentage,
                    FilteredComponentRequirements = enhancedResult.ComponentLevelRequirements
                        .Select(c => c.RequirementText).ToList(),
                    FilteredImplementationConstraints = enhancedResult.ImplementationConstraints
                        .Select(c => c.RequirementText).ToList()
                };

                var systemCount = enhancedResult.Statistics.TotalSystemRequirements;
                var nativeCount = enhancedResult.Statistics.NativeSystemRequirements;
                var elevatedCount = enhancedResult.Statistics.ElevatedRequirements;
                var elevationRate = enhancedResult.Statistics.ElevationSuccessRate;

                progressCallback?.Invoke(
                    $"✅ Enhanced MBSE Complete: {systemCount} system requirements from {originalCount} candidates " +
                    $"({nativeCount} native + {elevatedCount} elevated) " +
                    $"→ Filtered: {enhancedResult.Statistics.ComponentLevelRequirements} component, " +
                    $"{enhancedResult.Statistics.ImplementationConstraints} constraints, " +
                    $"{enhancedResult.Statistics.InvalidRequirements} invalid " +
                    $"(elevation success: {elevationRate:F1}%)");

                _logger.LogInformation(
                    "Enhanced MBSE analysis completed: {SystemCount} system requirements " +
                    "({NativeCount} native + {ElevatedCount} elevated) from {OriginalCount} candidates. " +
                    "Elevation success rate: {ElevationRate:F1}%, avg MBSE score: {AvgScore:F2}",
                    systemCount, nativeCount, elevatedCount, originalCount, 
                    elevationRate, enhancedResult.Statistics.AverageMBSEScore);

                // Log examples of elevated requirements
                if (enhancedResult.ElevatedRequirements.Count > 0)
                {
                    var elevationExamples = string.Join("; ", 
                        enhancedResult.ElevatedRequirements.Take(2).Select(e => 
                            $"'{e.OriginalCapability.RequirementText}' → '{e.SystemLevelCapability.RequirementText}'"));
                    _logger.LogDebug("Requirement elevation examples: {Examples}", elevationExamples);
                }

                // Log traceability information
                if (enhancedResult.TraceabilityMatrix.Count > 0)
                {
                    _logger.LogDebug("Generated {TraceCount} traceability records for requirement transformations", 
                        enhancedResult.TraceabilityMatrix.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enhanced MBSE filtering failed - proceeding with unfiltered capabilities");
                progressCallback?.Invoke($"⚠️ Enhanced MBSE analysis failed: {ex.Message} - proceeding with {originalCount} unfiltered capabilities");
                
                // Add warning but don't fail the entire derivation
                result.ProcessingWarnings.Add($"Enhanced MBSE analysis failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Convert a DerivedCapability to a Requirement for real-time streaming
        /// </summary>
        private Requirement ConvertCapabilityToRequirement(DerivedCapability capability)
        {
            return new Requirement
            {
                GlobalId = $"DERIVED-{DateTime.Now.Ticks}-{capability.RequirementText.GetHashCode():X}",
                Description = capability.RequirementText,
                RequirementType = capability.TaxonomyCategory ?? "Functional",
                ItemType = "System Capability",
                Method = TestCaseEditorApp.MVVM.Models.VerificationMethod.Test, // Default verification method
                CreatedDate = DateTime.Now,
                ModifiedDate = DateTime.Now,
                Status = "Active",
                
                // Store derivation metadata in tags
                Tags = $"Derived,{capability.TaxonomyCategory},{capability.VerificationIntent}".Replace(",", ";"),
                
                // Store additional metadata in existing fields where possible
                Rationale = capability.DerivationRationale ?? "",
                Name = $"ATP Derived: {capability.RequirementText.Substring(0, Math.Min(50, capability.RequirementText.Length))}..."
            };
        }
    }
}
