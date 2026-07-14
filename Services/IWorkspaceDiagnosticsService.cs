using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    public interface IWorkspaceDiagnosticsService
    {
        Task ExportAnalysisLogsAsync();
        Task CommitSelectedArtifactAsync(string artifactPath);
        Task ProbeJamaLookupFieldsAsync();
    }
}