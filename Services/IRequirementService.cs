using System.Collections.Generic;
using TestCaseEditorApp.Models;

namespace TestCaseEditorApp.Services
{
    public interface IRequirementService
    {
        // import Jama All Data DOCX (original method)
        List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path);

        // other public entrypoints used by the app (match signatures in RequirementService)
        List<Requirement> ImportRequirementsFromWord(string path);

        string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string outPath, string project, string testPlan);
        void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath);

        // optional helper used elsewher

        // Explicit interface implementation to satisfy IRequirementService
        int CountExportableSteps(string path)
        {
            // Forward to the existing static implementation
            return RequirementService.CountExportableSteps(path);
        }
    }
}