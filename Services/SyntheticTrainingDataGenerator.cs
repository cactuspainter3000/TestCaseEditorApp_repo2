using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Prompts;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for generating synthetic training data pairs (ATP steps -> system capabilities)
    /// to improve the quality and consistency of the capability derivation engine.
    /// </summary>
    public class SyntheticTrainingDataGenerator : ISyntheticTrainingDataGenerator
    {
        private readonly ITextGenerationService _llmService;
        private readonly ISystemCapabilityDerivationService _derivationService;
        private readonly ICapabilityDerivationPromptBuilder _promptBuilder;
        private readonly SystemRequirementTaxonomy _taxonomy;
        private readonly ILogger<SyntheticTrainingDataGenerator> _logger;

        private readonly List<string> _baseATPTemplates;
        private readonly Dictionary<string, List<string>> _domainSpecificTerms;

        public SyntheticTrainingDataGenerator(
            ITextGenerationService llmService,
            ISystemCapabilityDerivationService derivationService,
            ICapabilityDerivationPromptBuilder promptBuilder,
            ILogger<SyntheticTrainingDataGenerator> logger)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _derivationService = derivationService ?? throw new ArgumentNullException(nameof(derivationService));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _taxonomy = SystemRequirementTaxonomy.Default;

            _baseATPTemplates = InitializeATPTemplates();
            _domainSpecificTerms = InitializeDomainTerms();
        }

        /// <summary>
        /// Generates a synthetic training dataset with ATP steps and expected capability derivations
        /// </summary>
        public async Task<SyntheticTrainingDataset> GenerateTrainingDatasetAsync(
            TrainingDataGenerationOptions options)
        {
            _logger.LogInformation("Starting synthetic training data generation with {Count} examples across {Categories} taxonomy categories", 
                options.TargetExampleCount, options.TaxonomyCategoriesToInclude?.Count ?? _taxonomy.Categories.Count);

            var dataset = new SyntheticTrainingDataset
            {
                DatasetId = Guid.NewGuid().ToString(),
                GeneratedAt = DateTime.Now,
                GenerationOptions = options,
                Examples = new List<SyntheticTrainingExample>()
            };

            var categoriesToGenerate = options.TaxonomyCategoriesToInclude ?? 
                _taxonomy.Categories.Select(c => c.Code).ToList();

            var examplesPerCategory = Math.Max(1, options.TargetExampleCount / categoriesToGenerate.Count);

            foreach (var categoryCode in categoriesToGenerate)
            {
                var category = _taxonomy.Categories.FirstOrDefault(c => c.Code == categoryCode);
                if (category == null) continue;

                _logger.LogDebug("Generating {Count} examples for category {Category}", 
                    examplesPerCategory, categoryCode);

                for (int i = 0; i < examplesPerCategory; i++)
                {
                    try
                    {
                        var example = await GenerateSyntheticExampleAsync(category, options);
                        if (example != null)
                        {
                            dataset.Examples.Add(example);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to generate example {Index} for category {Category}", 
                            i, categoryCode);
                    }

                    // Add small delay to avoid overwhelming the LLM service
                    if (options.GenerationDelayMs > 0)
                    {
                        await Task.Delay(options.GenerationDelayMs);
                    }
                }
            }

            dataset.GeneratedExampleCount = dataset.Examples.Count;
            dataset.QualityMetrics = await EvaluateDatasetQualityAsync(dataset);

            _logger.LogInformation("Generated {Count} synthetic training examples with average quality score {Quality:F2}", 
                dataset.Examples.Count, dataset.QualityMetrics.AverageQualityScore);

            return dataset;
        }

        /// <summary>
        /// Generates a single synthetic training example for a specific taxonomy category
        /// </summary>
        private async Task<SyntheticTrainingExample> GenerateSyntheticExampleAsync(
            RequirementCategory category, 
            TrainingDataGenerationOptions options)
        {
            // Start with a random subcategory for focused generation
            var subcategory = category.Subcategories[new Random().Next(category.Subcategories.Count)];
            
            // Generate realistic ATP step based on category and subcategory
            var atpStep = await GenerateRealisticATPStepAsync(subcategory, options.DomainContext);
            
            if (string.IsNullOrWhiteSpace(atpStep))
            {
                return null;
            }

            // Use the actual derivation service to create the "ground truth" capability
            var derivationOptions = new DerivationOptions
            {
                SourceMetadata = new Dictionary<string, string>
                {
                    ["GenerationMethod"] = "Synthetic",
                    ["TargetCategory"] = subcategory.Code,
                    ["DomainContext"] = options.DomainContext
                }
            };

            var derivationResult = await _derivationService.DeriveCapabilitiesAsync(
                atpStep, derivationOptions);

            if (!derivationResult.DerivedCapabilities.Any())
            {
                _logger.LogWarning("Derivation service failed to generate capability for ATP step: {Step}", atpStep);
                return null;
            }

            var primaryCapability = derivationResult.DerivedCapabilities.First();

            // Create synthetic training example
            var example = new SyntheticTrainingExample
            {
                ExampleId = Guid.NewGuid().ToString(),
                GeneratedAt = DateTime.Now,
                
                // Input
                ATPStepText = atpStep,
                DomainContext = options.DomainContext,
                
                // Expected Output
                ExpectedCapability = new ExpectedCapabilityDerivation
                {
                    RequirementText = primaryCapability.RequirementText,
                    TaxonomyCategory = primaryCapability.TaxonomyCategory,
                    TaxonomySubcategory = primaryCapability.TaxonomySubcategory,
                    DerivationRationale = primaryCapability.DerivationRationale,
                    AllocationTargets = primaryCapability.AllocationTargets,
                    MissingSpecifications = primaryCapability.MissingSpecifications,
                    VerificationIntent = primaryCapability.VerificationIntent
                },
                
                // Metadata
                SourceCategory = category.Code,
                SourceSubcategory = subcategory.Code,
                GenerationMethod = "LLM_Derivation",
                QualityScore = 0.0 // Will be calculated later
            };

            // Calculate initial quality score
            example.QualityScore = CalculateExampleQualityScore(example);

            return example;
        }

        /// <summary>
        /// Generates a realistic ATP step for a given subcategory
        /// </summary>
        private async Task<string> GenerateRealisticATPStepAsync(RequirementSubcategory subcategory, string domainContext)
        {
            var prompt = BuildATPGenerationPrompt(subcategory, domainContext);
            
            try
            {
                var response = await _llmService.GenerateAsync(prompt);
                
                // Extract ATP step from response (assume LLM returns just the step text)
                var atpStep = ExtractATPStepFromResponse(response);
                
                // Validate the generated step
                if (IsValidATPStep(atpStep, subcategory))
                {
                    return atpStep;
                }
                else
                {
                    _logger.LogDebug("Generated ATP step failed validation for subcategory {Subcategory}: {Step}", 
                        subcategory.Code, atpStep);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate ATP step for subcategory {Subcategory}", subcategory.Code);
                return null;
            }
        }

        /// <summary>
        /// Builds a prompt for generating realistic ATP steps
        /// </summary>
        private string BuildATPGenerationPrompt(RequirementSubcategory subcategory, string domainContext)
        {
            var domainTerms = _domainSpecificTerms.ContainsKey(subcategory.Code) 
                ? _domainSpecificTerms[subcategory.Code] 
                : new List<string>();

            var termsText = domainTerms.Any() ? $"Include relevant terms: {string.Join(", ", domainTerms.Take(5))}" : "";

            return $@"Generate a realistic ATP (Acceptance Test Procedure) step for testing {domainContext} systems.

Target Category: {subcategory.Code} - {subcategory.Name}
Description: {subcategory.Description}
{termsText}

Requirements:
- Write as a single, specific test step (not a requirement)
- Include action verbs like: verify, measure, apply, connect, monitor
- Include specific technical values where appropriate 
- Make it realistic for avionics/test equipment domain
- Keep it concise (1-2 sentences max)
- Focus on {subcategory.Name.ToLower()}

Example ATP step format: ""Verify that [system] [does something specific] within [specification].""

Generate only the ATP step text, no additional formatting:";
        }

        /// <summary>
        /// Extracts clean ATP step text from LLM response
        /// </summary>
        private string ExtractATPStepFromResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return null;

            // Clean up the response - remove quotes, extra whitespace, etc.
            var cleaned = response.Trim();
            
            // Remove common prefixes that LLMs might add
            var prefixesToRemove = new[] { "ATP Step:", "Step:", "\"", "'" };
            foreach (var prefix in prefixesToRemove)
            {
                if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(prefix.Length).Trim();
                }
            }

            // Remove trailing quotes or periods if they seem like formatting artifacts
            cleaned = cleaned.Trim('"', '\'');

            // Ensure it's a reasonable length
            if (cleaned.Length < 10 || cleaned.Length > 500)
                return null;

            return cleaned;
        }

        /// <summary>
        /// Validates that a generated ATP step is appropriate for the target subcategory
        /// </summary>
        private bool IsValidATPStep(string atpStep, RequirementSubcategory subcategory)
        {
            if (string.IsNullOrWhiteSpace(atpStep))
                return false;

            var stepLower = atpStep.ToLower();

            // Check for action verbs (should be test procedures, not requirements)
            var actionVerbs = new[] { "verify", "measure", "monitor", "apply", "connect", "test", "check", "confirm", "ensure" };
            if (!actionVerbs.Any(verb => stepLower.Contains(verb)))
                return false;

            // Check that it's not phrased as a requirement (avoid "shall", "must")
            var requirementPhrases = new[] { " shall ", " must ", " should " };
            if (requirementPhrases.Any(phrase => stepLower.Contains(phrase)))
                return false;

            // Category-specific validation
            var categoryCode = subcategory.Code.Substring(0, 1);
            
            switch (categoryCode)
            {
                case "C": // Stimulus capabilities
                    return stepLower.Contains("apply") || stepLower.Contains("stimulus") ||
                           stepLower.Contains("generate") || stepLower.Contains("output");
                           
                case "D": // Measurement
                    return stepLower.Contains("measure") || stepLower.Contains("monitor") ||
                           stepLower.Contains("read") || stepLower.Contains("verify");
                           
                case "B": // Interfaces  
                    return stepLower.Contains("connect") || stepLower.Contains("interface") ||
                           stepLower.Contains("cable") || stepLower.Contains("port");
                           
                // Add more category-specific validations as needed
                default:
                    return true; // Generic validation passed
            }
        }

        /// <summary>
        /// Calculates a quality score for a synthetic training example
        /// </summary>
        private double CalculateExampleQualityScore(SyntheticTrainingExample example)
        {
            double score = 0.0;
            double maxScore = 0.0;

            // ATP Step Quality (30%)
            maxScore += 0.3;
            if (!string.IsNullOrWhiteSpace(example.ATPStepText))
            {
                var stepQuality = 0.0;
                var stepLower = example.ATPStepText.ToLower();
                
                // Has action verb
                if (new[] { "verify", "measure", "test", "check" }.Any(v => stepLower.Contains(v)))
                    stepQuality += 0.1;
                    
                // Has specific technical content 
                if (stepLower.Any(char.IsDigit) || stepLower.Contains("±") || stepLower.Contains("%"))
                    stepQuality += 0.1;
                    
                // Appropriate length
                if (example.ATPStepText.Length >= 20 && example.ATPStepText.Length <= 200)
                    stepQuality += 0.1;
                    
                score += stepQuality;
            }

            // Capability Quality (40%)
            maxScore += 0.4;
            if (example.ExpectedCapability != null)
            {
                var capQuality = 0.0;
                
                // Has requirement text
                if (!string.IsNullOrWhiteSpace(example.ExpectedCapability.RequirementText))
                    capQuality += 0.1;
                    
                // Has valid taxonomy assignment
                if (!string.IsNullOrWhiteSpace(example.ExpectedCapability.TaxonomySubcategory) &&
                    _taxonomy.IsValidSubcategory(example.ExpectedCapability.TaxonomySubcategory))
                    capQuality += 0.1;
                    
                // Has rationale
                if (!string.IsNullOrWhiteSpace(example.ExpectedCapability.DerivationRationale))
                    capQuality += 0.1;
                    
                // Has allocation
                if (example.ExpectedCapability.AllocationTargets?.Any() == true)
                    capQuality += 0.1;
                    
                score += capQuality;
            }

            // Consistency (30%)  
            maxScore += 0.3;
            if (example.ExpectedCapability != null && !string.IsNullOrWhiteSpace(example.SourceSubcategory))
            {
                // Check if derived category matches source category
                if (example.ExpectedCapability.TaxonomySubcategory == example.SourceSubcategory)
                    score += 0.15;
                    
                // Check semantic consistency between ATP step and derived capability
                var semanticConsistency = EvaluateSemanticConsistency(
                    example.ATPStepText, example.ExpectedCapability.RequirementText);
                score += semanticConsistency * 0.15;
            }

            return maxScore > 0 ? score / maxScore : 0.0;
        }

        /// <summary>
        /// Evaluates semantic consistency between ATP step and derived capability
        /// </summary>
        private double EvaluateSemanticConsistency(string atpStep, string capability)
        {
            if (string.IsNullOrWhiteSpace(atpStep) || string.IsNullOrWhiteSpace(capability))
                return 0.0;

            var atpWords = atpStep.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var capWords = capability.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Simple word overlap metric 
            var commonWords = atpWords.Intersect(capWords)
                .Where(w => w.Length > 3) // Ignore short words
                .Count();
                
            var totalUniqueWords = atpWords.Union(capWords).Count();
            
            return totalUniqueWords > 0 ? (double)commonWords / totalUniqueWords : 0.0;
        }

        /// <summary>
        /// Evaluates overall quality metrics for a generated dataset
        /// </summary>
        private async Task<DatasetQualityMetrics> EvaluateDatasetQualityAsync(SyntheticTrainingDataset dataset)
        {
            var metrics = new DatasetQualityMetrics();

            if (!dataset.Examples.Any())
            {
                return metrics;
            }

            // Basic statistics
            metrics.TotalExamples = dataset.Examples.Count;
            metrics.AverageQualityScore = dataset.Examples.Average(e => e.QualityScore);
            
            // Category distribution
            metrics.CategoryDistribution = dataset.Examples
                .GroupBy(e => e.SourceCategory)
                .ToDictionary(g => g.Key, g => g.Count());
                
            // Quality distribution  
            metrics.HighQualityExamples = dataset.Examples.Count(e => e.QualityScore >= 0.8);
            metrics.MediumQualityExamples = dataset.Examples.Count(e => e.QualityScore >= 0.6 && e.QualityScore < 0.8);
            metrics.LowQualityExamples = dataset.Examples.Count(e => e.QualityScore < 0.6);

            // Diversity metrics
            var uniqueATPSteps = dataset.Examples.Select(e => e.ATPStepText).Distinct().Count();
            metrics.DiversityScore = (double)uniqueATPSteps / dataset.Examples.Count;

            return metrics;
        }

        /// <summary>
        /// Initializes base ATP templates for different test scenarios
        /// </summary>
        private List<string> InitializeATPTemplates()
        {
            return new List<string>
            {
                "Verify that {system} {action} {specification}",
                "Measure {parameter} at {location} and confirm {criteria}",
                "Apply {stimulus} to {interface} and verify {response}",
                "Connect {equipment} to {port} and check {functionality}", 
                "Monitor {signal} during {operation} and record {data}",
                "Test {component} under {conditions} for {duration}",
                "Confirm {system} responds to {input} within {time}",
                "Check {parameter} remains within {limits} during {test}"
            };
        }

        /// <summary>
        /// Initializes domain-specific terminology for realistic ATP generation
        /// </summary>
        private Dictionary<string, List<string>> InitializeDomainTerms()
        {
            return new Dictionary<string, List<string>>
            {
                ["A1"] = new List<string> { "system", "UUT", "test equipment", "configuration" },
                ["B1"] = new List<string> { "voltage", "current", "power", "ground", "interface", "connector" },
                ["B2"] = new List<string> { "mechanical", "alignment", "mating", "connector", "cable" },
                ["B3"] = new List<string> { "ethernet", "network", "data", "communication", "protocol" },
                ["C1"] = new List<string> { "voltage", "current", "power supply", "rail", "sequencing" },
                ["C2"] = new List<string> { "discrete", "digital", "signal", "assert", "deassert" },
                ["C3"] = new List<string> { "analog", "waveform", "frequency", "amplitude", "stimulus" },
                ["D1"] = new List<string> { "measure", "voltage", "current", "DMM", "accuracy" },
                ["D2"] = new List<string> { "digital", "state", "logic level", "monitor", "capture" },
                ["D3"] = new List<string> { "oscilloscope", "waveform", "timing", "analysis", "capture" }
            };
        }
    }
}