using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for intelligent allocation of derived capabilities to appropriate subsystems
    /// </summary>
    public interface ICapabilityAllocator
    {
        /// <summary>
        /// Allocates a collection of derived capabilities to appropriate subsystems based on taxonomy and characteristics
        /// </summary>
        /// <param name="capabilities">Collection of capabilities to allocate</param>
        /// <param name="options">Optional allocation configuration</param>
        /// <returns>Allocation result with assignments and analysis</returns>
        Task<AllocationResult> AllocateCapabilitiesAsync(
            IEnumerable<DerivedCapability> capabilities,
            CapabilityAllocationOptions options = null);
    }
}