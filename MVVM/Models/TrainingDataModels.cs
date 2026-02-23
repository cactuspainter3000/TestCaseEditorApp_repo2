using System;
using System.Collections.Generic;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents a single training example for system capability derivation.
    /// Used to build datasets for LLM training and validation.
    /// </summary>
    public class TrainingExample
    {
        /// <summary>
        /// Unique identifier for this training example
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The ATP step or test procedure input text
        /// </summary>
        public string InputAtpStep { get; set; } = string.Empty;

        /// <summary>
        /// Expected system capabilities that should be derived from this ATP step
        /// </summary>
        public List<ExpectedCapability> ExpectedCapabilities { get; set; } = new List<ExpectedCapability>();

        /// <summary>
        /// Expected items that should be rejected (not system-level requirements)
        /// </summary>
        public List<ExpectedRejection> ExpectedRejections { get; set; } = new List<ExpectedRejection>();

        /// <summary>
        /// Primary taxonomy category this example focuses on (A-N)
        /// </summary>
        public string FocusCategory { get; set; } = string.Empty;

        /// <summary>
        /// Difficulty level of this training example (Beginner, Intermediate, Advanced)
        /// </summary>
        public string DifficultyLevel { get; set; } = "Intermediate";

        /// <summary>
        /// Source of this training example (Synthetic, Manual, Real-ATP)
        /// </summary>
        public string ExampleSource { get; set; } = "Synthetic";

        /// <summary>
        /// Has this example been validated by a human expert?
        /// </summary>
        public bool IsValidated { get; set; } = false;

        /// <summary>
        /// Human validation result if this example has been reviewed
        /// </summary>
        public TrainingExampleValidation? Validation { get; set; }

        /// <summary>
        /// Creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Tags for categorizing and filtering examples (e.g., "power", "digital-io", "safety")
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Metadata about the ATP context (document type, system type, etc.)
        /// </summary>
        public Dictionary<string, string> ContextMetadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Learning objectives this example is designed to teach
        /// </summary>
        public List<string> LearningObjectives { get; set; } = new List<string>();

        /// <summary>
        /// Get a quality score for this training example (0.0 to 1.0)
        /// </summary>
        public double GetQualityScore()
        {
            if (!IsValidated) return 0.0;
            if (Validation == null) return 0.0;
            
            return Validation.OverallQuality;
        }

        /// <summary>
        /// Check if this example has complete expected outputs
        /// </summary>
        public bool IsComplete => ExpectedCapabilities.Count > 0 || ExpectedRejections.Count > 0;

        public override string ToString()
        {
            var validated = IsValidated ? "✓" : "⏳";
            return $"[{FocusCategory}] {validated} {InputAtpStep.Substring(0, Math.Min(50, InputAtpStep.Length))}...";
        }
    }

    /// <summary>
    /// Expected system capability that should be derived from ATP input
    /// </summary>
    public class ExpectedCapability
    {
        /// <summary>
        /// The derived system requirement text
        /// </summary>
        public string RequirementText { get; set; } = string.Empty;

        /// <summary>
        /// Expected taxonomy category (e.g., "C1", "D3")
        /// </summary>
        public string TaxonomyCategory { get; set; } = string.Empty;

        /// <summary>
        /// Rationale for why this capability should be derived
        /// </summary>
        public string DerivationRationale { get; set; } = string.Empty;

        /// <summary>
        /// Expected missing specifications (e.g., ["tolerance", "settling_time"])
        /// </summary>
        public List<string> ExpectedMissingSpecs { get; set; } = new List<string>();

        /// <summary>
        /// Expected allocation targets (e.g., ["PowerSubsystem", "ProtectionSubsystem"])
        /// </summary>
        public List<string> ExpectedAllocationTargets { get; set; } = new List<string>();

        /// <summary>
        /// Priority of getting this derivation correct (Critical, High, Medium, Low)
        /// </summary>
        public string Priority { get; set; } = "High";

        public override string ToString()
        {
            return $"[{TaxonomyCategory}] {RequirementText}";
        }
    }

    /// <summary>
    /// Expected rejection item that should NOT be treated as a system requirement
    /// </summary>
    public class ExpectedRejection
    {
        /// <summary>
        /// Text fragment that should be rejected
        /// </summary>
        public string TextToReject { get; set; } = string.Empty;

        /// <summary>
        /// Expected rejection reason
        /// </summary>
        public string ExpectedReason { get; set; } = string.Empty;

        /// <summary>
        /// Expected alternative level/placement (e.g., "TestArtifact", "DesignConstraint")
        /// </summary>
        public string ExpectedLevel { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"REJECT: {TextToReject} → {ExpectedReason}";
        }
    }

    /// <summary>
    /// Human validation result for a training example
    /// </summary>
    public class TrainingExampleValidation
    {
        /// <summary>
        /// Overall quality rating (0.0 to 1.0)
        /// </summary>
        public double OverallQuality { get; set; } = 0.0;

        /// <summary>
        /// Validation decision (Accept, Modify, Reject)
        /// </summary>
        public string Decision { get; set; } = "Accept";

        /// <summary>
        /// Validator feedback and comments
        /// </summary>
        public string ValidatorComments { get; set; } = string.Empty;

        /// <summary>
        /// Who performed the validation
        /// </summary>
        public string ValidatedBy { get; set; } = string.Empty;

        /// <summary>
        /// When validation was performed
        /// </summary>
        public DateTime ValidatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Specific issues identified with expected capabilities
        /// </summary>
        public List<CapabilityValidationIssue> CapabilityIssues { get; set; } = new List<CapabilityValidationIssue>();

        /// <summary>
        /// Specific issues with expected rejections
        /// </summary>
        public List<RejectionValidationIssue> RejectionIssues { get; set; } = new List<RejectionValidationIssue>();

        /// <summary>
        /// Corrections or improvements suggested
        /// </summary>
        public List<string> SuggestedImprovements { get; set; } = new List<string>();

        /// <summary>
        /// Is this example suitable for training use?
        /// </summary>
        public bool ApprovedForTraining => Decision == "Accept" && OverallQuality >= 0.7;
    }

    /// <summary>
    /// Validation issue with an expected capability
    /// </summary>
    public class CapabilityValidationIssue
    {
        public string CapabilityText { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty; // WrongCategory, IncorrectText, MissingAllocation
        public string Description { get; set; } = string.Empty;
        public string SuggestedFix { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium"; // Low, Medium, High, Critical
    }

    /// <summary>
    /// Validation issue with an expected rejection
    /// </summary>
    public class RejectionValidationIssue
    {
        public string RejectionText { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty; // ShouldAccept, WrongReason, WrongLevel
        public string Description { get; set; } = string.Empty;
        public string SuggestedFix { get; set; } = string.Empty;
        public string Severity { get; set; } = "Medium";
    }

    /// <summary>
    /// Complete dataset of training examples with management capabilities
    /// </summary>
    public class SyntheticDataset
    {
        /// <summary>
        /// Unique dataset identifier
        /// </summary>
        public string DatasetId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Human-readable dataset name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Dataset description and purpose
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Version of this dataset
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// All training examples in this dataset
        /// </summary>
        public List<TrainingExample> Examples { get; set; } = new List<TrainingExample>();

        /// <summary>
        /// Dataset creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Last modification timestamp
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Generation parameters used to create synthetic examples
        /// </summary>
        public DatasetGenerationParameters? GenerationParameters { get; set; }

        /// <summary>
        /// Statistics about the dataset
        /// </summary>
        public DatasetStatistics GetStatistics()
        {
            var stats = new DatasetStatistics
            {
                TotalExamples = Examples.Count,
                ValidatedExamples = Examples.Count(e => e.IsValidated),
                ApprovedExamples = Examples.Count(e => e.GetQualityScore() >= 0.7),
                SyntheticExamples = Examples.Count(e => e.ExampleSource == "Synthetic"),
                ManualExamples = Examples.Count(e => e.ExampleSource == "Manual"),
                RealAtpExamples = Examples.Count(e => e.ExampleSource == "Real-ATP")
            };

            stats.CategoryCoverage = Examples
                .Select(e => e.FocusCategory)
                .Where(c => !string.IsNullOrEmpty(c))
                .GroupBy(c => c)
                .ToDictionary(g => g.Key, g => g.Count());

            stats.AverageQuality = Examples.Where(e => e.IsValidated)
                .Select(e => e.GetQualityScore())
                .DefaultIfEmpty(0.0)
                .Average();

            return stats;
        }

        /// <summary>
        /// Get examples filtered by criteria
        /// </summary>
        public List<TrainingExample> GetExamples(
            string? category = null,
            bool? validated = null,
            string? difficultyLevel = null,
            double minQuality = 0.0)
        {
            return Examples.Where(example =>
                (category == null || example.FocusCategory == category) &&
                (validated == null || example.IsValidated == validated) &&
                (difficultyLevel == null || example.DifficultyLevel == difficultyLevel) &&
                example.GetQualityScore() >= minQuality
            ).ToList();
        }

        /// <summary>
        /// Split dataset into training and validation sets
        /// </summary>
        public (List<TrainingExample> training, List<TrainingExample> validation) SplitForTraining(double validationRatio = 0.2)
        {
            var approved = Examples.Where(e => e.GetQualityScore() >= 0.7).ToList();
            var shuffled = approved.OrderBy(e => Guid.NewGuid()).ToList();
            
            var validationCount = (int)(shuffled.Count * validationRatio);
            var validationSet = shuffled.Take(validationCount).ToList();
            var trainingSet = shuffled.Skip(validationCount).ToList();

            return (trainingSet, validationSet);
        }

        /// <summary>
        /// Add a new training example to the dataset
        /// </summary>
        public void AddExample(TrainingExample example)
        {
            Examples.Add(example);
            ModifiedAt = DateTime.Now;
        }

        /// <summary>
        /// Remove examples that don't meet quality standards
        /// </summary>
        public int PruneByQuality(double minQuality = 0.5)
        {
            var originalCount = Examples.Count;
            Examples.RemoveAll(e => e.IsValidated && e.GetQualityScore() < minQuality);
            ModifiedAt = DateTime.Now;
            return originalCount - Examples.Count;
        }
    }

    /// <summary>
    /// Statistical summary of a training dataset
    /// </summary>
    public class DatasetStatistics
    {
        public int TotalExamples { get; set; }
        public int ValidatedExamples { get; set; }
        public int ApprovedExamples { get; set; }
        public int SyntheticExamples { get; set; }
        public int ManualExamples { get; set; }  
        public int RealAtpExamples { get; set; }
        public double AverageQuality { get; set; }
        public Dictionary<string, int> CategoryCoverage { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Get validation completion percentage
        /// </summary>
        public double ValidationProgress => TotalExamples > 0 ? (double)ValidatedExamples / TotalExamples * 100 : 0;

        /// <summary>
        /// Get approval rate for validated examples
        /// </summary>
        public double ApprovalRate => ValidatedExamples > 0 ? (double)ApprovedExamples / ValidatedExamples * 100 : 0;

        public override string ToString()
        {
            return $"Total: {TotalExamples}, Validated: {ValidatedExamples} ({ValidationProgress:F1}%), " +
                   $"Approved: {ApprovedExamples} ({ApprovalRate:F1}%), Quality: {AverageQuality:F2}";
        }
    }

    /// <summary>
    /// Parameters used for generating synthetic training data
    /// </summary>
    public class DatasetGenerationParameters
    {
        /// <summary>
        /// Number of examples to generate per taxonomy category
        /// </summary>
        public int ExamplesPerCategory { get; set; } = 10;

        /// <summary>
        /// Distribution of difficulty levels (Beginner: 30%, Intermediate: 50%, Advanced: 20%)
        /// </summary>
        public Dictionary<string, double> DifficultyDistribution { get; set; } = new Dictionary<string, double>
        {
            ["Beginner"] = 0.3,
            ["Intermediate"] = 0.5,
            ["Advanced"] = 0.2
        };

        /// <summary>
        /// Focus categories to generate examples for (empty = all categories)
        /// </summary>
        public List<string> FocusCategories { get; set; } = new List<string>();

        /// <summary>
        /// System types to generate examples for (avionics, automotive, medical, etc.)
        /// </summary>
        public List<string> SystemTypes { get; set; } = new List<string> { "avionics" };

        /// <summary>
        /// LLM model used for generation
        /// </summary>
        public string GenerationModel { get; set; } = string.Empty;

        /// <summary>
        /// Temperature/randomness setting for generation
        /// </summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Include negative examples (rejections) in generation
        /// </summary>
        public bool IncludeNegativeExamples { get; set; } = true;

        /// <summary>
        /// Ratio of negative to positive examples
        /// </summary>
        public double NegativeExampleRatio { get; set; } = 0.3;

        /// <summary>
        /// Generation timestamp
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Result of validating a training dataset
    /// </summary>
    public class DatasetValidationResult
    {
        /// <summary>
        /// Dataset that was validated
        /// </summary>
        public string DatasetId { get; set; } = string.Empty;

        /// <summary>
        /// Overall dataset quality score (0.0 to 1.0)
        /// </summary>
        public double OverallQuality { get; set; } = 0.0;

        /// <summary>
        /// Issues found during validation
        /// </summary>
        public List<string> ValidationIssues { get; set; } = new List<string>();

        /// <summary>
        /// Recommended improvements
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();

        /// <summary>
        /// Coverage gaps (categories with insufficient examples)
        /// </summary>
        public List<string> CoverageGaps { get; set; } = new List<string>();

        /// <summary>
        /// Is this dataset ready for training use?
        /// </summary>
        public bool IsReadyForTraining => OverallQuality >= 0.8 && CoverageGaps.Count == 0;

        /// <summary>
        /// Validation timestamp
        /// </summary>
        public DateTime ValidatedAt { get; set; } = DateTime.Now;
    }
}