using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for Jama Connect service following Architectural Guide AI patterns
    /// Provides testable abstraction over Jama REST API integration
    /// </summary>
    public interface IJamaConnectService
    {
        /// <summary>
        /// Whether the service has been properly configured with authentication
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Get all projects accessible to the current user
        /// </summary>
        Task<List<JamaProject>> GetProjectsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get requirements for a specific project
        /// </summary>
        Task<List<JamaItem>> GetRequirementsAsync(int projectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Convert Jama items to requirements using standard processing
        /// </summary>
        Task<List<Requirement>> ConvertToRequirementsAsync(List<JamaItem> jamaItems);

        /// <summary>
        /// Convert Jama items to requirements with enhanced enum decoding
        /// </summary>
        Task<List<Requirement>> ConvertToRequirementsWithEnumDecodingAsync(List<JamaItem> items, int projectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Test API connectivity and authentication
        /// </summary>
        Task<(bool Success, string Message)> TestConnectionAsync();
    }
}