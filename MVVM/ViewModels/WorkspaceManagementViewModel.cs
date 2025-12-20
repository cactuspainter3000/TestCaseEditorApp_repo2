using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using Application = System.Windows.Application;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages workspace operations: initialization, loading, saving, auto-save, and diagnostics
/// </summary>
public partial class WorkspaceManagementViewModel : ObservableObject
{
    private readonly ILogger<WorkspaceManagementViewModel> _logger;
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

    public WorkspaceManagementViewModel(ILogger<WorkspaceManagementViewModel> logger)
    {
        _logger = logger;
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
            // TODO: Implement actual workspace step initialization logic
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
        _logger.LogInformation("Reloading workspace");
        
        if (string.IsNullOrEmpty(WorkspacePath))
        {
            _logger.LogWarning("No workspace path set for reload");
            _mainViewModel?.SetTransientStatus("No workspace to reload", 3);
            return;
        }

        try
        {
            await LoadWorkspaceFromPathAsync(WorkspacePath);
            _mainViewModel?.SetTransientStatus("Workspace reloaded successfully", 3);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to reload workspace from: {WorkspacePath}");
            _mainViewModel?.SetTransientStatus($"Reload failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Save the current workspace synchronously
    /// </summary>
    public void SaveWorkspace()
    {
        _logger.LogInformation("Saving workspace synchronously");
        
        try
        {
            if (string.IsNullOrEmpty(WorkspacePath))
            {
                _logger.LogWarning("No workspace path set for save");
                _mainViewModel?.SetTransientStatus("No workspace path set - use Save As", 3);
                return;
            }

            // Save workspace using WorkspaceFileManager
            if (CurrentWorkspace != null)
            {
                WorkspaceFileManager.Save(WorkspacePath, CurrentWorkspace);
                HasUnsavedChanges = false;
                IsDirty = false;
                
                LogPostSaveDiagnostics(WorkspacePath);
                _mainViewModel?.SetTransientStatus($"Workspace saved: {Path.GetFileName(WorkspacePath)}", 3);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to save workspace to: {WorkspacePath}");
            _mainViewModel?.SetTransientStatus($"Save failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Save the current workspace asynchronously
    /// </summary>
    public async Task SaveWorkspaceAsync()
    {
        _logger.LogInformation("Saving workspace asynchronously");
        
        await Task.Run(() => SaveWorkspace());
    }

    /// <summary>
    /// Load workspace with file dialog
    /// </summary>
    public void LoadWorkspace()
    {
        _logger.LogInformation("Loading workspace with file dialog");
        
        try
        {
            // TODO: Show file dialog to select workspace file
            _mainViewModel?.SetTransientStatus("Load workspace dialog would open here", 3);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show load workspace dialog");
            _mainViewModel?.SetTransientStatus($"Failed to show load dialog: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Load workspace from specific file path
    /// </summary>
    public void LoadWorkspaceFromPath(string filePath)
    {
        _logger.LogInformation($"Loading workspace from path: {filePath}");
        
        Task.Run(async () => await LoadWorkspaceFromPathAsync(filePath));
    }

    /// <summary>
    /// Load workspace from specific file path asynchronously
    /// </summary>
    public async Task LoadWorkspaceFromPathAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning($"Workspace file not found: {filePath}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _mainViewModel?.SetTransientStatus("Workspace file not found", 5, true);
                });
                return;
            }

            // Load workspace using WorkspaceFileManager
            var workspace = await Task.Run(() => WorkspaceFileManager.Load(filePath));
            
            if (workspace == null)
            {
                _logger.LogWarning($"Failed to load workspace from: {filePath}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _mainViewModel?.SetTransientStatus("Invalid workspace file", 5, true);
                });
                return;
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentWorkspace = workspace;
                WorkspacePath = filePath;
                HasUnsavedChanges = false;
                IsDirty = false;

                // Update MainViewModel requirements collection
                if (_mainViewModel?.Requirements != null)
                {
                    _mainViewModel.Requirements.Clear();
                    foreach (var req in workspace.Requirements ?? Enumerable.Empty<Requirement>())
                    {
                        _mainViewModel.Requirements.Add(req);
                    }
                }

                _mainViewModel?.SetTransientStatus($"Loaded workspace: {Path.GetFileName(filePath)}", 3);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to load workspace from: {filePath}");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _mainViewModel?.SetTransientStatus($"Load failed: {ex.Message}", 5, true);
            });
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
    /// Log diagnostics information after saving
    /// </summary>
    public void LogPostSaveDiagnostics(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                _logger.LogInformation($"Workspace saved successfully - File: {path}, Size: {fileInfo.Length} bytes, Modified: {fileInfo.LastWriteTime}");
                
                // Log requirements count if available
                if (CurrentWorkspace?.Requirements != null)
                {
                    _logger.LogInformation($"Workspace contains {CurrentWorkspace.Requirements.Count} requirements");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log post-save diagnostics");
        }
    }

    /// <summary>
    /// Mark workspace as having unsaved changes
    /// </summary>
    public void MarkAsChanged()
    {
        HasUnsavedChanges = true;
        IsDirty = true;
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
}