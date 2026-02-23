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
        /// <returns>Complete derivation result with capabilities, rejections, and quality metrics</returns>
        Task<DerivationResult> DeriveCapabilitiesAsync(string atpContent, DerivationOptions? options = null);

        /// <summary>
        /// Derive capabilities from a single ATP step (more focused analysis).
        /// </summary>
        /// <param name="atpStep">Individual test step or procedure</param>
        /// <param name="options">Optional derivation configuration</param>
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
        /// Source document metadata for traceability
        /// </summary>
        public Dictionary<string, string> SourceMetadata { get; set; } = new Dictionary<string, string>();
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