using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Design-time / no-op IRequirementService implementation.
    /// Returns safe defaults and performs no I/O.
    /// Update to match your real IRequirementService if it has additional members.
    /// </summary>
    internal class NoOpRequirementService : IRequirementService
    {
        // Import methods: return empty lists
        public List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path) => new List<Requirement>();

        public List<Requirement> ImportRequirementsFromWord(string path) => new List<Requirement>();

        // Export methods:
        // - CSV export returns a string (e.g., path or result) in your real interface — return empty string here.
        public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string folderPath, string filePrefix, string extra)
        {
            return string.Empty;
        }

        // - Excel export returns void in your real interface; implement as no-op.
        public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath)
        {
            // no-op
        }

        // Add additional no-op implementations here if your IRequirementService declares more members.
    }
}