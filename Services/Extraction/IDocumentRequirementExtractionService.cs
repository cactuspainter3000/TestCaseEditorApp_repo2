using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services.Extraction
{
    public interface IDocumentRequirementExtractionService
    {
        Task<DocumentRequirementExtractionResult> AnalyzeAsync(string documentContent, string documentName, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ReverseValidationVerdict>> ValidateRequirementsAsync(
            IReadOnlyList<Requirement> requirements,
            string documentContent,
            string documentName,
            CancellationToken cancellationToken = default);
    }
}