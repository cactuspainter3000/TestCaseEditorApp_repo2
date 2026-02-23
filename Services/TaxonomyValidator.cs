using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Validation result for taxonomy classification and quality assessment
    /// </summary>
    public class TaxonomyValidationResult
    {
        /// <summary>
        /// Overall validation success
        /// </summary>
        public bool IsValid { get; set; } = true;

        /// <summary>
        /// Quality score (0.0 to 1.0) based on taxonomy compliance and clarity
        /// </summary>
        public double QualityScore { get; set; } = 1.0;

        /// <summary>
        /// Specific validation issues found
        /// </summary>
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();

        /// <summary>
        /// Suggested improvements or corrections
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();

        /// <summary>
        /// Taxonomy category coverage analysis
        /// </summary>
        public CategoryCoverageAnalysis Coverage { get; set; } = new CategoryCoverageAnalysis();
    }

    /// <summary>
    /// Individual validation issue with detailed context
    /// </summary>
    public class ValidationIssue
    {
        /// <summary>
        /// Severity level of the issue
        /// </summary>
        public TaxonomyValidationSeverity Severity { get; set; }

        /// <summary>
        /// Type of validation issue
        /// </summary>
        public ValidationIssueType IssueType { get; set; }

        /// <summary>
        /// Descriptive message about the issue
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// ID of the capability with the issue
        /// </summary>
        public string CapabilityId { get; set; } = string.Empty;

        /// <summary>
        /// Current taxonomy assignment (if applicable)
        /// </summary>
        public string CurrentCategory { get; set; } = string.Empty;

        /// <summary>
        /// Suggested taxonomy correction (if applicable)
        /// </summary>
        public string SuggestedCategory { get; set; } = string.Empty;

        /// <summary>
        /// Additional context for the issue
        /// </summary>
        public Dictionary<string, string> Context { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Severity levels for taxonomy validation issues
    /// </summary>
    public enum TaxonomyValidationSeverity
    {
        Info,       // Informational - minor suggestions
        Warning,    // Potential issues that should be reviewed
        Error,      // Clear violations that need correction
        Critical    // Blocking issues that prevent system implementation
    }

    /// <summary>
    /// Types of validation issues that can be detected
    /// </summary>
    public enum ValidationIssueType
    {
        InvalidTaxonomyCode,        // Category/subcategory codes don't exist
        IncorrectCategorization,    // Wrong taxonomy assignment for content
        MissingSpecifications,      // Too many [TBD] markers or vague requirements
        PoorRequirementQuality,     // Unclear, untestable, or non-implementable
        AllocationConflict,         // Unrealistic or conflicting subsystem assignments
        DuplicateCapability,        // Redundant capabilities that should be consolidated
        ScopeViolation,             // Not actually a system capability
        IncompleteDerivation        // Missing expected capabilities for ATP content
    }

    /// <summary>
    /// Analysis of taxonomy category coverage across derived capabilities
    /// </summary>
    public class CategoryCoverageAnalysis
    {
        /// <summary>
        /// Categories represented in the derivation
        /// </summary>
        public List<string> CoveredCategories { get; set; } = new List<string>();

        /// <summary>
        /// Categories that might be expected but are missing
        /// </summary>
        public List<string> MissingCategories { get; set; } = new List<string>();

        /// <summary>
        /// Categories that are over-represented relative to ATP content
        /// </summary>
        public List<string> OverrepresentedCategories { get; set; } = new List<string>();

        /// <summary>
        /// Distribution of capabilities across categories
        /// </summary>
        public Dictionary<string, int> CategoryDistribution { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Coverage completeness score (0.0 to 1.0)
        /// </summary>
        public double CompletenessScore { get; set; } = 1.0;
    }

    /// <summary>
    /// Configuration options for taxonomy validation
    /// </summary>
    public class TaxonomyValidationOptions
    {
        /// <summary>
        /// Minimum acceptable quality score (default: 0.7)
        /// </summary>
        public double MinimumQualityThreshold { get; set; } = 0.7;

        /// <summary>
        /// Maximum allowed [TBD] specifications per capability (default: 3)
        /// </summary>
        public int MaxTBDSpecifications { get; set; } = 3;

        /// <summary>
        /// Require specific subcategories instead of general categories
        /// </summary>
        public bool RequireSpecificSubcategories { get; set; } = true;

        /// <summary>
        /// Check for expected categories based on ATP content type
        /// </summary>
        public bool ValidateExpectedCategories { get; set; } = true;

        /// <summary>
        /// Perform duplicate capability detection
        /// </summary>
        public bool DetectDuplicates { get; set; } = true;

        /// <summary>
        /// System type for context-specific validation
        /// </summary>
        public string SystemType { get; set; } = "Generic";

        /// <summary>
        /// Expected categories for this type of ATP content
        /// </summary>
        public List<string> ExpectedCategories { get; set; } = new List<string>();
    }

    /// <summary>
    /// Service for validating derived capabilities against A-N taxonomy rules and quality standards.
    /// Ensures capability derivation results are taxonomically correct, complete, and implementable.
    /// </summary>
    public class TaxonomyValidator
    {
        private readonly SystemRequirementTaxonomy _taxonomy;
        private readonly ILogger<TaxonomyValidator> _logger;

        // Quality assessment patterns
        private static readonly Regex TBDPattern = new Regex(@"\[TBD[^\]]*\]", RegexOptions.IgnoreCase);
        private static readonly Regex VagueLanguagePattern = new Regex(@"\b(adequate|sufficient|appropriate|reasonable|user-friendly|efficient|optimal)\b", RegexOptions.IgnoreCase);
        private static readonly Regex SystemCapabilityPattern = new Regex(@"\b(system\s+shall|shall\s+provide|shall\s+be\s+capable\s+of|shall\s+support)\b", RegexOptions.IgnoreCase);

        // Expected category patterns based on content keywords
        private static readonly Dictionary<string, List<string>> CategoryKeywords = new Dictionary<string, List<string>>
        {
            ["A"] = new List<string> { "mission", "purpose", "goal", "objective", "function" },
            ["B"] = new List<string> { "interface", "connection", "external", "communication", "protocol" },
            ["C"] = new List<string> { "signal", "input", "output", "response", "stimulus", "control" },
            ["D"] = new List<string> { "measure", "monitor", "evaluate", "assessment", "criteria", "verification" },
            ["E"] = new List<string> { "analyze", "process", "calculation", "algorithm", "evaluation" },
            ["F"] = new List<string> { "sequence", "flow", "procedure", "steps", "process", "workflow" },
            ["G"] = new List<string> { "safety", "protection", "hazard", "risk", "failure", "fault", "emergency" },
            ["H"] = new List<string> { "configure", "parameter", "setting", "adjustment", "calibration" },
            ["I"] = new List<string> { "data", "information", "record", "log", "store", "retrieve", "evidence" },
            ["J"] = new List<string> { "diagnostic", "status", "health", "condition", "monitoring" },
            ["K"] = new List<string> { "performance", "timing", "speed", "throughput", "latency", "response time" },
            ["L"] = new List<string> { "operator", "user", "human", "interaction", "display", "indication" },
            ["M"] = new List<string> { "security", "access", "authentication", "authorization", "encryption" },
            ["N"] = new List<string> { "compliance", "standard", "regulation", "requirement", "specification" }
        };

        public TaxonomyValidator(ILogger<TaxonomyValidator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _taxonomy = SystemRequirementTaxonomy.Default;
        }

        /// <summary>
        /// Validate a complete set of derived capabilities for taxonomy compliance and quality
        /// </summary>
        /// <param name="capabilities">Capabilities to validate</param>
        /// <param name="sourceAtpContent">Original ATP content for context</param>
        /// <param name="options">Validation configuration options</param>
        /// <returns>Comprehensive validation result</returns>
        public TaxonomyValidationResult ValidateDerivationResult(
            List<DerivedCapability> capabilities,
            string sourceAtpContent = "",
            TaxonomyValidationOptions options = null)
        {
            try
            {
                var validationOptions = options ?? new TaxonomyValidationOptions();
                var result = new TaxonomyValidationResult();

                _logger.LogDebug("Validating {CapabilityCount} derived capabilities", capabilities.Count);

                // Validate each capability individually
                foreach (var capability in capabilities)
                {
                    var capabilityIssues = ValidateSingleCapability(capability, validationOptions);
                    result.Issues.AddRange(capabilityIssues);
                }

                // Perform cross-capability analysis
                var crossValidationIssues = ValidateCapabilitySet(capabilities, sourceAtpContent, validationOptions);
                result.Issues.AddRange(crossValidationIssues);

                // Analyze taxonomy coverage
                result.Coverage = AnalyzeCategoryCoverage(capabilities, sourceAtpContent, validationOptions);

                // Calculate overall quality score
                result.QualityScore = CalculateQualityScore(capabilities, result.Issues);
                result.IsValid = result.QualityScore >= validationOptions.MinimumQualityThreshold && 
                                !result.Issues.Any(i => i.Severity == TaxonomyValidationSeverity.Critical);

                // Generate recommendations
                result.Recommendations = GenerateRecommendations(result.Issues, result.Coverage, validationOptions);

                _logger.LogInformation("Validation completed: {IsValid}, Quality: {QualityScore:F2}, Issues: {IssueCount}",
                    result.IsValid, result.QualityScore, result.Issues.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Taxonomy validation failed");
                return new TaxonomyValidationResult
                {
                    IsValid = false,
                    QualityScore = 0.0,
                    Issues = new List<ValidationIssue>
                    {
                        new ValidationIssue
                        {
                            Severity = TaxonomyValidationSeverity.Error,
                            IssueType = ValidationIssueType.IncompleteDerivation,
                            Message = $"Validation process failed: {ex.Message}"
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Quick validation of a single capability's taxonomy assignment
        /// </summary>
        /// <param name="capability">Capability to validate</param>
        /// <param name="options">Validation options</param>
        /// <returns>List of issues found with this capability</returns>
        public List<ValidationIssue> ValidateSingleCapability(DerivedCapability capability, TaxonomyValidationOptions options = null)
        {
            var validationOptions = options ?? new TaxonomyValidationOptions();
            var issues = new List<ValidationIssue>();

            // Validate taxonomy codes exist
            if (!_taxonomy.IsValidCategory(capability.TaxonomyCategory))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Error,
                    IssueType = ValidationIssueType.InvalidTaxonomyCode,
                    Message = $"Invalid taxonomy category: {capability.TaxonomyCategory}",
                    CapabilityId = capability.Id,
                    CurrentCategory = capability.TaxonomyCategory
                });
            }

            if (!_taxonomy.IsValidSubcategory(capability.TaxonomySubcategory))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Error,
                    IssueType = ValidationIssueType.InvalidTaxonomyCode,
                    Message = $"Invalid taxonomy subcategory: {capability.TaxonomySubcategory}",
                    CapabilityId = capability.Id,
                    CurrentCategory = capability.TaxonomySubcategory
                });
            }

            // Check if subcategory matches category
            if (_taxonomy.IsValidCategory(capability.TaxonomyCategory) && 
                _taxonomy.IsValidSubcategory(capability.TaxonomySubcategory))
            {
                var category = _taxonomy.GetCategory(capability.TaxonomyCategory);
                if (category != null && !category.Subcategories.Any(s => s.Code == capability.TaxonomySubcategory))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = TaxonomyValidationSeverity.Error,
                        IssueType = ValidationIssueType.IncorrectCategorization,
                        Message = $"Subcategory {capability.TaxonomySubcategory} does not belong to category {capability.TaxonomyCategory}",
                        CapabilityId = capability.Id,
                        CurrentCategory = $"{capability.TaxonomyCategory}/{capability.TaxonomySubcategory}"
                    });
                }
            }

            // Validate requirement text quality
            var qualityIssues = ValidateRequirementQuality(capability, validationOptions);
            issues.AddRange(qualityIssues);

            // Validate categorization appropriateness
            var categorizationIssues = ValidateCategorization(capability);
            issues.AddRange(categorizationIssues);

            return issues;
        }

        /// <summary>
        /// Get suggested taxonomy category based on capability content analysis
        /// </summary>
        /// <param name="capability">Capability to analyze</param>
        /// <returns>Suggested category code or null if no clear suggestion</returns>
        public string SuggestTaxonomyCategory(DerivedCapability capability)
        {
            var text = capability.RequirementText.ToLowerInvariant();
            var scores = new Dictionary<string, int>();

            // Score based on keyword matches
            foreach (var categoryPair in CategoryKeywords)
            {
                var category = categoryPair.Key;
                var keywords = categoryPair.Value;
                
                var matchCount = keywords.Count(keyword => text.Contains(keyword));
                if (matchCount > 0)
                {
                    scores[category] = matchCount;
                }
            }

            // Return highest scoring category
            return scores.Count > 0 ? scores.OrderByDescending(s => s.Value).First().Key : null;
        }

        // Private helper methods

        private List<ValidationIssue> ValidateCapabilitySet(
            List<DerivedCapability> capabilities,
            string sourceAtpContent,
            TaxonomyValidationOptions options)
        {
            var issues = new List<ValidationIssue>();

            // Check for duplicates if enabled
            if (options.DetectDuplicates)
            {
                var duplicateIssues = DetectDuplicateCapabilities(capabilities);
                issues.AddRange(duplicateIssues);
            }

            // Validate allocation consistency
            var allocationIssues = ValidateAllocationConsistency(capabilities);
            issues.AddRange(allocationIssues);

            return issues;
        }

        private List<ValidationIssue> ValidateRequirementQuality(DerivedCapability capability, TaxonomyValidationOptions options)
        {
            var issues = new List<ValidationIssue>();
            var text = capability.RequirementText;

            // Check for excessive TBD specifications
            var tbdMatches = TBDPattern.Matches(text);
            if (tbdMatches.Count > options.MaxTBDSpecifications)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Warning,
                    IssueType = ValidationIssueType.MissingSpecifications,
                    Message = $"Too many TBD specifications ({tbdMatches.Count}), should be ≤ {options.MaxTBDSpecifications}",
                    CapabilityId = capability.Id,
                    Context = { ["TBDCount"] = tbdMatches.Count.ToString() }
                });
            }

            // Check for vague language
            var vagueMatches = VagueLanguagePattern.Matches(text);
            if (vagueMatches.Count > 0)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Warning,
                    IssueType = ValidationIssueType.PoorRequirementQuality,
                    Message = $"Contains vague language: {string.Join(", ", vagueMatches.Cast<Match>().Select(m => m.Value))}",
                    CapabilityId = capability.Id
                });
            }

            // Check for proper system capability language
            if (!SystemCapabilityPattern.IsMatch(text))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Info,
                    IssueType = ValidationIssueType.PoorRequirementQuality,
                    Message = "Consider using standard capability language ('System shall provide...', 'System shall be capable of...')",
                    CapabilityId = capability.Id
                });
            }

            return issues;
        }

        private List<ValidationIssue> ValidateCategorization(DerivedCapability capability)
        {
            var issues = new List<ValidationIssue>();
            var suggestedCategory = SuggestTaxonomyCategory(capability);
            
            if (suggestedCategory != null && suggestedCategory != capability.TaxonomyCategory)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Warning,
                    IssueType = ValidationIssueType.IncorrectCategorization,
                    Message = $"Content suggests category {suggestedCategory} rather than {capability.TaxonomyCategory}",
                    CapabilityId = capability.Id,
                    CurrentCategory = capability.TaxonomyCategory,
                    SuggestedCategory = suggestedCategory
                });
            }

            return issues;
        }

        private List<ValidationIssue> DetectDuplicateCapabilities(List<DerivedCapability> capabilities)
        {
            var issues = new List<ValidationIssue>();
            
            // Simple similarity detection - could be enhanced with more sophisticated algorithms
            for (int i = 0; i < capabilities.Count; i++)
            {
                for (int j = i + 1; j < capabilities.Count; j++)
                {
                    var cap1 = capabilities[i];
                    var cap2 = capabilities[j];
                    
                    var similarity = CalculateTextSimilarity(cap1.RequirementText, cap2.RequirementText);
                    if (similarity > 0.8) // 80% similarity threshold
                    {
                        issues.Add(new ValidationIssue
                        {
                            Severity = TaxonomyValidationSeverity.Warning,
                            IssueType = ValidationIssueType.DuplicateCapability,
                            Message = $"Potential duplicate of capability {cap2.Id} (similarity: {similarity:P0})",
                            CapabilityId = cap1.Id,
                            Context = { ["SimilarCapabilityId"] = cap2.Id, ["Similarity"] = similarity.ToString("F2") }
                        });
                    }
                }
            }

            return issues;
        }

        private List<ValidationIssue> ValidateAllocationConsistency(List<DerivedCapability> capabilities)
        {
            var issues = new List<ValidationIssue>();
            
            // Check for over-allocation to single subsystems
            var allocationCounts = capabilities
                .SelectMany(c => c.AllocationTargets)
                .GroupBy(target => target)
                .Where(g => g.Count() > 10) // Arbitrary threshold
                .ToList();

            foreach (var overallocation in allocationCounts)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = TaxonomyValidationSeverity.Info,
                    IssueType = ValidationIssueType.AllocationConflict,
                    Message = $"High allocation load on {overallocation.Key}: {overallocation.Count()} capabilities",
                    Context = { ["SubsystemName"] = overallocation.Key, ["AllocationCount"] = overallocation.Count().ToString() }
                });
            }

            return issues;
        }

        private CategoryCoverageAnalysis AnalyzeCategoryCoverage(
            List<DerivedCapability> capabilities,
            string sourceAtpContent,
            TaxonomyValidationOptions options)
        {
            var analysis = new CategoryCoverageAnalysis();
            
            // Calculate category distribution
            analysis.CategoryDistribution = capabilities
                .GroupBy(c => c.TaxonomyCategory)
                .ToDictionary(g => g.Key, g => g.Count());

            analysis.CoveredCategories = analysis.CategoryDistribution.Keys.ToList();

            // Identify potentially missing categories based on ATP content
            if (options.ValidateExpectedCategories && !string.IsNullOrEmpty(sourceAtpContent))
            {
                var expectedCategories = AnalyzeExpectedCategories(sourceAtpContent);
                analysis.MissingCategories = expectedCategories.Except(analysis.CoveredCategories).ToList();
            }

            // Calculate completeness score
            var totalExpectedCategories = Math.Max(analysis.CoveredCategories.Count + analysis.MissingCategories.Count, 1);
            analysis.CompletenessScore = (double)analysis.CoveredCategories.Count / totalExpectedCategories;

            return analysis;
        }

        private List<string> AnalyzeExpectedCategories(string atpContent)
        {
            var expectedCategories = new List<string>();
            var lowerContent = atpContent.ToLowerInvariant();

            foreach (var categoryPair in CategoryKeywords)
            {
                var category = categoryPair.Key;
                var keywords = categoryPair.Value;
                
                if (keywords.Any(keyword => lowerContent.Contains(keyword)))
                {
                    expectedCategories.Add(category);
                }
            }

            return expectedCategories;
        }

        private double CalculateQualityScore(List<DerivedCapability> capabilities, List<ValidationIssue> issues)
        {
            if (capabilities.Count == 0) return 0.0;

            // Base score from capability confidence scores
            var baseScore = capabilities.Average(c => c.ConfidenceScore);

            // Penalty for issues
            var criticalPenalty = issues.Count(i => i.Severity == TaxonomyValidationSeverity.Critical) * 0.5;
            var errorPenalty = issues.Count(i => i.Severity == TaxonomyValidationSeverity.Error) * 0.2;
            var warningPenalty = issues.Count(i => i.Severity == TaxonomyValidationSeverity.Warning) * 0.1;

            var totalPenalty = Math.Min(1.0, criticalPenalty + errorPenalty + warningPenalty);
            
            return Math.Max(0.0, baseScore - totalPenalty);
        }

        private List<string> GenerateRecommendations(
            List<ValidationIssue> issues,
            CategoryCoverageAnalysis coverage,
            TaxonomyValidationOptions options)
        {
            var recommendations = new List<string>();

            // Category-specific recommendations
            if (coverage.MissingCategories.Count > 0)
            {
                recommendations.Add($"Consider deriving capabilities in missing categories: {string.Join(", ", coverage.MissingCategories)}");
            }

            // Issue-specific recommendations
            var criticalIssues = issues.Count(i => i.Severity == TaxonomyValidationSeverity.Critical);
            if (criticalIssues > 0)
            {
                recommendations.Add($"Address {criticalIssues} critical issues before proceeding with implementation");
            }

            var tbdIssues = issues.Count(i => i.IssueType == ValidationIssueType.MissingSpecifications);
            if (tbdIssues > 0)
            {
                recommendations.Add("Work with stakeholders to specify [TBD] parameters before system design");
            }

            var duplicateIssues = issues.Count(i => i.IssueType == ValidationIssueType.DuplicateCapability);
            if (duplicateIssues > 0)
            {
                recommendations.Add("Review and consolidate duplicate or overlapping capabilities");
            }

            return recommendations;
        }

        private double CalculateTextSimilarity(string text1, string text2)
        {
            // Simple Jaccard similarity - could be enhanced with more sophisticated algorithms
            var words1 = text1.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var words2 = text2.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();

            return union == 0 ? 0.0 : (double)intersection / union;
        }
    }
}

