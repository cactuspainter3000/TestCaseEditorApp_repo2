using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for deriving system capabilities from ATP (Acceptance Test Procedure) steps.
    /// Transforms test procedure text into structured system requirements using A-N taxonomy.
    /// </summary>
    public interface ISystemCapabilityDerivationService
    {
        /// <summary>
        /// Derive system capabilities from ATP content using LLM analysis and structured taxonomy.
        /// </summary>
        /// <param name="atpContent">Raw ATP content (test steps, procedures)</param>
        /// <param name="options">Optional derivation configuration</param>
        /// <param name="progressCallback">Optional callback for step-by-step progress updates</param>
        /// <param name="retrySkippedCallback">Optional callback for handling skipped items retry decisions</param>
        /// <param name="onRequirementDiscovered">Optional callback for real-time requirement streaming as capabilities are discovered</param>
        /// <returns>Complete derivation result with capabilities, rejections, and quality metrics</returns>
    Task<DerivationResult> DeriveCapabilitiesAsync(string atpContent, DerivationOptions? options = null, Action<string>? progressCallback = null, Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>? retrySkippedCallback = null, Action<Requirement>? onRequirementDiscovered = null);
        /// <returns>Derivation result for single step</returns>
        Task<DerivationResult> DeriveSingleStepAsync(string atpStep, DerivationOptions? options = null);

        /// <summary>
        /// Validate that derived capabilities follow taxonomy rules and quality standards.
        /// </summary>
        /// <param name="derivationResult">Result to validate</param>
        /// <returns>Validation result with issues and recommendations</returns>
        Task<TestCaseEditorApp.MVVM.Models.ValidationResult> ValidateDerivationAsync(DerivationResult derivationResult);

        /// <summary>
        /// Allocate derived capabilities to subsystems based on taxonomy and system architecture.
        /// </summary>
        /// <param name="capabilities">Capabilities to allocate</param>
        /// <param name="allocationOptions">Optional allocation configuration</param>
        /// <returns>Allocation result with subsystem assignments</returns>
        Task<AllocationResult> AllocateCapabilitiesAsync(List<DerivedCapability> capabilities, CapabilityAllocationOptions? allocationOptions = null);

        /// <summary>
        /// Get service health and configuration status.
        /// </summary>
        /// <returns>Service status information</returns>
        Task<ServiceStatus> GetServiceStatusAsync();
    }

    /// <summary>
    /// Configuration options for capability derivation process
    /// </summary>
    public class DerivationOptions
    {
        /// <summary>
        /// Focus on specific taxonomy categories (empty = all categories)
        /// </summary>
        public List<string> FocusCategories { get; set; } = new List<string>();

        /// <summary>
        /// System type context (avionics, automotive, medical, etc.)
        /// </summary>
        public string SystemType { get; set; } = "avionics";

        /// <summary>
        /// Include rejection analysis (find non-system-level statements)
        /// </summary>
        public bool IncludeRejectionAnalysis { get; set; } = true;

        /// <summary>
        /// Perform quality scoring and confidence analysis
        /// </summary>
        public bool EnableQualityScoring { get; set; } = true;

        /// <summary>
        /// Maximum processing time before timeout
        /// </summary>
        public TimeSpan MaxProcessingTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Timeout for individual LLM calls (per ATP step) - increased for RAG-enhanced prompts
        /// </summary>
        public TimeSpan PerStepTimeout { get; set; } = TimeSpan.FromMinutes(8);

        /// <summary>
        /// Source document metadata for traceability
        /// </summary>
        public Dictionary<string, string> SourceMetadata { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Represents an ATP step that timed out during processing
    /// </summary>
    public class SkippedAtpStep
    {
        /// <summary>
        /// The ATP step text that timed out
        /// </summary>
        public string StepText { get; set; } = string.Empty;

        /// <summary>
        /// Step number in the document
        /// </summary>
        public int StepNumber { get; set; }

        /// <summary>
        /// Step identifier
        /// </summary>
        public string StepId { get; set; } = string.Empty;

        /// <summary>
        /// Timeout duration that was attempted
        /// </summary>
        public TimeSpan TimeoutDuration { get; set; }

        /// <summary>
        /// Reason for skipping (timeout, error, etc.)
        /// </summary>
        public string SkipReason { get; set; } = string.Empty;

        /// <summary>
        /// Preview of step content for user display
        /// </summary>
        public string Preview => StepText.Length > 80 ? StepText.Substring(0, 77) + "..." : StepText;
    }

    /// <summary>
    /// User's decision about retrying skipped ATP steps
    /// </summary>
    public class TimeoutRetryDecision
    {
        /// <summary>
        /// Whether user wants to retry skipped items
        /// </summary>
        public bool ShouldRetry { get; set; }

        /// <summary>
        /// Additional timeout to add for retry attempts
        /// </summary>
        public TimeSpan ExtendedTimeout { get; set; }

        /// <summary>
        /// User-friendly description of the decision
        /// </summary>
        public string DecisionDescription { get; set; } = string.Empty;
    }

    /// <summary>
    /// Predefined timeout extension options for user selection
    /// </summary>
    public static class TimeoutExtensions
    {
        public static readonly TimeSpan Quick = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan Moderate = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan Extended = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan Maximum = TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// Configuration options for subsystem allocation
    /// </summary>
    public class AllocationOptions
    {
        /// <summary>
        /// Available subsystems for allocation (empty = use default taxonomy subsystems)
        /// </summary>
        public List<string> AvailableSubsystems { get; set; } = new List<string>();

        /// <summary>
        /// Allocation strategy (Balanced, Specialized, MinimalInterfaces)
        /// </summary>
        public string AllocationStrategy { get; set; } = "Balanced";

        /// <summary>
        /// Consider interface dependencies when allocating
        /// </summary>
        public bool ConsiderInterfaceDependencies { get; set; } = true;

        /// <summary>
        /// Maximum capabilities per subsystem (0 = no limit)
        /// </summary>
        public int MaxCapabilitiesPerSubsystem { get; set; } = 0;
    }

    // Note: Uses ServiceStatus from GenericServiceMonitor.cs for consistency
}