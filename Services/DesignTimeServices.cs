using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Interfaces;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Design-time / no-op service stubs for standalone compilation
    /// Extracted from MainViewModel to reduce its size and improve maintainability
    /// </summary>
    public static class DesignTimeServices
    {
        /// <summary>
        /// Simple service provider stub for design-time scenarios
        /// </summary>
        public class SimpleServiceProviderStub : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        /// <summary>
        /// No-op requirement service for design-time scenarios
        /// </summary>
        public class NoOpRequirementService : IRequirementService
        {
            public List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path) => new List<Requirement>();
            public List<Requirement> ImportRequirementsFromWord(string path) => new List<Requirement>();
            public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string folderPath, string filePrefix, string extra) => string.Empty;
            public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath) { /* no-op */ }
        }

        /// <summary>
        /// No-op persistence service for design-time scenarios
        /// </summary>
        public class NoOpPersistenceService : IPersistenceService
        {
            public void Save<T>(string key, T value) { }
            public T? Load<T>(string keyOrPath) => default;
            public void Save(string path, Workspace workspace) { }
            public bool Exists(string path) => false;
        }

        /// <summary>
        /// No-op file dialog service for design-time scenarios
        /// </summary>
        public class NoOpFileDialogService : IFileDialogService
        {
            public string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null) => null;
            public string? ShowOpenFile(string title, string filter, string? initialDirectory = null) => null;
            public string? ShowFolderDialog(string title, string? initialDirectory = null) => null;
        }
    }
}