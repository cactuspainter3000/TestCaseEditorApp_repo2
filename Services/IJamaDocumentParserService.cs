using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Interface for Jama document parsing service following Architectural Guide AI patterns
    /// Provides LLM-powered requirement extraction from Jama attachments (PDFs, Word, Excel)
    /// </summary>
    public interface IJamaDocumentParserService
    {
        /// <summary>
        /// Parse a Jama attachment and extract requirements using LLM analysis
        /// </summary>
        /// <param name="attachment">Jama attachment metadata object</param>
        /// <param name="projectId">Jama project ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of extracted requirements with rich metadata</returns>
        Task<List<Requirement>> ParseAttachmentAsync(JamaAttachment attachment, int projectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Parse multiple Jama attachments in batch
        /// </summary>
        /// <param name="attachmentIds">List of Jama attachment IDs</param>
        /// <param name="projectId">Jama project ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Combined list of extracted requirements from all documents</returns>
        Task<List<Requirement>> ParseAttachmentsBatchAsync(List<int> attachmentIds, int projectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if the service is properly configured
        /// </summary>
        bool IsConfigured { get; }
    }
}
