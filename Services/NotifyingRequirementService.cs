using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Wrapper around RequirementService that provides integrated notification support.
    /// Replaces MessageBox dialogs with toast notifications.
    /// </summary>
    public class NotifyingRequirementService : IRequirementService
    {
        private readonly RequirementService _inner;
        private readonly NotificationService _notificationService;

        public NotifyingRequirementService(RequirementService inner, NotificationService notificationService)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        }

        public List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path)
        {
            try
            {
                // Call the original service
                return _inner.ImportRequirementsFromJamaAllDataDocx(path);
            }
            catch (NotSupportedException nse)
            {
                _notificationService.ShowWarning(nse.Message);
                return new List<Requirement>();
            }
            catch (System.IO.IOException ioex) when (IsSharingViolation(ioex))
            {
                var file = System.IO.Path.GetFileName(path);
                _notificationService.ShowInfo(
                    $"Can't read '{file}' because it's open in another app (Word, Preview, or a sync/indexer). Please close the file and try again.");
                return new List<Requirement>();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Import failed: {ex.Message}");
                return new List<Requirement>();
            }
        }

        public List<Requirement> ImportRequirementsFromWord(string path)
        {
            try
            {
                return _inner.ImportRequirementsFromWord(path);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Import failed: {ex.Message}");
                return new List<Requirement>();
            }
        }

        public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string outPath, string project, string testPlan)
        {
            try
            {
                return _inner.ExportAllGeneratedTestCasesToCsv(requirements, outPath, project, testPlan);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"CSV export failed: {ex.Message}");
                return string.Empty;
            }
        }

        public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath)
        {
            try
            {
                _inner.ExportAllGeneratedTestCasesToExcel(requirements, outputPath);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Excel export failed: {ex.Message}");
            }
        }

        public int CountExportableSteps(string path)
        {
            try
            {
                return RequirementService.CountExportableSteps(path);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Error counting exportable steps: {ex.Message}");
                return 0;
            }
        }

        private static bool IsSharingViolation(System.IO.IOException ex)
            => ex.HResult == unchecked((int)0x80070020);
    }
}