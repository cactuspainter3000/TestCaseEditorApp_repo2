using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for Direct RAG (Retrieval-Augmented Generation) service using Ollama embeddings.
    /// Provides document search and context enhancement capabilities without AnythingLLM dependency.
    /// Follows Architectural Guide AI patterns for service interfaces.
    /// </summary>
    public interface IDirectRagService
    {
        /// <summary>
        /// Gets whether the service is properly configured and ready to use
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Index a document from Jama attachment for future RAG queries.
        /// Creates embeddings and stores document chunks for similarity search.
        /// </summary>
        /// <param name="attachment">Jama attachment to index</param>
        /// <param name="documentContent">Full text content of the document</param>
        /// <param name="projectId">Jama project ID for scoping</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if indexing succeeded</returns>
        Task<bool> IndexDocumentAsync(JamaAttachment attachment, string documentContent, int projectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Search for requirements across all indexed documents in a project.
        /// Returns relevant document chunks with similarity scores.
        /// </summary>
        /// <param name="query">Natural language search query</param>
        /// <param name="projectId">Jama project ID to search within</param>
        /// <param name="maxResults">Maximum number of results to return (default: 5)</param>
        /// <param name="similarityThreshold">Minimum similarity score (0.0-1.0, default: 0.7)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of matching document chunks with metadata</returns>
        Task<List<DocumentSearchResult>> SearchDocumentsAsync(string query, int projectId, int maxResults = 5, float similarityThreshold = 0.7f, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get enhanced context for requirement analysis by finding related project documents.
        /// Used behind-the-scenes to improve LLM analysis quality.
        /// </summary>
        /// <param name="requirementText">The requirement text to analyze</param>
        /// <param name="projectId">Jama project ID for context</param>
        /// <param name="maxContextChunks">Maximum context chunks to return (default: 3)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Relevant context from project documents</returns>
        Task<string> GetRequirementAnalysisContextAsync(string requirementText, int projectId, int maxContextChunks = 3, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clear all indexed documents for a specific project.
        /// Useful when project documents change significantly.
        /// </summary>
        /// <param name="projectId">Jama project ID to clear</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if clearing succeeded</returns>
        Task<bool> ClearProjectIndexAsync(int projectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get statistics about indexed documents for a project.
        /// </summary>
        /// <param name="projectId">Jama project ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Index statistics</returns>
        Task<DocumentIndexStats> GetProjectIndexStatsAsync(int projectId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a search result from document RAG queries
    /// </summary>
    public class DocumentSearchResult
    {
        public string DocumentName { get; set; } = "";
        public int AttachmentId { get; set; }
        public string ChunkText { get; set; } = "";
        public float SimilarityScore { get; set; }
        public int ChunkIndex { get; set; }
        public string DocumentType { get; set; } = "";
        public DateTime LastIndexed { get; set; }
    }

    /// <summary>
    /// Statistics about document indexing for a project
    /// </summary>
    public class DocumentIndexStats
    {
        public int ProjectId { get; set; }
        public int TotalDocuments { get; set; }
        public int TotalChunks { get; set; }
        public DateTime LastIndexUpdate { get; set; }
        public List<string> IndexedDocuments { get; set; } = new();
    }
}