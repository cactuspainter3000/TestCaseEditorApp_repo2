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
    private MainViewModel? _mainViewModel;
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
        _mainViewModel = mainViewModel;
    }

    /// <summary>
    /// Creates a new project with comprehensive workflow in main GUI
    /// </summary>
    public void CreateNewProject()
    {
        try
        {
            _logger.LogInformation("CreateNewProject started");
            
            // Note: Navigation header reset will be handled by the NavigationHeaderManagementViewModel
            // _mainViewModel?.NavigationHeaderManagement?.CreateAndAssignNewProjectHeader();
            
            // Show the full workflow in the main content area
            if (_mainViewModel?.NewProjectWorkflow == null && _anythingLLMService != null && _toastService != null && _mainViewModel != null)
            {
                var workflow = new NewProjectWorkflowViewModel(_anythingLLMService, _toastService);
                _mainViewModel.NewProjectWorkflow = workflow;
                workflow.ProjectCreated += OnNewProjectCreated;
                workflow.ProjectCancelled += OnNewProjectCancelled;
            }
            
            (_mainViewModel?.NewProjectWorkflow as NewProjectWorkflowViewModel)?.Initialize();
            if (_mainViewModel != null && _mainViewModel.NewProjectWorkflow != null)
            {
                _mainViewModel.CurrentStepViewModel = _mainViewModel.NewProjectWorkflow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateNewProject");
            _notificationService?.ShowError($"Error creating new project: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens an existing project by selecting from available AnythingLLM workspaces
    /// </summary>
    public void OpenProject()
    {
        try
        {
            _logger.LogInformation("OpenProject started");
            
            // Show workspace selection modal for selecting existing workspace
            _mainViewModel?.ShowWorkspaceSelectionModalForOpen();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to open project: {ex.Message}");
            _notificationService?.ShowError($"Error opening project: {ex.Message}");
            _mainViewModel?.SetTransientStatus("❌ Failed to open project", 3);
        }
    }

    /// <summary>
    /// Saves the current project state
    /// </summary>
    public void SaveProject()
    {
        try
        {
            if (string.IsNullOrEmpty(_mainViewModel?.CurrentAnythingLLMWorkspaceSlug))
            {
                _mainViewModel?.SetTransientStatus("⚠️ No AnythingLLM workspace selected", 3);
                return;
            }
            
            // Use existing SaveWorkspace functionality
            _mainViewModel?.SaveWorkspace();
            
            _mainViewModel?.SetTransientStatus($"💾 Project saved", 3);
            _logger.LogInformation($"Project saved for workspace '{_mainViewModel?.CurrentAnythingLLMWorkspaceSlug}'");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to save project: {ex.Message}");
            _mainViewModel?.SetTransientStatus("❌ Failed to save project", 3);
        }
    }

    /// <summary>
    /// Closes the current project
    /// </summary>
    public void CloseProject()
    {
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
            _mainViewModel.IsDirty = false;
            _mainViewModel.DisplayName = "Test Case Editor";
            _mainViewModel.SapStatus = string.Empty;
            
            _mainViewModel.SetTransientStatus("📄 Project closed", 3);
            _logger.LogInformation("Project closed and GUI reset to initial state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to close project: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles project creation cancellation
    /// </summary>
    public void OnNewProjectCancelled(object? sender, EventArgs e)
    {
        // Return to default view
        if (_mainViewModel != null)
        {
            _mainViewModel.SelectedMenuSection = "Requirements";
        }
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