using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using System.IO;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;

/// <summary>
/// Manages requirement processing operations including import, parsing, and file handling
/// Following proper domain architecture with mediator communication
/// </summary>
public partial class RequirementProcessingViewModel : BaseDomainViewModel
{
    private readonly IRequirementService _requirementService;
    private readonly IFileDialogService _fileDialog;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _processingStatus = "Ready";

    public RequirementProcessingViewModel(
        ITestCaseGenerationMediator mediator,
        ILogger<RequirementProcessingViewModel> logger, 
        IRequirementService requirementService,
        IFileDialogService fileDialog) : base(mediator, logger)
    {
        _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
    }

    /// <summary>
    /// Import requirements from a file path using proper domain coordination
    /// </summary>
    public async Task ProcessRequirementsAsync(string path, bool replace = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("ProcessRequirementsAsync called with empty file path");
            return;
        }

        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing requirements...";
            
            _logger.LogInformation("Processing requirements from: {FilePath}", path);
            
            // Use the proper mediator to import requirements
            var mediator = (ITestCaseGenerationMediator)_mediator;
            var success = await mediator.ImportRequirementsAsync(path, replace ? "Replace" : "Auto");
            
            if (success)
            {
                ProcessingStatus = "Processing completed successfully";
                _logger.LogInformation("Requirements processing completed for: {FilePath}", path);
            }
            else
            {
                ProcessingStatus = "Processing failed";
                _logger.LogWarning("Requirements processing failed for: {FilePath}", path);
            }
        }
        catch (Exception ex)
        {
            ProcessingStatus = "Processing error occurred";
            _logger.LogError(ex, "Error processing requirements from: {FilePath}", path);
        }
        finally
        {
            IsProcessing = false;
        }
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

        _logger.LogInformation("Loading requirements from file: {FilePath}", filePath);
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Loading requirements...";

            // Check if it's a workspace file - delegate to workspace management
            if (string.Equals(Path.GetExtension(filePath), ".tcex.json", StringComparison.OrdinalIgnoreCase))
            {
                // Workspace files should be handled by WorkspaceManagement domain
                _logger.LogInformation("Detected workspace file, delegating to workspace management domain");
                // TODO: Coordinate with WorkspaceManagement domain for workspace file loading
            }
            else
            {
                await ProcessRequirementsAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load requirements from file: {FilePath}", filePath);
        }
        finally
        {
            IsProcessing = false;
            ProcessingStatus = "Ready";
        }
    }

    /// <summary>
    /// Process Word document requirements using domain mediator
    /// </summary>
    public async Task ProcessWordFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("ProcessWordFileAsync called with empty path");
            return;
        }

        _logger.LogInformation("Processing Word file: {Path}", path);
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing Word document...";

            var mediator = (ITestCaseGenerationMediator)_mediator;
            var success = await mediator.ImportRequirementsAsync(path, "Word");
            
            if (success)
            {
                ProcessingStatus = "Word document processed successfully";
            }
            else
            {
                ProcessingStatus = "Word document processing failed";
            }
        }
        catch (Exception ex)
        {
            ProcessingStatus = "Word document processing error";
            _logger.LogError(ex, "Failed to process Word file: {Path}", path);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Process DOCX file requirements using domain mediator
    /// </summary>
    public async Task ProcessDocxFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("ProcessDocxFileAsync called with empty path");
            return;
        }

        _logger.LogInformation("Processing DOCX file: {Path}", path);
        
        try
        {
            IsProcessing = true;
            ProcessingStatus = "Processing DOCX file...";

            // Try both Jama and Word parsers through the mediator
            var mediator = (ITestCaseGenerationMediator)_mediator;
            var success = await mediator.ImportRequirementsAsync(path, "Auto");
            
            if (success)
            {
                ProcessingStatus = "DOCX file processed successfully";
            }
            else
            {
                ProcessingStatus = "DOCX file processing failed";
            }
        }
        catch (Exception ex)
        {
            ProcessingStatus = "DOCX file processing error";
            _logger.LogError(ex, "Failed to process DOCX file: {Path}", path);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    #region BaseDomainViewModel Implementation

    protected override bool CanSave() => false; // This ViewModel doesn't handle saving

    protected override bool CanCancel() => IsProcessing; // Can cancel if processing

    protected override bool CanRefresh() => !IsProcessing; // Can refresh if not processing

    protected override async Task SaveAsync()
    {
        // Not applicable for this ViewModel
        await Task.CompletedTask;
    }

    protected override async Task RefreshAsync()
    {
        if (IsProcessing)
        {
            _logger.LogWarning("Cannot refresh while processing");
            return;
        }

        _logger.LogInformation("Refreshing requirement processing state");
        ProcessingStatus = "Ready";
        await Task.CompletedTask;
    }

    protected override void Cancel()
    {
        if (IsProcessing)
        {
            _logger.LogInformation("Cancelling requirement processing");
            ProcessingStatus = "Cancelled";
            IsProcessing = false;
        }
    }

    #endregion
}