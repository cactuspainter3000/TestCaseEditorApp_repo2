using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Handles project lifecycle operations: create, open, save, close
/// </summary>
public partial class ProjectManagementViewModel : ObservableObject
{
    private readonly ILogger<ProjectManagementViewModel> _logger;
    // Removed unused field - pending domain coordinator implementation: private MainViewModel? _mainViewModel;
    private readonly IViewModelFactory? _viewModelFactory;
    private readonly AnythingLLMService? _anythingLLMService;
    private readonly ToastNotificationService? _toastService;
    private readonly NotificationService? _notificationService;

    public ProjectManagementViewModel(
        ILogger<ProjectManagementViewModel> logger,
        IViewModelFactory? viewModelFactory = null,
        AnythingLLMService? anythingLLMService = null,
        ToastNotificationService? toastService = null,
        NotificationService? notificationService = null)
    {
        _logger = logger;
        _viewModelFactory = viewModelFactory;
        _anythingLLMService = anythingLLMService;
        _toastService = toastService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Set reference to MainViewModel for coordination during operations
    /// </summary>
    public void Initialize(MainViewModel mainViewModel)
    {
        // TODO: Replace with proper domain mediator injection
        _logger.LogWarning("Initialize: Method disabled pending domain mediator refactoring");
        // _mainViewModel = mainViewModel; // Disabled - causes architectural violations
    }

    /// <summary>
    /// Creates a new project with comprehensive workflow in main GUI
    /// </summary>
    public void CreateNewProject()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("CreateNewProject: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
    }

    /// <summary>
    /// Opens an existing project by selecting from available AnythingLLM workspaces
    /// <summary>
    /// Opens a project
    /// </summary>
    public void OpenProject()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("OpenProject: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
    }

    public void OpenExistingProject()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("OpenExistingProject: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
    }

    /// <summary>
    /// Saves the current project state
    /// </summary>
    public void SaveProject()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("SaveProject: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
        /*
        try
        {
            if (string.IsNullOrEmpty(_mainViewModel?.CurrentAnythingLLMWorkspaceSlug))
            {
                // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("⚠️ No AnythingLLM workspace selected", 3);
                return;
            }
            
            // Use existing SaveWorkspace functionality
            _mainViewModel?.SaveWorkspace();
            
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"💾 Project saved", 3);
            _logger.LogInformation($"Project saved for workspace '{_mainViewModel?.CurrentAnythingLLMWorkspaceSlug}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to save project: {ex.Message}");
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("❌ Failed to save project", 3);
        }
        */
    }

    /// <summary>
    /// Closes the current project
    /// </summary>
    public void CloseProject()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("CloseProject: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
        /*
        try
        {
            if (_mainViewModel == null) return;
            
            // Clear basic project data
            _mainViewModel.Requirements.Clear();
            _mainViewModel.CurrentAnythingLLMWorkspaceSlug = null;
            _mainViewModel.WorkspacePath = null;
            _mainViewModel.CurrentWorkspace = null;
            _mainViewModel.CurrentRequirement = null;
            
            // Clear imported data if properties exist
            _mainViewModel.WordFilePath = null;
            _mainViewModel.LooseTables.Clear();
            _mainViewModel.LooseParagraphs.Clear();
            
            // Reset basic state
            // TODO: Replace with proper domain coordination: _mainViewModel.IsDirty = false;
            _mainViewModel.DisplayName = "Test Case Editor";
            _mainViewModel.SapStatus = string.Empty;
            
            // TODO: Replace with proper domain UI coordinator: _mainViewModel.SetTransientStatus("📄 Project closed", 3);
            _logger.LogInformation("Project closed and GUI reset to initial state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to close project: {ex.Message}");
        }
        */
    }

    /// <summary>
    /// Handles project creation cancellation
    /// </summary>
    public void OnNewProjectCancelled(object? sender, EventArgs e)
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("OnNewProjectCancelled: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
        /*
        // Return to default view
        if (_mainViewModel != null)
        {
            _mainViewModel.SelectedMenuSection = "Requirements";
        }
        */
    }

    /// <summary>
    /// Handles new project created events
    /// </summary>
    private void OnNewProjectCreated(object? sender, object e)
    {
        try
        {
            _logger.LogInformation("New project creation completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing new project creation");
            _notificationService?.ShowError($"Error creating project: {ex.Message}");
        }
    }
}