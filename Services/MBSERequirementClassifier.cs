using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Prompts;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// MBSE-compliant requirement classifier that evaluates requirements against 
    /// rigorous system-level criteria. Implements the definitive MBSE filter:
    /// "Can I verify this requirement while treating the system as a black box?"
    /// </summary>
    public class MBSERequirementClassifier : IMBSERequirementClassifier
    {
        private readonly ITextGenerationService _llmService;
        private readonly ILogger<MBSERequirementClassifier> _logger;

        public MBSERequirementClassifier(
            ITextGenerationService llmService,
            ILogger<MBSERequirementClassifier> logger)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MBSEClassificationResult> ClassifyRequirementAsync(
            DerivedCapability capability, 
            string? systemBoundaryContext = null)
        {
            if (capability?.RequirementText == null)
            {
                return new MBSEClassificationResult
                {
                    ClassificationType = RequirementClassificationType.Invalid,
                    BlockingIssues = new() { "Invalid or missing requirement text" }
                };
            }

            try
            {
                var prompt = BuildEnhancedMBSEClassificationPrompt(capability, systemBoundaryContext);
                var response = await _llmService.GenerateAsync(prompt);
                
                return ParseEnhancedMBSEClassificationResponse(response, capability.RequirementText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error classifying requirement: {RequirementText}", 
                    capability.RequirementText);
                
                return new MBSEClassificationResult
                {
                    ClassificationType = RequirementClassificationType.Invalid,
                    BlockingIssues = new() { $"Classification error: {ex.Message}" }
                };
            }
        }

        public async Task<RequirementElevationResult> ElevateToSystemLevelAsync(
            DerivedCapability derivedRequirement,
            string? systemBoundaryContext = null)
        {
            if (string.IsNullOrWhiteSpace(derivedRequirement?.RequirementText))
            {
                return new RequirementElevationResult
                {
                    ElevationSuccessful = false,
                    ElevationIssues = new() { "Invalid or missing requirement text" }
                };
            }

            try
            {
                var prompt = BuildRequirementElevationPrompt(derivedRequirement, systemBoundaryContext);
                var response = await _llmService.GenerateAsync(prompt);
                
                return ParseRequirementElevationResponse(response, derivedRequirement.RequirementText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error elevating requirement: {RequirementText}", 
                    derivedRequirement.RequirementText);
                
                return new RequirementElevationResult
                {
                    OriginalRequirement = derivedRequirement.RequirementText,
                    ElevationSuccessful = false,
                    ElevationIssues = new() { $"Elevation error: {ex.Message}" }
                };
            }
        }

        public async Task<EnhancedMBSEFilterResult> AnalyzeAndElevateRequirementsAsync(
            IEnumerable<DerivedCapability> capabilities, 
            double minimumMBSEScore = 0.7,
            bool enableRequirementElevation = true,
            int maxProcessingTimeMinutes = 30)
        {
            var result = new EnhancedMBSEFilterResult();
            var allScores = new List<double>();
            var elevationConfidences = new List<double>();
            var capabilityList = capabilities.ToList();
            
            _logger.LogInformation("Starting enhanced MBSE analysis of {Count} capabilities with {TimeoutMinutes}min timeout...", 
                capabilityList.Count, maxProcessingTimeMinutes);

            // Create overall timeout for the entire operation
            using var overallCts = new CancellationTokenSource(TimeSpan.FromMinutes(maxProcessingTimeMinutes));
            var processingCancelled = false;

            for (int i = 0; i < capabilityList.Count && !processingCancelled; i++)
            {
                var capability = capabilityList[i];
                
                // Check for overall timeout
                if (overallCts.Token.IsCancellationRequested)
                {
                    processingCancelled = true;
                    _logger.LogWarning("Processing cancelled due to timeout after {Minutes} minutes at item {Current}/{Total}", 
                        maxProcessingTimeMinutes, i + 1, capabilityList.Count);
                    break;
                }
                
                // Progress logging every 10 items
                if (i % 10 == 0 || i == capabilityList.Count - 1)
                {
                    var elapsed = DateTime.Now.Subtract(DateTime.Now.AddMinutes(-maxProcessingTimeMinutes)).TotalMinutes;
                    _logger.LogInformation("Processing capability {Current}/{Total}: {Percentage:F1}% complete, ~{EstimatedMinutesRemaining:F1}min remaining", 
                        i + 1, capabilityList.Count, ((double)(i + 1) / capabilityList.Count) * 100,
                        (capabilityList.Count - i - 1) * (elapsed / Math.Max(1, i)) );
                }
                
                try
                {
                    // Apply per-item timeout (3 minutes for classification + elevation)
                    using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                    itemCts.CancelAfter(TimeSpan.FromMinutes(3));
                    
                    var classification = await ClassifyRequirementAsync(capability);
                    allScores.Add(classification.OverallMBSEScore);

                    switch (classification.ClassificationType)
                    {
                        case RequirementClassificationType.SystemLevel:
                            if (classification.OverallMBSEScore >= minimumMBSEScore)
                            {
                                result.NativeSystemRequirements.Add(capability);
                                _logger.LogDebug("Classified as native system requirement: {Text}", 
                                    capability.RequirementText?.Substring(0, Math.Min(50, capability.RequirementText.Length)));
                            }
                            else
                            {
                                result.ComponentLevelRequirements.Add(capability);
                            }
                            break;

                        case RequirementClassificationType.DerivedRequirement:
                            if (enableRequirementElevation && !itemCts.Token.IsCancellationRequested)
                            {
                                _logger.LogDebug("Attempting elevation for derived requirement: {Text}", 
                                    capability.RequirementText?.Substring(0, Math.Min(50, capability.RequirementText.Length)));
                                    
                                var elevationResult = await ElevateToSystemLevelAsync(capability);
                                elevationConfidences.Add(elevationResult.ElevationConfidence);
                                
                                if (elevationResult.ElevationSuccessful && 
                                    elevationResult.ElevatedMBSEScore >= minimumMBSEScore)
                                {
                                    var elevatedCapability = CreateElevatedCapability(capability, elevationResult);
                                    result.ElevatedRequirements.Add(new ElevatedRequirement
                                    {
                                        OriginalCapability = capability,
                                        SystemLevelCapability = elevatedCapability,
                                        ElevationDetails = elevationResult
                                    });
                                    
                                    // Add traceability record
                                    result.TraceabilityMatrix.Add(elevationResult.Traceability);
                                    
                                    _logger.LogDebug("Successfully elevated requirement to system level");
                                }
                                else
                                {
                                    result.UnElevatableRequirements.Add(capability);
                                    _logger.LogDebug("Failed to elevate requirement: {Reason}", 
                                        string.Join("; ", elevationResult.ElevationIssues ?? new List<string>()));
                                }
                            }
                            else
                            {
                                result.ComponentLevelRequirements.Add(capability);
                            }
                            break;

                        case RequirementClassificationType.ComponentLevel:
                            result.ComponentLevelRequirements.Add(capability);
                            break;

                        case RequirementClassificationType.ImplementationConstraint:
                            result.ImplementationConstraints.Add(capability);
                            break;

                        case RequirementClassificationType.Invalid:
                            result.InvalidRequirements.Add(capability);
                            _logger.LogWarning("Invalid requirement detected: {Text}", 
                                capability.RequirementText?.Substring(0, Math.Min(100, capability.RequirementText?.Length  ?? 0)));
                            break;
                    }
                }
                catch (OperationCanceledException) when (overallCts.Token.IsCancellationRequested)
                {
                    processingCancelled = true;
                    _logger.LogWarning("Processing cancelled due to overall timeout at item {Current}/{Total}", 
                        i + 1, capabilityList.Count);
                    result.InvalidRequirements.Add(capability);
                    break;
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is TimeoutException)
                {
                    _logger.LogWarning("Timeout processing capability {Index}/{Total}: {Text}. Marking as invalid and continuing.", 
                        i + 1, capabilityList.Count,
                        capability.RequirementText?.Substring(0, Math.Min(50, capability.RequirementText?.Length ?? 0)));
                    
                    result.InvalidRequirements.Add(capability);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing capability {Index}/{Total}: {Text}", 
                        i + 1, capabilityList.Count,
                        capability.RequirementText?.Substring(0, Math.Min(50, capability.RequirementText?.Length ?? 0)));
                    
                    // Add to invalid requirements so processing can continue
                    result.InvalidRequirements.Add(capability);
                }
            }

            // Calculate statistics
            result.Statistics = new EnhancedMBSEStatistics
            {
                TotalCandidates = capabilities.Count(),
                NativeSystemRequirements = result.NativeSystemRequirements.Count,
                ElevatedRequirements = result.ElevatedRequirements.Count,
                UnElevatableRequirements = result.UnElevatableRequirements.Count,
                ComponentLevelRequirements = result.ComponentLevelRequirements.Count,
                ImplementationConstraints = result.ImplementationConstraints.Count,
                InvalidRequirements = result.InvalidRequirements.Count,
                AverageMBSEScore = allScores.Count > 0 ? allScores.Average() : 0,
                AverageElevationConfidence = elevationConfidences.Count > 0 ? elevationConfidences.Average() : 0
            };

            var processedCount = result.NativeSystemRequirements.Count + result.ElevatedRequirements.Count + 
                               result.UnElevatableRequirements.Count + result.ComponentLevelRequirements.Count + 
                               result.ImplementationConstraints.Count + result.InvalidRequirements.Count;

            if (processingCancelled)
            {
                _logger.LogWarning(
                    "Enhanced MBSE analysis TIMED OUT after {TimeoutMinutes}min: processed {ProcessedCount}/{TotalCount} items. " +
                    "Results: {SystemTotal} system requirements ({NativeCount} native + {ElevatedCount} elevated), " +
                    "elevation success rate: {ElevationRate:F1}%",
                    maxProcessingTimeMinutes, processedCount, result.Statistics.TotalCandidates,
                    result.Statistics.TotalSystemRequirements,
                    result.Statistics.NativeSystemRequirements,
                    result.Statistics.ElevatedRequirements,
                    result.Statistics.ElevationSuccessRate);
            }
            else
            {
                _logger.LogInformation(
                    "Enhanced MBSE analysis COMPLETED successfully: {SystemTotal} total system requirements " +
                    "({NativeCount} native + {ElevatedCount} elevated) from {TotalCount} candidates, " +
                    "elevation success rate: {ElevationRate:F1}%",
                    result.Statistics.TotalSystemRequirements,
                    result.Statistics.NativeSystemRequirements,
                    result.Statistics.ElevatedRequirements,
                    result.Statistics.TotalCandidates,
                    result.Statistics.ElevationSuccessRate);
            }

            return result;
        }

        public async Task<bool> PassesBlackBoxVerificationTestAsync(string requirementText)
        {
            if (string.IsNullOrWhiteSpace(requirementText))
                return false;

            var prompt = BuildBlackBoxTestPrompt(requirementText);
            var response = await _llmService.GenerateAsync(prompt);
            
            return ParseBlackBoxTestResponse(response);
        }

        [Obsolete("Use AnalyzeAndElevateRequirementsAsync for enhanced derived requirement handling")]
        public async Task<MBSEFilterResult> FilterToSystemLevelRequirementsAsync(
            IEnumerable<DerivedCapability> capabilities, 
            double minimumMBSEScore = 0.7)
        {
            // Legacy compatibility - convert enhanced result to old format
            var enhancedResult = await AnalyzeAndElevateRequirementsAsync(capabilities, minimumMBSEScore, enableRequirementElevation: false);
            
            return new MBSEFilterResult
            {
                SystemLevelRequirements = enhancedResult.NativeSystemRequirements,
                ComponentLevelRequirements = enhancedResult.ComponentLevelRequirements,
                ImplementationConstraints = enhancedResult.ImplementationConstraints,
                Statistics = new MBSEFilterStatistics
                {
                    TotalCandidates = enhancedResult.Statistics.TotalCandidates,
                    SystemLevelCount = enhancedResult.Statistics.NativeSystemRequirements,
                    ComponentLevelCount = enhancedResult.Statistics.ComponentLevelRequirements,
                    ImplementationConstraintCount = enhancedResult.Statistics.ImplementationConstraints,
                    AverageMBSEScore = enhancedResult.Statistics.AverageMBSEScore
                }
            };
        }

        private string BuildEnhancedMBSEClassificationPrompt(DerivedCapability capability, string? boundaryContext)
        {
            return $@"You are an MBSE expert performing enhanced requirement classification. Classify requirements into:
1. SYSTEM LEVEL - boundary-verifiable, implementation-agnostic capabilities
2. DERIVED REQUIREMENT - valid requirement expressed at wrong abstraction level (can be elevated)
3. COMPONENT LEVEL - legitimate subsystem requirement
4. IMPLEMENTATION CONSTRAINT - design detail that should be filtered
5. INVALID - malformed or nonsensical

REQUIREMENT TO CLASSIFY:
""{capability.RequirementText}""

CONTEXT:
- Source: {capability.SourceATPStep}
- Rationale: {capability.DerivationRationale}
- Category: {capability.TaxonomyCategory}
{(boundaryContext != null ? $"- System Boundary: {boundaryContext}" : "")}

CLASSIFICATION CRITERIA:

SYSTEM LEVEL Requirements:
✅ Observable/testable at system boundary (black-box verifiable)
✅ Implementation agnostic (no specific technologies prescribed)
✅ Stakeholder traceable (links to mission needs)
✅ Complete context (conditions, behavior, measurable criteria)

DERIVED Requirements (can be elevated):
✅ Legitimate requirement but at subsystem/component level
✅ Indicates underlying system need (e.g., nameplate → identification marking)
✅ Can be abstracted to system boundary behavior
✅ Has clear system-level parent concept

Examples:
• ""UUT shall have metal nameplate with laser etched ID"" → DERIVED (system identification need)
• ""System shall reject input noise >5ms at interfaces"" → SYSTEM LEVEL
• ""FPGA shall implement CRC algorithm"" → COMPONENT LEVEL  
• ""PCB shall be 4-layer stackup"" → IMPLEMENTATION CONSTRAINT

RESPOND with JSON:
{{
  ""classificationType"": ""SystemLevel|DerivedRequirement|ComponentLevel|ImplementationConstraint|Invalid"",
  ""overallMBSEScore"": <0.0-1.0>,
  ""criteriaScores"": {{
    ""boundaryBased"": <0.0-1.0>,
    ""implementationAgnostic"": <0.0-1.0>,
    ""systemVerifiable"": <0.0-1.0>,
    ""stakeholderTraceable"": <0.0-1.0>,
    ""allocatable"": <0.0-1.0>,
    ""contextComplete"": <0.0-1.0>
  }},
  ""rationale"": ""<explanation of classification>"",
  ""blockingIssues"": [""<specific issues>""],
  ""improvements"": [""<suggestions>""],
  ""derivationIndicators"": [""<if derived, what system concepts it indicates>""]
}}";
        }

        private string BuildRequirementElevationPrompt(DerivedCapability derivedRequirement, string? boundaryContext)
        {
            return $@"You are an MBSE expert specializing in requirement elevation. Transform a derived/subsystem requirement into a proper system-level requirement while maintaining the underlying need.

DERIVED REQUIREMENT TO ELEVATE:
""{derivedRequirement.RequirementText}""

ELEVATION PROCESS:
1. Identify the underlying system need/capability
2. Abstract to system boundary behavior
3. Remove implementation specifics
4. Ensure black-box verifiability
5. Add appropriate standards/compliance references

ELEVATION EXAMPLES:

Original: ""UUT shall have metal nameplate with identifying criteria laser etched into it""
Elevated: ""The system shall include externally visible product identification markings in accordance with applicable product definition and marking standards.""
Domain: Interface/Identification
Rationale: Abstracts physical implementation to system identification requirement

Original: ""Software shall log errors to internal database table""  
Elevated: ""The system shall record and maintain diagnostic information for operational monitoring and troubleshooting purposes.""
Domain: System Management
Rationale: Removes implementation (database) while preserving diagnostic capability need

Original: ""Power supply circuit shall provide 3.3V ±5% to digital circuits""
Elevated: ""The system shall provide stable power within specified tolerances to ensure reliable operation of all internal subsystems.""
Domain: Power Management  
Rationale: Abstracts to system power stability requirement

{(boundaryContext != null ? $"\nSYSTEM BOUNDARY CONTEXT:\n{boundaryContext}" : "")}

RESPOND with JSON:
{{
  ""elevatedRequirement"": ""<recomposed system-level requirement>"",
  ""elevationSuccessful"": <true/false>,
  ""elevationConfidence"": <0.0-1.0>,
  ""elevationRationale"": ""<explanation of elevation process>"",
  ""systemDomain"": ""<Interface|Performance|Safety|Security|Power|Thermal|Communication|Identification|etc>"",
  ""elevatedMBSEScore"": <0.0-1.0>,
  ""traceability"": {{
    ""transformationType"": ""DerivedToSystem"",
    ""sourceRequirement"": ""{derivedRequirement.RequirementText}"",
    ""transformationRationale"": ""<why this elevation was performed>"",
    ""confidenceScore"": <0.0-1.0>
  }},
  ""elevationIssues"": [""<any issues preventing clean elevation>""]
}}";
        }

        private string BuildBlackBoxTestPrompt(string requirementText)
        {
            return $@"MBSE Black-Box Verification Test for: ""{requirementText}""

Question: Can this requirement be verified while treating the system as a black box?

This means:
✅ Verification requires only external observation
✅ No need to probe internal components
✅ No need to examine internal architecture
✅ Testable at system boundary/interfaces

❌ Cannot be verified if it requires:
- Opening the system
- Probing internal signals  
- Examining component behavior
- Internal architecture inspection

Respond with JSON:
{{
  ""passesBlackBoxTest"": <true/false>,
  ""reasoning"": ""<brief explanation>""
}}";
        }

        private string CleanJsonResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return "{}";

            var cleaned = response.Trim();

            // Remove markdown code block markers
            if (cleaned.StartsWith("```json"))
                cleaned = cleaned.Substring(7);
            else if (cleaned.StartsWith("```"))
                cleaned = cleaned.Substring(3);
            
            if (cleaned.EndsWith("```"))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);

            cleaned = cleaned.Trim();

            // Find JSON boundaries - look for first { and last }
            var firstBrace = cleaned.IndexOf('{');
            var lastBrace = cleaned.LastIndexOf('}');
            
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            else
            {
                // Try to find array boundaries [ ]
                var firstBracket = cleaned.IndexOf('[');
                var lastBracket = cleaned.LastIndexOf(']');
                if (firstBracket >= 0 && lastBracket > firstBracket)
                {
                    cleaned = cleaned.Substring(firstBracket, lastBracket - firstBracket + 1);
                }
            }

            // Fix common JSON issues
            cleaned = FixCommonJsonIssues(cleaned.Trim());
            
            return cleaned;
        }

        private string FixCommonJsonIssues(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "{}";

            // Remove trailing commas in arrays and objects
            json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*([}\]])", "$1");
            
            // Fix unescaped quotes in strings (basic fix) - replace double quotes inside quoted strings with single quotes
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"([^\"]*?)\"([^\"]*?)\"([^\":,}\\]]*?)\"", "\"$1'$2'$3\"");
            
            // Remove invalid characters after values  
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"\\s*[^,}\\]:\\s]*?([,}\\]])", "\"$1");
            
            return json;
        }

        private MBSEClassificationResult ParseEnhancedMBSEClassificationResponse(string response, string requirementText)
        {
            try
            {
                var cleanedResponse = CleanJsonResponse(response);
                
                var jsonDoc = JsonDocument.Parse(cleanedResponse);
                var root = jsonDoc.RootElement;

                var result = new MBSEClassificationResult
                {
                    OverallMBSEScore = root.GetProperty("overallMBSEScore").GetDouble(),
                    ClassificationRationale = root.GetProperty("rationale").GetString() ?? ""
                };

                // Parse classification type
                var classificationTypeStr = root.GetProperty("classificationType").GetString();
                result.ClassificationType = classificationTypeStr switch
                {
                    "SystemLevel" => RequirementClassificationType.SystemLevel,
                    "DerivedRequirement" => RequirementClassificationType.DerivedRequirement,
                    "ComponentLevel" => RequirementClassificationType.ComponentLevel,
                    "ImplementationConstraint" => RequirementClassificationType.ImplementationConstraint,
                    _ => RequirementClassificationType.Invalid
                };

                // Parse criteria scores
                if (root.TryGetProperty("criteriaScores", out var criteriaElement))
                {
                    result.CriteriaScores = new MBSECriteriaEvaluation
                    {
                        BoundaryBasedScore = criteriaElement.GetProperty("boundaryBased").GetDouble(),
                        ImplementationAgnosticScore = criteriaElement.GetProperty("implementationAgnostic").GetDouble(),
                        SystemVerifiableScore = criteriaElement.GetProperty("systemVerifiable").GetDouble(),
                        StakeholderTraceableScore = criteriaElement.GetProperty("stakeholderTraceable").GetDouble(),
                        AllocatableScore = criteriaElement.GetProperty("allocatable").GetDouble(),
                        ContextCompleteScore = criteriaElement.GetProperty("contextComplete").GetDouble()
                    };
                }

                // Parse arrays
                if (root.TryGetProperty("blockingIssues", out var issuesElement))
                {
                    result.BlockingIssues = issuesElement.EnumerateArray()
                        .Select(e => e.GetString() ?? "").ToList();
                }

                if (root.TryGetProperty("improvements", out var improvementsElement))
                {
                    result.ImprovementSuggestions = improvementsElement.EnumerateArray()
                        .Select(e => e.GetString() ?? "").ToList();
                }

                if (root.TryGetProperty("derivationIndicators", out var indicatorsElement))
                {
                    result.DerivationIndicators = indicatorsElement.EnumerateArray()
                        .Select(e => e.GetString() ?? "").ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse enhanced MBSE classification response for: {RequirementText}", requirementText);
                
                return new MBSEClassificationResult
                {
                    ClassificationType = RequirementClassificationType.Invalid,
                    BlockingIssues = new() { "Failed to parse classification response" }
                };
            }
        }

        private RequirementElevationResult ParseRequirementElevationResponse(string response, string originalRequirement)
        {
            try
            {
                var cleanedResponse = CleanJsonResponse(response);
                var jsonDoc = JsonDocument.Parse(cleanedResponse);
                var root = jsonDoc.RootElement;

                var result = new RequirementElevationResult
                {
                    OriginalRequirement = originalRequirement,
                    ElevatedRequirement = root.GetProperty("elevatedRequirement").GetString() ?? "",
                    ElevationSuccessful = root.GetProperty("elevationSuccessful").GetBoolean(),
                    ElevationConfidence = root.GetProperty("elevationConfidence").GetDouble(),
                    ElevationRationale = root.GetProperty("elevationRationale").GetString() ?? "",
                    SystemDomain = root.GetProperty("systemDomain").GetString() ?? "",
                    ElevatedMBSEScore = root.GetProperty("elevatedMBSEScore").GetDouble()
                };

                // Parse traceability
                if (root.TryGetProperty("traceability", out var traceElement))
                {
                    result.Traceability = new RequirementTraceability
                    {
                        TransformationType = traceElement.GetProperty("transformationType").GetString() ?? "",
                        SourceRequirement = traceElement.GetProperty("sourceRequirement").GetString() ?? "",
                        TargetRequirement = result.ElevatedRequirement,
                        TransformationRationale = traceElement.GetProperty("transformationRationale").GetString() ?? "",
                        ConfidenceScore = traceElement.GetProperty("confidenceScore").GetDouble()
                    };
                }

                // Parse elevation issues
                if (root.TryGetProperty("elevationIssues", out var issuesElement))
                {
                    result.ElevationIssues = issuesElement.EnumerateArray()
                        .Select(e => e.GetString() ?? "").ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse requirement elevation response for: {RequirementText}", originalRequirement);
                
                return new RequirementElevationResult
                {
                    OriginalRequirement = originalRequirement,
                    ElevationSuccessful = false,
                    ElevationIssues = new() { $"Failed to parse elevation response: {ex.Message}" }
                };
            }
        }

        private DerivedCapability CreateElevatedCapability(DerivedCapability original, RequirementElevationResult elevation)
        {
            return new DerivedCapability
            {
                Id = Guid.NewGuid().ToString(),
                SourceATPStep = original.SourceATPStep,
                RequirementText = elevation.ElevatedRequirement,
                TaxonomyCategory = elevation.SystemDomain,
                TaxonomySubcategory = "System Level (Elevated)",
                DerivationRationale = $"Elevated from derived requirement: {elevation.ElevationRationale}",
                ConfidenceScore = elevation.ElevationConfidence,
                VerificationIntent = "System Test",
                DerivedAt = DateTime.Now,
                MissingSpecifications = elevation.ElevationIssues.Any() 
                    ? elevation.ElevationIssues 
                    : new List<string>(),
                AllocationTargets = new List<string> { "System Level" }
            };
        }

        private bool ParseBlackBoxTestResponse(string response)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(response);
                return jsonDoc.RootElement.GetProperty("passesBlackBoxTest").GetBoolean();
            }
            catch
            {
                return false;
            }
        }

        private bool IsImplementationConstraint(string requirementText)
        {
            var implementationKeywords = new[]
            {
                "shall use", "shall implement", "shall be written in", 
                "FPGA shall", "PCB shall", "software shall use",
                "hardware shall use", "connector", "protocol",
                "C++", "VHDL", "layers", "impedance"
            };

            return implementationKeywords.Any(keyword => 
                requirementText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
}