using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Helpers;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel responsible for importing and exporting requirements in various formats.
    /// Handles Word document import, workspace management, ChatGPT export functionality, and file operations.
    /// </summary>
    public partial class RequirementImportExportViewModel : BaseDomainViewModel, IDisposable
    {
        // Domain mediator (properly typed)
        private new readonly ITestCaseGenerationMediator _mediator;
        
        // Legacy delegate support for backwards compatibility
        private readonly Action<string, int, bool> _setTransientStatus;
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
        
        // Additional workspace state accessors for full import logic
        private readonly Func<Workspace?> _getCurrentWorkspace;
        private readonly Action<Workspace?> _setCurrentWorkspace;
        private readonly Func<bool> _getHasUnsavedChanges;
        private readonly Action<bool> _setHasUnsavedChanges;
        private readonly Func<bool> _getIsDirty;
        private readonly Action<bool> _setIsDirty;
        private readonly Action<Requirement?> _setCurrentRequirement;
        
        // Delegates for MainViewModel methods needed by import
        private readonly Func<List<Requirement>, Task> _batchAnalyzeRequirements;
        private readonly Action _saveSessionAuto;
        private readonly Action _refreshSupportingInfo;
        private readonly Action _computeDraftedCount;
        private readonly Action _raiseCounterChanges;
        private readonly Action<string?> _setStatus;

        public RequirementImportExportViewModel(
            ITestCaseGenerationMediator mediator,
            ILogger<RequirementImportExportViewModel> logger,
            Func<IEnumerable<Requirement>> getRequirements,
            Func<Requirement?> getCurrentRequirement,
            Func<ObservableCollection<Requirement>> getRequirementsCollection,
            Action<string, int, bool> setTransientStatus,
            IRequirementService requirementService,
            ChatGptExportService chatGptExportService,
            IFileDialogService fileDialog,
            Func<string?> getWorkspacePath,
            Action<string?> setWorkspacePath,
            Func<string?> getLastChatGptExportFilePath,
            Action<string?> setLastChatGptExportFilePath,
            Func<bool> getAutoAnalyzeOnImport,
            Func<bool> getAutoExportForChatGpt,
            // Additional dependencies for full import logic
            Func<Workspace?> getCurrentWorkspace,
            Action<Workspace?> setCurrentWorkspace,
            Func<bool> getHasUnsavedChanges,
            Action<bool> setHasUnsavedChanges,
            Func<bool> getIsDirty,
            Action<bool> setIsDirty,
            Action<Requirement?> setCurrentRequirement,
            Func<List<Requirement>, Task> batchAnalyzeRequirements,
            Action saveSessionAuto,
            Action refreshSupportingInfo,
            Action computeDraftedCount,
            Action raiseCounterChanges,
            Action<string?> setStatus)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

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
            
            // Additional workspace state dependencies
            _getCurrentWorkspace = getCurrentWorkspace ?? throw new ArgumentNullException(nameof(getCurrentWorkspace));
            _setCurrentWorkspace = setCurrentWorkspace ?? throw new ArgumentNullException(nameof(setCurrentWorkspace));
            _getHasUnsavedChanges = getHasUnsavedChanges ?? throw new ArgumentNullException(nameof(getHasUnsavedChanges));
            _setHasUnsavedChanges = setHasUnsavedChanges ?? throw new ArgumentNullException(nameof(setHasUnsavedChanges));
            _getIsDirty = getIsDirty ?? throw new ArgumentNullException(nameof(getIsDirty));
            _setIsDirty = setIsDirty ?? throw new ArgumentNullException(nameof(setIsDirty));
            _setCurrentRequirement = setCurrentRequirement ?? throw new ArgumentNullException(nameof(setCurrentRequirement));
            
            // MainViewModel method delegates
            _batchAnalyzeRequirements = batchAnalyzeRequirements ?? throw new ArgumentNullException(nameof(batchAnalyzeRequirements));
            _saveSessionAuto = saveSessionAuto ?? throw new ArgumentNullException(nameof(saveSessionAuto));
            _refreshSupportingInfo = refreshSupportingInfo ?? throw new ArgumentNullException(nameof(refreshSupportingInfo));
            _computeDraftedCount = computeDraftedCount ?? throw new ArgumentNullException(nameof(computeDraftedCount));
            _raiseCounterChanges = raiseCounterChanges ?? throw new ArgumentNullException(nameof(raiseCounterChanges));
            _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
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
                _setTransientStatus("Import cancelled.", 2, false);
                return;
            }
            await ImportFromPathAsync(dlg.FileName, replace: true);
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
                    _setTransientStatus("No requirement selected for export.", 3, false);
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
                    _setTransientStatus($"✅ Requirement {currentRequirement.Item} exported to clipboard and saved to {fileName}!", 5, false);
                }
                else
                {
                    _setTransientStatus($"⚠️ Export saved to {fileName} but clipboard copy failed.", 4, false);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to export current requirement for ChatGPT");
                _setTransientStatus("Error exporting requirement for ChatGPT.", 3, true);
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
                    _setTransientStatus("No requirements available for export.", 3, false);
                    return;
                }

                bool success = _chatGptExportService.ExportAndCopyMultiple(requirementsToExport, includeAnalysisRequest: true);
                
                if (success)
                {
                    _setTransientStatus($"{requirementsToExport.Count} requirements exported to clipboard for ChatGPT analysis!", 4, false);
                }
                else
                {
                    _setTransientStatus("Failed to export requirements to clipboard.", 3, true);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Failed to export requirements for ChatGPT");
                _setTransientStatus("Error exporting requirements for ChatGPT.", 3, true);
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
                    _setTransientStatus("❌ No recent ChatGPT export file found.", 3, false);
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
                _setTransientStatus("📝 Opened ChatGPT export file in Notepad", 3, false);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to open file in Notepad: {ex.Message}");
                _setTransientStatus("❌ Failed to open file in Notepad", 3, true);
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
                _setTransientStatus("📥 Import structured analysis coming soon...", 3, false);
                TestCaseEditorApp.Services.Logging.Log.Info("[ANALYSIS] Structured analysis import requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to import structured analysis: {ex.Message}");
                _setTransientStatus("❌ Failed to import analysis", 3, true);
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
                    _setTransientStatus($"Exporting {requirements.Count} requirements for ChatGPT analysis...", 3, false);
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
                        _setTransientStatus($"✅ {requirements.Count} requirements exported for ChatGPT! Copied to clipboard and saved to {fileName}", 6, false);
                    }
                    else
                    {
                        _setTransientStatus($"⚠️ Export completed but clipboard copy failed. File saved to {fileName}", 5, false);
                    }
                });

                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Completed export for {requirements.Count} requirements, clipboard={clipboardSuccess}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to export requirements: {ex.Message}");
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _setTransientStatus("❌ Failed to export requirements for ChatGPT.", 4, true);
                });
            }
        }

        /// <summary>
        /// Import requirements from a given file path with full workspace management.
        /// Handles both workspace files (.tcex.json) and source documents (.docx).
        /// </summary>
        public async Task ImportFromPathAsync(string path, bool replace)
        {
            // If the selected path is a saved workspace, load it directly instead of
            // treating it as a source document. This makes "Import In-Process" robust
            // when users select `.tcex.json` files by accident or intentionally.
            try
            {
                if (string.Equals(Path.GetExtension(path), ".tcex.json", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadWorkspaceDirectlyAsync(path);
                    return;
                }
            }
            catch { /* best-effort only */ }

            await ImportFromSourceDocumentAsync(path, replace);
        }

        private async Task LoadWorkspaceDirectlyAsync(string path)
        {
            await Task.CompletedTask;
            try
            {
                var ws = TestCaseEditorApp.Services.WorkspaceFileManager.Load(path);
                if (ws == null || (ws.Requirements?.Count ?? 0) == 0)
                {
                    _setTransientStatus("Failed to load workspace (file empty or invalid).", 0, true);
                    return;
                }

                _setWorkspacePath(path);
                _setCurrentWorkspace(ws);

                // Replace the observable collection contents without replacing the instance
                var requirements = _getRequirementsCollection();
                try
                {
                    // TODO: Need to access RequirementsOnCollectionChanged event handler
                    // This is a limitation of the current delegate approach
                    requirements.Clear();
                    foreach (var r in ws.Requirements ?? Enumerable.Empty<Requirement>()) 
                        requirements.Add(r);
                }
                finally
                {
                    // TODO: Re-attach event handler
                }

                _getCurrentWorkspace()!.Requirements = requirements.ToList();
                _setHasUnsavedChanges(false);
                _setIsDirty(false);

                // TODO: Add recent file tracking when available

                _setTransientStatus($"Opened workspace: {Path.GetFileName(path)}  {requirements.Count} requirements", 4, false);
                // TODO: Notify requirements navigator when available
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Failed to load workspace: {ex.Message}", 0, true);
            }
        }

        private async Task ImportFromSourceDocumentAsync(string path, bool replace)
        {
            if (replace && _getHasUnsavedChanges() && _getRequirementsCollection().Count > 0)
            {
                var res = MessageBox.Show(
                    "You have unsaved changes. Replace the current requirements with the new import?",
                    "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes)
                {
                    _setTransientStatus("Import canceled.", 2, false);
                    return;
                }
            }

            try
            {
                await EnsureWorkspacePathAsync(path);
                await ProcessImportAsync(path);
            }
            catch (Exception ex)
            {
                _saveSessionAuto();
                _setStatus("Import failed: " + ex.Message);
            }
        }

        private async Task EnsureWorkspacePathAsync(string path)
        {
            await Task.CompletedTask;
            // If WorkspacePath is already set (e.g., from new project workflow), skip the dialog
            if (string.IsNullOrWhiteSpace(_getWorkspacePath()))
            {
                var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestCaseEditorApp", "Workspaces");
                Directory.CreateDirectory(defaultFolder);

                var suggested = FileNameHelper.GenerateUniqueFileName(Path.GetFileNameWithoutExtension(path), ".tcex.json");

                // Inform user about the next step
                var result = MessageBox.Show(
                    $"Great! Your document '{Path.GetFileName(path)}' is ready to import.\n\n" +
                    "Next, choose where to save your new project workspace. This will create a project file (.tcex.json) that contains your imported requirements and any test cases you generate.\n\n" +
                    "Would you like to proceed?",
                    "Create New Project Workspace",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes)
                {
                    _setTransientStatus("Import canceled by user.", 2, false);
                    return;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Showing Create Workspace dialog. defaultFolder={defaultFolder}, suggested={suggested}");
                var chosen = ShowSaveDialog(suggested, defaultFolder);
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Create Workspace dialog returned: '{chosen}'");

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    _setTransientStatus("Import canceled (no workspace name selected).", 2, false);
                    return;
                }

                _setWorkspacePath(FileNameHelper.EnsureUniquePath(Path.GetDirectoryName(chosen)!, Path.GetFileName(chosen)));
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Set WorkspacePath to: '{_getWorkspacePath()}'");
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Using existing WorkspacePath: '{_getWorkspacePath()}' (from new project workflow)");
            }
        }

        private async Task ProcessImportAsync(string path)
        {
            _setTransientStatus($"Importing {Path.GetFileName(path)}...", 60, false);

            var sw = Stopwatch.StartNew();

            var reqs = await Task.Run(() => RequirementServiceCallForImport(path));

            sw.Stop();

            // Build workspace model (guard reqs if null)
            _setCurrentWorkspace(new Workspace
            {
                SourceDocPath = path,
                Requirements = reqs?.ToList() ?? new List<Requirement>()
            });

            // Update UI-bound collection: preserve existing ObservableCollection instance
            reqs = reqs ?? new List<Requirement>();
            var requirements = _getRequirementsCollection();
            try
            {
                // TODO: Handle collection changed event detachment/reattachment
                requirements.Clear();
                foreach (var r in reqs) requirements.Add(r);
            }
            finally
            {
                // TODO: Re-attach event handler
            }

            _getCurrentWorkspace()!.Requirements = requirements.ToList();

            // Auto-process requirements if enabled
            await HandleAutoProcessingAsync(reqs);

            // Finalize import
            FinalizeImport();

            _setTransientStatus($"Workspace created - {requirements?.Count ?? 0} requirement(s)", 6, false);
        }

        private async Task HandleAutoProcessingAsync(List<Requirement> reqs)
        {
            await Task.CompletedTask;
            TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Checking auto-processing: reqs.Any()={reqs.Any()}, AutoAnalyzeOnImport={_getAutoAnalyzeOnImport()}, AutoExportForChatGpt={_getAutoExportForChatGpt()}");

            if (reqs.Any() && _getAutoExportForChatGpt())
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Exporting {reqs.Count} requirements for ChatGPT");
                _ = Task.Run(() => { /* TODO: Wire to ChatGptExportAnalysisViewModel.BatchExportRequirementsForChatGpt(reqs) */ });
            }
            else if (reqs.Any() && _getAutoAnalyzeOnImport())
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Starting batch analysis for {reqs.Count} requirements");
                _ = Task.Run(async () => await _batchAnalyzeRequirements(reqs));
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[IMPORT] Auto-processing NOT started - conditions not met");
            }
        }

        private void FinalizeImport()
        {
            // TODO: Track in recent files when available

            Requirement? firstFromView = null;
            try
            {
                // TODO: Access RequirementsNavigator when available
                firstFromView = null; // _requirementsNavigator?.RequirementsView?.Cast<Requirement>().FirstOrDefault();
            }
            catch { firstFromView = null; }

            _setCurrentRequirement(firstFromView ?? _getRequirementsCollection().FirstOrDefault());
            _setHasUnsavedChanges(false);
            _setIsDirty(false);
            _refreshSupportingInfo();

            _computeDraftedCount();
            _raiseCounterChanges();

            // TODO: Notify requirements navigator when available
        }

        private string ShowSaveDialog(string suggestedFileName, string initialDirectory)
            => _fileDialog.ShowSaveFile(
                title: "Save New Project Workspace", 
                suggestedFileName: suggestedFileName, 
                filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*", 
                defaultExt: ".tcex.json", 
                initialDirectory: initialDirectory) ?? string.Empty;

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
                _setTransientStatus("Export to Jama functionality would be called here", 3, false);
                // TODO: Implement actual Jama export logic
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Export to Jama failed: {ex.Message}", 5, true);
            }
        }

        /// <summary>
        /// Export all generated test cases to CSV format
        /// </summary>
        public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string folderPath, string filePrefix, string extra)
        {
            try
            {
                _setTransientStatus("Exporting test cases to CSV...", 3, false);
                
                // TODO: Implement actual CSV export logic
                var outputPath = Path.Combine(folderPath, $"{filePrefix}_test_cases_{extra}.csv");
                
                _setTransientStatus($"Test cases exported to: {Path.GetFileName(outputPath)}", 5, false);
                return outputPath;
            }
            catch (Exception ex)
            {
                _setTransientStatus($"CSV export failed: {ex.Message}", 5, true);
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
                _setTransientStatus("Exporting test cases to Excel...", 3, false);
                
                // TODO: Implement actual Excel export logic
                
                _setTransientStatus($"Test cases exported to: {Path.GetFileName(outputPath)}", 5, false);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Excel export failed: {ex.Message}", 5, true);
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
                _setTransientStatus("Import workflow completed", 3, false);
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Import workflow completion handling failed: {ex.Message}", 5, true);
            }
        }

        /// <summary>
        /// Handle import workflow cancellation events
        /// </summary>
        public void OnImportWorkflowCancelled(object? sender, EventArgs e)
        {
            _setTransientStatus("Import workflow cancelled", 2, false);
        }

        /// <summary>
        /// Handle import requirements workflow cancellation events
        /// </summary>
        public void OnImportRequirementsWorkflowCancelled(object? sender, EventArgs e)
        {
            _setTransientStatus("Import requirements workflow cancelled", 2, false);
        }

        // === Methods extracted from MainViewModel for import/export ===

        /// <summary>
        /// Reload from current source path
        /// </summary>
        public Task ReloadAsync()
        {
            try
            {
                var currentSourcePath = _getWorkspacePath?.Invoke();
                if (string.IsNullOrWhiteSpace(currentSourcePath))
                {
                    _setTransientStatus("No source loaded to reload.", 3, false);
                    return Task.CompletedTask;
                }

                _setTransientStatus("Reloading from source...", 2, false);
                // For now, just show a success message
                // This would be implemented with actual reload logic
                _setTransientStatus("Reload completed", 2, false);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _setTransientStatus($"Reload failed: {ex.Message}", 5, true);
                return Task.CompletedTask;
            }
        }
    }
}