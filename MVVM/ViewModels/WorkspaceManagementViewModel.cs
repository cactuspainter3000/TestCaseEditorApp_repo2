using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Win32;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using Application = System.Windows.Application;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages workspace operations: initialization, loading, saving, auto-save, and diagnostics
/// Follows architectural guidelines as shared infrastructure (not domain-specific)
/// </summary>
public partial class WorkspaceManagementViewModel : ObservableObject
{
    private readonly ILogger<WorkspaceManagementViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationService? _notificationService;
    private MainViewModel? _mainViewModel;
    
    // Auto-save functionality
    private System.Timers.Timer? _autoSaveTimer;
    private const int AutoSaveIntervalMinutes = 3;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _workspacePath;

    [ObservableProperty]
    private Workspace? _currentWorkspace;

    [ObservableProperty]
    private string? _currentSourcePath;

    public WorkspaceManagementViewModel(
        ILogger<WorkspaceManagementViewModel> logger,
        IServiceProvider serviceProvider,
        NotificationService? notificationService = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Initialize the ViewModel with MainViewModel reference for coordination
    /// </summary>
    public void Initialize(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        InitializeAutoSave();
    }

    /// <summary>
    /// Initialize workspace steps and configuration
    /// </summary>
    public void InitializeSteps()
    {
        _logger.LogInformation("Initializing workspace steps");
        
        try
        {
            // Add Project step first
            _mainViewModel?.TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "project",
                DisplayName = "Project",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc =>
                {
                    return new ProjectViewModel();
                }
            });

            _mainViewModel?.TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "requirements",
                DisplayName = "Requirement",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc => new RequirementsViewModel(
                    new TestCaseEditorApp.Services.NoOpPersistenceService(), 
                    _mainViewModel, 
                    new TestCaseGenerator_CoreVM())
            });

            _mainViewModel?.TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "llm-learning",
                DisplayName = "LLM Learning",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc =>
                {
                    return new LLMLearningViewModel();
                }
            });

            _mainViewModel?.TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "testcase-creation",
                DisplayName = "Test Case Generator",
                Badge = string.Empty,
                HasFileMenu = true,
                IsSelectable = true,  // Allow selection when test cases exist
                CreateViewModel = svc => new TestCaseGenerator_CreationVM(_mainViewModel)
            });
            
            _mainViewModel?.SetTransientStatus("Workspace steps initialized", 2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize workspace steps");
            _mainViewModel?.SetTransientStatus($"Failed to initialize steps: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Reload the current workspace
    /// </summary>
    public async Task ReloadAsync()
    {
        await Task.CompletedTask;
        
        if (string.IsNullOrEmpty(WorkspacePath))
        {
            _mainViewModel?.SetTransientStatus("No workspace to reload.", 2);
            return;
        }

        LoadWorkspaceFromPath(WorkspacePath);
    }

    /// <summary>
    /// Save the current workspace synchronously
    /// </summary>
    public void SaveWorkspace()
    {
        _logger.LogInformation($"[SaveWorkspace] Quick save called. WorkspacePath='{WorkspacePath ?? "<null>"}'");
        
        if (string.IsNullOrWhiteSpace(WorkspacePath))
        {
            _logger.LogInformation("[SaveWorkspace] No existing path, delegating to SaveAs");
            // No existing path, delegate to SaveAs
            _ = SaveWorkspaceAsync();
            return;
        }

        if (_mainViewModel?.Requirements == null || _mainViewModel.Requirements.Count == 0)
        {
            _mainViewModel?.SetTransientStatus("Nothing to save.", 2);
            return;
        }

        var ws = new Workspace
        {
            SourceDocPath = CurrentSourcePath,
            Requirements = _mainViewModel.Requirements.ToList()
        };

        try
        {
            TestCaseEditorApp.Services.WorkspaceFileManager.Save(WorkspacePath!, ws);
            LogPostSaveDiagnostics(WorkspacePath!);
            CurrentWorkspace = ws;
            IsDirty = false;
            HasUnsavedChanges = false;
            
            // Track in recent files
            try 
            { 
                var recentFilesService = _serviceProvider.GetService<RecentFilesService>();
                recentFilesService?.AddRecentFile(WorkspacePath!); 
            } 
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add file to recent files");
            }
            
            _mainViewModel?.SetTransientStatus($"Saved: {Path.GetFileName(WorkspacePath)}", 3);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to save workspace to: {WorkspacePath}");
            _notificationService?.ShowError($"Failed to save workspace: {ex.Message}", 8);
        }
    }

    /// <summary>
    /// Save As - prompts for location
    /// </summary>
    public async Task SaveWorkspaceAsync()
    {
        // Ensure async methods contain an await to satisfy analyzer when method is mostly synchronous
        await Task.CompletedTask;
        
        if (_mainViewModel?.Requirements == null || _mainViewModel.Requirements.Count == 0)
        {
            _mainViewModel?.SetTransientStatus("Nothing to save.", 2);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Title = "Save Session As",
            Filter = "Test Case Editor Session|*.tcex.json",
            DefaultExt = ".tcex.json",
            RestoreDirectory = true,
            InitialDirectory = !string.IsNullOrWhiteSpace(WorkspacePath) ? Path.GetDirectoryName(WorkspacePath) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (sfd.ShowDialog() != true) return;

        WorkspacePath = sfd.FileName;
        SaveWorkspace(); // Delegate to quick save now that we have a path
    }

    /// <summary>
    /// Load workspace with file dialog
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
        _logger.LogInformation($"[LoadWorkspace] Starting to load workspace from: {filePath}");
        
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogInformation($"[LoadWorkspace] Invalid file path or file doesn't exist: {filePath}");
            _mainViewModel?.SetTransientStatus("Invalid workspace file path.", blockingError: true);
            return;
        }

        WorkspacePath = filePath;
        _logger.LogInformation($"[LoadWorkspace] Set WorkspacePath to: {WorkspacePath}");

        try
        {
            var workspace = TestCaseEditorApp.Services.WorkspaceFileManager.Load(filePath);
            if (workspace == null)
            {
                _logger.LogWarning($"[LoadWorkspace] Failed to load workspace from: {filePath}");
                _mainViewModel?.SetTransientStatus("Failed to load workspace file.", blockingError: true);
                return;
            }
            
            _logger.LogInformation($"[LoadWorkspace] Successfully loaded workspace with {workspace.Requirements?.Count ?? 0} requirements");

            CurrentWorkspace = workspace;
            CurrentSourcePath = workspace.SourceDocPath;

            // Clear existing requirements and load from workspace
            if (_mainViewModel?.Requirements != null)
            {
                _mainViewModel.Requirements.Clear();
                if (workspace.Requirements != null)
                {
                    foreach (var req in workspace.Requirements)
                    {
                        _mainViewModel.Requirements.Add(req);
                    }
                }
            }

            IsDirty = false;
            HasUnsavedChanges = false;
            
            // Track in recent files
            try 
            { 
                var recentFilesService = _serviceProvider.GetService<RecentFilesService>();
                recentFilesService?.AddRecentFile(filePath); 
            } 
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add file to recent files");
            }
            
            _mainViewModel?.SetTransientStatus($"Loaded: {Path.GetFileName(filePath)}", 3);
            _logger.LogInformation($"[LoadWorkspace] Successfully completed loading workspace from: {filePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[LoadWorkspace] Failed to load workspace from: {filePath}");
            _notificationService?.ShowError($"Failed to load workspace: {ex.Message}", 8);
        }
    }



    /// <summary>
    /// Invoke save workspace operation with error handling
    /// </summary>
    public void TryInvokeSaveWorkspace()
    {
        _logger.LogInformation("Invoking save workspace operation");
        
        try
        {
            SaveWorkspace();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TryInvokeSaveWorkspace failed");
            _mainViewModel?.SetTransientStatus($"Save operation failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Invoke load workspace operation with error handling
    /// </summary>
    public void TryInvokeLoadWorkspace()
    {
        _logger.LogInformation("Invoking load workspace operation");
        
        try
        {
            LoadWorkspace();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TryInvokeLoadWorkspace failed");
            _mainViewModel?.SetTransientStatus($"Load operation failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Initialize auto-save functionality
    /// </summary>
    public void InitializeAutoSave()
    {
        _logger.LogInformation("Initializing auto-save functionality");
        
        try
        {
            // Dispose existing timer if any
            _autoSaveTimer?.Dispose();
            
            // Create new timer for auto-save
            _autoSaveTimer = new System.Timers.Timer(AutoSaveIntervalMinutes * 60 * 1000); // Convert minutes to milliseconds
            _autoSaveTimer.Elapsed += (sender, e) => SaveSessionAuto();
            _autoSaveTimer.AutoReset = true;
            _autoSaveTimer.Start();
            
            _logger.LogInformation($"Auto-save enabled with {AutoSaveIntervalMinutes} minute interval");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize auto-save");
        }
    }

    /// <summary>
    /// Perform automatic session save
    /// </summary>
    public void SaveSessionAuto()
    {
        if (!HasUnsavedChanges || string.IsNullOrEmpty(WorkspacePath))
        {
            return;
        }

        try
        {
            _logger.LogInformation("Performing auto-save");
            
            SaveWorkspace();
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _mainViewModel?.SetTransientStatus("Auto-saved", 1);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-save failed");
        }
    }

    /// <summary>
    /// Log diagnostic information after saving workspace
    /// </summary>
    private void LogPostSaveDiagnostics(string savedPath)
    {
        try
        {
            var fileInfo = new FileInfo(savedPath);
            if (fileInfo.Exists)
            {
                _logger.LogInformation($"[SaveWorkspace] File confirmed saved at: {fileInfo.FullName}");
                _logger.LogInformation($"[SaveWorkspace] File size: {fileInfo.Length} bytes");
                _logger.LogInformation($"[SaveWorkspace] File modified: {fileInfo.LastWriteTime}");
            }
            else
            {
                _logger.LogWarning($"[SaveWorkspace] File not found after save attempt: {savedPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[SaveWorkspace] Error during post-save diagnostics for: {savedPath}");
        }
    }

    /// <summary>
    /// Mark workspace as dirty (has unsaved changes)
    /// </summary>
    public void MarkDirty()
    {
        IsDirty = true;
        HasUnsavedChanges = true;
        _logger.LogDebug("Workspace marked as dirty");
    }

    /// <summary>
    /// Mark workspace as having unsaved changes (legacy method name)
    /// </summary>
    public void MarkAsChanged()
    {
        MarkDirty();
    }

    /// <summary>
    /// Check if workspace has unsaved changes
    /// </summary>
    public bool HasChanges => HasUnsavedChanges || IsDirty;

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
    }

    /// <summary>
    /// Updates window title to reflect dirty state with asterisk
    /// </summary>
    public void UpdateWindowTitle()
    {
        // This method will need access to MainViewModel's workspace header
        // For now, delegate back to MainViewModel until proper coordination is established
        if (_mainViewModel != null)
        {
            var baseName = string.IsNullOrEmpty(_mainViewModel.WorkspacePath)
                ? "Test Case Editor"
                : System.IO.Path.GetFileNameWithoutExtension(_mainViewModel.WorkspacePath);
            // Update will need proper header access
        }
    }
}