using System;
using System.Collections.Generic;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents a single system capability derived from an ATP step or test procedure.
    /// Contains the structured requirement text, taxonomy classification, and allocation information.
    /// </summary>
    public class DerivedCapability
    {
        /// <summary>
        /// Unique identifier for this derived capability
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The ATP step or test procedure text that this capability was derived from
        /// </summary>
        public string SourceATPStep { get; set; } = string.Empty;

        /// <summary>
        /// The derived system requirement text (should be atomic and testable)
        /// </summary>
        public string RequirementText { get; set; } = string.Empty;

        /// <summary>
        /// Category code from the A-N taxonomy (e.g., "C1", "D3")
        /// </summary>
        public string TaxonomyCategory { get; set; } = string.Empty;

        /// <summary>
        /// Subcategory code (e.g., "C1" for Power Provisioning)
        /// </summary>
        public string TaxonomySubcategory { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable explanation of why this capability was derived from the ATP step
        /// </summary>
        public string DerivationRationale { get; set; } = string.Empty;

        /// <summary>
        /// Specifications that are missing or need to be defined (e.g., ["tolerance", "settling_time"])
        /// </summary>
        public List<string> MissingSpecifications { get; set; } = new List<string>();

        /// <summary>
        /// Subsystems this capability should be allocated to
        /// </summary>
        public List<string> AllocationTargets { get; set; } = new List<string>();

        /// <summary>
        /// Confidence score for this derivation (0.0 to 1.0)
        /// </summary>
        public double ConfidenceScore { get; set; } = 0.0;

        /// <summary>
        /// Verification intent (Test, Analysis, Inspection, Demonstration)
        /// </summary>
        public string VerificationIntent { get; set; } = "Test";

        /// <summary>
        /// Timestamp when this capability was derived
        /// </summary>
        public DateTime DerivedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Priority level (High, Medium, Low) based on ATP context
        /// </summary>
        public string Priority { get; set; } = "Medium";

        /// <summary>
        /// Any validation warnings or issues with this derivation
        /// </summary>
        public List<string> ValidationWarnings { get; set; } = new List<string>();

        /// <summary>
        /// Metadata about the source ATP step (e.g., step ID, type, verbs, etc.)
        /// </summary>
        public Dictionary<string, string> SourceMetadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Helper method to get full taxonomy description
        /// </summary>
        public string GetTaxonomyDescription()
        {
            var taxonomy = SystemRequirementTaxonomy.Default;
            var subcategory = taxonomy.FindSubcategory(TaxonomySubcategory);
            return subcategory?.Description ?? "Unknown category";
        }

        /// <summary>
        /// Check if this capability has all required specifications
        /// </summary>
        public bool IsCompleteSpecification => MissingSpecifications.Count == 0;

        public override string ToString()
        {
            return $"[{TaxonomySubcategory}] {RequirementText}";
        }
    }

    /// <summary>
    /// Contains the complete result of deriving system capabilities from ATP content.
    /// Includes successful derivations, rejected items, and analysis metadata.
    /// </summary>
    public class DerivationResult
    {
        /// <summary>
        /// Unique identifier for this derivation session
        /// </summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Original ATP content that was processed
        /// </summary>
        public string SourceATPContent { get; set; } = string.Empty;

        /// <summary>
        /// Successfully derived system capabilities
        /// </summary>
        public List<DerivedCapability> DerivedCapabilities { get; set; } = new List<DerivedCapability>();

        /// <summary>
        /// Items from ATP that were rejected as not being system-level requirements
        /// </summary>
        public List<RejectedItem> RejectedItems { get; set; } = new List<RejectedItem>();

        /// <summary>
        /// Overall quality score for the derivation (0.0 to 1.0)
        /// </summary>
        public double QualityScore { get; set; } = 0.0;

        /// <summary>
        /// Time taken to perform the derivation analysis
        /// </summary>
        public TimeSpan ProcessingTime { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Timestamp when derivation was performed
        /// </summary>
        public DateTime ProcessedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Model/service used for derivation (e.g., "Claude-3.5-Sonnet", "GPT-4")
        /// </summary>
        public string AnalysisModel { get; set; } = string.Empty;

        /// <summary>
        /// Any errors or warnings during processing
        /// </summary>
        public List<string> ProcessingWarnings { get; set; } = new List<string>();

        /// <summary>
        /// Metadata about the ATP source (document name, section, etc.)
        /// </summary>
        public Dictionary<string, string> SourceMetadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Coverage analysis - which taxonomy categories were addressed
        /// </summary>
        public List<string> CoveredCategories => DerivedCapabilities
            .Select(c => c.TaxonomyCategory)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        /// <summary>
        /// Get statistics about the derivation
        /// </summary>
        public DerivationStatistics GetStatistics()
        {
            return new DerivationStatistics
            {
                TotalCapabilitiesDerived = DerivedCapabilities.Count,
                TotalItemsRejected = RejectedItems.Count,
                AverageConfidenceScore = DerivedCapabilities.Count > 0 
                    ? DerivedCapabilities.Average(c => c.ConfidenceScore) 
                    : 0.0,
                CategoriesAddressed = CoveredCategories.Count,
                IncompleteSpecifications = DerivedCapabilities.Count(c => !c.IsCompleteSpecification),
                ProcessingTimeMs = (int)ProcessingTime.TotalMilliseconds
            };
        }

        /// <summary>
        /// Check if the derivation was successful
        /// </summary>
        public bool IsSuccessful => DerivedCapabilities.Count > 0 && ProcessingWarnings.Count == 0;
    }

    /// <summary>
    /// Represents an ATP item that was rejected as not being a system requirement
    /// </summary>
    public class RejectedItem
    {
        /// <summary>
        /// The original ATP text that was rejected
        /// </summary>
        public string OriginalText { get; set; } = string.Empty;

        /// <summary>
        /// Reason why this item was rejected
        /// </summary>
        public string RejectionReason { get; set; } = string.Empty;

        /// <summary>
        /// Suggested level where this item belongs (e.g., "TestArtifact", "DesignConstraint", "OperatorProcedure")
        /// </summary>
        public string SuggestedLevel { get; set; } = string.Empty;

        /// <summary>
        /// Alternative placement suggestion if applicable
        /// </summary>
        public string SuggestedPlacement { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"REJECTED: {OriginalText} → {RejectionReason}";
        }
    }

    /// <summary>
    /// Statistical summary of a derivation result
    /// </summary>
    public class DerivationStatistics
    {
        public int TotalCapabilitiesDerived { get; set; }
        public int TotalItemsRejected { get; set; }
        public double AverageConfidenceScore { get; set; }
        public int CategoriesAddressed { get; set; }
        public int IncompleteSpecifications { get; set; }
        public int ProcessingTimeMs { get; set; }

        public override string ToString()
        {
            return $"Derived: {TotalCapabilitiesDerived}, Rejected: {TotalItemsRejected}, " +
                   $"Avg Confidence: {AverageConfidenceScore:F2}, Categories: {CategoriesAddressed}, " +
                   $"Processing: {ProcessingTimeMs}ms";
        }
    }

    /// <summary>
    /// Represents allocation of a capability to specific subsystems with rationale
    /// </summary>
    public class CapabilityAllocation
    {
        /// <summary>
        /// Reference to the capability being allocated
        /// </summary>
        public string CapabilityId { get; set; } = string.Empty;

        /// <summary>
        /// Target subsystem (from SystemRequirementTaxonomy.GetAllocationTargets())
        /// </summary>
        public string TargetSubsystem { get; set; } = string.Empty;

        /// <summary>
        /// Allocation type (Primary, Secondary, Interface)
        /// </summary>
        public string AllocationType { get; set; } = "Primary";

        /// <summary>
        /// Rationale for why this capability belongs to this subsystem
        /// </summary>
        public string AllocationRationale { get; set; } = string.Empty;

        /// <summary>
        /// Confidence in this allocation (0.0 to 1.0)
        /// </summary>
        public double AllocationConfidence { get; set; } = 1.0;

        /// <summary>
        /// Interface dependencies (if this allocation requires coordination with other subsystems)
        /// </summary>
        public List<string> InterfaceDependencies { get; set; } = new List<string>();

        /// <summary>
        /// Implementation complexity estimate (Low, Medium, High)
        /// </summary>
        public string ImplementationComplexity { get; set; } = "Medium";

        /// <summary>
        /// Risk factors associated with this allocation
        /// </summary>
        public List<string> RiskFactors { get; set; } = new List<string>();

        public override string ToString()
        {
            return $"{TargetSubsystem} ({AllocationType}) - {AllocationRationale}";
        }
    }

    /// <summary>
    /// Represents the allocation of a specific capability to one or more subsystems
    /// </summary>
    public class SubsystemAllocation
    {
        /// <summary>
        /// ID of the capability being allocated
        /// </summary>
        public string CapabilityId { get; set; } = string.Empty;

        /// <summary>
        /// The requirement text being allocated
        /// </summary>
        public string RequirementText { get; set; } = string.Empty;

        /// <summary>
        /// Taxonomy category of the capability
        /// </summary>
        public string TaxonomyCategory { get; set; } = string.Empty;

        /// <summary>
        /// List of subsystems this capability is assigned to
        /// </summary>
        public List<string> AssignedSubsystems { get; set; } = new List<string>();

        /// <summary>
        /// Confidence score for this allocation (0.0 to 1.0)
        /// </summary>
        public double ConfidenceScore { get; set; } = 0.0;

        /// <summary>
        /// Human-readable explanation for why this allocation was made
        /// </summary>
        public string AllocationReason { get; set; } = string.Empty;

        /// <summary>
        /// Alternative subsystem options with their scores
        /// </summary>
        public Dictionary<string, double> AlternativeOptions { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Timestamp when allocation was performed
        /// </summary>
        public DateTime AllocatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Complete result of capability allocation process
    /// </summary>
    public class AllocationResult
    {
        /// <summary>
        /// Unique identifier for this allocation session
        /// </summary>
        public string AllocationId { get; set; } = string.Empty;

        /// <summary>
        /// When the allocation was performed
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Configuration options used for allocation
        /// </summary>
        public CapabilityAllocationOptions Options { get; set; } = new CapabilityAllocationOptions();

        /// <summary>
        /// Individual capability allocations
        /// </summary>
        public List<SubsystemAllocation> Allocations { get; set; } = new List<SubsystemAllocation>();

        /// <summary>
        /// Total number of capabilities processed
        /// </summary>
        public int TotalCapabilities { get; set; } = 0;

        /// <summary>
        /// Number of capabilities successfully allocated
        /// </summary>
        public int AllocatedCapabilities { get; set; } = 0;

        /// <summary>
        /// Number of capabilities that could not be allocated
        /// </summary>
        public int UnallocatedCapabilities { get; set; } = 0;

        /// <summary>
        /// Average confidence score across all allocations
        /// </summary>
        public double AverageConfidenceScore { get; set; } = 0.0;

        /// <summary>
        /// Summary text describing allocation results
        /// </summary>
        public string AllocationSummary { get; set; } = string.Empty;

        /// <summary>
        /// Recommendations for improving allocation quality
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();

        /// <summary>
        /// Allocation success rate (0.0 to 1.0)
        /// </summary>
        public double SuccessRate => TotalCapabilities > 0 ? (double)AllocatedCapabilities / TotalCapabilities : 0.0;

        /// <summary>
        /// Get allocation statistics by subsystem
        /// </summary>
        public Dictionary<string, int> GetSubsystemStats()
        {
            return Allocations
                .SelectMany(a => a.AssignedSubsystems)
                .GroupBy(s => s)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    /// <summary>
    /// Options for controlling allocation behavior
    /// </summary>
    public class CapabilityAllocationOptions
    {
        public AllocationStrategy AllocationStrategy { get; set; } = AllocationStrategy.HighestScore;
        public double MinConfidenceThreshold { get; set; } = 0.5;
        public bool AllowMultipleAllocations { get; set; } = true;
        public bool PreferSpecializedSubsystems { get; set; } = true;
    }

    /// <summary>
    /// Strategy for determining how to allocate capabilities to subsystems
    /// </summary>
    public enum AllocationStrategy
    {
        HighestScore,      // Allocate to single highest-scoring subsystem
        TopTwo,            // Allocate to top two scoring subsystems
        AboveThreshold,    // Allocate to all subsystems above threshold
        Distributed        // Distribute across multiple relevant subsystems
    }

    /// <summary>
    /// Allocation rule for subsystem assignment decisions
    /// </summary>
    public class AllocationRule
    {
        public string SubsystemName { get; }
        public double Score { get; }
        public double Weight { get; }
        public string Description { get; }
        public Func<DerivedCapability, bool> Condition { get; }

        public AllocationRule(string subsystemName, double score, double weight, string description, Func<DerivedCapability, bool> condition)
        {
            SubsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
            Score = Math.Max(0.0, Math.Min(1.0, score));
            Weight = Math.Max(0.0, weight);
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }
    }

    /// <summary>
    /// Profile defining subsystem characteristics and allocation preferences
    /// </summary>
    public class SubsystemProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, double> TaxonomyAffinities { get; set; } = new();
        public List<string> KeywordPatterns { get; set; } = new();
    }
}