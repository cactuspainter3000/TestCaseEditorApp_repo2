using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service responsible for intelligent allocation of derived capabilities to appropriate subsystems.
    /// Uses A-N taxonomy rules, capability characteristics, and subsystem patterns to make allocation decisions.
    /// </summary>
    public class CapabilityAllocator : ICapabilityAllocator
    {
        private readonly SystemRequirementTaxonomy _taxonomy;
        private readonly Dictionary<string, SubsystemProfile> _subsystemProfiles;

        public CapabilityAllocator()
        {
            _taxonomy = SystemRequirementTaxonomy.Default;
            _subsystemProfiles = InitializeSubsystemProfiles();
        }

        /// <summary>
        /// Allocates a collection of derived capabilities to appropriate subsystems based on taxonomy and characteristics
        /// </summary>
        public async Task<AllocationResult> AllocateCapabilitiesAsync(
            IEnumerable<DerivedCapability> capabilities,
            CapabilityAllocationOptions options = null)
        {
            options ??= new CapabilityAllocationOptions();
            
            var result = new AllocationResult
            {
                AllocationId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Options = options
            };

            var capabilityList = capabilities.ToList();
            var allocations = new List<SubsystemAllocation>();

            // Group capabilities by taxonomy category for efficient processing
            var categorizedCapabilities = capabilityList.GroupBy(c => c.TaxonomySubcategory);

            foreach (var categoryGroup in categorizedCapabilities)
            {
                var subcategoryCode = categoryGroup.Key;
                var categoryCapabilities = categoryGroup.ToList();

                // Get allocation rules for this taxonomy category
                var allocationRules = GetAllocationRulesForCategory(subcategoryCode);

                foreach (var capability in categoryCapabilities)
                {
                    var allocation = await AllocateSingleCapabilityAsync(capability, allocationRules, options);
                    
                    if (allocation != null)
                    {
                        allocations.Add(allocation);
                        
                        // Update capability with allocation information
                        capability.AllocationTargets = allocation.AssignedSubsystems;
                        
                        // Add metadata about allocation decision
                        capability.SourceMetadata["AllocationReason"] = allocation.AllocationReason;
                        capability.SourceMetadata["AllocationConfidence"] = allocation.ConfidenceScore.ToString("F2");
                    }
                }
            }

            result.Allocations = allocations;
            result.TotalCapabilities = capabilityList.Count;
            result.AllocatedCapabilities = allocations.Count;
            result.UnallocatedCapabilities = capabilityList.Count - allocations.Count;
            result.AverageConfidenceScore = allocations.Any() ? allocations.Average(a => a.ConfidenceScore) : 0.0;

            // Generate allocation summary and recommendations
            result.AllocationSummary = GenerateAllocationSummary(allocations);
            result.Recommendations = GenerateAllocationRecommendations(allocations, capabilityList);

            return result;
        }

        /// <summary>
        /// Allocates a single capability to appropriate subsystems
        /// </summary>
        private async Task<SubsystemAllocation> AllocateSingleCapabilityAsync(
            DerivedCapability capability,
            List<AllocationRule> rules,
            CapabilityAllocationOptions options)
        {
            var allocation = new SubsystemAllocation
            {
                CapabilityId = capability.Id,
                RequirementText = capability.RequirementText,
                TaxonomyCategory = capability.TaxonomySubcategory
            };

            var scores = new Dictionary<string, double>();

            // Calculate allocation scores for each subsystem based on rules
            foreach (var subsystemName in _subsystemProfiles.Keys)
            {
                var score = CalculateAllocationScore(capability, subsystemName, rules);
                scores[subsystemName] = score;
            }

            // Apply allocation strategy
            var selectedSubsystems = ApplyAllocationStrategy(scores, options.AllocationStrategy, options.MinConfidenceThreshold);
            
            if (!selectedSubsystems.Any())
            {
                return null; // Could not allocate with sufficient confidence
            }

            allocation.AssignedSubsystems = selectedSubsystems.Select(s => s.Key).ToList();
            allocation.ConfidenceScore = selectedSubsystems.Max(s => s.Value);
            allocation.AllocationReason = GenerateAllocationReason(capability, selectedSubsystems, rules);
            allocation.AlternativeOptions = scores.Where(s => !selectedSubsystems.ContainsKey(s.Key) && s.Value > 0.3)
                                                  .ToDictionary(s => s.Key, s => s.Value);

            return allocation;
        }

        /// <summary>
        /// Calculates allocation score for a capability-subsystem pair
        /// </summary>
        private double CalculateAllocationScore(DerivedCapability capability, string subsystemName, List<AllocationRule> rules)
        {
            var profile = _subsystemProfiles[subsystemName];
            double totalScore = 0.0;
            double totalWeight = 0.0;

            // Taxonomy-based scoring
            if (profile.TaxonomyAffinities.ContainsKey(capability.TaxonomySubcategory))
            {
                totalScore += profile.TaxonomyAffinities[capability.TaxonomySubcategory] * 0.4;
                totalWeight += 0.4;
            }

            // Keyword-based scoring
            var requirementText = capability.RequirementText.ToLower();
            foreach (var keyword in profile.KeywordPatterns)
            {
                if (requirementText.Contains(keyword.ToLower()))
                {
                    totalScore += 0.6 * 0.3; // High relevance keyword match
                    totalWeight += 0.3;
                    break;
                }
            }

            // Rule-based scoring
            foreach (var rule in rules)
            {
                if (rule.SubsystemName == subsystemName && rule.Condition(capability))
                {
                    totalScore += rule.Score * rule.Weight;
                    totalWeight += rule.Weight;
                }
            }

            // Missing specifications penalty
            var specificationPenalty = capability.MissingSpecifications.Count * 0.05;
            totalScore = Math.Max(0.0, totalScore - specificationPenalty);

            // Confidence adjustment based on derivation quality
            var confidenceMultiplier = Math.Max(0.5, capability.ConfidenceScore);
            
            return totalWeight > 0 ? (totalScore / totalWeight) * confidenceMultiplier : 0.0;
        }

        /// <summary>
        /// Applies allocation strategy to select appropriate subsystems
        /// </summary>
        private Dictionary<string, double> ApplyAllocationStrategy(
            Dictionary<string, double> scores, 
            AllocationStrategy strategy, 
            double minThreshold)
        {
            var filteredScores = scores.Where(s => s.Value >= minThreshold)
                                      .OrderByDescending(s => s.Value)
                                      .ToDictionary(s => s.Key, s => s.Value);

            return strategy switch
            {
                AllocationStrategy.HighestScore => filteredScores.Take(1).ToDictionary(s => s.Key, s => s.Value),
                AllocationStrategy.TopTwo => filteredScores.Take(2).ToDictionary(s => s.Key, s => s.Value),
                AllocationStrategy.AboveThreshold => filteredScores,
                AllocationStrategy.Distributed => DistributeAllocation(filteredScores),
                _ => filteredScores.Take(1).ToDictionary(s => s.Key, s => s.Value)
            };
        }

        /// <summary>
        /// Distributes allocation across multiple subsystems for cross-cutting concerns
        /// </summary>
        private Dictionary<string, double> DistributeAllocation(Dictionary<string, double> scores)
        {
            var result = new Dictionary<string, double>();
            var topScore = scores.Values.Max();
            
            foreach (var score in scores)
            {
                if (score.Value >= topScore * 0.7) // Include subsystems within 30% of top score
                {
                    result[score.Key] = score.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Generates human-readable explanation for allocation decision
        /// </summary>
        private string GenerateAllocationReason(
            DerivedCapability capability, 
            Dictionary<string, double> selectedSubsystems,
            List<AllocationRule> rules)
        {
            var primarySubsystem = selectedSubsystems.OrderByDescending(s => s.Value).First();
            var profile = _subsystemProfiles[primarySubsystem.Key];
            
            var reasons = new List<string>();

            // Add taxonomy reason
            if (profile.TaxonomyAffinities.ContainsKey(capability.TaxonomySubcategory))
            {
                reasons.Add($"Taxonomy match ({capability.TaxonomySubcategory})");
            }

            // Add keyword reason
            var requirementText = capability.RequirementText.ToLower();
            var matchedKeywords = profile.KeywordPatterns.Where(k => requirementText.Contains(k.ToLower())).ToList();
            if (matchedKeywords.Any())
            {
                reasons.Add($"Keyword match ({string.Join(", ", matchedKeywords.Take(2))})");
            }

            // Add rule-based reasons
            var matchedRules = rules.Where(r => r.SubsystemName == primarySubsystem.Key && r.Condition(capability)).ToList();
            if (matchedRules.Any())
            {
                reasons.Add($"Rule match ({matchedRules.First().Description})");
            }

            var reasonText = reasons.Any() ? string.Join("; ", reasons) : "Default allocation";
            return $"{reasonText} (confidence: {primarySubsystem.Value:F2})";
        }

        /// <summary>
        /// Gets allocation rules specific to a taxonomy category
        /// </summary>
        private List<AllocationRule> GetAllocationRulesForCategory(string subcategoryCode)
        {
            var rules = new List<AllocationRule>();

            // A-series (Mission and Scope)
            if (subcategoryCode.StartsWith("A"))
            {
                rules.Add(new AllocationRule("SoftwareSubsystem", 0.7, 0.3, "Mission scope requires software orchestration",
                    c => c.RequirementText.ToLower().Contains("system") || c.RequirementText.ToLower().Contains("coordinate")));
            }

            // B-series (External Interfaces)
            if (subcategoryCode.StartsWith("B"))
            {
                rules.Add(new AllocationRule("InterconnectSubsystem", 0.8, 0.4, "External interface requirement",
                    c => c.RequirementText.ToLower().Contains("interface") || c.RequirementText.ToLower().Contains("connect")));
                rules.Add(new AllocationRule("SafetySubsystem", 0.7, 0.3, "Safety interface requirement",
                    c => c.RequirementText.ToLower().Contains("safety") || c.RequirementText.ToLower().Contains("e-stop")));
            }

            // C-series (Stimulus Capabilities)
            if (subcategoryCode.StartsWith("C"))
            {
                rules.Add(new AllocationRule("PowerSubsystem", 0.9, 0.5, "Power stimulus requirement",
                    c => c.TaxonomySubcategory == "C1" || c.RequirementText.ToLower().Contains("power") || c.RequirementText.ToLower().Contains("voltage")));
                rules.Add(new AllocationRule("InstrumentationSubsystem", 0.8, 0.4, "Stimulus generation requirement",
                    c => c.RequirementText.ToLower().Contains("analog") || c.RequirementText.ToLower().Contains("digital") || c.RequirementText.ToLower().Contains("signal")));
            }

            // D-series (Measurement and Observability)
            if (subcategoryCode.StartsWith("D"))
            {
                rules.Add(new AllocationRule("InstrumentationSubsystem", 0.9, 0.5, "Measurement requirement",
                    c => c.RequirementText.ToLower().Contains("measure") || c.RequirementText.ToLower().Contains("monitor") || c.RequirementText.ToLower().Contains("sense")));
            }

            // Add more systematic rules for other categories (E-N)...
            
            return rules;
        }

        /// <summary>
        /// Initializes subsystem profiles with taxonomy affinities and characteristics
        /// </summary>
        private Dictionary<string, SubsystemProfile> InitializeSubsystemProfiles()
        {
            return new Dictionary<string, SubsystemProfile>
            {
                ["PowerSubsystem"] = new SubsystemProfile
                {
                    Name = "PowerSubsystem",
                    Description = "Power generation, distribution, and management",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["C1"] = 0.9, ["D1"] = 0.8, ["E1"] = 0.7, ["F1"] = 0.6, ["I1"] = 0.7
                    },
                    KeywordPatterns = new List<string> { "power", "voltage", "current", "rail", "supply", "battery" }
                },
                
                ["InstrumentationSubsystem"] = new SubsystemProfile
                {
                    Name = "InstrumentationSubsystem",
                    Description = "Measurement, sensing, and signal generation",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["C2"] = 0.8, ["C3"] = 0.9, ["C4"] = 0.8, ["D1"] = 0.9, ["D2"] = 0.9, ["D3"] = 0.8, ["E2"] = 0.7
                    },
                    KeywordPatterns = new List<string> { "measure", "analog", "digital", "signal", "sensor", "instrument", "calibrate" }
                },

                ["SoftwareSubsystem"] = new SubsystemProfile
                {
                    Name = "SoftwareSubsystem", 
                    Description = "Test orchestration, data processing, and user interface",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["A1"] = 0.8, ["A4"] = 0.9, ["F2"] = 0.9, ["G1"] = 0.9, ["G2"] = 0.8, ["H1"] = 0.8, ["K1"] = 0.7
                    },
                    KeywordPatterns = new List<string> { "software", "algorithm", "process", "data", "interface", "user", "display" }
                },

                ["InterconnectSubsystem"] = new SubsystemProfile
                {
                    Name = "InterconnectSubsystem",
                    Description = "Physical and logical connections between components",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["B1"] = 0.9, ["B2"] = 0.9, ["B3"] = 0.8, ["E3"] = 0.7, ["I2"] = 0.8
                    },
                    KeywordPatterns = new List<string> { "cable", "connector", "interface", "connection", "network", "bus" }
                },

                ["ProtectionSubsystem"] = new SubsystemProfile
                {
                    Name = "ProtectionSubsystem",
                    Description = "Overcurrent, overvoltage, and fault protection",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["C5"] = 0.9, ["E1"] = 0.8, ["I3"] = 0.9, ["J1"] = 0.8, ["J2"] = 0.8
                    },
                    KeywordPatterns = new List<string> { "protection", "fault", "overcurrent", "overvoltage", "limit", "fuse", "breaker" }
                },

                ["OperatorWorkflowSubsystem"] = new SubsystemProfile
                {
                    Name = "OperatorWorkflowSubsystem",
                    Description = "Human machine interface and workflow management", 
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["A4"] = 0.7, ["B4"] = 0.9, ["F2"] = 0.8, ["H2"] = 0.9, ["K2"] = 0.8
                    },
                    KeywordPatterns = new List<string> { "operator", "user", "workflow", "procedure", "manual", "prompt", "display" }
                },

                ["DataHandlingSubsystem"] = new SubsystemProfile
                {
                    Name = "DataHandlingSubsystem",
                    Description = "Data acquisition, storage, and communication",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["B3"] = 0.8, ["G1"] = 0.9, ["G2"] = 0.9, ["H1"] = 0.8, ["L1"] = 0.9, ["L2"] = 0.8
                    },
                    KeywordPatterns = new List<string> { "data", "storage", "database", "record", "log", "communication", "transfer" }
                },

                ["SafetySubsystem"] = new SubsystemProfile
                {
                    Name = "SafetySubsystem",
                    Description = "Personnel and equipment safety mechanisms",
                    TaxonomyAffinities = new Dictionary<string, double>
                    {
                        ["B5"] = 0.9, ["J3"] = 0.9, ["M1"] = 0.9, ["M2"] = 0.8, ["N1"] = 0.8
                    },
                    KeywordPatterns = new List<string> { "safety", "emergency", "e-stop", "interlock", "hazard", "protection" }
                }
            };
        }

        /// <summary>
        /// Generates allocation summary statistics and insights
        /// </summary>
        private string GenerateAllocationSummary(List<SubsystemAllocation> allocations)
        {
            if (!allocations.Any())
                return "No capabilities were allocated to any subsystems.";

            var subsystemCounts = allocations
                .SelectMany(a => a.AssignedSubsystems)
                .GroupBy(s => s)
                .ToDictionary(g => g.Key, g => g.Count());

            var highestAllocation = subsystemCounts.OrderByDescending(s => s.Value).First();
            var avgConfidence = allocations.Average(a => a.ConfidenceScore);

            return $"Allocated {allocations.Count} capabilities across {subsystemCounts.Count} subsystems. " +
                   $"Primary allocation: {highestAllocation.Key} ({highestAllocation.Value} capabilities). " +
                   $"Average confidence: {avgConfidence:F2}";
        }

        /// <summary>
        /// Generates recommendations for improving allocation quality
        /// </summary>
        private List<string> GenerateAllocationRecommendations(
            List<SubsystemAllocation> allocations,
            List<DerivedCapability> allCapabilities)
        {
            var recommendations = new List<string>();

            // Check for unallocated capabilities
            var unallocated = allCapabilities.Count - allocations.Count;
            if (unallocated > 0)
            {
                recommendations.Add($"Consider reviewing {unallocated} unallocated capabilities for missing specifications or unclear requirements.");
            }

            // Check for low confidence allocations
            var lowConfidence = allocations.Where(a => a.ConfidenceScore < 0.6).ToList();
            if (lowConfidence.Any())
            {
                recommendations.Add($"Review {lowConfidence.Count} low-confidence allocations - consider adding more specific requirements or keywords.");
            }

            // Check for allocation imbalances
            var subsystemCounts = allocations
                .SelectMany(a => a.AssignedSubsystems)
                .GroupBy(s => s)
                .ToDictionary(g => g.Key, g => g.Count());

            if (subsystemCounts.Any())
            {
                var maxCount = subsystemCounts.Values.Max();
                var minCount = subsystemCounts.Values.Min();
                
                if (maxCount > minCount * 3)
                {
                    recommendations.Add("Consider reviewing allocation balance - some subsystems may be over-allocated.");
                }
            }

            return recommendations;
        }
    }
}
