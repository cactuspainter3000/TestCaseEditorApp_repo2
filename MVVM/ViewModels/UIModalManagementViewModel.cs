using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages all modal dialog operations and UI coordination
/// Follows architectural guidelines with proper dependency injection
/// </summary>
public partial class UIModalManagementViewModel : ObservableObject
{
    private readonly ILogger<UIModalManagementViewModel> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationService? _notificationService;
    private MainViewModel? _mainViewModel;

    [ObservableProperty]
    private object? _currentModal;

    [ObservableProperty]
    private string _modalTitle = string.Empty;

    [ObservableProperty]
    private bool _isModalVisible;

    public UIModalManagementViewModel(
        ILogger<UIModalManagementViewModel> logger, 
        IServiceProvider serviceProvider,
        NotificationService? notificationService = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Set reference to MainViewModel for coordination
    /// </summary>
    public void Initialize(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
    }

    /// <summary>
    /// Shows a modal dialog with the specified view model and title
    /// </summary>
    public void ShowModal(object viewModel, string title = "Modal Dialog")
    {
        _logger.LogInformation($"Showing modal: {title}");
        
        CurrentModal = viewModel;
        ModalTitle = title;
        IsModalVisible = true;
    }

    /// <summary>
    /// Closes the current modal dialog
    /// </summary>
    public void CloseModal()
    {
        _logger.LogInformation("Closing modal");
        
        CurrentModal = null;
        ModalTitle = string.Empty;
        IsModalVisible = false;
    }



    /// <summary>
    /// Handles API key configuration completion
    /// </summary>
    public void OnApiKeyConfigured(object? sender, ApiKeyConfiguredEventArgs e)
    {
        _logger.LogInformation("API key configuration completed");
        CloseModal();
        _notificationService?.ShowSuccess("API key configured successfully");
    }

    /// <summary>
    /// Handles API key configuration cancellation
    /// </summary>
    public void OnApiKeyConfigCancelled(object? sender, EventArgs e)
    {
        _logger.LogInformation("API key configuration cancelled");
        CloseModal();
    }

    /// <summary>
    /// Handles workspace selection cancellation
    /// </summary>
    public void OnWorkspaceSelectionCancelled(object? sender, EventArgs e)
    {
        _logger.LogInformation("Workspace selection cancelled");
        CloseModal();
    }



    /// <summary>
    /// Handles requirement editing cancellation
    /// </summary>
    public void OnRequirementEditCancelled(object? sender, EventArgs e)
    {
        _logger.LogInformation("Requirement editing cancelled");
        CloseModal();
    }

    /// <summary>
    /// Handles text splitting completion
    /// </summary>
    public void OnTextSplitCompleted(object? sender, TextSplitCompletedEventArgs e)
    {
        _logger.LogInformation("Text splitting completed");
        CloseModal();
        
        if (e.SplitResults?.Count > 0)
        {
            _notificationService?.ShowSuccess($"Text split into {e.SplitResults.Count} parts");
        }
    }

    /// <summary>
    /// Handles text splitting cancellation
    /// </summary>
    public void OnTextSplitCancelled(object? sender, EventArgs e)
    {
        _logger.LogInformation("Text splitting cancelled");
        CloseModal();
    }

    /// <summary>
    /// Shows API key configuration modal
    /// </summary>
    public void ShowApiKeyConfigModal()
    {
        _logger.LogInformation("Showing API key config modal");
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();
        var viewModel = new ApiKeyConfigViewModel(notificationService);
        viewModel.ApiKeyConfigured += OnApiKeyConfigured;
        viewModel.Cancelled += OnApiKeyConfigCancelled;
        ShowModal(viewModel, "Configure AnythingLLM API Key");
    }

    /// <summary>
    /// Shows workspace selection modal for creating new project
    /// </summary>
    public void ShowWorkspaceSelectionModal()
    {
        _logger.LogInformation("Showing workspace selection modal for new project");
        var anythingLLMService = _serviceProvider.GetRequiredService<AnythingLLMService>();
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();
        var viewModel = new WorkspaceSelectionViewModel(anythingLLMService, notificationService, WorkspaceSelectionViewModel.SelectionMode.CreateNew);
        
        // Connect event handlers through MainViewModel if available
        if (_mainViewModel != null)
        {
            viewModel.WorkspaceSelected += _mainViewModel.OnWorkspaceSelected;
        }
        viewModel.Cancelled += OnWorkspaceSelectionCancelled;
        ShowModal(viewModel, "Create New Project");
    }

    /// <summary>
    /// Shows workspace selection modal for opening existing project
    /// </summary>
    public void ShowWorkspaceSelectionModalForOpen()
    {
        _logger.LogInformation("Showing workspace selection modal for opening project");
        var anythingLLMService = _serviceProvider.GetRequiredService<AnythingLLMService>();
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();
        var viewModel = new WorkspaceSelectionViewModel(anythingLLMService, notificationService, WorkspaceSelectionViewModel.SelectionMode.SelectExisting);
        
        // Connect event handlers through MainViewModel if available
        if (_mainViewModel != null)
        {
            viewModel.WorkspaceSelected += _mainViewModel.OnWorkspaceSelected;
        }
        viewModel.Cancelled += OnWorkspaceSelectionCancelled;
        ShowModal(viewModel, "Open Existing Project");
    }

    /// <summary>
    /// Shows import workflow modal
    /// </summary>
    public void ShowImportWorkflow()
    {
        _logger.LogInformation("Showing import workflow modal");
        var viewModel = new ImportWorkflowViewModel();
        
        // Connect event handlers through MainViewModel if available
        if (_mainViewModel != null)
        {
            viewModel.ImportWorkflowCompleted += _mainViewModel.OnImportWorkflowCompleted;
            viewModel.ImportWorkflowCancelled += _mainViewModel.OnImportWorkflowCancelled;
        }
        viewModel.Show();
        ShowModal(viewModel, "Import Requirements Document");
    }

    /// <summary>
    /// Shows requirement description editor modal
    /// </summary>
    public void ShowRequirementDescriptionEditorModal(Requirement requirement)
    {
        if (requirement == null)
        {
            _logger.LogWarning("ShowRequirementDescriptionEditorModal called with null requirement");
            return;
        }

        _logger.LogInformation($"Showing requirement description editor for {requirement.Item}");
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();
        var viewModel = new RequirementDescriptionEditorViewModel(requirement, notificationService);
        
        // Connect event handlers through MainViewModel if available
        if (_mainViewModel != null)
        {
            viewModel.RequirementEdited += _mainViewModel.OnRequirementEdited;
            viewModel.AnalysisRequested += _mainViewModel.OnRequirementAnalysisRequested;
        }
        viewModel.Cancelled += OnRequirementEditCancelled;
        ShowModal(viewModel, "Edit Requirement Description");
    }

    /// <summary>
    /// Shows requirement editor modal
    /// </summary>
    public void ShowRequirementEditor(Requirement requirement)
    {
        _logger.LogInformation($"ShowRequirementEditor called for {requirement?.Item}");
        
        if (requirement == null)
        {
            _logger.LogWarning("ShowRequirementEditor called with null requirement");
            return;
        }

        // Implementation would go here - this seems to be a placeholder in MainViewModel
        // For now, delegate to the requirement description editor
        ShowRequirementDescriptionEditorModal(requirement);
    }

    /// <summary>
    /// Shows split text editor modal
    /// </summary>
    public void ShowSplitTextEditorModal(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("ShowSplitTextEditorModal called with null or empty text");
            return;
        }

        _logger.LogInformation($"Showing split text editor for text (length: {text.Length})");
        var notificationService = _serviceProvider.GetRequiredService<NotificationService>();
        var viewModel = new SplitTextEditorViewModel(text, notificationService);
        viewModel.SplitCompleted += OnTextSplitCompleted;
        viewModel.Cancelled += OnTextSplitCancelled;
        ShowModal(viewModel, "Split Text Editor");
    }

    /// <summary>
    /// Helper for file dialog operations
    /// </summary>
    public string ShowSaveFileDialogHelper(string suggestedFileName, string initialDirectory)
    {
        _logger.LogInformation($"Showing save file dialog: {suggestedFileName}");
        
        // TODO: Implement actual file dialog logic
        return string.Empty;
    }
}