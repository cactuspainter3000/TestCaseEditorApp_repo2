using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.IO;
using TestCaseEditorApp.Helpers;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages requirement processing operations including import, parsing, and file handling
/// </summary>
public partial class RequirementProcessingViewModel : ObservableObject
{
    private readonly ILogger<RequirementProcessingViewModel> _logger;
    private readonly IRequirementService _requirementService;
    private readonly IFileDialogService _fileDialog;
    private MainViewModel? _mainViewModel;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _processingStatus = "Ready";

    public RequirementProcessingViewModel(
        ILogger<RequirementProcessingViewModel> logger, 
        IRequirementService requirementService,
        IFileDialogService fileDialog)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
    }

    /// <summary>
    /// Set reference to MainViewModel for coordination
    /// </summary>
    public void Initialize(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _logger.LogInformation("RequirementProcessingViewModel initialized with MainViewModel reference");
    }

    /// <summary>
    /// Import requirements from a file path with workspace creation and processing logic
    /// </summary>
    public async Task ProcessRequirementsAsync(string path, bool replace = true)
    {
        if (_mainViewModel == null)
        {
            _logger.LogError("MainViewModel not initialized");
            return;
        }

        await ImportFromPathAsync(path, replace);
    }

    /// <summary>
    /// Load requirements from a file with auto-detection of workspace vs document files
    /// </summary>
    public async Task LoadRequirementsFromFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("LoadRequirementsFromFileAsync called with empty file path");
            return;
        }

        _logger.LogInformation($"Loading requirements from file: {filePath}");
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Loading requirements...";

            // Check if it's a workspace file
            if (string.Equals(Path.GetExtension(filePath), ".tcex.json", StringComparison.OrdinalIgnoreCase))
            {
                await LoadWorkspaceFileAsync(filePath);
            }
            else
            {
                await ProcessRequirementsAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load requirements from file: {filePath}");
            _mainViewModel?.SetTransientStatus($"Failed to load requirements: {ex.Message}", 5, true);
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = "Ready";
        }
    }

    /// <summary>
    /// Process Word document requirements
    /// </summary>
    public async Task ProcessWordFile(string path)
    {
        _logger.LogInformation($"Processing Word file: {path}");
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing Word document...";

            var requirements = await Task.Run(() => _requirementService.ImportRequirementsFromWord(path));
            await HandleLoadedRequirements(requirements, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process Word file: {path}");
            _mainViewModel?.SetTransientStatus($"Word processing failed: {ex.Message}", 5, true);
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = "Ready";
        }
    }

    /// <summary>
    /// Process CSV file requirements
    /// </summary>
    public async Task ProcessCsvFile(string path)
    {
        _logger.LogInformation($"Processing CSV file: {path}");
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing CSV file...";

            // CSV processing would be implemented here
            var requirements = new List<Requirement>(); // TODO: Implement CSV parsing
            await HandleLoadedRequirements(requirements, path);
            
            _logger.LogWarning("CSV processing not yet implemented");
            _mainViewModel?.SetTransientStatus("CSV processing not yet implemented", 3);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process CSV file: {path}");
            _mainViewModel?.SetTransientStatus($"CSV processing failed: {ex.Message}", 5, true);
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = "Ready";
        }
    }

    /// <summary>
    /// Process Excel file requirements
    /// </summary>
    public async Task ProcessExcelFile(string path)
    {
        _logger.LogInformation($"Processing Excel file: {path}");
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing Excel file...";

            // Excel processing would be implemented here
            var requirements = new List<Requirement>(); // TODO: Implement Excel parsing
            await HandleLoadedRequirements(requirements, path);
            
            _logger.LogWarning("Excel processing not yet implemented");
            _mainViewModel?.SetTransientStatus("Excel processing not yet implemented", 3);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process Excel file: {path}");
            _mainViewModel?.SetTransientStatus($"Excel processing failed: {ex.Message}", 5, true);
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = "Ready";
        }
    }

    /// <summary>
    /// Process DOCX file requirements using both Jama and Word parsers
    /// </summary>
    public async Task ProcessDocxFile(string path)
    {
        _logger.LogInformation($"Processing DOCX file: {path}");
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing DOCX file...";

            var requirements = await Task.Run(() => RequirementServiceCallForImport(path));
            await HandleLoadedRequirements(requirements, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process DOCX file: {path}");
            _mainViewModel?.SetTransientStatus($"DOCX processing failed: {ex.Message}", 5, true);
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = "Ready";
        }
    }

    /// <summary>
    /// Handle loaded requirements and update the workspace
    /// </summary>
    public async Task HandleLoadedRequirements(List<Requirement> requirements, string sourcePath)
    {
        if (_mainViewModel == null)
        {
            _logger.LogError("MainViewModel not initialized for handling loaded requirements");
            return;
        }

        _logger.LogInformation($"Handling {requirements.Count} loaded requirements from {sourcePath}");

        try
        {
            ProcessingStatus = "Updating workspace...";

            // Build workspace model
            var workspace = new Workspace
            {
                SourceDocPath = sourcePath,
                Requirements = requirements.ToList()
            };

            _mainViewModel.CurrentWorkspace = workspace;

            // Update UI-bound collection: preserve existing ObservableCollection instance
            try
            {
                _mainViewModel.Requirements.CollectionChanged -= _mainViewModel.RequirementsOnCollectionChanged;
                _mainViewModel.Requirements.Clear();
                foreach (var r in requirements) 
                {
                    _mainViewModel.Requirements.Add(r);
                }
            }
            finally
            {
                _mainViewModel.Requirements.CollectionChanged += _mainViewModel.RequirementsOnCollectionChanged;
            }

            _mainViewModel.CurrentWorkspace.Requirements = _mainViewModel.Requirements.ToList();

            // Set current requirement
            var firstRequirement = _mainViewModel.Requirements.FirstOrDefault();
            SetCurrentRequirement(firstRequirement);

            _mainViewModel.HasUnsavedChanges = false;
            _mainViewModel.IsDirty = false;

            _mainViewModel?.SetTransientStatus($"Loaded {requirements.Count} requirements", 4);
            
            // Auto-processing logic
            await ProcessAutoActions(requirements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle loaded requirements");
            _mainViewModel?.SetTransientStatus($"Failed to update workspace: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Set the current requirement in the workspace
    /// </summary>
    public void SetCurrentRequirement(Requirement? requirement)
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.CurrentRequirement = requirement;
            _logger.LogInformation($"Set current requirement: {requirement?.GlobalId ?? "null"}");
        }
    }

    /// <summary>
    /// Import from file path with workspace management (from MainViewModel)
    /// </summary>
    private async Task ImportFromPathAsync(string path, bool replace)
    {
        if (_mainViewModel == null) return;

        // If the selected path is a saved workspace, load it directly
        try
        {
            if (string.Equals(Path.GetExtension(path), ".tcex.json", StringComparison.OrdinalIgnoreCase))
            {
                await LoadWorkspaceFileAsync(path);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load workspace file: {path}");
            _mainViewModel.SetTransientStatus($"Failed to load workspace: {ex.Message}", 5, true);
            return;
        }

        if (replace && _mainViewModel.HasUnsavedChanges && _mainViewModel.Requirements.Count > 0)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Replace the current requirements with the new import?",
                "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                _mainViewModel.SetTransientStatus("Import canceled.", 2);
                _logger.LogInformation("Import canceled by user (unsaved changes).");
                return;
            }
        }

        try
        {
            // Handle workspace path creation if needed
            await EnsureWorkspacePathAsync(path);

            _mainViewModel.SetTransientStatus($"Importing {Path.GetFileName(path)}...", 60);
            _logger.LogInformation("Starting import of '{Path}'", path);

            var sw = Stopwatch.StartNew();
            var requirements = await Task.Run(() => RequirementServiceCallForImport(path));
            sw.Stop();

            _logger.LogInformation("Parser returned {Count} requirement(s) in {ElapsedMs}ms", requirements?.Count ?? 0, sw.ElapsedMilliseconds);

            await HandleLoadedRequirements(requirements ?? new List<Requirement>(), path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during import");
            _mainViewModel.SetTransientStatus($"Import failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Load workspace file directly
    /// </summary>
    private async Task LoadWorkspaceFileAsync(string path)
    {
        if (_mainViewModel == null) return;

        try
        {
            var workspace = TestCaseEditorApp.Services.WorkspaceFileManager.Load(path);
            if (workspace == null || (workspace.Requirements?.Count ?? 0) == 0)
            {
                _mainViewModel.SetTransientStatus("Failed to load workspace (file empty or invalid).", 5, true);
                return;
            }

            _mainViewModel.WorkspacePath = path;
            _mainViewModel.CurrentWorkspace = workspace;

            // Replace the observable collection contents
            try
            {
                _mainViewModel.Requirements.CollectionChanged -= _mainViewModel.RequirementsOnCollectionChanged;
                _mainViewModel.Requirements.Clear();
                foreach (var r in workspace.Requirements ?? Enumerable.Empty<Requirement>())
                {
                    _mainViewModel.Requirements.Add(r);
                }
            }
            finally
            {
                _mainViewModel.Requirements.CollectionChanged += _mainViewModel.RequirementsOnCollectionChanged;
            }

            _mainViewModel.CurrentWorkspace.Requirements = _mainViewModel.Requirements.ToList();
            _mainViewModel.HasUnsavedChanges = false;
            _mainViewModel.IsDirty = false;

            _mainViewModel.SetTransientStatus($"Opened workspace: {Path.GetFileName(path)} - {_mainViewModel.Requirements.Count} requirements", 4);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load workspace file: {path}");
            _mainViewModel.SetTransientStatus($"Failed to load workspace: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Ensure workspace path is set up properly
    /// </summary>
    private async Task EnsureWorkspacePathAsync(string sourcePath)
    {
        if (_mainViewModel == null) return;

        // If WorkspacePath is already set, skip the dialog
        if (!string.IsNullOrWhiteSpace(_mainViewModel.WorkspacePath))
        {
            _logger.LogInformation($"Using existing WorkspacePath: '{_mainViewModel.WorkspacePath}'");
            return;
        }

        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestCaseEditorApp", "Workspaces");
        Directory.CreateDirectory(defaultFolder);

        var suggested = FileNameHelper.GenerateUniqueFileName(Path.GetFileNameWithoutExtension(sourcePath), ".tcex.json");

        // Inform user about workspace creation
        var result = MessageBox.Show(
            $"Great! Your document '{Path.GetFileName(sourcePath)}' is ready to import.\n\n" +
            "Next, choose where to save your new project workspace. This will create a project file (.tcex.json) that contains your imported requirements and any test cases you generate.\n\n" +
            "Would you like to proceed?",
            "Create New Project Workspace",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes)
        {
            _mainViewModel.SetTransientStatus("Import canceled by user.", 2);
            _logger.LogInformation("Import canceled by user at workspace creation step.");
            return;
        }

        var workspacePath = _fileDialog.ShowSaveFile(
            title: "Save New Project Workspace", 
            suggestedFileName: suggested, 
            filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*", 
            defaultExt: ".tcex.json", 
            initialDirectory: defaultFolder);

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            _mainViewModel.SetTransientStatus("Import canceled (no workspace name selected).", 2);
            _logger.LogInformation("Import canceled: no workspace name selected.");
            return;
        }

        _mainViewModel.WorkspacePath = FileNameHelper.EnsureUniquePath(Path.GetDirectoryName(workspacePath)!, Path.GetFileName(workspacePath));
        _logger.LogInformation($"Set WorkspacePath to: '{_mainViewModel.WorkspacePath}'");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Call requirement service for import operations (from MainViewModel)
    /// </summary>
    private List<Requirement> RequirementServiceCallForImport(string path)
    {
        try
        {
            _logger.LogInformation($"RequirementServiceCallForImport called with path: {path}");

            if (_requirementService == null)
            {
                _logger.LogWarning("RequirementService is null - cannot import");
                return new List<Requirement>();
            }

            // First try the Jama All Data parser
            _logger.LogInformation("Trying Jama All Data parser...");
            var jamaResults = _requirementService.ImportRequirementsFromJamaAllDataDocx(path) ?? new List<Requirement>();
            _logger.LogInformation($"Jama parser returned {jamaResults.Count} requirements");

            if (jamaResults.Count > 0)
            {
                _logger.LogInformation($"Successfully parsed {jamaResults.Count} requirements using Jama All Data parser");
                return jamaResults;
            }

            // If that didn't work, try the regular Word parser
            _logger.LogInformation("Trying regular Word parser...");
            var wordResults = _requirementService.ImportRequirementsFromWord(path) ?? new List<Requirement>();
            _logger.LogInformation($"Word parser returned {wordResults.Count} requirements");

            if (wordResults.Count > 0)
            {
                _logger.LogInformation($"Successfully parsed {wordResults.Count} requirements using Word parser");
                return wordResults;
            }

            _logger.LogWarning($"No requirements found with either parser for file: {path}");
            return new List<Requirement>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during import: {ex.Message}");
            return new List<Requirement>();
        }
    }

    /// <summary>
    /// Handle auto-processing actions after requirements are loaded
    /// </summary>
    private async Task ProcessAutoActions(List<Requirement> requirements)
    {
        if (_mainViewModel == null || !requirements.Any()) return;

        try
        {
            // Auto-process requirements if enabled
            _logger.LogInformation($"Checking auto-processing: reqs.Any()={requirements.Any()}, AutoAnalyzeOnImport={_mainViewModel.AutoAnalyzeOnImport}, AutoExportForChatGpt={_mainViewModel.AutoExportForChatGpt}");

            if (_mainViewModel.AutoExportForChatGpt)
            {
                _logger.LogInformation($"Auto-exporting {requirements.Count} requirements for ChatGPT");
                // TODO: Wire to ChatGptExportAnalysisViewModel.BatchExportRequirementsForChatGpt(requirements)
                _mainViewModel.SetTransientStatus("Auto-export for ChatGPT not yet implemented", 3);
            }
            else if (requirements.Any() && _mainViewModel.AutoAnalyzeOnImport)
            {
                _logger.LogInformation($"Starting batch analysis for {requirements.Count} requirements");
                // TODO: Wire to batch analysis functionality
                _mainViewModel.SetTransientStatus("Auto-analysis not yet implemented", 3);
            }
            else
            {
                _logger.LogInformation("Auto-processing NOT started - conditions not met");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during auto-processing");
        }

        await Task.CompletedTask;
    }
}