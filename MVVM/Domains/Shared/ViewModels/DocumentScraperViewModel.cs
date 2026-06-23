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
using System.Diagnostics;
using System.Windows.Threading;

namespace TestCaseEditorApp.MVVM.Domains.Shared.ViewModels
{
    /// <summary>
    /// Workflow states for the document scraper smart button
    /// </summary>
    public enum DocumentScrapingWorkflowState
    {
        /// <summary>
        /// Ready to search for attachments
        /// </summary>
        ReadyToSearch,
        
        /// <summary>
        /// Attachments found, ready to select and scan
        /// </summary>
        ReadyToScan,
        
        /// <summary>
        /// Currently scanning document
        /// </summary>
        Scanning,
        
        /// <summary>
        /// Requirements extracted, ready to import
        /// </summary>
        ReadyToImport,
        
        /// <summary>
        /// No Jama project available
        /// </summary>
        NoJamaProject
    }
    /// <summary>
    /// Self-contained Document Scraper ViewModel that can be embedded as a tab in any view.
    /// Automatically monitors workspace changes and manages attachment scanning lifecycle.
    /// ARCHITECTURAL COMPLIANCE: Self-contained shared component, minimal dependencies
    /// </summary>
    public partial class DocumentScraperViewModel : ObservableObject
    {
        private readonly IJamaConnectService _jamaService;
        private readonly IWorkspaceContext _workspaceContext;
        private readonly ILogger<DocumentScraperViewModel> _logger;
        private CancellationTokenSource? _scanCancellationSource;
        private Stopwatch? _scanStopwatch;
        private DispatcherTimer? _elapsedTimer;
        private int? _resolvedJamaProjectId;

        // Properties for UI binding
        [ObservableProperty]
        private string _title = "Document Scraper";

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
        private string _elapsedTime = "00:00";

        [ObservableProperty]
        private bool _showElapsedTime = false;

        [ObservableProperty]
        private DocumentScrapingWorkflowState _currentWorkflowState = DocumentScrapingWorkflowState.NoJamaProject;

        [ObservableProperty]
        private string _smartButtonText = "No Jama Project";

        [ObservableProperty]
        private bool _smartButtonEnabled = false;

        [ObservableProperty]
        private string _smartToggleButtonText = "🔍 Scan Jama for Attachments";

        [ObservableProperty]
        private bool _smartToggleButtonEnabled = false;

        [ObservableProperty]
        private bool _isInScanMode = false; // Toggle state: false = scan Jama, true = scrape document

        [ObservableProperty]
        private string _currentWorkspaceName = string.Empty;
        
        [ObservableProperty]
        private JamaAttachment? _selectedAttachment;
        
        /// <summary>
        /// Whether we can scan the selected attachment for requirements
        /// </summary>
        public bool CanScanSelectedAttachment => SelectedAttachment != null && !IsScanning;
        
        /// <summary>
        /// Whether we have extracted requirements available for import
        /// </summary>
        public bool HasExtractedRequirements => ExtractedRequirements.Any();

        partial void OnSelectedAttachmentChanged(JamaAttachment? value)
        {
            OnPropertyChanged(nameof(CanScanSelectedAttachment));
            UpdateWorkflowState();
        }
        
        partial void OnIsScanningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanScanSelectedAttachment));
            UpdateWorkflowState();
        }

        partial void OnHasJamaProjectChanged(bool value)
        {
            UpdateWorkflowState();
        }

        // Collections for UI - using existing service models
        public ObservableCollection<JamaAttachment> FoundAttachments { get; } = new();
        public ObservableCollection<string> ParsingResults { get; } = new(); // Simplified for now
        public ObservableCollection<Requirement> ExtractedRequirements { get; }
        
        private void InitializeCollections()
        {
            ExtractedRequirements.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(HasExtractedRequirements));
                UpdateWorkflowState();
            };
            FoundAttachments.CollectionChanged += (s, e) => UpdateWorkflowState();
        }

        /// <summary>
        /// Initialize the elapsed time timer
        /// </summary>
        private void InitializeTimer()
        {
            _elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // Update every second
            };
            _elapsedTimer.Tick += OnTimerTick;
        }

        /// <summary>
        /// Timer tick handler to update elapsed time display
        /// </summary>
        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_scanStopwatch?.IsRunning == true)
            {
                var elapsed = _scanStopwatch.Elapsed;
                ElapsedTime = $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            }
        }

        /// <summary>
        /// Start the elapsed time tracking
        /// </summary>
        private void StartElapsedTimeTracking()
        {
            _scanStopwatch = Stopwatch.StartNew();
            _elapsedTimer?.Start();
            ShowElapsedTime = true;
            ElapsedTime = "00:00";
        }

        /// <summary>
        /// Stop the elapsed time tracking
        /// </summary>
        private void StopElapsedTimeTracking()
        {
            _scanStopwatch?.Stop();
            _elapsedTimer?.Stop();
            ShowElapsedTime = false;
        }

        /// <summary>
        /// Update the workflow state and smart button text based on current conditions
        /// </summary>
        private void UpdateWorkflowState()
        {
            if (!HasJamaProject)
            {
                CurrentWorkflowState = DocumentScrapingWorkflowState.NoJamaProject;
                SmartButtonText = "🔍 Search for Attachments";
                SmartButtonEnabled = true;
                
                // Toggle button disabled when no Jama project
                SmartToggleButtonEnabled = false;
                SmartToggleButtonText = "🔍 Scan Jama for Attachments";
                IsInScanMode = false;
            }
            else if (IsScanning)
            {
                CurrentWorkflowState = DocumentScrapingWorkflowState.Scanning;
                SmartButtonText = "⏹️ Cancel Scan";
                SmartButtonEnabled = true;
                
                // Toggle button disabled during scanning
                SmartToggleButtonEnabled = false;
                SmartToggleButtonText = IsInScanMode ? "📄 Scraping Document..." : "🔍 Scanning Jama...";
            }
            else if (HasExtractedRequirements)
            {
                CurrentWorkflowState = DocumentScrapingWorkflowState.ReadyToImport;
                SmartButtonText = "📤 Import Requirements";
                SmartButtonEnabled = true;
                
                // Toggle button enabled - switch to scan mode after extraction
                SmartToggleButtonEnabled = true;
                SmartToggleButtonText = "🔍 Scan Jama for Attachments";
                IsInScanMode = false;
            }
            else if (FoundAttachments.Any() && SelectedAttachment != null)
            {
                CurrentWorkflowState = DocumentScrapingWorkflowState.ReadyToScan;
                SmartButtonText = "📄 Scan Document";
                SmartButtonEnabled = true;
                
                // Toggle button enabled - can switch between scan and scrape modes
                SmartToggleButtonEnabled = true;
                if (!IsInScanMode)
                {
                    SmartToggleButtonText = "📄 Scrape Selected Document for Requirements";
                }
                else
                {
                    SmartToggleButtonText = "🔍 Scan Jama for Attachments";
                }
            }
            else if (FoundAttachments.Any())
            {
                CurrentWorkflowState = DocumentScrapingWorkflowState.ReadyToScan;
                SmartButtonText = "📋 Select & Scan Document";
                SmartButtonEnabled = true;
                
                // Toggle button enabled - can switch to scan mode, but scrape requires selection
                SmartToggleButtonEnabled = true;
                SmartToggleButtonText = IsInScanMode ? "🔍 Scan Jama for Attachments" : "📄 Scrape Selected Document for Requirements";
            }
            else
            {
                CurrentWorkflowState = DocumentScrapingWorkflowState.ReadyToSearch;
                SmartButtonText = "🔍 Search for Attachments";
                SmartButtonEnabled = true;
                
                // Toggle button enabled - can scan for attachments
                SmartToggleButtonEnabled = true;
                SmartToggleButtonText = "🔍 Scan Jama for Attachments";
                IsInScanMode = false;
            }
        }

        /// <summary>
        /// Constructor with direct service injection for self-contained operation
        /// </summary>
        public DocumentScraperViewModel(
            IJamaConnectService jamaService,
            IWorkspaceContext workspaceContext,
            ILogger<DocumentScraperViewModel> logger)
        {
            _jamaService = jamaService ?? throw new ArgumentNullException(nameof(jamaService));
            _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            ExtractedRequirements = new ObservableCollection<Requirement>();
            InitializeCollections();
            InitializeTimer();

            // Subscribe to workspace changes for auto-detection
            _workspaceContext.WorkspaceChanged += OnWorkspaceChanged;
            
            // Initialize with current workspace
            OnWorkspaceChanged(null, EventArgs.Empty);

            _logger.LogInformation("[DocumentScraper] Self-contained component initialized");
            
            // Initialize workflow state
            UpdateWorkflowState();
        }

        /// <summary>
        /// Auto-detect Jama project when workspace changes
        /// </summary>
        private async void OnWorkspaceChanged(object? sender, EventArgs e)
        {
            try
            {
                var workspace = _workspaceContext.CurrentWorkspace;
                CurrentWorkspaceName = workspace?.Name ?? "No workspace";

                _logger.LogInformation("[DocumentScraper] Workspace changed: {WorkspaceName}, JamaProject: {JamaProject}, ImportSource: {ImportSource}", 
                    CurrentWorkspaceName, workspace?.JamaProject, workspace?.ImportSource);

                // DEBUG: Log all workspace properties to understand what we have
                _logger.LogInformation("[DocumentScraper] DEBUG Workspace Properties:");
                _logger.LogInformation("[DocumentScraper] - Name: {Name}", workspace?.Name);
                _logger.LogInformation("[DocumentScraper] - JamaProject: '{JamaProject}'", workspace?.JamaProject ?? "NULL");
                _logger.LogInformation("[DocumentScraper] - JamaTestPlan: '{JamaTestPlan}'", workspace?.JamaTestPlan ?? "NULL");
                _logger.LogInformation("[DocumentScraper] - ImportSource: '{ImportSource}'", workspace?.ImportSource ?? "NULL");
                _logger.LogInformation("[DocumentScraper] - SourceDocPath: '{SourceDocPath}'", workspace?.SourceDocPath ?? "NULL");

                // Resolve Jama project ID deterministically from workspace values.
                var jamaProjectId = await TryResolveJamaProjectIdAsync(workspace);
                if (jamaProjectId.HasValue)
                {
                    _resolvedJamaProjectId = jamaProjectId.Value;
                    HasJamaProject = true;
                    // Display project name from JamaTestPlan if available, otherwise use JamaProject
                    CurrentJamaProjectName = workspace?.JamaTestPlan ?? workspace?.JamaProject ?? $"Project {jamaProjectId.Value}";
                    StatusMessage = $"Ready to scan attachments for {CurrentJamaProjectName}";
                    
                    _logger.LogInformation("[DocumentScraper] Detected Jama project: {ProjectName} (ID: {ProjectId})", 
                        CurrentJamaProjectName, jamaProjectId.Value);
                    
                    // Auto-trigger scanning if we have a Jama project
                    _ = Task.Run(async () => await TriggerAttachmentScanAsync(jamaProjectId.Value));
                }
                else
                {
                    _resolvedJamaProjectId = null;
                    HasJamaProject = false;
                    CurrentJamaProjectName = "No Jama project";
                    
                    // Check if this is a Jama import but we couldn't parse the ID
                    if (workspace?.ImportSource == "Jama" && !string.IsNullOrEmpty(workspace?.JamaProject))
                    {
                        StatusMessage = $"Jama project '{workspace.JamaProject}' detected, but project ID not available for attachment scanning";
                        _logger.LogWarning("[DocumentScraper] Jama import detected but could not extract project ID from: {JamaProject}", workspace.JamaProject);
                    }
                    else
                    {
                        StatusMessage = "No Jama project associated with current workspace";
                        _logger.LogDebug("[DocumentScraper] No Jama association found - ImportSource: {ImportSource}, JamaProject: {JamaProject}", 
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
                _logger.LogError(ex, "[DocumentScraper] Error handling workspace change");
                StatusMessage = "Error detecting Jama project";
            }
        }

        /// <summary>
        /// Resolve Jama project ID from workspace.
        /// Uses explicit numeric IDs first, then exact name/key lookup from Jama projects.
        /// </summary>
        private async Task<int?> TryResolveJamaProjectIdAsync(Workspace? workspace)
        {
            if (workspace == null)
            {
                return null;
            }

            // Try direct numeric ID first
            if (!string.IsNullOrWhiteSpace(workspace.JamaProject) && int.TryParse(workspace.JamaProject, out var directId))
            {
                return directId;
            }

            // Try to get from JamaTestPlan if it's numeric
            if (!string.IsNullOrEmpty(workspace.JamaTestPlan) && int.TryParse(workspace.JamaTestPlan, out var testPlanId))
            {
                return testPlanId;
            }

            var lookupCandidates = new[] { workspace.JamaProject, workspace.JamaTestPlan }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (lookupCandidates.Count == 0)
            {
                return null;
            }

            try
            {
                var projects = await _jamaService.GetProjectsAsync();
                var resolved = projects.FirstOrDefault(p =>
                    lookupCandidates.Any(candidate =>
                        string.Equals(candidate, p.Name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate, p.Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate, p.Id.ToString(), StringComparison.OrdinalIgnoreCase)));

                if (resolved != null)
                {
                    _logger.LogInformation("[DocumentScraper] Resolved Jama project '{ProjectName}' (key '{ProjectKey}') to ID {ProjectId}",
                        resolved.Name, resolved.Key, resolved.Id);
                    return resolved.Id;
                }

                _logger.LogWarning("[DocumentScraper] Could not map workspace Jama project identifiers to a Jama project ID. Candidates: {Candidates}",
                    string.Join(", ", lookupCandidates));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DocumentScraper] Failed to resolve Jama project ID from project list");
            }

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
                    _logger.LogDebug("[DocumentScraper] Scan already in progress, ignoring trigger");
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

                _logger.LogInformation("[DocumentScraper] Starting attachment scan for project {ProjectId}", jamaProjectId);

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
                _logger.LogInformation("[DocumentScraper] Attachment scan cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DocumentScraper] Error during attachment scanning");
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
        /// Smart button command that executes the next logical action in the workflow
        /// </summary>
        [RelayCommand]
        private async Task ExecuteSmartActionAsync()
        {
            switch (CurrentWorkflowState)
            {
                case DocumentScrapingWorkflowState.ReadyToSearch:
                    await RefreshAttachmentsAsync();
                    break;
                    
                case DocumentScrapingWorkflowState.ReadyToScan:
                    if (SelectedAttachment == null && FoundAttachments.Any())
                    {
                        // Auto-select first attachment if none selected
                        SelectedAttachment = FoundAttachments.First();
                    }
                    
                    if (SelectedAttachment != null)
                    {
                        await ScanSelectedAttachmentAsync();
                    }
                    else
                    {
                        StatusMessage = "No attachment selected for scanning.";
                    }
                    break;
                    
                case DocumentScrapingWorkflowState.Scanning:
                    CancelScan();
                    break;
                    
                case DocumentScrapingWorkflowState.ReadyToImport:
                    await ImportSelectedRequirementsAsync();
                    break;
                    
                case DocumentScrapingWorkflowState.NoJamaProject:
                    StatusMessage = "Please configure a Jama project first.";
                    break;
                    
                default:
                    StatusMessage = "Unknown workflow state.";
                    break;
            }
        }

        /// <summary>
        /// Smart toggle command that alternates between scanning Jama and scraping selected document
        /// </summary>
        [RelayCommand]
        private async Task SmartToggleAsync()
        {
            if (!IsInScanMode)
            {
                // Currently in "Scan Jama" mode, execute Jama scan and switch to scrape mode
                await RefreshAttachmentsAsync();
                IsInScanMode = true; // Switch to scrape mode after scanning Jama
            }
            else
            {
                // Currently in "Scrape Document" mode, execute document scraping and switch to scan mode
                if (SelectedAttachment != null)
                {
                    await ScanSelectedAttachmentAsync();
                    IsInScanMode = false; // Switch back to scan mode after scraping
                }
                else
                {
                    StatusMessage = "Please select an attachment to scrape.";
                }
            }
            
            // Update the UI state after the action
            UpdateWorkflowState();
        }

        /// <summary>
        /// Scan selected attachment for requirements
        /// </summary>
        [RelayCommand]
        private async Task ScanSelectedAttachmentAsync()
        {
            if (SelectedAttachment == null)
            {
                StatusMessage = "No attachment selected for scanning.";
                return;
            }
            
            try
            {
                // Start elapsed time tracking
                StartElapsedTimeTracking();
                
                StatusMessage = $"Scanning {SelectedAttachment.Name} for requirements...";
                _logger.LogInformation("[DocumentScraper] Starting requirement scan for attachment {AttachmentId}: {AttachmentName}", 
                    SelectedAttachment.Id, SelectedAttachment.Name);
                
                // Clear previous extraction results
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ExtractedRequirements.Clear();
                    ParsingResults.Clear();
                });
                
                // For now, add placeholder extraction logic
                // This would be where you integrate with document parsing/AI analysis
                await Task.Delay(2000); // Simulate processing time
                
                // Add mock extracted requirements for demonstration
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ParsingResults.Add($"Processing {SelectedAttachment.Name}...");
                    ParsingResults.Add($"Document type: {SelectedAttachment.MimeType}");
                    ParsingResults.Add("Searching for requirement patterns...");
                    ParsingResults.Add($"Analysis complete. Ready for requirement extraction.");
                    
                    // Add sample extracted requirement (replace with actual extraction logic)
                    var sampleReq = new Requirement
                    {
                        Item = "EXT-001",
                        Name = $"Sample requirement from {SelectedAttachment.Name}",
                        Description = $"Sample requirement text extracted from {SelectedAttachment.Name}",
                        ItemType = "Functional",
                        Project = CurrentJamaProjectName
                    };
                    ExtractedRequirements.Add(sampleReq);
                });
                
                StatusMessage = $"Scan completed in {ElapsedTime}. Found {ExtractedRequirements.Count} requirements in {SelectedAttachment.Name}.";
                _logger.LogInformation("[DocumentScraper] Requirement scan completed for {AttachmentName}. Found {RequirementCount} requirements.", 
                    SelectedAttachment.Name, ExtractedRequirements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DocumentScraper] Error during requirement scanning for attachment {AttachmentId}", 
                    SelectedAttachment?.Id);
                StatusMessage = "Error occurred during requirement scanning.";
            }
            finally
            {
                // Stop elapsed time tracking
                StopElapsedTimeTracking();
                // Update workflow state after scan completion
                UpdateWorkflowState();
            }
        }

        /// <summary>
        /// Manual refresh command
        /// </summary>
        [RelayCommand]
        private async Task RefreshAttachmentsAsync()
        {
            var projectId = _resolvedJamaProjectId;
            if (!projectId.HasValue)
            {
                projectId = await TryResolveJamaProjectIdAsync(_workspaceContext.CurrentWorkspace);
                _resolvedJamaProjectId = projectId;
            }

            if (projectId.HasValue)
            {
                await TriggerAttachmentScanAsync(projectId.Value);
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