using System;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    /// <summary>
    /// WorkspaceManagementVM handles all file operations, workspace persistence, and import/export functionality.
    /// This ViewModel is responsible for:
    /// - Loading and saving workspace files (.tcex.json)
    /// - Importing documents (Word, structured analysis)
    /// - Exporting data (JAMA, ChatGPT formats)
    /// - Managing workspace state and file paths
    /// - Coordinating with persistence and file dialog services
    /// </summary>
    public partial class WorkspaceManagementVM : ObservableObject
    {
        // --- Services ---
        private readonly IFileDialogService _fileDialog;
        private readonly IPersistenceService? _persistence;
        private readonly IRequirementService _requirementService;
        private readonly NotificationService _notificationService;
        private readonly RecentFilesService? _recentFilesService;
        private readonly IRequirementAnalysisService? _analysisService;

        // --- Core Properties (shared with MainViewModel) ---
        private string? _workspacePath;
        public string? WorkspacePath
        {
            get => _workspacePath;
            set => SetProperty(ref _workspacePath, value);
        }

        private string? _currentSourcePath;
        public string? CurrentSourcePath
        {
            get => _currentSourcePath;
            set => SetProperty(ref _currentSourcePath, value);
        }

        private string? _wordFilePath;
        public string? WordFilePath
        {
            get => _wordFilePath;
            set => SetProperty(ref _wordFilePath, value);
        }

        private Workspace? _currentWorkspace;
        public Workspace? CurrentWorkspace
        {
            get => _currentWorkspace;
            set => SetProperty(ref _currentWorkspace, value);
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        // --- Auto-processing Settings ---
        private bool _autoAnalyzeOnImport = true;
        public bool AutoAnalyzeOnImport
        {
            get => _autoAnalyzeOnImport;
            set
            {
                if (SetProperty(ref _autoAnalyzeOnImport, value))
                {
                    try { _persistence?.Save("AutoAnalyzeOnImport", value); } catch { }
                }
            }
        }

        private bool _autoExportForChatGpt = false;
        public bool AutoExportForChatGpt
        {
            get => _autoExportForChatGpt;
            set
            {
                if (SetProperty(ref _autoExportForChatGpt, value))
                {
                    try { _persistence?.Save("AutoExportForChatGpt", value); } catch { }
                }
            }
        }

        private string? _lastChatGptExportFilePath;
        public string? LastChatGptExportFilePath
        {
            get => _lastChatGptExportFilePath;
            set => SetProperty(ref _lastChatGptExportFilePath, value);
        }

        // --- External Dependencies (delegated from MainViewModel) ---
        public ObservableCollection<Requirement> Requirements { get; }
        public Requirement? CurrentRequirement { get; set; }
        
        // --- Events ---
        public event Action<string, int>? StatusMessageRequested;
        public event Action<string, bool>? TransientStatusRequested;
        public event Action? WorkspaceLoaded;
        public event Action? RequirementsChanged;
        public event Action<List<Requirement>>? ImportCompleted;

        // --- Commands ---
        public IAsyncRelayCommand? ImportWordCommand { get; private set; }
        public IAsyncRelayCommand? QuickImportCommand { get; private set; }
        public ICommand? LoadWorkspaceCommand { get; private set; }
        public ICommand? SaveWorkspaceCommand { get; private set; }
        public IAsyncRelayCommand? SaveWorkspaceAsCommand { get; private set; }
        public ICommand? ReloadCommand { get; private set; }
        public ICommand? ExportAllToJamaCommand { get; private set; }
        public ICommand? ExportForChatGptCommand { get; private set; }
        public ICommand? ExportSelectedForChatGptCommand { get; private set; }
        public ICommand? ToggleAutoExportCommand { get; private set; }
        public ICommand? OpenChatGptExportCommand { get; private set; }
        public ICommand? SaveProjectCommand { get; private set; }

        public IAsyncRelayCommand? ImportStructuredAnalysisCommand { get; private set; }

        /// <summary>
        /// Constructor for WorkspaceManagementVM.
        /// </summary>
        /// <param name="requirements">Shared Requirements collection from MainViewModel</param>
        /// <param name="fileDialog">File dialog service for open/save operations</param>
        /// <param name="persistence">Settings persistence service</param>
        /// <param name="requirementService">Core requirement processing service</param>
        /// <param name="notificationService">User notification service</param>
        /// <param name="recentFilesService">Recent files tracking service</param>
        /// <param name="analysisService">Requirement analysis service</param>
        public WorkspaceManagementVM(
            ObservableCollection<Requirement> requirements,
            IFileDialogService fileDialog,
            IPersistenceService? persistence,
            IRequirementService requirementService,
            NotificationService notificationService,
            RecentFilesService? recentFilesService,
            IRequirementAnalysisService? analysisService)
        {
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
            _persistence = persistence;
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _recentFilesService = recentFilesService;
            _analysisService = analysisService;

            InitializeCommands();
            LoadSettings();
        }

        private void InitializeCommands()
        {
            // Import Commands
            ImportWordCommand = new AsyncRelayCommand(ImportWordAsync);

            ImportStructuredAnalysisCommand = new AsyncRelayCommand(ImportStructuredAnalysisAsync);

            // Workspace Commands
            LoadWorkspaceCommand = new RelayCommand(LoadWorkspace);
            SaveWorkspaceCommand = new RelayCommand(SaveWorkspace);
            SaveWorkspaceAsCommand = new AsyncRelayCommand(SaveWorkspaceAsync);
            ReloadCommand = new AsyncRelayCommand(ReloadAsync);

            // Export Commands
            ExportAllToJamaCommand = new RelayCommand(TryInvokeExportAllToJama);
            ExportForChatGptCommand = new RelayCommand(ExportCurrentRequirementForChatGpt, () => CurrentRequirement != null);
            ExportSelectedForChatGptCommand = new RelayCommand(ExportSelectedForChatGpt);
            ToggleAutoExportCommand = new RelayCommand(() => AutoExportForChatGpt = !AutoExportForChatGpt);
            OpenChatGptExportCommand = new RelayCommand(OpenChatGptExportFile, () => !string.IsNullOrEmpty(LastChatGptExportFilePath) && File.Exists(LastChatGptExportFilePath));

            // Project Commands  
            SaveProjectCommand = new RelayCommand(SaveProject);
        }

        private void LoadSettings()
        {
            try
            {
                // Load auto-processing settings
                if (_persistence != null && _persistence.Exists("AutoAnalyzeOnImport"))
                {
                    var val = _persistence.Load<bool>("AutoAnalyzeOnImport");
                    _autoAnalyzeOnImport = val;
                }

                if (_persistence != null && _persistence.Exists("AutoExportForChatGpt"))
                {
                    var val = _persistence.Load<bool>("AutoExportForChatGpt");
                    _autoExportForChatGpt = val;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"Failed to load workspace management settings: {ex.Message}");
            }
        }

        // --- Import Methods ---

        /// <summary>
        /// Import a Word document by showing file dialog
        /// </summary>
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
                SetTransientStatus("Import cancelled.", 2);
                return;
            }
            await ImportFromPathAsync(dlg.FileName, replace: true);
        }



        /// <summary>
        /// Import structured analysis data  
        /// </summary>
        public async Task ImportStructuredAnalysisAsync()
        {
            // This would be implemented based on the specific structured analysis format
            await Task.CompletedTask;
            SetTransientStatus("Structured analysis import - feature coming soon", 3);
        }

        /// <summary>
        /// Core import method that handles file parsing and requirement creation
        /// </summary>
        private async Task ImportFromPathAsync(string path, bool replace)
        {
            try
            {
                await Task.CompletedTask; // Satisfy async analyzer
                TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Starting ImportFromPathAsync: path={path}, replace={replace}");
                
                if (replace)
                {
                    Requirements.Clear();
                    CurrentRequirement = null;
                }

                WordFilePath = path;
                CurrentSourcePath = path;
                
                SetTransientStatus("📄 Parsing document...", -1);
                
                // Use the requirement service to parse the document
                var reqs = _requirementService.ImportRequirementsFromWord(path);
                
                if (!reqs.Any())
                {
                    SetTransientStatus("⚠️ No requirements found in document", 4);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[IMPORT] No requirements found in: {path}");
                    return;
                }

                // Add imported requirements
                foreach (var req in reqs)
                {
                    Requirements.Add(req);
                }

                // Set current requirement to first imported item
                if (!replace && CurrentRequirement == null && reqs.Any())
                {
                    CurrentRequirement = reqs.First();
                }
                else if (replace && reqs.Any())
                {
                    CurrentRequirement = reqs.First();
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Imported {reqs.Count} requirements, AutoAnalyzeOnImport={AutoAnalyzeOnImport}, AutoExportForChatGpt={AutoExportForChatGpt}");
                
                if (reqs.Any() && AutoExportForChatGpt)
                {
                    ExportCurrentRequirementForChatGpt();
                }
                
                SetTransientStatus($"✅ Imported {reqs.Count} requirement{(reqs.Count == 1 ? "" : "s")}", 4);
                IsDirty = true;
                HasUnsavedChanges = true;
                
                // Notify that import is complete
                ImportCompleted?.Invoke(reqs);
                RequirementsChanged?.Invoke();

                TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Import completed successfully. Total requirements: {Requirements.Count}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[IMPORT] ImportFromPathAsync failed: {ex.Message}");
                _notificationService.ShowError($"Import failed: {ex.Message}", 8);
            }
        }

        // --- Workspace Load/Save Methods ---

        /// <summary>
        /// Load workspace by showing file dialog
        /// </summary>
        public void LoadWorkspace()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Open Saved Session",
                Filter = "Test Case Editor Session|*.tcex.json",
                DefaultExt = ".tcex.json",
                RestoreDirectory = true,
                InitialDirectory = !string.IsNullOrWhiteSpace(WorkspacePath) ? Path.GetDirectoryName(WorkspacePath) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (ofd.ShowDialog() != true) return;

            LoadWorkspaceFromPath(ofd.FileName);
        }

        /// <summary>
        /// Load workspace from specific file path
        /// </summary>
        public void LoadWorkspaceFromPath(string filePath)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Starting to load workspace from: {filePath}");
            
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Invalid file path or file doesn't exist: {filePath}");
                SetTransientStatus("Invalid workspace file path.", blockingError: true);
                return;
            }

            WorkspacePath = filePath;
            TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Set WorkspacePath to: {WorkspacePath}");
            
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Attempting to load workspace file...");
                var ws = WorkspaceFileManager.Load(WorkspacePath!);
                if (ws == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[LoadWorkspace] Workspace file loaded but returned null");
                    SetTransientStatus("Failed to load workspace (file empty or invalid).", blockingError: true);
                    return;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Successfully loaded workspace. Requirements count: {ws.Requirements?.Count ?? 0}");
                CurrentWorkspace = ws;
                
                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Clearing existing requirements and loading new ones...");
                Requirements.Clear();
                foreach (var r in ws.Requirements ?? Enumerable.Empty<Requirement>())
                    Requirements.Add(r);

                CurrentRequirement = Requirements.FirstOrDefault();
                CurrentSourcePath = ws.SourceDocPath;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Workspace loading completed successfully. Current requirement: {CurrentRequirement?.GlobalId}");
                SetTransientStatus($"Opened workspace: {Path.GetFileName(WorkspacePath)} - {Requirements.Count} requirements", 4);
                HasUnsavedChanges = false;
                IsDirty = false;
                
                // Notify workspace loaded
                WorkspaceLoaded?.Invoke();
                RequirementsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[LoadWorkspace] Exception occurred while loading workspace: {ex.Message}");
                _notificationService.ShowError($"Failed to load workspace: {ex.Message}", 8);
            }
        }

        /// <summary>
        /// Quick save to existing workspace path
        /// </summary>
        public void SaveWorkspace()
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[SaveWorkspace] Quick save called. WorkspacePath='{WorkspacePath ?? "<null>"}'");
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[SaveWorkspace] No existing path, delegating to SaveAs");
                // No existing path, delegate to SaveAs
                _ = SaveWorkspaceAsync();
                return;
            }

            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("Nothing to save.", 2);
                return;
            }

            var ws = new Workspace
            {
                SourceDocPath = CurrentSourcePath,
                Requirements = Requirements.ToList()
            };

            try
            {
                WorkspaceFileManager.Save(WorkspacePath!, ws);
                CurrentWorkspace = ws;
                IsDirty = false;
                HasUnsavedChanges = false;
                
                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
                
                SetTransientStatus($"Saved: {Path.GetFileName(WorkspacePath)}", 3);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to save workspace: {ex.Message}", 8);
            }
        }

        /// <summary>
        /// Save As - prompts for location
        /// </summary>
        public async Task SaveWorkspaceAsync()
        {
            // Ensure async methods contain an await to satisfy analyzer when method is mostly synchronous
            await Task.CompletedTask;
            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("Nothing to save.", 2);
                return;
            }

            var suggested = $"{(string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(WordFilePath)) ? "Workspace" : Path.GetFileNameWithoutExtension(WordFilePath))}.tcex.json";
            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestCaseEditorApp", "Workspaces");
            Directory.CreateDirectory(defaultFolder);

            TestCaseEditorApp.Services.Logging.Log.Info($"[WorkspaceManagementVM] Showing SaveFile dialog. initialDirectory={defaultFolder}, suggestedFileName={suggested}");
            var chosen = _fileDialog.ShowSaveFile(
                title: "Save Workspace",
                suggestedFileName: suggested,
                filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                defaultExt: ".tcex.json",
                initialDirectory: defaultFolder);

            TestCaseEditorApp.Services.Logging.Log.Debug($"[WorkspaceManagementVM] File dialog returned: '{chosen ?? "NULL"}'");

            if (string.IsNullOrWhiteSpace(chosen))
            {
                SetTransientStatus("Save cancelled.", 2);
                return;
            }

            try
            {
                // Ensure valid file path
                if (Directory.Exists(chosen) || string.IsNullOrWhiteSpace(Path.GetFileName(chosen)))
                {
                    chosen = Path.Combine(chosen, suggested);
                }

                if (string.IsNullOrWhiteSpace(Path.GetExtension(chosen)))
                {
                    chosen = Path.ChangeExtension(chosen, ".tcex.json");
                }

                WorkspacePath = chosen;
                var ws = new Workspace
                {
                    SourceDocPath = CurrentSourcePath,
                    Requirements = Requirements.ToList()
                };

                WorkspaceFileManager.Save(WorkspacePath!, ws);
                CurrentWorkspace = ws;
                IsDirty = false;
                HasUnsavedChanges = false;
                
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
                
                SetTransientStatus($"Saved: {Path.GetFileName(WorkspacePath)}", 3);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveWorkspaceAsync] Save operation failed: {ex.Message}");
                MessageBox.Show($"Failed to save workspace: {ex.Message}", "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Reload current workspace
        /// </summary>
        public async Task ReloadAsync()
        {
            await Task.CompletedTask;
            if (!string.IsNullOrWhiteSpace(WorkspacePath) && File.Exists(WorkspacePath))
            {
                LoadWorkspaceFromPath(WorkspacePath);
            }
            else if (!string.IsNullOrWhiteSpace(CurrentSourcePath) && File.Exists(CurrentSourcePath))
            {
                await ImportFromPathAsync(CurrentSourcePath, replace: true);
            }
            else
            {
                SetTransientStatus("No file to reload.", 2);
            }
        }

        // --- Export Methods ---

        /// <summary>
        /// Export all requirements to JAMA format
        /// </summary>
        private void TryInvokeExportAllToJama()
        {
            try
            {
                if (!Requirements.Any())
                {
                    SetTransientStatus("No requirements to export.", 3);
                    return;
                }
                
                // Implementation would depend on JAMA export service
                SetTransientStatus("JAMA export - feature coming soon", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[EXPORT] TryInvokeExportAllToJama called");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"TryInvokeExportAllToJama failed: {ex.Message}");
                _notificationService.ShowError($"JAMA export failed: {ex.Message}", 5);
            }
        }

        /// <summary>
        /// Export current requirement for ChatGPT
        /// </summary>
        private void ExportCurrentRequirementForChatGpt()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    SetTransientStatus("No current requirement to export.", 3);
                    return;
                }
                
                // Implementation would use ChatGPT export service
                SetTransientStatus("ChatGPT export - feature coming soon", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[EXPORT] ExportCurrentRequirementForChatGpt called for requirement: {CurrentRequirement.GlobalId}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"ExportCurrentRequirementForChatGpt failed: {ex.Message}");
                _notificationService.ShowError($"ChatGPT export failed: {ex.Message}", 5);
            }
        }

        /// <summary>
        /// Export selected requirements for ChatGPT
        /// </summary>
        private void ExportSelectedForChatGpt()
        {
            try
            {
                // Implementation would handle selected requirements export
                SetTransientStatus("Selected ChatGPT export - feature coming soon", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[EXPORT] ExportSelectedForChatGpt called");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"ExportSelectedForChatGpt failed: {ex.Message}");
                _notificationService.ShowError($"Selected ChatGPT export failed: {ex.Message}", 5);
            }
        }

        /// <summary>
        /// Open previously exported ChatGPT file
        /// </summary>
        private void OpenChatGptExportFile()
        {
            try
            {
                if (string.IsNullOrEmpty(LastChatGptExportFilePath) || !File.Exists(LastChatGptExportFilePath))
                {
                    SetTransientStatus("No ChatGPT export file available.", 3);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = LastChatGptExportFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"OpenChatGptExportFile failed: {ex.Message}");
                _notificationService.ShowError($"Failed to open ChatGPT export file: {ex.Message}", 5);
            }
        }

        // --- Project Methods ---

        /// <summary>
        /// Save current project state
        /// </summary>
        private void SaveProject()
        {
            try
            {
                // Use existing SaveWorkspace functionality
                SaveWorkspace();
                
                SetTransientStatus($"💾 Project saved", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Project saved");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[PROJECT] Failed to save project: {ex.Message}");
                SetTransientStatus("❌ Failed to save project", 3);
            }
        }

        // --- Helper Methods ---

        /// <summary>
        /// Set transient status message
        /// </summary>
        private void SetTransientStatus(string message, int duration = 3, bool blockingError = false)
        {
            TransientStatusRequested?.Invoke(message, blockingError);
            if (duration > 0)
            {
                StatusMessageRequested?.Invoke(message, duration);
            }
        }

        /// <summary>
        /// Update command can-execute states
        /// </summary>
        public void UpdateCommandStates()
        {
            ((RelayCommand?)ExportForChatGptCommand)?.NotifyCanExecuteChanged();
            ((RelayCommand?)OpenChatGptExportCommand)?.NotifyCanExecuteChanged();
        }
    }
}
