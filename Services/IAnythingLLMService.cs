using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static TestCaseEditorApp.Services.AnythingLLMService;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for AnythingLLM service following Architectural Guide AI patterns
    /// Provides testable abstraction over AnythingLLM workspace and LLM operations
    /// </summary>
    public interface IAnythingLLMService
    {
        /// <summary>
        /// Create a new workspace in AnythingLLM
        /// </summary>
        Task<Workspace?> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete a workspace from AnythingLLM
        /// </summary>
        Task<bool> DeleteWorkspaceAsync(string slug, CancellationToken cancellationToken = default);

        /// <summary>
        /// Upload a document to a workspace
        /// </summary>
        Task<bool> UploadDocumentAsync(string slug, string documentName, string content, CancellationToken cancellationToken = default);

        /// <summary>
        /// Send a chat message to a workspace and get response
        /// </summary>
        Task<string?> SendChatMessageAsync(string workspaceSlug, string message, CancellationToken cancellationToken = default);
        Task<string?> SendChatMessageAsync(string workspaceSlug, string message, TimeSpan timeout, CancellationToken cancellationToken = default);
        Task<JsonElement?> GetWorkspaceDocumentsAsync(string workspaceSlug, CancellationToken cancellationToken = default);
        Task<bool> ForceDocumentReprocessingAsync(string workspaceSlug, string documentName, string content, CancellationToken cancellationToken = default);
        Task<bool> DiagnoseVectorizationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all workspaces
        /// </summary>
        Task<List<Workspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Test connectivity to AnythingLLM service
        /// </summary>
        Task<(bool Success, string Message)> TestConnectivityAsync();

        /// <summary>
        /// Ensure AnythingLLM service is running (start if needed)
        /// </summary>
        Task<(bool Success, string Message)> EnsureServiceRunningAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if AnythingLLM service is available
        /// </summary>
        Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests RAG document retrieval with a diagnostic query
        /// </summary>
        Task<(bool success, string diagnostics)> TestDocumentAccessAsync(string workspaceSlug, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fixes workspace RAG configuration based on AnythingLLM documentation recommendations
        /// </summary>
        Task<bool> FixRagConfigurationAsync(string workspaceSlug, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks document content quality to determine if PDF extraction was successful
        /// </summary>
        Task<bool> CheckDocumentContentAsync(string workspaceSlug);

        /// <summary>
        /// Attempts alternative upload methods when PDF processing fails
        /// </summary>
        Task<bool> TryAlternativeUploadAsync(string filePath, string workspaceSlug);
    }
}
