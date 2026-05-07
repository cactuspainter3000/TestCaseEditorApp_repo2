using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Classifies and scores requirements against MBSE system-level criteria.
    /// Implements the rigorous MBSE definition of system-level requirements:
    /// boundary-verifiable, implementation-agnostic, stakeholder-traceable capabilities.
    /// Also identifies derived requirements and recomposes them to system level.
    /// </summary>
    public interface IMBSERequirementClassifier
    {
        /// <summary>
        /// Evaluates a derived capability against the 6 mandatory MBSE system-level criteria
        /// and determines if it's a system requirement, derived requirement, or non-requirement.
        /// </summary>
        /// <param name="capability">The derived capability to classify</param>
        /// <param name="systemBoundaryContext">Context about the system boundary definition</param>
        /// <returns>MBSE compliance score and detailed criteria evaluation</returns>
        Task<MBSEClassificationResult> ClassifyRequirementAsync(
            DerivedCapability capability, 
            string? systemBoundaryContext = null);

        /// <summary>
        /// Identifies derived requirements and recomposes them into proper system-level requirements.
        /// Example: "UUT shall have metal nameplate" -> "System shall include externally visible product identification"
        /// </summary>
        /// <param name="derivedRequirement">The derived/subsystem requirement to elevate</param>
        /// <param name="systemBoundaryContext">Context about the system boundary</param>
        /// <returns>Recomposed system-level requirement with traceability</returns>
        Task<RequirementElevationResult> ElevateToSystemLevelAsync(
            DerivedCapability derivedRequirement,
            string? systemBoundaryContext = null);

        /// <summary>
        /// Applies the definitive MBSE litmus test: 
        /// "Can I verify this requirement while treating the system as a black box?"
        /// </summary>
        /// <param name="requirementText">The requirement text to evaluate</param>
        /// <returns>True if verifiable at system boundary, false otherwise</returns>
        Task<bool> PassesBlackBoxVerificationTestAsync(string requirementText);

        /// <summary>
        /// Enhanced filtering that identifies system requirements, elevates derived requirements,
        /// and filters out implementation noise. Provides complete requirement intelligence.
        /// </summary>
        /// <param name="capabilities">Candidate capabilities to analyze</param>
        /// <param name="minimumMBSEScore">Minimum MBSE compliance score (0.0 to 1.0)</param>
        /// <param name="enableRequirementElevation">Whether to elevate derived requirements to system level</param>
        /// <param name="maxProcessingTimeMinutes">Maximum total processing time in minutes (default: 30)</param>
        /// <returns>Filtered and elevated requirements with full traceability</returns>
        Task<EnhancedMBSEFilterResult> AnalyzeAndElevateRequirementsAsync(
            IEnumerable<DerivedCapability> capabilities, 
            double minimumMBSEScore = 0.7,
            bool enableRequirementElevation = true,
            int maxProcessingTimeMinutes = 30);

        /// <summary>
        /// Legacy method maintained for compatibility - use AnalyzeAndElevateRequirementsAsync for enhanced functionality
        /// </summary>
        [Obsolete("Use AnalyzeAndElevateRequirementsAsync for enhanced derived requirement handling")]
        Task<MBSEFilterResult> FilterToSystemLevelRequirementsAsync(
            IEnumerable<DerivedCapability> capabilities, 
            double minimumMBSEScore = 0.7);
    }

    /// <summary>
    /// Result of MBSE requirement classification with detailed scoring.
    /// Enhanced to include derived requirement detection and elevation capabilities.
    /// </summary>
    public class MBSEClassificationResult
    {
        /// <summary>Overall MBSE compliance score (0.0 to 1.0)</summary>
        public double OverallMBSEScore { get; set; }
        
        /// <summary>Actual classification type determined by analysis</summary>
        public RequirementClassificationType ClassificationType { get; set; }
        
        /// <summary>True if this qualifies as a system-level requirement</summary>
        public bool IsSystemLevelRequirement => ClassificationType == RequirementClassificationType.SystemLevel;
        
        /// <summary>True if this is a derived requirement that can be elevated</summary>
        public bool IsDerivedRequirement => ClassificationType == RequirementClassificationType.DerivedRequirement;
        
        /// <summary>Detailed evaluation against each MBSE criterion</summary>
        public MBSECriteriaEvaluation CriteriaScores { get; set; } = new();
        
        /// <summary>Explanation of classification decision</summary>
        public string ClassificationRationale { get; set; } = string.Empty;
        
        /// <summary>Specific issues that prevent system-level classification</summary>
        public List<string> BlockingIssues { get; set; } = new();
        
        /// <summary>Suggested improvements to make requirement system-level</summary>
        public List<string> ImprovementSuggestions { get; set; } = new();
        
        /// <summary>If derived requirement, indicators of what system concept it derives from</summary>
        public List<string> DerivationIndicators { get; set; } = new();
    }

    /// <summary>
    /// Classification types for requirements in MBSE analysis
    /// </summary>
    public enum RequirementClassificationType
    {
        /// <summary>True system-level requirement - boundary verifiable, implementation agnostic</summary>
        SystemLevel,
        
        /// <summary>Valid requirement expressed at wrong abstraction level - can be elevated</summary>
        DerivedRequirement,
        
        /// <summary>Component or subsystem requirement - legitimate but not system level</summary>
        ComponentLevel,
        
        /// <summary>Implementation constraint or design detail - should be filtered out</summary>
        ImplementationConstraint,
        
        /// <summary>Invalid or malformed requirement text</summary>
        Invalid
    }

    /// <summary>
    /// Result of elevating a derived requirement to system level with full traceability
    /// </summary>
    public class RequirementElevationResult
    {
        /// <summary>Original derived requirement text</summary>
        public string OriginalRequirement { get; set; } = string.Empty;
        
        /// <summary>Recomposed system-level requirement</summary>
        public string ElevatedRequirement { get; set; } = string.Empty;
        
        /// <summary>Success of the elevation process</summary>
        public bool ElevationSuccessful { get; set; }
        
        /// <summary>Confidence in the elevation (0.0 to 1.0)</summary>
        public double ElevationConfidence { get; set; }
        
        /// <summary>Explanation of how the elevation was performed</summary>
        public string ElevationRationale { get; set; } = string.Empty;
        
        /// <summary>System domain the requirement was elevated to (Interface, Performance, etc.)</summary>
        public string SystemDomain { get; set; } = string.Empty;
        
        /// <summary>Traceability information linking derived to system requirement</summary>
        public RequirementTraceability Traceability { get; set; } = new();
        
        /// <summary>MBSE score of the elevated requirement</summary>
        public double ElevatedMBSEScore { get; set; }
        
        /// <summary>Issues that prevented successful elevation</summary>
        public List<string> ElevationIssues { get; set; } = new();
    }

    /// <summary>
    /// Enhanced filter result that includes requirement elevation and complete traceability
    /// </summary>
    public class EnhancedMBSEFilterResult
    {
        /// <summary>Requirements that already met system-level criteria</summary>
        public List<DerivedCapability> NativeSystemRequirements { get; set; } = new();
        
        /// <summary>Derived requirements successfully elevated to system level</summary>
        public List<ElevatedRequirement> ElevatedRequirements { get; set; } = new();
        
        /// <summary>Derived requirements that could not be elevated</summary>
        public List<DerivedCapability> UnElevatableRequirements { get; set; } = new();
        
        /// <summary>Component-level requirements (legitimate but not system-level)</summary>
        public List<DerivedCapability> ComponentLevelRequirements { get; set; } = new();
        
        /// <summary>Implementation constraints and design details (filtered noise)</summary>
        public List<DerivedCapability> ImplementationConstraints { get; set; } = new();
        
        /// <summary>Invalid or malformed requirements</summary>
        public List<DerivedCapability> InvalidRequirements { get; set; } = new();
        
        /// <summary>Statistical summary of the enhanced analysis</summary>
        public EnhancedMBSEStatistics Statistics { get; set; } = new();
        
        /// <summary>Complete traceability matrix for all requirement transformations</summary>
        public List<RequirementTraceability> TraceabilityMatrix { get; set; } = new();

        /// <summary>
        /// Get all system-level requirements (native + elevated)
        /// </summary>
        public List<DerivedCapability> GetAllSystemRequirements()
        {
            var result = new List<DerivedCapability>(NativeSystemRequirements);
            result.AddRange(ElevatedRequirements.Select(e => e.SystemLevelCapability));
            return result;
        }
    }

    /// <summary>
    /// Elevated requirement with full traceability to its derived origin
    /// </summary>
    public class ElevatedRequirement
    {
        /// <summary>Original derived requirement</summary>
        public DerivedCapability OriginalCapability { get; set; } = new();
        
        /// <summary>Elevated system-level requirement</summary>
        public DerivedCapability SystemLevelCapability { get; set; } = new();
        
        /// <summary>Elevation process details</summary>
        public RequirementElevationResult ElevationDetails { get; set; } = new();
    }

    /// <summary>
    /// Traceability information for requirement transformations
    /// </summary>
    public class RequirementTraceability
    {
        /// <summary>Unique ID for this traceability record</summary>
        public string TraceId { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>Type of transformation performed</summary>
        public string TransformationType { get; set; } = string.Empty;
        
        /// <summary>Source requirement information</summary>
        public string SourceRequirement { get; set; } = string.Empty;
        
        /// <summary>Target/result requirement information</summary>
        public string TargetRequirement { get; set; } = string.Empty;
        
        /// <summary>Rationale for the transformation</summary>
        public string TransformationRationale { get; set; } = string.Empty;
        
        /// <summary>Timestamp of the transformation</summary>
        public DateTime TransformationTimestamp { get; set; } = DateTime.Now;
        
        /// <summary>Confidence score in the transformation</summary>
        public double ConfidenceScore { get; set; }
        
        /// <summary>Additional metadata about the transformation</summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Enhanced statistics for MBSE requirement analysis including elevation metrics
    /// </summary>
    public class EnhancedMBSEStatistics
    {
        public int TotalCandidates { get; set; }
        public int NativeSystemRequirements { get; set; }
        public int ElevatedRequirements { get; set; }
        public int UnElevatableRequirements { get; set; }
        public int ComponentLevelRequirements { get; set; }
        public int ImplementationConstraints { get; set; }
        public int InvalidRequirements { get; set; }
        
        public int TotalSystemRequirements => NativeSystemRequirements + ElevatedRequirements;
        public double SystemRequirementPercentage => TotalCandidates > 0 ? (double)TotalSystemRequirements / TotalCandidates * 100 : 0;
        public double ElevationSuccessRate => (NativeSystemRequirements + UnElevatableRequirements + ComponentLevelRequirements) > 0 
            ? (double)ElevatedRequirements / (ElevatedRequirements + UnElevatableRequirements) * 100 : 0;
        
        public double AverageMBSEScore { get; set; }
        public double AverageElevationConfidence { get; set; }

        /// <summary>
        /// Get a comprehensive summary of the enhanced MBSE analysis
        /// </summary>
        public string GetComprehensiveSummary()
        {
            return $"Enhanced MBSE Analysis: {TotalSystemRequirements}/{TotalCandidates} system requirements " +
                   $"({NativeSystemRequirements} native, {ElevatedRequirements} elevated) " +
                   $"• Elevation success: {ElevationSuccessRate:F1}% " +
                   $"• Filtered: {ComponentLevelRequirements} component-level, {ImplementationConstraints} constraints, {InvalidRequirements} invalid " +
                   $"• Avg scores: MBSE {AverageMBSEScore:F2}, Elevation {AverageElevationConfidence:F2}";
        }
    }

    /// <summary>
    /// Detailed scoring against the 6 mandatory MBSE system-level criteria.
    /// </summary>
    public class MBSECriteriaEvaluation
    {
        /// <summary>References behavior observable at system boundary (0.0-1.0)</summary>
        public double BoundaryBasedScore { get; set; }
        
        /// <summary>Does not prescribe solution architecture (0.0-1.0)</summary>
        public double ImplementationAgnosticScore { get; set; }
        
        /// <summary>Verifiable at system level without internal inspection (0.0-1.0)</summary>
        public double SystemVerifiableScore { get; set; }
        
        /// <summary>Traces to stakeholder need or mission requirement (0.0-1.0)</summary>
        public double StakeholderTraceableScore { get; set; }
        
        /// <summary>Can be allocated to subsystem requirements (0.0-1.0)</summary>
        public double AllocatableScore { get; set; }
        
        /// <summary>Defines conditions, behavior, and measurable criteria (0.0-1.0)</summary>
        public double ContextCompleteScore { get; set; }

        /// <summary>Computed average of all criteria scores</summary>
        public double AverageScore => 
            (BoundaryBasedScore + ImplementationAgnosticScore + SystemVerifiableScore + 
             StakeholderTraceableScore + AllocatableScore + ContextCompleteScore) / 6.0;
    }

    /// <summary>
    /// Result of filtering capabilities to system-level requirements.
    /// </summary>
    public class MBSEFilterResult
    {
        /// <summary>Requirements that meet MBSE system-level criteria</summary>
        public List<DerivedCapability> SystemLevelRequirements { get; set; } = new();
        
        /// <summary>Requirements that are component/subsystem level</summary>
        public List<DerivedCapability> ComponentLevelRequirements { get; set; } = new();
        
        /// <summary>Requirements that are implementation constraints</summary>
        public List<DerivedCapability> ImplementationConstraints { get; set; } = new();
        
        /// <summary>Statistical summary of filtering results</summary>
        public MBSEFilterStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// Statistics from MBSE filtering operation.
    /// </summary>
    public class MBSEFilterStatistics
    {
        public int TotalCandidates { get; set; }
        public int SystemLevelCount { get; set; }
        public int ComponentLevelCount { get; set; }
        public int ImplementationConstraintCount { get; set; }
        public double SystemLevelPercentage => TotalCandidates > 0 ? (double)SystemLevelCount / TotalCandidates * 100 : 0;
        public double AverageMBSEScore { get; set; }
    }
}