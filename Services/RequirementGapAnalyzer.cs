using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Analyzes gaps between derived system capabilities (from ATP analysis) and existing requirements.
    /// Identifies missing requirements, overlapping coverage, and specification inconsistencies.
    /// </summary>
    public interface IRequirementGapAnalyzer
    {
        /// <summary>
        /// Performs comprehensive gap analysis comparing derived capabilities with existing requirements
        /// </summary>
        Task<GapAnalysisResult> AnalyzeGapsAsync(
            List<DerivedCapability> derivedCapabilities, 
            List<Requirement> existingRequirements,
            GapAnalysisOptions? options = null);

        /// <summary>
        /// Finds requirements that may overlap or conflict with derived capabilities
        /// </summary>
        Task<List<RequirementOverlap>> FindOverlapsAsync(
            List<DerivedCapability> derivedCapabilities,
            List<Requirement> existingRequirements,
            double similarityThreshold = 0.75);

        /// <summary>
        /// Suggests requirement updates or additions based on gap analysis
        /// </summary>
        Task<List<RequirementRecommendation>> GenerateRecommendationsAsync(GapAnalysisResult analysisResult);
    }

    public class RequirementGapAnalyzer : IRequirementGapAnalyzer
    {
        /// <summary>
        /// Performs comprehensive gap analysis between derived capabilities and existing requirements
        /// </summary>
        public async Task<GapAnalysisResult> AnalyzeGapsAsync(
            List<DerivedCapability> derivedCapabilities, 
            List<Requirement> existingRequirements,
            GapAnalysisOptions? options = null)
        {
            options ??= new GapAnalysisOptions();
            
            var result = new GapAnalysisResult
            {
                AnalysisDate = DateTime.Now,
                TotalDerivedCapabilities = derivedCapabilities.Count,
                TotalExistingRequirements = existingRequirements.Count,
                Options = options
            };

            try
            {
                // 1. Identify uncovered capabilities (gaps)
                result.UncoveredCapabilities = await FindUncoveredCapabilitiesAsync(derivedCapabilities, existingRequirements, options);
                
                // 2. Find overlapping requirements
                result.RequirementOverlaps = await FindOverlapsAsync(derivedCapabilities, existingRequirements, options.OverlapSimilarityThreshold);
                
                // 3. Detect specification inconsistencies
                result.SpecificationInconsistencies = await FindSpecificationInconsistenciesAsync(derivedCapabilities, existingRequirements);
                
                // 4. Check taxonomy coverage
                result.TaxonomyCoverage = AnalyzeTaxonomyCoverage(derivedCapabilities, existingRequirements);
                
                // 5. Validate verification method alignment
                result.VerificationAlignmentIssues = CheckVerificationMethodAlignment(derivedCapabilities, existingRequirements);
                
                // 6. Calculate overall coverage metrics
                result.CoverageMetrics = CalculateCoverageMetrics(result);
                
                result.Success = true;
                result.Summary = GenerateAnalysisSummary(result);
                
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Gap analysis failed: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Finds derived capabilities that are not adequately covered by existing requirements
        /// </summary>
        private async Task<List<UncoveredCapability>> FindUncoveredCapabilitiesAsync(
            List<DerivedCapability> derivedCapabilities,
            List<Requirement> existingRequirements,
            GapAnalysisOptions options)
        {
            var uncovered = new List<UncoveredCapability>();

            foreach (var capability in derivedCapabilities)
            {
                var coveringRequirements = FindCoveringRequirements(capability, existingRequirements, options.CoverageSimilarityThreshold);
                
                if (!coveringRequirements.Any())
                {
                    // No existing requirement covers this capability
                    uncovered.Add(new UncoveredCapability
                    {
                        Capability = capability,
                        GapType = GapType.Missing,
                        Severity = AssessGapSeverity(capability),
                        Recommendation = GenerateCapabilityRecommendation(capability, GapType.Missing),
                        EstimatedEffort = EstimateImplementationEffort(capability)
                    });
                }
                else if (coveringRequirements.Count == 1 && coveringRequirements[0].SimilarityScore < options.AdequateCoverageThreshold)
                {
                    // Partial coverage - existing requirement doesn't fully address the capability
                    uncovered.Add(new UncoveredCapability
                    {
                        Capability = capability,
                        GapType = GapType.PartialCoverage,
                        Severity = GapSeverity.Medium,
                        RelatedRequirements = coveringRequirements.Select(r => r.Requirement).ToList(),
                        Recommendation = GenerateCapabilityRecommendation(capability, GapType.PartialCoverage),
                        EstimatedEffort = EstimateImplementationEffort(capability) * 0.5 // Less effort since partial coverage exists
                    });
                }
            }

            return uncovered;
        }

        /// <summary>
        /// Finds requirements that overlap with or potentially duplicate derived capabilities
        /// </summary>
        public async Task<List<RequirementOverlap>> FindOverlapsAsync(
            List<DerivedCapability> derivedCapabilities,
            List<Requirement> existingRequirements,
            double similarityThreshold = 0.75)
        {
            var overlaps = new List<RequirementOverlap>();

            foreach (var capability in derivedCapabilities)
            {
                var similarRequirements = FindCoveringRequirements(capability, existingRequirements, similarityThreshold);
                
                if (similarRequirements.Count > 1)
                {
                    overlaps.Add(new RequirementOverlap
                    {
                        Capability = capability,
                        OverlappingRequirements = similarRequirements,
                        OverlapType = DetermineOverlapType(similarRequirements),
                        ConflictSeverity = AssessConflictSeverity(capability, similarRequirements),
                        RecommendedAction = SuggestOverlapResolution(capability, similarRequirements)
                    });
                }
            }

            return overlaps;
        }

        /// <summary>
        /// Finds requirements that cover or are similar to a given capability
        /// </summary>
        private List<RequirementCoverage> FindCoveringRequirements(
            DerivedCapability capability,
            List<Requirement> existingRequirements,
            double similarityThreshold)
        {
            var coveringReqs = new List<RequirementCoverage>();

            foreach (var requirement in existingRequirements)
            {
                var similarity = CalculateSimilarityScore(capability, requirement);
                
                if (similarity >= similarityThreshold)
                {
                    coveringReqs.Add(new RequirementCoverage
                    {
                        Requirement = requirement,
                        SimilarityScore = similarity,
                        CoverageType = DetermineCoverageType(capability, requirement, similarity),
                        SpecificationAlignment = CheckSpecificationAlignment(capability, requirement)
                    });
                }
            }

            return coveringReqs.OrderByDescending(r => r.SimilarityScore).ToList();
        }

        /// <summary>
        /// Calculates similarity between a derived capability and existing requirement
        /// </summary>
        private double CalculateSimilarityScore(DerivedCapability capability, Requirement requirement)
        {
            // Multi-factor similarity analysis
            var textSimilarity = CalculateTextSimilarity(capability.RequirementText, requirement.Description);
            var taxonomySimilarity = CalculateTaxonomySimilarity(capability, requirement);
            var verificationSimilarity = CalculateVerificationSimilarity(capability, requirement);
            var allocationSimilarity = CalculateAllocationSimilarity(capability, requirement);
            
            // Weighted combination
            return (textSimilarity * 0.4) + 
                   (taxonomySimilarity * 0.25) + 
                   (verificationSimilarity * 0.2) + 
                   (allocationSimilarity * 0.15);
        }

        /// <summary>
        /// Analyzes text similarity using token-based comparison
        /// </summary>
        private double CalculateTextSimilarity(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
                return 0.0;

            // Tokenize and normalize
            var tokens1 = TokenizeText(text1.ToLowerInvariant());
            var tokens2 = TokenizeText(text2.ToLowerInvariant());
            
            // Calculate Jaccard similarity
            var intersection = tokens1.Intersect(tokens2).Count();
            var union = tokens1.Union(tokens2).Count();
            
            return union > 0 ? (double)intersection / union : 0.0;
        }

        /// <summary>
        /// Tokenizes text for similarity analysis
        /// </summary>
        private HashSet<string> TokenizeText(string text)
        {
            // Remove common words and extract meaningful tokens
            var stopWords = new HashSet<string> { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "shall", "will", "must" };
            
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Where(token => token.Length > 2 && !stopWords.Contains(token))
                      .ToHashSet();
        }

        /// <summary>
        /// Calculates taxonomy similarity between capability and requirement
        /// </summary>
        private double CalculateTaxonomySimilarity(DerivedCapability capability, Requirement requirement)
        {
            // Check if requirement has taxonomy-related tags or categories
            if (string.IsNullOrEmpty(capability.TaxonomyCategory))
                return 0.5; // Neutral if no taxonomy info
                
            var reqTags = requirement.Tags?.ToLowerInvariant() ?? "";
            var reqType = requirement.RequirementType?.ToLowerInvariant() ?? "";
            
            // Look for taxonomy indicators in requirement
            if (reqTags.Contains(capability.TaxonomyCategory.ToLowerInvariant()) ||
                reqType.Contains(capability.TaxonomyCategory.ToLowerInvariant()))
            {
                return 1.0;
            }
            
            return 0.3; // Default similarity for different categories
        }

        /// <summary>
        /// Calculates verification method similarity
        /// </summary>
        private double CalculateVerificationSimilarity(DerivedCapability capability, Requirement requirement)
        {
            var capVerification = capability.VerificationIntent?.ToLowerInvariant() ?? "";
            var reqVerification = requirement.VerificationMethodText?.ToLowerInvariant() ?? "";
            
            if (string.IsNullOrEmpty(capVerification) || string.IsNullOrEmpty(reqVerification))
                return 0.5; // Neutral if no verification info
                
            // Direct match
            if (reqVerification.Contains(capVerification))
                return 1.0;
                
            // Similar verification methods
            var verificationSimilarities = new Dictionary<string, string[]>
            {
                {"test", new[] {"test", "testing", "validation", "verification"}},
                {"analysis", new[] {"analysis", "calculation", "modeling", "simulation"}},
                {"inspection", new[] {"inspection", "review", "examination"}},
                {"demonstration", new[] {"demonstration", "demo", "showing"}}
            };
            
            foreach (var (method, synonyms) in verificationSimilarities)
            {
                if (capVerification.Contains(method) && synonyms.Any(syn => reqVerification.Contains(syn)))
                {
                    return 0.8;
                }
            }
            
            return 0.2;
        }

        /// <summary>
        /// Calculates allocation/subsystem similarity
        /// </summary>
        private double CalculateAllocationSimilarity(DerivedCapability capability, Requirement requirement)
        {
            if (capability.AllocationTargets?.Any() != true)
                return 0.5; // Neutral if no allocation info
                
            var reqAllocations = requirement.Allocations?.ToLowerInvariant() ?? "";
            
            if (string.IsNullOrEmpty(reqAllocations))
                return 0.5;
                
            var matchCount = capability.AllocationTargets
                .Count(target => reqAllocations.Contains(target.ToLowerInvariant()));
                
            return capability.AllocationTargets.Count > 0 
                ? (double)matchCount / capability.AllocationTargets.Count 
                : 0.5;
        }

        /// <summary>
        /// Finds specification inconsistencies between capabilities and requirements
        /// </summary>
        private async Task<List<SpecificationInconsistency>> FindSpecificationInconsistenciesAsync(
            List<DerivedCapability> derivedCapabilities,
            List<Requirement> existingRequirements)
        {
            var inconsistencies = new List<SpecificationInconsistency>();

            foreach (var capability in derivedCapabilities)
            {
                var relatedRequirements = FindCoveringRequirements(capability, existingRequirements, 0.6);
                
                foreach (var reqCoverage in relatedRequirements)
                {
                    var conflicts = DetectSpecificationConflicts(capability, reqCoverage.Requirement);
                    if (conflicts.Any())
                    {
                        inconsistencies.Add(new SpecificationInconsistency
                        {
                            Capability = capability,
                            ConflictingRequirement = reqCoverage.Requirement,
                            ConflictTypes = conflicts,
                            Severity = AssessInconsistencySeverity(conflicts),
                            RecommendedResolution = SuggestResolution(capability, reqCoverage.Requirement, conflicts)
                        });
                    }
                }
            }

            return inconsistencies;
        }

        /// <summary>
        /// Detects specification conflicts between capability and requirement
        /// </summary>
        private List<ConflictType> DetectSpecificationConflicts(DerivedCapability capability, Requirement requirement)
        {
            var conflicts = new List<ConflictType>();

            // Check verification method conflicts
            if (!string.IsNullOrEmpty(capability.VerificationIntent) && 
                !string.IsNullOrEmpty(requirement.VerificationMethodText))
            {
                if (CalculateVerificationSimilarity(capability, requirement) < 0.3)
                {
                    conflicts.Add(ConflictType.VerificationMethod);
                }
            }

            // Check priority conflicts (if requirement has priority info)
            var reqPriority = ExtractPriorityFromRequirement(requirement);
            if (!string.IsNullOrEmpty(reqPriority) && 
                !string.IsNullOrEmpty(capability.Priority) &&
                !reqPriority.Equals(capability.Priority, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(ConflictType.Priority);
            }

            // Check specification completeness
            if (capability.MissingSpecifications.Any() && IsRequirementComplete(requirement))
            {
                conflicts.Add(ConflictType.CompletenessLevel);
            }

            return conflicts;
        }

        /// <summary>
        /// Analyzes taxonomy coverage between derived capabilities and existing requirements
        /// </summary>
        private TaxonomyCoverageAnalysis AnalyzeTaxonomyCoverage(
            List<DerivedCapability> derivedCapabilities,
            List<Requirement> existingRequirements)
        {
            var analysis = new TaxonomyCoverageAnalysis();

            // Get all taxonomy categories from capabilities
            var capabilityCategories = derivedCapabilities
                .Where(c => !string.IsNullOrEmpty(c.TaxonomyCategory))
                .GroupBy(c => c.TaxonomyCategory)
                .ToDictionary(g => g.Key, g => g.Count());

            // Analyze coverage for each category
            foreach (var (category, count) in capabilityCategories)
            {
                var coveringReqs = existingRequirements
                    .Where(r => RequirementCoversCategory(r, category))
                    .ToList();

                analysis.CategoryCoverage.Add(category, new CategoryCoverageInfo
                {
                    Category = category,
                    DerivedCapabilityCount = count,
                    CoveringRequirementCount = coveringReqs.Count,
                    CoverageRatio = coveringReqs.Count / (double)count,
                    UncoveredCapabilities = derivedCapabilities
                        .Where(c => c.TaxonomyCategory == category)
                        .Where(c => !HasAdequateCoverage(c, existingRequirements))
                        .ToList()
                });
            }

            analysis.OverallCoverageRatio = analysis.CategoryCoverage.Values
                .Average(c => c.CoverageRatio);

            return analysis;
        }

        /// <summary>
        /// Checks verification method alignment between capabilities and requirements
        /// </summary>
        private List<VerificationAlignmentIssue> CheckVerificationMethodAlignment(
            List<DerivedCapability> derivedCapabilities,
            List<Requirement> existingRequirements)
        {
            var issues = new List<VerificationAlignmentIssue>();

            foreach (var capability in derivedCapabilities)
            {
                var relatedReqs = FindCoveringRequirements(capability, existingRequirements, 0.7);
                
                foreach (var reqCoverage in relatedReqs)
                {
                    var verificationSimilarity = CalculateVerificationSimilarity(capability, reqCoverage.Requirement);
                    
                    if (verificationSimilarity < 0.5)
                    {
                        issues.Add(new VerificationAlignmentIssue
                        {
                            Capability = capability,
                            RequirementWithMismatch = reqCoverage.Requirement,
                            CapabilityVerificationIntent = capability.VerificationIntent,
                            RequirementVerificationMethod = reqCoverage.Requirement.VerificationMethodText,
                            AlignmentScore = verificationSimilarity,
                            RecommendedAction = SuggestVerificationAlignment(capability, reqCoverage.Requirement)
                        });
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Calculates overall coverage metrics for the gap analysis
        /// </summary>
        private CoverageMetrics CalculateCoverageMetrics(GapAnalysisResult result)
        {
            var totalCapabilities = result.TotalDerivedCapabilities;
            var uncoveredCount = result.UncoveredCapabilities.Count;
            var partialCoverageCount = result.UncoveredCapabilities.Count(u => u.GapType == GapType.PartialCoverage);
            var overlapCount = result.RequirementOverlaps.Count;
            var inconsistencyCount = result.SpecificationInconsistencies.Count;

            return new CoverageMetrics
            {
                TotalCapabilities = totalCapabilities,
                CoveredCapabilities = totalCapabilities - uncoveredCount,
                UncoveredCapabilities = uncoveredCount,
                PartialCoverageCapabilities = partialCoverageCount,
                OverlappingRequirements = overlapCount,
                SpecificationInconsistencies = inconsistencyCount,
                OverallCoveragePercentage = totalCapabilities > 0 
                    ? ((totalCapabilities - uncoveredCount) / (double)totalCapabilities) * 100 
                    : 0,
                QualityScore = CalculateQualityScore(result)
            };
        }

        /// <summary>
        /// Generates recommendations based on gap analysis results
        /// </summary>
        public async Task<List<RequirementRecommendation>> GenerateRecommendationsAsync(GapAnalysisResult analysisResult)
        {
            var recommendations = new List<RequirementRecommendation>();

            // Recommendations for uncovered capabilities
            foreach (var uncovered in analysisResult.UncoveredCapabilities)
            {
                recommendations.Add(new RequirementRecommendation
                {
                    Type = RecommendationType.AddRequirement,
                    Priority = uncovered.Severity,
                    Title = $"Add requirement for {uncovered.Capability.TaxonomyCategory} capability",
                    Description = uncovered.Recommendation,
                    EstimatedEffort = uncovered.EstimatedEffort,
                    RelatedCapability = uncovered.Capability,
                    ProposedRequirementText = GenerateProposedRequirementText(uncovered.Capability)
                });
            }

            // Recommendations for overlaps
            foreach (var overlap in analysisResult.RequirementOverlaps)
            {
                recommendations.Add(new RequirementRecommendation
                {
                    Type = RecommendationType.ResolveOverlap,
                    Priority = ConvertConflictSeverityToGapSeverity(overlap.ConflictSeverity),
                    Title = $"Resolve requirement overlap for {overlap.Capability.TaxonomyCategory}",
                    Description = overlap.RecommendedAction,
                    EstimatedEffort = EstimateOverlapResolutionEffort(overlap),
                    RelatedCapability = overlap.Capability,
                    AffectedRequirements = overlap.OverlappingRequirements.Select(o => o.Requirement).ToList()
                });
            }

            // Recommendations for inconsistencies
            foreach (var inconsistency in analysisResult.SpecificationInconsistencies)
            {
                recommendations.Add(new RequirementRecommendation
                {
                    Type = RecommendationType.ResolveInconsistency,
                    Priority = ConvertInconsistencySeverityToGapSeverity(inconsistency.Severity),
                    Title = $"Resolve specification inconsistency",
                    Description = inconsistency.RecommendedResolution,
                    EstimatedEffort = EstimateInconsistencyResolutionEffort(inconsistency),
                    RelatedCapability = inconsistency.Capability,
                    AffectedRequirements = new List<Requirement> { inconsistency.ConflictingRequirement }
                });
            }

            return recommendations.OrderBy(r => r.Priority).ThenByDescending(r => r.EstimatedEffort).ToList();
        }

        // Helper methods for generating analysis summary and other utilities
        private string GenerateAnalysisSummary(GapAnalysisResult result)
        {
            var summary = $"Gap Analysis Summary:\n" +
                         $"• Total Derived Capabilities: {result.TotalDerivedCapabilities}\n" +
                         $"• Total Existing Requirements: {result.TotalExistingRequirements}\n" +
                         $"• Coverage: {result.CoverageMetrics.OverallCoveragePercentage:F1}%\n" +
                         $"• Uncovered Capabilities: {result.CoverageMetrics.UncoveredCapabilities}\n" +
                         $"• Requirement Overlaps: {result.CoverageMetrics.OverlappingRequirements}\n" +
                         $"• Specification Inconsistencies: {result.CoverageMetrics.SpecificationInconsistencies}";

            if (result.CoverageMetrics.QualityScore > 0)
            {
                summary += $"\n• Quality Score: {result.CoverageMetrics.QualityScore:F2}/10";
            }

            return summary;
        }

        // Additional helper methods for assessments and calculations
        private GapSeverity AssessGapSeverity(DerivedCapability capability)
        {
            // High priority capabilities or safety-critical ones get high severity
            if (capability.Priority == "High" || 
                capability.TaxonomyCategory?.StartsWith("A") == true || // Safety categories
                capability.ValidationWarnings.Any(w => w.Contains("safety") || w.Contains("critical")))
            {
                return GapSeverity.High;
            }
            
            if (capability.ConfidenceScore > 0.8 && capability.MissingSpecifications.Count == 0)
            {
                return GapSeverity.Medium;
            }
            
            return GapSeverity.Low;
        }

        private string GenerateCapabilityRecommendation(DerivedCapability capability, GapType gapType)
        {
            return gapType switch
            {
                GapType.Missing => $"Create new requirement for {capability.TaxonomyCategory} capability: {capability.RequirementText}",
                GapType.PartialCoverage => $"Enhance existing requirement to fully cover {capability.TaxonomyCategory} capability requirements",
                _ => "Review and update requirement coverage"
            };
        }

        private double EstimateImplementationEffort(DerivedCapability capability)
        {
            // Estimate effort in story points (1-13 scale)
            var baseEffort = 3.0; // Base effort for any requirement
            
            // Add complexity based on missing specifications
            if (capability.MissingSpecifications.Count > 3)
                baseEffort += 2.0;
            else if (capability.MissingSpecifications.Count > 0)
                baseEffort += 1.0;
                
            // Add effort based on allocation targets
            if (capability.AllocationTargets.Count > 2)
                baseEffort += 1.0;
                
            // Reduce effort for high-confidence derivations
            if (capability.ConfidenceScore > 0.9)
                baseEffort *= 0.8;
                
            return Math.Min(baseEffort, 13.0);
        }

        // Additional helper methods would continue here...
        private OverlapType DetermineOverlapType(List<RequirementCoverage> similarRequirements) => OverlapType.Duplicate;
        private ConflictSeverity AssessConflictSeverity(DerivedCapability capability, List<RequirementCoverage> requirements) => ConflictSeverity.Medium;
        private string SuggestOverlapResolution(DerivedCapability capability, List<RequirementCoverage> requirements) => "Consolidate overlapping requirements";
        private CoverageType DetermineCoverageType(DerivedCapability capability, Requirement requirement, double similarity) => CoverageType.Full;
        private SpecificationAlignment CheckSpecificationAlignment(DerivedCapability capability, Requirement requirement) => SpecificationAlignment.Aligned;
        private InconsistencySeverity AssessInconsistencySeverity(List<ConflictType> conflicts) => InconsistencySeverity.Medium;
        private string SuggestResolution(DerivedCapability capability, Requirement requirement, List<ConflictType> conflicts) => "Align specifications";
        private string ExtractPriorityFromRequirement(Requirement requirement) => requirement.Tags?.Contains("High") == true ? "High" : "Medium";
        private bool IsRequirementComplete(Requirement requirement) => !string.IsNullOrEmpty(requirement.Description) && !string.IsNullOrEmpty(requirement.VerificationMethodText);
        private bool RequirementCoversCategory(Requirement requirement, string category) => requirement.Tags?.Contains(category) == true;
        private bool HasAdequateCoverage(DerivedCapability capability, List<Requirement> requirements) => FindCoveringRequirements(capability, requirements, 0.7).Any();
        private string SuggestVerificationAlignment(DerivedCapability capability, Requirement requirement) => $"Align verification method to {capability.VerificationIntent}";
        private double CalculateQualityScore(GapAnalysisResult result) => Math.Max(0, 10 - (result.UncoveredCapabilities.Count * 0.5) - (result.RequirementOverlaps.Count * 0.3));
        private string GenerateProposedRequirementText(DerivedCapability capability) => capability.RequirementText;
        private double EstimateOverlapResolutionEffort(RequirementOverlap overlap) => 2.0;
        private double EstimateInconsistencyResolutionEffort(SpecificationInconsistency inconsistency) => 1.5;
        
        /// <summary>
        /// Converts ConflictSeverity to GapSeverity
        /// </summary>
        private GapSeverity ConvertConflictSeverityToGapSeverity(ConflictSeverity conflictSeverity)
        {
            return conflictSeverity switch
            {
                ConflictSeverity.Low => GapSeverity.Low,
                ConflictSeverity.Medium => GapSeverity.Medium,
                ConflictSeverity.High => GapSeverity.High,
                _ => GapSeverity.Medium
            };
        }
        
        /// <summary>
        /// Converts InconsistencySeverity to GapSeverity
        /// </summary>
        private GapSeverity ConvertInconsistencySeverityToGapSeverity(InconsistencySeverity inconsistencySeverity)
        {
            return inconsistencySeverity switch
            {
                InconsistencySeverity.Low => GapSeverity.Low,
                InconsistencySeverity.Medium => GapSeverity.Medium,
                InconsistencySeverity.High => GapSeverity.High,
                _ => GapSeverity.Medium
            };
        }
    }

    #region Supporting Data Models

    /// <summary>
    /// Configuration options for gap analysis
    /// </summary>
    public class GapAnalysisOptions
    {
        public double CoverageSimilarityThreshold { get; set; } = 0.6;
        public double AdequateCoverageThreshold { get; set; } = 0.75;
        public double OverlapSimilarityThreshold { get; set; } = 0.8;
        public bool IncludeLowConfidenceCapabilities { get; set; } = true;
        public bool DetectSpecificationInconsistencies { get; set; } = true;
        public bool AnalyzeTaxonomyCoverage { get; set; } = true;
    }

    /// <summary>
    /// Overall result of gap analysis
    /// </summary>
    public class GapAnalysisResult
    {
        public DateTime AnalysisDate { get; set; }
        public int TotalDerivedCapabilities { get; set; }
        public int TotalExistingRequirements { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string Summary { get; set; } = string.Empty;
        public GapAnalysisOptions Options { get; set; } = new();
        public List<UncoveredCapability> UncoveredCapabilities { get; set; } = new();
        public List<RequirementOverlap> RequirementOverlaps { get; set; } = new();
        public List<SpecificationInconsistency> SpecificationInconsistencies { get; set; } = new();
        public TaxonomyCoverageAnalysis TaxonomyCoverage { get; set; } = new();
        public List<VerificationAlignmentIssue> VerificationAlignmentIssues { get; set; } = new();
        public CoverageMetrics CoverageMetrics { get; set; } = new();
    }

    /// <summary>
    /// Capability that lacks adequate requirement coverage
    /// </summary>
    public class UncoveredCapability
    {
        public DerivedCapability Capability { get; set; } = new();
        public GapType GapType { get; set; }
        public GapSeverity Severity { get; set; }
        public List<Requirement> RelatedRequirements { get; set; } = new();
        public string Recommendation { get; set; } = string.Empty;
        public double EstimatedEffort { get; set; }
    }

    /// <summary>
    /// Requirements that overlap with derived capabilities
    /// </summary>
    public class RequirementOverlap
    {
        public DerivedCapability Capability { get; set; } = new();
        public List<RequirementCoverage> OverlappingRequirements { get; set; } = new();
        public OverlapType OverlapType { get; set; }
        public ConflictSeverity ConflictSeverity { get; set; }
        public string RecommendedAction { get; set; } = string.Empty;
    }

    /// <summary>
    /// Specification inconsistency between capability and requirement
    /// </summary>
    public class SpecificationInconsistency
    {
        public DerivedCapability Capability { get; set; } = new();
        public Requirement ConflictingRequirement { get; set; } = new();
        public List<ConflictType> ConflictTypes { get; set; } = new();
        public InconsistencySeverity Severity { get; set; }
        public string RecommendedResolution { get; set; } = string.Empty;
    }

    /// <summary>
    /// Coverage information for a requirement
    /// </summary>
    public class RequirementCoverage
    {
        public Requirement Requirement { get; set; } = new();
        public double SimilarityScore { get; set; }
        public CoverageType CoverageType { get; set; }
        public SpecificationAlignment SpecificationAlignment { get; set; }
    }

    /// <summary>
    /// Taxonomy coverage analysis
    /// </summary>
    public class TaxonomyCoverageAnalysis
    {
        public Dictionary<string, CategoryCoverageInfo> CategoryCoverage { get; set; } = new();
        public double OverallCoverageRatio { get; set; }
    }

    /// <summary>
    /// Coverage information for a taxonomy category
    /// </summary>
    public class CategoryCoverageInfo
    {
        public string Category { get; set; } = string.Empty;
        public int DerivedCapabilityCount { get; set; }
        public int CoveringRequirementCount { get; set; }
        public double CoverageRatio { get; set; }
        public List<DerivedCapability> UncoveredCapabilities { get; set; } = new();
    }

    /// <summary>
    /// Verification method alignment issue
    /// </summary>
    public class VerificationAlignmentIssue
    {
        public DerivedCapability Capability { get; set; } = new();
        public Requirement RequirementWithMismatch { get; set; } = new();
        public string CapabilityVerificationIntent { get; set; } = string.Empty;
        public string RequirementVerificationMethod { get; set; } = string.Empty;
        public double AlignmentScore { get; set; }
        public string RecommendedAction { get; set; } = string.Empty;
    }

    /// <summary>
    /// Overall coverage metrics
    /// </summary>
    public class CoverageMetrics
    {
        public int TotalCapabilities { get; set; }
        public int CoveredCapabilities { get; set; }
        public int UncoveredCapabilities { get; set; }
        public int PartialCoverageCapabilities { get; set; }
        public int OverlappingRequirements { get; set; }
        public int SpecificationInconsistencies { get; set; }
        public double OverallCoveragePercentage { get; set; }
        public double QualityScore { get; set; }
    }

    /// <summary>
    /// Recommendation for addressing gaps
    /// </summary>
    public class RequirementRecommendation
    {
        public RecommendationType Type { get; set; }
        public GapSeverity Priority { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double EstimatedEffort { get; set; }
        public DerivedCapability? RelatedCapability { get; set; }
        public List<Requirement> AffectedRequirements { get; set; } = new();
        public string ProposedRequirementText { get; set; } = string.Empty;
    }

    #endregion

    #region Enums

    public enum GapType
    {
        Missing,
        PartialCoverage,
        SpecificationGap
    }

    public enum GapSeverity
    {
        Low,
        Medium,
        High
    }

    public enum OverlapType
    {
        Duplicate,
        Conflicting,
        Complementary
    }

    public enum ConflictSeverity
    {
        Low,
        Medium,
        High
    }

    public enum CoverageType
    {
        Full,
        Partial,
        Minimal
    }

    public enum SpecificationAlignment
    {
        Aligned,
        PartiallyAligned,
        Misaligned
    }

    public enum ConflictType
    {
        VerificationMethod,
        Priority,
        CompletenessLevel,
        TaxonomyCategory,
        AllocationTarget
    }

    public enum InconsistencySeverity
    {
        Low,
        Medium,
        High
    }

    public enum RecommendationType
    {
        AddRequirement,
        UpdateRequirement,
        ResolveOverlap,
        ResolveInconsistency,
        EnhanceCoverage
    }

    #endregion
}