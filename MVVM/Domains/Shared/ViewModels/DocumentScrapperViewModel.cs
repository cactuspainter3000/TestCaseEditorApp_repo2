using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Domains.Shared.ViewModels
{
    /// <summary>
    /// Self-contained Document Scrapper ViewModel that can be embedded as a tab in any view.
    /// Automatically monitors workspace changes and manages attachment scanning lifecycle.
    /// ARCHITECTURAL COMPLIANCE: Self-contained shared component, minimal dependencies
    /// </summary>
    public partial class DocumentScrapperViewModel : ObservableObject
    {
        private readonly IJamaConnectService _jamaService;
        private readonly IWorkspaceContext _workspaceContext;
        private readonly ILogger<DocumentScrapperViewModel> _logger;
        private CancellationTokenSource? _scanCancellationSource;

        // Properties for UI binding
        [ObservableProperty]
        private string _title = "Document Scrapper";

        [ObservableProperty]
        private string _statusMessage = "Ready to scan attachments...";

        [ObservableProperty]
        private bool _isScanning = false;

        [ObservableProperty]
        private bool _hasJamaProject = false;

        [ObservableProperty]
        private int _backgroundScanProgress = 0;

        [ObservableProperty]
        private int _backgroundScanTotal = 0;

        [ObservableProperty]
        private string _currentJamaProjectName = string.Empty;

        [ObservableProperty]
        private string _currentWorkspaceName = string.Empty;

        // Collections for UI - using existing service models
        public ObservableCollection<JamaAttachment> FoundAttachments { get; } = new();
        public ObservableCollection<string> ParsingResults { get; } = new(); // Simplified for now
        public ObservableCollection<Requirement> ExtractedRequirements { get; } = new();

        /// <summary>
        /// Constructor with direct service injection for self-contained operation
        /// </summary>
        public DocumentScrapperViewModel(
            IJamaConnectService jamaService,
            IWorkspaceContext workspaceContext,
            ILogger<DocumentScrapperViewModel> logger)
        {
            _jamaService = jamaService ?? throw new ArgumentNullException(nameof(jamaService));
            _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to workspace changes for auto-detection
            _workspaceContext.WorkspaceChanged += OnWorkspaceChanged;
            
            // Initialize with current workspace
            OnWorkspaceChanged(null, EventArgs.Empty);

            _logger.LogInformation("[DocumentScrapper] Self-contained component initialized");
        }

        /// <summary>
        /// Auto-detect Jama project when workspace changes
        /// </summary>
        private void OnWorkspaceChanged(object? sender, EventArgs e)
        {
            try
            {
                var workspace = _workspaceContext.CurrentWorkspace;
                CurrentWorkspaceName = workspace?.Name ?? "No workspace";

                _logger.LogInformation("[DocumentScrapper] Workspace changed: {WorkspaceName}, JamaProject: {JamaProject}, ImportSource: {ImportSource}", 
                    CurrentWorkspaceName, workspace?.JamaProject, workspace?.ImportSource);

                // DEBUG: Log all workspace properties to understand what we have
                _logger.LogInformation("[DocumentScrapper] DEBUG Workspace Properties:");
                _logger.LogInformation("[DocumentScrapper] - Name: {Name}", workspace?.Name);
                _logger.LogInformation("[DocumentScrapper] - JamaProject: '{JamaProject}'", workspace?.JamaProject ?? "NULL");
                _logger.LogInformation("[DocumentScrapper] - JamaTestPlan: '{JamaTestPlan}'", workspace?.JamaTestPlan ?? "NULL");
                _logger.LogInformation("[DocumentScrapper] - ImportSource: '{ImportSource}'", workspace?.ImportSource ?? "NULL");
                _logger.LogInformation("[DocumentScrapper] - SourceDocPath: '{SourceDocPath}'", workspace?.SourceDocPath ?? "NULL");

                // Check for Jama project association - handle both numeric IDs and project names
                var jamaProjectId = TryGetJamaProjectId(workspace);
                if (jamaProjectId.HasValue)
                {
                    HasJamaProject = true;
                    // Display project name from JamaTestPlan if available, otherwise use JamaProject
                    CurrentJamaProjectName = workspace?.JamaTestPlan ?? workspace?.JamaProject ?? $"Project {jamaProjectId.Value}";
                    StatusMessage = $"Ready to scan attachments for {CurrentJamaProjectName}";
                    
                    _logger.LogInformation("[DocumentScrapper] Detected Jama project: {ProjectName} (ID: {ProjectId})", 
                        CurrentJamaProjectName, jamaProjectId.Value);
                    
                    // Auto-trigger scanning if we have a Jama project
                    _ = Task.Run(async () => await TriggerAttachmentScanAsync(jamaProjectId.Value));
                }
                else
                {
                    HasJamaProject = false;
                    CurrentJamaProjectName = "No Jama project";
                    
                    // Check if this is a Jama import but we couldn't parse the ID
                    if (workspace?.ImportSource == "Jama" && !string.IsNullOrEmpty(workspace?.JamaProject))
                    {
                        StatusMessage = $"Jama project '{workspace.JamaProject}' detected, but project ID not available for attachment scanning";
                        _logger.LogWarning("[DocumentScrapper] Jama import detected but could not extract project ID from: {JamaProject}", workspace.JamaProject);
                    }
                    else
                    {
                        StatusMessage = "No Jama project associated with current workspace";
                        _logger.LogDebug("[DocumentScrapper] No Jama association found - ImportSource: {ImportSource}, JamaProject: {JamaProject}", 
                            workspace?.ImportSource, workspace?.JamaProject);
                    }
                    
                    // Clear previous results
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FoundAttachments.Clear();
                        ParsingResults.Clear();
                        ExtractedRequirements.Clear();
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DocumentScrapper] Error handling workspace change");
                StatusMessage = "Error detecting Jama project";
            }
        }

        /// <summary>
        /// Extract Jama project ID from workspace - handles both numeric IDs and project names
        /// </summary>
        private int? TryGetJamaProjectId(Workspace? workspace)
        {
            if (workspace?.JamaProject == null) return null;

            // Try direct numeric ID first
            if (int.TryParse(workspace.JamaProject, out var directId))
            {
                return directId;
            }

            // For project names like "DECAGON-REQ_RC-5", we need to look up the actual project ID
            // This would require calling the Jama API to get all projects and find the matching one
            // For now, let's check if we can extract any numeric part from the project name
            var numbers = System.Text.RegularExpressions.Regex.Matches(workspace.JamaProject, @"\d+");
            if (numbers.Count > 0 && int.TryParse(numbers[0].Value, out var extractedId))
            {
                _logger.LogInformation("[DocumentScrapper] Extracted potential project ID {ProjectId} from project name {ProjectName}", 
                    extractedId, workspace.JamaProject);
                return extractedId;
            }

            // If we can't extract an ID, we'll need to enhance this to look up the project by name
            _logger.LogWarning("[DocumentScrapper] Could not extract project ID from Jama project: {JamaProject}", workspace.JamaProject);
            return null;
        }

        /// <summary>
        /// Trigger attachment scanning for the current Jama project
        /// </summary>
        private async Task TriggerAttachmentScanAsync(int jamaProjectId)
        {
            try
            {
                if (IsScanning)
                {
                    _logger.LogDebug("[DocumentScrapper] Scan already in progress, ignoring trigger");
                    return;
                }

                _scanCancellationSource = new CancellationTokenSource();
                IsScanning = true;
                StatusMessage = "Scanning for attachments...";
                BackgroundScanProgress = 0;
                BackgroundScanTotal = 0;

                // Clear previous results
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FoundAttachments.Clear();
                    ParsingResults.Clear();
                    ExtractedRequirements.Clear();
                });

                _logger.LogInformation("[DocumentScrapper] Starting attachment scan for project {ProjectId}", jamaProjectId);

                // Get all attachments for the project using available service method
                var attachments = await _jamaService.GetProjectAttachmentsAsync(
                    jamaProjectId, 
                    _scanCancellationSource.Token, 
                    (current, total, message) => 
                    {
                        // Update progress on UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = message;
                            BackgroundScanProgress = current;
                            BackgroundScanTotal = total;
                        });
                    }, 
                    CurrentJamaProjectName);
                
                if (attachments?.Any() == true)
                {
                    // Update UI on main thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var attachment in attachments)
                        {
                            FoundAttachments.Add(attachment);
                        }
                    });

                    BackgroundScanTotal = attachments.Count;
                    StatusMessage = $"Found {attachments.Count} attachments. Analysis complete.";

                    // For now, just add simple parsing results since we don't have direct parsing access
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var attachment in attachments)
                        {
                            ParsingResults.Add($"Attachment {attachment.Id} ({attachment.Name}): Ready for analysis");
                        }
                    });

                    StatusMessage = $"Scan completed. Found {attachments.Count} attachments ready for analysis.";
                }
                else
                {
                    StatusMessage = "No attachments found for this project.";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Attachment scan cancelled.";
                _logger.LogInformation("[DocumentScrapper] Attachment scan cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DocumentScrapper] Error during attachment scanning");
                StatusMessage = "Error occurred during attachment scanning.";
            }
            finally
            {
                IsScanning = false;
                _scanCancellationSource?.Dispose();
                _scanCancellationSource = null;
            }
        }

        /// <summary>
        /// Manual refresh command
        /// </summary>
        [RelayCommand]
        private async Task RefreshAttachmentsAsync()
        {
            var workspace = _workspaceContext.CurrentWorkspace;
            if (workspace?.JamaProject != null && int.TryParse(workspace.JamaProject, out var projectId))
            {
                await TriggerAttachmentScanAsync(projectId);
            }
            else
            {
                StatusMessage = "No Jama project to refresh.";
            }
        }

        /// <summary>
        /// Cancel current scan
        /// </summary>
        [RelayCommand]
        private void CancelScan()
        {
            _scanCancellationSource?.Cancel();
        }

        /// <summary>
        /// Import selected requirements (placeholder for future implementation)
        /// </summary>
        [RelayCommand]
        private async Task ImportSelectedRequirementsAsync()
        {
            // This could be enhanced to allow selective import
            StatusMessage = "Import functionality would be implemented here.";
            await Task.Delay(1000); // Placeholder
        }

        /// <summary>
        /// Cleanup when disposed
        /// </summary>
        public void Dispose()
        {
            _workspaceContext.WorkspaceChanged -= OnWorkspaceChanged;
            _scanCancellationSource?.Cancel();
            _scanCancellationSource?.Dispose();
        }
    }
}