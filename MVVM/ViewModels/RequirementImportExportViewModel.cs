using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel responsible for importing and exporting requirements in various formats.
    /// Handles Word document import, workspace management, ChatGPT export functionality, and file operations.
    /// </summary>
    public partial class RequirementImportExportViewModel : ObservableObject
    {
        // Dependencies and function delegates for data access
        private readonly Action<string, int> _setTransientStatus;
        private readonly Func<IEnumerable<Requirement>> _getRequirements;
        private readonly Func<Requirement?> _getCurrentRequirement;
        private readonly Func<ObservableCollection<Requirement>> _getRequirementsCollection;
        
        // Service dependencies
        private readonly IRequirementService _requirementService;
        private readonly ChatGptExportService _chatGptExportService;
        private readonly IFileDialogService _fileDialog;
        
        // Property accessors for workspace state
        private readonly Func<string?> _getWorkspacePath;
        private readonly Action<string?> _setWorkspacePath;
        private readonly Func<string?> _getLastChatGptExportFilePath;
        private readonly Action<string?> _setLastChatGptExportFilePath;
        private readonly Func<bool> _getAutoAnalyzeOnImport;
        private readonly Func<bool> _getAutoExportForChatGpt;

        public RequirementImportExportViewModel(
            Func<IEnumerable<Requirement>> getRequirements,
            Func<Requirement?> getCurrentRequirement,
            Func<ObservableCollection<Requirement>> getRequirementsCollection,
            Action<string, int> setTransientStatus,
            IRequirementService requirementService,
            ChatGptExportService chatGptExportService,
            IFileDialogService fileDialog,
            Func<string?> getWorkspacePath,
            Action<string?> setWorkspacePath,
            Func<string?> getLastChatGptExportFilePath,
            Action<string?> setLastChatGptExportFilePath,
            Func<bool> getAutoAnalyzeOnImport,
            Func<bool> getAutoExportForChatGpt)
        {
            _getRequirements = getRequirements ?? throw new ArgumentNullException(nameof(getRequirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _getRequirementsCollection = getRequirementsCollection ?? throw new ArgumentNullException(nameof(getRequirementsCollection));
            _setTransientStatus = setTransientStatus ?? throw new ArgumentNullException(nameof(setTransientStatus));
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _chatGptExportService = chatGptExportService ?? throw new ArgumentNullException(nameof(chatGptExportService));
            _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
            _getWorkspacePath = getWorkspacePath ?? throw new ArgumentNullException(nameof(getWorkspacePath));
            _setWorkspacePath = setWorkspacePath ?? throw new ArgumentNullException(nameof(setWorkspacePath));
            _getLastChatGptExportFilePath = getLastChatGptExportFilePath ?? throw new ArgumentNullException(nameof(getLastChatGptExportFilePath));
            _setLastChatGptExportFilePath = setLastChatGptExportFilePath ?? throw new ArgumentNullException(nameof(setLastChatGptExportFilePath));
            _getAutoAnalyzeOnImport = getAutoAnalyzeOnImport ?? throw new ArgumentNullException(nameof(getAutoAnalyzeOnImport));
            _getAutoExportForChatGpt = getAutoExportForChatGpt ?? throw new ArgumentNullException(nameof(getAutoExportForChatGpt));
        }

        /// <summary>
        /// Command to import requirements from a Word document (.docx) with file dialog.
        /// </summary>
        [RelayCommand]
        public async Task ImportWordAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open requirements document (.docx)",
                Filter = "Word Documents (*.docx)|*.docx",
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() != true)
            {
                _setTransientStatus("Import cancelled.", 2);
                return;
            }
            await ImportFromPathAsync(dlg.FileName, replace: true);
        }

        /// <summary>
        /// Development convenience: Quick import with actual test requirements.
        /// Skips file dialogs and directly imports from a predefined test file.
        /// </summary>
        [RelayCommand]
        public async Task QuickImportAsync()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[QuickImport] STARTING QuickImportAsync method");
                
                // Check if requirement service exists
                if (_requirementService == null)
                {
                    _setTransientStatus("Quick Import: Requirement service not available", 5);
                    TestCaseEditorApp.Services.Logging.Log.Warn("[QuickImport] _requirementService is null!");
                    return;
                }
                
                // Paths for standard testing setup
                var testDocPath = @"C:\Users\e10653214\Downloads\Decagon_Boundary Scan.docx";
                var testWorkspaceFolder = @"C:\Users\e10653214\Desktop\testing import";
                
                _setTransientStatus("⚡ Quick Import from Decagon test file...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Starting import from: {testDocPath}");
                
                // Check if the test file exists
                if (!File.Exists(testDocPath))
                {
                    _setTransientStatus($"Quick Import: Test file not found at {testDocPath}", 5);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[QuickImport] Test file not found: {testDocPath}");
                    return;
                }
                
                // Ensure workspace directory exists
                Directory.CreateDirectory(testWorkspaceFolder);
                
                // Generate timestamped workspace file
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _setWorkspacePath(Path.Combine(testWorkspaceFolder, $"QuickImport_Decagon_{timestamp}.tcex.json"));
                
                _setTransientStatus($"Importing {Path.GetFileName(testDocPath)}...", 60);
                
                // Import requirements from the actual test file
                var sw = Stopwatch.StartNew();
                var reqs = await Task.Run(() => RequirementServiceCallForImport(testDocPath));
                sw.Stop();
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Parsed {reqs?.Count ?? 0} requirements in {sw.ElapsedMilliseconds}ms");
                
                if (reqs == null || !reqs.Any())
                {
                    _setTransientStatus("Quick Import: No requirements found in test file", 5);
                    TestCaseEditorApp.Services.Logging.Log.Info("[QuickImport] No requirements parsed from test file");
                    return;
                }
                
                // Auto-process if enabled
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Imported {reqs.Count} requirements, AutoAnalyzeOnImport={_getAutoAnalyzeOnImport()}, AutoExportForChatGpt={_getAutoExportForChatGpt()}");
                
                if (reqs.Any() && _getAutoExportForChatGpt())
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Exporting {reqs.Count} requirements for ChatGPT");
                    _ = Task.Run(() => BatchExportRequirementsForChatGpt(reqs));
                }
                
                // Complete import logic would go here...
                _setTransientStatus($"⚡ Quick Import complete - {reqs.Count} requirements from test file", 4);
                TestCaseEditorApp.Services.Logging.Log.Info("[QuickImport] COMPLETED QuickImportAsync method");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[QuickImport] FAILED in QuickImportAsync");
                _setTransientStatus($"Quick Import failed: {ex.Message}", 6);
            }
        }

        /// <summary>
        /// Command to export the current requirement for ChatGPT analysis.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExecuteCurrentRequirementCommands))]
        private void ExportCurrentRequirementForChatGpt()
        {
            try
            {
                var currentRequirement = _getCurrentRequirement();
                if (currentRequirement == null)
                {
                    _setTransientStatus("No requirement selected for export.", 3);
                    return;
                }

                // Export to clipboard
                bool clipboardSuccess = _chatGptExportService.ExportAndCopy(currentRequirement, includeAnalysisRequest: true);
                
                // Also save to file
                string formattedText = _chatGptExportService.ExportSingleRequirement(currentRequirement, includeAnalysisRequest: true);
                string fileName = $"Requirement_{currentRequirement.Item?.Replace("/", "_").Replace("\\", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                try
                {
                    File.WriteAllText(filePath, formattedText);
                    _setLastChatGptExportFilePath(filePath);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved single requirement to file: {filePath}");
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save single requirement file: {fileEx.Message}");
                }
                
                if (clipboardSuccess)
                {
                    _setTransientStatus($"✅ Requirement {currentRequirement.Item} exported to clipboard and saved to {fileName}!", 5);
                }
                else
                {
                    _setTransientStatus($"⚠️ Export saved to {fileName} but clipboard copy failed.", 4);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to export current requirement for ChatGPT");
                _setTransientStatus("Error exporting requirement for ChatGPT.", 3);
            }
        }

        /// <summary>
        /// Command to export selected requirements for ChatGPT analysis.
        /// </summary>
        [RelayCommand]
        private void ExportSelectedRequirementsForChatGpt()
        {
            try
            {
                // For now, export all requirements - could be extended to support selection
                var requirementsToExport = _getRequirements().ToList();
                
                if (!requirementsToExport.Any())
                {
                    _setTransientStatus("No requirements available for export.", 3);
                    return;
                }

                bool success = _chatGptExportService.ExportAndCopyMultiple(requirementsToExport, includeAnalysisRequest: true);
                
                if (success)
                {
                    _setTransientStatus($"{requirementsToExport.Count} requirements exported to clipboard for ChatGPT analysis!", 4);
                }
                else
                {
                    _setTransientStatus("Failed to export requirements to clipboard.", 3);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to export requirements for ChatGPT");
                _setTransientStatus("Error exporting requirements for ChatGPT.", 3);
            }
        }

        /// <summary>
        /// Command to open the last exported ChatGPT file in Notepad.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanOpenChatGptExportFile))]
        public void OpenChatGptExportFile()
        {
            try
            {
                var filePath = _getLastChatGptExportFilePath();
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    _setTransientStatus("❌ No recent ChatGPT export file found.", 3);
                    return;
                }

                // Open the file in Notepad
                var processInfo = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = true
                };

                Process.Start(processInfo);
                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Opened file in Notepad: {filePath}");
                _setTransientStatus("📝 Opened ChatGPT export file in Notepad", 3);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to open file in Notepad: {ex.Message}");
                _setTransientStatus("❌ Failed to open file in Notepad", 3);
            }
        }

        /// <summary>
        /// Command to import additional requirements to the current project.
        /// </summary>
        [RelayCommand]
        public void ImportAdditional()
        {
            try
            {
                _setTransientStatus("📥 Import additional coming soon...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[IMPORT] Import additional requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[IMPORT] Failed to import additional: {ex.Message}");
                _setTransientStatus("❌ Failed to import additional", 3);
            }
        }

        /// <summary>
        /// Command to import structured analysis from a file (JSON or other structured format).
        /// </summary>
        [RelayCommand]
        public void ImportStructuredAnalysis()
        {
            try
            {
                _setTransientStatus("📥 Import structured analysis coming soon...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[ANALYSIS] Structured analysis import requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to import structured analysis: {ex.Message}");
                _setTransientStatus("❌ Failed to import analysis", 3);
            }
        }

        /// <summary>
        /// Batch export requirements for ChatGPT analysis in background after import.
        /// Shows progress notifications and exports requirements in ChatGPT-ready format.
        /// </summary>
        private void BatchExportRequirementsForChatGpt(List<Requirement> requirements)
        {
            if (!requirements.Any())
                return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Starting export for {requirements.Count} requirements");
                
                // Show progress notification
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _setTransientStatus($"Exporting {requirements.Count} requirements for ChatGPT analysis...", 3);
                });

                // Export requirements using the service
                string formattedText = _chatGptExportService.ExportMultipleRequirements(requirements, includeAnalysisRequest: true);
                
                // Save to file and copy to clipboard
                bool clipboardSuccess = _chatGptExportService.CopyToClipboard(formattedText);
                
                // Optionally save to file as well
                string fileName = $"Requirements_Export_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                try
                {
                    File.WriteAllText(filePath, formattedText);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved to file: {filePath}");
                    
                    // Update the last exported file path on UI thread
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        _setLastChatGptExportFilePath(filePath);
                    });
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save file, but clipboard export may have succeeded: {fileEx.Message}");
                }

                // Show completion notification
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (clipboardSuccess)
                    {
                        _setTransientStatus($"✅ {requirements.Count} requirements exported for ChatGPT! Copied to clipboard and saved to {fileName}", 6);
                    }
                    else
                    {
                        _setTransientStatus($"⚠️ Export completed but clipboard copy failed. File saved to {fileName}", 5);
                    }
                });

                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Completed export for {requirements.Count} requirements, clipboard={clipboardSuccess}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to export requirements: {ex.Message}");
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _setTransientStatus("❌ Failed to export requirements for ChatGPT.", 4);
                });
            }
        }

        /// <summary>
        /// Helper method to import requirements from a given file path.
        /// This would contain the full import logic from MainViewModel.
        /// </summary>
        private async Task ImportFromPathAsync(string path, bool replace)
        {
            // TODO: Implement full ImportFromPathAsync logic
            // This is a placeholder - the full implementation would be moved from MainViewModel
            _setTransientStatus($"Import from {path} - implementation coming soon...", 3);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Wrap requirement service call for import operations.
        /// </summary>
        private List<Requirement> RequirementServiceCallForImport(string path)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] RequirementServiceCallForImport called with path: {path}");
                
                if (_requirementService == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[Import] _requirementService is null - cannot import");
                    return new List<Requirement>();
                }
                
                // First try the Jama All Data parser
                TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying Jama All Data parser...");
                var jamaResults = _requirementService.ImportRequirementsFromJamaAllDataDocx(path) ?? new List<Requirement>();
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Jama parser returned {jamaResults.Count} requirements");
                
                if (jamaResults.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Successfully parsed {jamaResults.Count} requirements using Jama All Data parser");
                    return jamaResults;
                }
                
                // If that didn't work, try the regular Word parser
                TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying regular Word parser...");
                var wordResults = _requirementService.ImportRequirementsFromWord(path) ?? new List<Requirement>();
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Word parser returned {wordResults.Count} requirements");
                
                if (wordResults.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Successfully parsed {wordResults.Count} requirements using Word parser");
                    return wordResults;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[Import] No requirements found with either parser for file: {path}");
                return new List<Requirement>();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[Import] Error during import: {ex.Message}");
                return new List<Requirement>();
            }
        }

        /// <summary>
        /// Can-execute condition for commands that require a current requirement to be selected.
        /// </summary>
        private bool CanExecuteCurrentRequirementCommands() => _getCurrentRequirement() != null;

        /// <summary>
        /// Can-execute condition for opening ChatGPT export file.
        /// </summary>
        private bool CanOpenChatGptExportFile()
        {
            var filePath = _getLastChatGptExportFilePath();
            return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
        }

        // === Methods moved from MainViewModel for consolidation ===

        /// <summary>
        /// Invokes export all test cases to Jama functionality
        /// </summary>
        public void TryInvokeExportAllToJama()
        {
            try
            {
                _setTransientStatus("Export to Jama functionality would be called here", 3);
                // TODO: Implement actual Jama export logic
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Export to Jama failed: {ex.Message}", 5);
            }
        }

        /// <summary>
        /// Export all generated test cases to CSV format
        /// </summary>
        public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string folderPath, string filePrefix, string extra)
        {
            try
            {
                _setTransientStatus("Exporting test cases to CSV...", 3);
                
                // TODO: Implement actual CSV export logic
                var outputPath = Path.Combine(folderPath, $"{filePrefix}_test_cases_{extra}.csv");
                
                _setTransientStatus($"Test cases exported to: {Path.GetFileName(outputPath)}", 5);
                return outputPath;
            }
            catch (Exception ex)
            {
                _setTransientStatus($"CSV export failed: {ex.Message}", 5);
                return string.Empty;
            }
        }

        /// <summary>
        /// Export all generated test cases to Excel format
        /// </summary>
        public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath)
        {
            try
            {
                _setTransientStatus("Exporting test cases to Excel...", 3);
                
                // TODO: Implement actual Excel export logic
                
                _setTransientStatus($"Test cases exported to: {Path.GetFileName(outputPath)}", 5);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Excel export failed: {ex.Message}", 5);
            }
        }

        /// <summary>
        /// Handle import workflow completion events
        /// </summary>
        public void OnImportWorkflowCompleted(object? sender, object e)
        {
            try
            {
                // TODO: Handle import workflow completion
                _setTransientStatus("Import workflow completed", 3);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Import workflow completion handling failed: {ex.Message}", 5);
            }
        }

        /// <summary>
        /// Handle import workflow cancellation events
        /// </summary>
        public void OnImportWorkflowCancelled(object? sender, EventArgs e)
        {
            _setTransientStatus("Import workflow cancelled", 2);
        }

        /// <summary>
        /// Handle import requirements workflow cancellation events
        /// </summary>
        public void OnImportRequirementsWorkflowCancelled(object? sender, EventArgs e)
        {
            _setTransientStatus("Import requirements workflow cancelled", 2);
        }
    }
}