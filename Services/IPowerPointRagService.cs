using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for PowerPoint RAG (Retrieval-Augmented Generation) service.
    /// Allows users to upload PowerPoint presentations and query their content using natural language.
    /// Follows Architectural Guide AI patterns for service interfaces.
    /// </summary>
    public interface IPowerPointRagService
    {
        /// <summary>
        /// Gets whether the service is properly configured and ready to use
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Index a PowerPoint presentation for RAG queries.
        /// Extracts text from slides and creates embeddings for similarity search.
        /// </summary>
        /// <param name="filePath">Path to the PowerPoint file</param>
        /// <param name="documentName">Display name for the document (optional, uses filename if not provided)</param>
        /// <param name="workspaceId">Workspace identifier to group related presentations (default: "default")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if indexing succeeded</returns>
        Task<bool> IndexPowerPointAsync(string filePath, string? documentName = null, string workspaceId = "default", CancellationToken cancellationToken = default);

        /// <summary>
        /// Index PowerPoint content directly from byte array.
        /// Useful when the presentation is already loaded in memory.
        /// </summary>
        /// <param name="powerPointBytes">PowerPoint file content as byte array</param>
        /// <param name="documentName">Display name for the document</param>
        /// <param name="workspaceId">Workspace identifier to group related presentations (default: "default")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if indexing succeeded</returns>
        Task<bool> IndexPowerPointFromBytesAsync(byte[] powerPointBytes, string documentName, string workspaceId = "default", CancellationToken cancellationToken = default);

        /// <summary>
        /// Query indexed PowerPoint presentations with natural language.
        /// Returns relevant content chunks that answer the user's question.
        /// </summary>
        /// <param name="query">Natural language question about the presentations</param>
        /// <param name="workspaceId">Workspace to search within (default: "default")</param>
        /// <param name="maxResults">Maximum number of results to return (default: 5)</param>
        /// <param name="similarityThreshold">Minimum similarity score (0.0-1.0, default: 0.7)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of relevant content chunks with source information</returns>
        Task<List<PowerPointSearchResult>> QueryPresentationsAsync(string query, string workspaceId = "default", int maxResults = 5, float similarityThreshold = 0.7f, CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate a comprehensive answer to user questions using RAG.
        /// Retrieves relevant content and uses LLM to synthesize a natural language response.
        /// </summary>
        /// <param name="question">User's question about the presentations</param>
        /// <param name="workspaceId">Workspace to search within (default: "default")</param>
        /// <param name="maxContextChunks">Maximum context chunks to use for answer generation (default: 5)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Natural language answer based on presentation content</returns>
        Task<string> GenerateAnswerAsync(string question, string workspaceId = "default", int maxContextChunks = 5, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get list of all indexed presentations in a workspace.
        /// </summary>
        /// <param name="workspaceId">Workspace identifier (default: "default")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of indexed presentation metadata</returns>
        Task<List<IndexedPresentation>> GetIndexedPresentationsAsync(string workspaceId = "default", CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove a presentation from the index.
        /// </summary>
        /// <param name="documentName">Name of the document to remove</param>
        /// <param name="workspaceId">Workspace identifier (default: "default")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if removal succeeded</returns>
        Task<bool> RemovePresentationAsync(string documentName, string workspaceId = "default", CancellationToken cancellationToken = default);

        /// <summary>
        /// Clear all presentations from a workspace.
        /// </summary>
        /// <param name="workspaceId">Workspace identifier (default: "default")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if clearing succeeded</returns>
        Task<bool> ClearWorkspaceAsync(string workspaceId = "default", CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a search result from PowerPoint RAG queries
    /// </summary>
    public class PowerPointSearchResult
    {
        public string DocumentName { get; set; } = "";
        public string SlideContent { get; set; } = "";
        public int SlideNumber { get; set; }
        public float SimilarityScore { get; set; }
        public string WorkspaceId { get; set; } = "";
        public DateTime IndexedDate { get; set; }
        
        /// <summary>
        /// Brief preview of the content for display purposes
        /// </summary>
        public string ContentPreview => SlideContent.Length > 200 ? SlideContent.Substring(0, 200) + "..." : SlideContent;
    }

    /// <summary>
    /// Metadata about an indexed PowerPoint presentation
    /// </summary>
    public class IndexedPresentation
    {
        public string DocumentName { get; set; } = "";
        public int SlideCount { get; set; }
        public string WorkspaceId { get; set; } = "";
        public DateTime IndexedDate { get; set; }
        public long FileSizeBytes { get; set; }
        
        /// <summary>
        /// User-friendly file size display
        /// </summary>
        public string FileSizeDisplay
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = FileSizeBytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
}