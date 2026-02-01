using System;
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

        /// <summary>
        /// Get all attachments for a project
        /// </summary>
        Task<List<JamaAttachment>> GetProjectAttachmentsAsync(int projectId, CancellationToken cancellationToken = default, Action<int, int, string>? progressCallback = null, string projectName = "");

        /// <summary>
        /// Get limited attachments for a project (for faster automatic scanning)
        /// </summary>
        Task<List<JamaAttachment>> GetProjectAttachmentsLimitedAsync(int projectId, int maxItems = 20, CancellationToken cancellationToken = default);

        /// <summary>
        /// Download attachment by ID
        /// </summary>
        Task<byte[]?> DownloadAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default);
    }
}