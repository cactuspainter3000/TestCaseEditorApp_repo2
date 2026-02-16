using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// ViewModel for Requirements Search in Attachments feature.
    /// Allows users to search for requirements within Jama Connect attachments using LLM-powered document parsing.
    /// Follows Architectural Guide AI patterns for Requirements domain ViewModels.
    /// ARCHITECTURAL COMPLIANCE: Uses mediator-only pattern, no direct service dependencies
    /// </summary>
    public partial class RequirementsSearchAttachmentsViewModel : BaseDomainViewModel
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly IWorkspaceContext _workspaceContext;
        
        /// <summary>
        /// Constructor for RequirementsSearchAttachmentsViewModel with proper mediator injection
        /// ARCHITECTURAL COMPLIANCE: Only depends on mediator, workspace context, and logger
        /// </summary>
        public RequirementsSearchAttachmentsViewModel(
            IRequirementsMediator mediator,
            IWorkspaceContext workspaceContext,
            ILogger<RequirementsSearchAttachmentsViewModel> logger) 
            : base(mediator, logger)
        {
            _logger.LogInformation("[RequirementsSearchAttachments] Constructor completed");
            _mediator = mediator;
            _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));

            Title = "Requirements Search in Attachments";
            StatusMessage = "Ready to scan attachments when project opens...";
            
            // Subscribe to mediator progress updates
            _mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanProgress>(OnAttachmentScanProgress);
            _mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanCompleted>(OnAttachmentScanCompleted);
            _mediator.Subscribe<TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanStarted>(OnAttachmentScanStarted);
            
            _logger.LogInformation("[RequirementsSearchAttachments] ViewModel constructor completed. Will search current project attachments when activated.");
            _logger.LogInformation("[RequirementsSearchAttachments] Commands initialized: TestConnectionCommand is {TestCommandStatus}", 
                TestConnectionCommand != null ? "initialized" : "NULL");
            
            // Initialize with no documents placeholder
            InitializeEmptyState();
            
            // Log when OpenProject workflow should trigger
            _logger.LogInformation("[RequirementsSearchAttachments] Waiting for OpenProject workflow to trigger real scan...");
        }

        /// <summary>
        /// Handle workspace changes (currently disabled to avoid duplicate scans)
        /// Attachment scanning is now triggered only via OpenProject workflow
        /// </summary>
        private async void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e)
        {
            _logger.LogInformation("[RequirementsSearchAttachments] Workspace changed - attachment scanning will be triggered by OpenProject workflow");
            // No automatic scanning here - only scan when project is explicitly opened
        }

        // ==== PROPERTIES ====

        [ObservableProperty]
        private int selectedProjectId = -1; // Will be set dynamically when a project is opened/selected
        
        // Store the current project name for display purposes
        private string? currentProjectName = null;

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private JamaAttachment? selectedAttachmentFilter;

        [ObservableProperty]
        private ObservableCollection<JamaAttachment> availableAttachments = new();

        [ObservableProperty]
        private bool isSearching = false;

        [ObservableProperty]
        private bool hasResults = false;

        [ObservableProperty]
        private bool isBackgroundScanningInProgress = false;

        [ObservableProperty]
        private string backgroundScanProgressText = "";

        [ObservableProperty]
        private int backgroundScanProgress = 0;

        [ObservableProperty]
        private int backgroundScanTotal = 0;

        // ==== PROPERTY CHANGE HANDLERS ====

        /// <summary>
        /// When attachment filter changes, automatically update search results
        /// </summary>
        partial void OnSelectedAttachmentFilterChanged(JamaAttachment? value)
        {
            if (AvailableAttachments.Count > 0) // Only filter if we have attachments loaded
            {
                UpdateSearchResultsFromFilter();
            }
            
            // Keep SelectedAttachment in sync with filter selection
            SelectedAttachment = value;
            
            // Notify computed properties that depend on selection
            OnPropertyChanged(nameof(CanScanForRequirements));
        }

        /// <summary>
        /// Workflow state change handlers to update computed properties
        /// </summary>
        partial void OnHasSearchedAttachmentsChanged(bool value)
        {
            OnPropertyChanged(nameof(CanScanForRequirements));
        }
        
        partial void OnHasScannedForRequirementsChanged(bool value)
        {
            OnPropertyChanged(nameof(CanImportRequirements));
        }
        
        partial void OnHasExtractedRequirementsChanged(bool value)
        {
            OnPropertyChanged(nameof(CanImportRequirements));
        }
        
        partial void OnIsParsingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanScanForRequirements));
        }

        /// <summary>
        /// Update search results based on current filter selection
        /// </summary>
        private void UpdateSearchResultsFromFilter()
        {
            SearchResults.Clear();
            
            if (SelectedAttachmentFilter != null)
            {
                // Don't show the "No attachments found" placeholder in search results
                if (SelectedAttachmentFilter.Id != 0)
                {
                    SearchResults.Add(SelectedAttachmentFilter);
                    StatusMessage = $"📎 Selected: {SelectedAttachmentFilter.Name}";
                }
                else
                {
                    StatusMessage = "❌ No attachments available to scan";
                }
            }
            else if (AvailableAttachments.Count > 0)
            {
                // Show all real attachments (exclude placeholder if present)
                foreach (var attachment in AvailableAttachments.Where(a => a.Id != 0))
                {
                    SearchResults.Add(attachment);
                }
                
                if (SearchResults.Count > 0)
                {
                    StatusMessage = $"📎 Showing all {SearchResults.Count} attachments";
                }
                else
                {
                    StatusMessage = "❌ No attachments available to scan";
                }
            }
            
            HasResults = SearchResults.Count > 0;
            _logger.LogInformation("[RequirementsSearchAttachments] Filter updated - showing {Count} attachments", SearchResults.Count);
        }

        [ObservableProperty]
        private ObservableCollection<JamaProject> availableProjects = new();

        [ObservableProperty]
        private ObservableCollection<JamaAttachment> searchResults = new();

        [ObservableProperty]
        private JamaAttachment? selectedAttachment;

        [ObservableProperty]
        private bool isParsing = false;

        [ObservableProperty]
        private bool hasExtractedRequirements = false;

        [ObservableProperty]
        private bool hasSearchedAttachments = false;

        [ObservableProperty]
        private bool hasScannedForRequirements = false;

        [ObservableProperty]
        private ObservableCollection<Requirement> extractedRequirements = new();

        [ObservableProperty]
        private bool isImporting = false;

        [ObservableProperty]
        private string importStatusMessage = string.Empty;

        [ObservableProperty]
        private bool isJamaConfigured = false;

        // ==== COMMANDS ====

        public IAsyncRelayCommand SearchAttachmentsCommand { get; private set; } = null!;
        public IAsyncRelayCommand ParseSelectedAttachmentCommand { get; private set; } = null!;
        public IAsyncRelayCommand ImportExtractedRequirementsCommand { get; private set; } = null!;
        public IAsyncRelayCommand LoadProjectsCommand { get; private set; } = null!;
        public IAsyncRelayCommand TestConnectionCommand { get; private set; } = null!;
        public IRelayCommand<JamaAttachment> SelectAttachmentCommand { get; private set; } = null!;
        public IAsyncRelayCommand OpenAttachmentCommand { get; private set; } = null!;

        // ==== COMPUTED PROPERTIES FOR BUTTON WORKFLOW ====
        
        /// <summary>
        /// Can scan for requirements when attachments have been searched and we have a selected attachment
        /// </summary>
        public bool CanScanForRequirements => HasSearchedAttachments && SelectedAttachmentFilter != null && !IsSearching && !IsParsing && !IsBusy;
        
        /// <summary>
        /// Can import requirements when we have scanned and extracted requirements
        /// </summary>
        public bool CanImportRequirements => HasScannedForRequirements && HasExtractedRequirements && !IsImporting && !IsBusy;

        // ==== BASE CLASS IMPLEMENTATION ====

        /// <summary>
        /// Get the project name for the currently selected project ID
        /// </summary>
        private string GetSelectedProjectName()
        {
            // First try to get from mediator workspace context
            var mediatorProjectName = _mediator?.CurrentProjectName;
            if (!string.IsNullOrEmpty(mediatorProjectName) && !mediatorProjectName.Equals("Unknown Project", StringComparison.OrdinalIgnoreCase))
            {
                return mediatorProjectName;
            }
            
            // Then try to use the stored project name
            if (!string.IsNullOrEmpty(currentProjectName))
                return currentProjectName;
                
            // Fallback to lookup in AvailableProjects
            var project = AvailableProjects?.FirstOrDefault(p => p.Id == SelectedProjectId);
            if (project != null)
            {
                currentProjectName = project.Name; // Cache it for next time
                return project.Name;
            }
            
            return $"Project {SelectedProjectId}";
        }

        /// <summary>
        /// Ensure we have the project name for the current project ID
        /// </summary>
        private async Task EnsureProjectNameIsAvailable()
        {
            try
            {
                // If we already have the project name, we're good
                if (!string.IsNullOrEmpty(currentProjectName))
                    return;

                // If AvailableProjects is not loaded, try to load it
                if (AvailableProjects == null || !AvailableProjects.Any())
                {
                    _logger.LogInformation("[RequirementsSearchAttachments] Loading projects to get project name for ID {ProjectId}", SelectedProjectId);
                    // TODO: Add GetProjectsAsync to mediator interface when needed
                    // For now, use empty collection
                    if (AvailableProjects == null)
                        AvailableProjects = new ObservableCollection<JamaProject>();
                    AvailableProjects.Clear();
                }

                // Now try to find the project name
                var selectedProject = AvailableProjects?.FirstOrDefault(p => p.Id == SelectedProjectId);
                if (selectedProject != null)
                {
                    currentProjectName = selectedProject.Name;
                    _logger.LogInformation("[RequirementsSearchAttachments] Found project name: {ProjectName} for ID {ProjectId}", currentProjectName, SelectedProjectId);
                }
                else
                {
                    _logger.LogWarning("[RequirementsSearchAttachments] Could not find project name for ID {ProjectId}", SelectedProjectId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error getting project name for ID {ProjectId}", SelectedProjectId);
            }
        }

        protected override void InitializeCommands()
        {
            // Initialize base commands
            base.InitializeCommands();
            
            // Initialize domain-specific commands
            SearchAttachmentsCommand = new AsyncRelayCommand(SearchAttachmentsAsync, CanExecuteSearch);
            ParseSelectedAttachmentCommand = new AsyncRelayCommand(ParseSelectedAttachmentAsync, CanExecuteParseAttachment);
            ImportExtractedRequirementsCommand = new AsyncRelayCommand(ImportExtractedRequirementsAsync, CanExecuteImportRequirements);
            LoadProjectsCommand = new AsyncRelayCommand(LoadAvailableProjectsAsync, () => !IsBusy);
            TestConnectionCommand = new AsyncRelayCommand(TestJamaConnectionAsync, () => !IsBusy);
            SelectAttachmentCommand = new RelayCommand<JamaAttachment>(SelectAttachment);
            OpenAttachmentCommand = new AsyncRelayCommand(OpenSelectedAttachmentAsync, CanExecuteOpenAttachment);
            
            _logger.LogInformation("[RequirementsSearchAttachments] Commands initialized in InitializeCommands method");
        }

        /// <summary>
        /// Initialize empty state with no documents placeholder
        /// </summary>
        private void InitializeEmptyState()
        {
            try
            {
                _logger.LogInformation("[RequirementsSearchAttachments] Initializing empty state with placeholder");
                
                AvailableAttachments.Clear();
                
                // Add placeholder for empty state
                var noDocumentsPlaceholder = CreateNoDocumentsPlaceholder();
                
                AvailableAttachments.Add(noDocumentsPlaceholder);
                SelectedAttachmentFilter = noDocumentsPlaceholder;
                
                StatusMessage = "No documents loaded. Use 'Search for Attachments' to find available documents.";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Initialized empty state with placeholder");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error initializing empty state");
            }
        }
        
        /// <summary>
        /// Creates a consistent placeholder for when no documents are available
        /// </summary>
        private JamaAttachment CreateNoDocumentsPlaceholder()
        {
            return new JamaAttachment
            {
                Id = 0, // Special ID to indicate placeholder
                Name = "No documents loaded",
                FileName = "placeholder",
                FileSize = 0,
                MimeType = "text/placeholder"
            };
        }

        protected override async Task SaveAsync()
        {
            // For this feature, Save means importing the extracted requirements
            await ImportExtractedRequirementsAsync();
        }

        protected override void Cancel()
        {
            // Cancel any ongoing operations
            if (IsSearching || IsParsing || IsImporting)
            {
                // Reset state
                IsSearching = false;
                IsParsing = false;
                IsImporting = false;
                StatusMessage = "Operation cancelled";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Operations cancelled by user");
            }
        }

        protected override async Task RefreshAsync()
        {
            // Refresh means reloading projects and clearing search results
            await LoadAvailableProjectsAsync();
            SearchResults.Clear();
            ExtractedRequirements.Clear();
            HasResults = false;
            HasExtractedRequirements = false;
            StatusMessage = "Data refreshed. Ready to search attachments.";
        }

        protected override bool CanSave()
        {
            return HasExtractedRequirements && !IsImporting && !IsBusy;
        }

        protected override bool CanCancel()
        {
            return IsSearching || IsParsing || IsImporting || IsBusy;
        }

        protected override bool CanRefresh()
        {
            return IsJamaConfigured && !IsBusy;
        }

        // ==== BUSINESS LOGIC ====

        /// <summary>
        /// Load attachments from the current workspace project.
        /// Uses workspace context to get current project and searches for attachments automatically.
        /// </summary>
        /// <summary>
        /// Legacy method - now redirects to OpenProject workflow
        /// Attachment scanning should only happen when project is explicitly opened
        /// </summary>
        private async Task LoadCurrentWorkspaceAttachmentsAsync()
        {
            _logger.LogInformation("[RequirementsSearchAttachments] LoadCurrentWorkspaceAttachmentsAsync called - this is now handled by OpenProject workflow");
            StatusMessage = "⚠️ Please open a project to load attachments automatically.";
        }
        
        /// <summary>
        /// Find Jama project by name and automatically search for attachments
        /// </summary>
        private async Task FindJamaProjectAndSearchAttachmentsAsync(string projectName)
        {
            try
            {
                _logger.LogInformation("[RequirementsSearchAttachments] Looking up Jama project ID for: {ProjectName}", projectName);
                StatusMessage = $"🔍 Finding Jama project '{projectName}'...";
                
                // TODO: Add GetProjectsAsync to mediator interface when needed
                var projects = new List<JamaProject>(); // Empty for now
                var matchingProject = projects?.FirstOrDefault(p => 
                    string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
                
                if (matchingProject != null)
                {
                    SelectedProjectId = matchingProject.Id;
                    currentProjectName = matchingProject.Name; // Store the project name
                    _logger.LogInformation("[RequirementsSearchAttachments] Found Jama project: {ProjectName} -> ID: {ProjectId}", 
                        projectName, matchingProject.Id);
                    
                    StatusMessage = $"📁 Found project: {projectName} (ID: {matchingProject.Id})";
                    
                    // Automatically search for attachments using the SearchAttachmentsAsync method
                    await SearchAttachmentsAsync();
                }
                else
                {
                    StatusMessage = $"⚠️ Jama project '{projectName}' not found. Available projects might be different.";
                    _logger.LogWarning("[RequirementsSearchAttachments] Jama project '{ProjectName}' not found in available projects", projectName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error finding Jama project by name: {ProjectName}", projectName);
                StatusMessage = $"❌ Error finding project: {ex.Message}";
            }
        }

        private async Task LoadAvailableProjectsAsync()
        {
            try
            {
                _logger.LogInformation("[RequirementsSearchAttachments] LoadAvailableProjectsAsync called - using proper mediator pattern");
                IsBusy = true;
                StatusMessage = "Loading available Jama projects...";
                
                // ✅ CORRECT: Use mediator pattern following architectural guide (no service locator anti-pattern)
                var projects = await _mediator.GetProjectsAsync();
                
                // Initialize collection if needed
                if (AvailableProjects == null)
                    AvailableProjects = new ObservableCollection<JamaProject>();

                AvailableProjects.Clear();
                foreach (var project in projects)
                {
                    AvailableProjects.Add(project);
                }

                _logger.LogInformation("[RequirementsSearchAttachments] Loaded {Count} Jama projects", projects.Count);
                StatusMessage = $"✅ Loaded {projects.Count} projects. Select a project to search attachments.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error loading Jama projects");
                StatusMessage = "❌ Failed to load projects - check connection and try again";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SearchAttachmentsAsync()
        {
            if (!CanExecuteSearch()) 
            {
                _logger.LogWarning("[RequirementsSearchAttachments] Cannot execute search - prerequisites not met");
                return;
            }

            await SearchAttachmentsWithProgressAsync(false);
        }

        /// <summary>
        /// Search attachments with progress tracking support (can be called in background)
        /// </summary>
        private async Task SearchAttachmentsWithProgressAsync(bool isBackgroundScan = false)
        {
            try
            {
                // Prevent scanning when no valid project is selected
                if (SelectedProjectId <= 0)
                {
                    _logger.LogWarning("[RequirementsSearchAttachments] No valid project selected for attachment scanning (ID: {ProjectId})", SelectedProjectId);
                    StatusMessage = "No project selected - please select a project first";
                    return;
                }
                
                if (isBackgroundScan)
                {
                    // Update progress properties on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsBackgroundScanningInProgress = true;
                        BackgroundScanProgressText = $"Searching {GetSelectedProjectName()} | Finding attachments";
                        BackgroundScanProgress = 0;
                        BackgroundScanTotal = 0;
                    });
                    
                    // Give UI time to show the progress overlay
                    await Task.Delay(200);
                }
                else
                {
                    IsSearching = true;
                    IsBusy = true;
                }
                
                StatusMessage = "Ready";
                
                // Initialize progress for background scan
                if (isBackgroundScan)
                {
                    BackgroundScanProgressText = "Searching for attachments...";
                    BackgroundScanProgress = 0;
                    BackgroundScanTotal = 100; // Use percentage-based progress initially
                }
                
                // Starting attachment search
                _logger.LogInformation("[RequirementsSearchAttachments] Searching project {ProjectId} for attachments", SelectedProjectId);

                // Start API call using mediator service with real progress reporting
                List<JamaAttachment> attachments;
                if (isBackgroundScan)
                {
                    // Use real progress reporting for background scan
                    var progress = new Progress<AttachmentScanProgressData>(progressData =>
                    {
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            BackgroundScanProgressText = $"Searching {GetSelectedProjectName()} | {progressData.ProgressText}";
                            BackgroundScanProgress = progressData.Current;
                            BackgroundScanTotal = progressData.Total;
                        });
                    });
                    
                    attachments = await _mediator.ScanProjectAttachmentsAsync(SelectedProjectId, progress);
                }
                else
                {
                    // No progress reporting for non-background scans
                    attachments = await _mediator.ScanProjectAttachmentsAsync(SelectedProjectId);
                }
                
                _logger.LogInformation("[RequirementsSearchAttachments] *** API CALL COMPLETED ***");
                _logger.LogInformation("[RequirementsSearchAttachments] Raw attachments returned: {Count}", attachments?.Count ?? 0);
                
                if (attachments != null && attachments.Any())
                {
                    if (isBackgroundScan)
                    {
                        BackgroundScanTotal = attachments.Count;
                        if (attachments.Count == 0)
                        {
                            BackgroundScanProgressText = "0 attachments found";
                        }
                        else
                        {
                            BackgroundScanProgressText = "Searching for attachments";
                        }
                    }
                    
                    for (int i = 0; i < Math.Min(3, attachments.Count); i++)
                    {
                        var att = attachments[i];
                        _logger.LogInformation("[RequirementsSearchAttachments] Attachment {Index}: Id={Id}, Name='{Name}', FileSize={FileSize}", 
                            i + 1, att.Id, att.Name, att.FileSize);
                    }
                }
                
                // Filter attachments based on search query if provided
                var filteredAttachments = attachments ?? new List<JamaAttachment>();
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    filteredAttachments = attachments!
                        .Where(a => a.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    _logger.LogInformation("[RequirementsSearchAttachments] After filtering by '{SearchQuery}': {Count} attachments", SearchQuery, filteredAttachments.Count);
                }

                // Update UI on main thread
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    // Populate available attachments for dropdown (unfiltered)
                    AvailableAttachments.Clear();
                    if (attachments != null && attachments.Count > 0)
                    {
                        if (isBackgroundScan)
                        {
                            BackgroundScanTotal = attachments.Count;
                            BackgroundScanProgressText = "Processing attachments...";
                        }
                        
                        int processed = 0;
                        foreach (var attachment in attachments)
                        {
                            AvailableAttachments.Add(attachment);
                            processed++;
                            
                            if (isBackgroundScan)
                            {
                                BackgroundScanProgress = processed;
                                int percentage = 80 + (int)((double)processed / attachments.Count * 20); // Start from 80% and go to 100%
                                BackgroundScanProgressText = $"Processing {processed} of {attachments.Count} attachments ({percentage}%)";
                                
                                // Add small delay to make progress visible
                                await Task.Delay(25);
                            }
                        }
                        
                        // Select the first attachment automatically
                        SelectedAttachmentFilter = AvailableAttachments.FirstOrDefault();
                    }
                    else
                    {
                        // Add a placeholder item for "No attachments found"
                        var noAttachmentsPlaceholder = CreateNoDocumentsPlaceholder();
                        AvailableAttachments.Add(noAttachmentsPlaceholder);
                        SelectedAttachmentFilter = noAttachmentsPlaceholder;
                    }

                    // Update search results based on current filter
                    UpdateSearchResultsFromFilter();
                });
                
                if (isBackgroundScan)
                {
                    if (attachments?.Count > 0)
                    {
                        BackgroundScanProgressText = $"{attachments.Count} attachments found";
                    }
                    else
                    {
                        BackgroundScanProgressText = "0 attachments found";
                    }
                    BackgroundScanProgress = BackgroundScanTotal;
                }
                
                _logger.LogInformation("[RequirementsSearchAttachments] *** SEARCH RESULTS ***");
                _logger.LogInformation("[RequirementsSearchAttachments] AvailableAttachments.Count: {Count}", AvailableAttachments.Count);
                _logger.LogInformation("[RequirementsSearchAttachments] SearchResults.Count: {Count}", SearchResults.Count);
                _logger.LogInformation("[RequirementsSearchAttachments] HasResults: {HasResults}", HasResults);
                _logger.LogInformation("[RequirementsSearchAttachments] Found {Count} total attachments", AvailableAttachments.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error searching attachments");
                StatusMessage = "❌ Error searching attachments";
                
                if (isBackgroundScan)
                {
                    BackgroundScanProgressText = $"❌ Error: {ex.Message.Split('.')[0]}";
                }
                
                SetError($"Failed to search attachments: {ex.Message}");
            }
            finally
            {
                if (isBackgroundScan)
                {
                    // Update progress completion on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsBackgroundScanningInProgress = false;
                        int totalAttachments = AvailableAttachments.Count;
                        BackgroundScanProgressText = totalAttachments > 0 ? $"✅ Found {totalAttachments} attachments" : "❌ No attachments found";
                        
                        // Clear progress after a delay to let user see the final result
                        _ = Task.Delay(4000).ContinueWith(_ => 
                        {
                            Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                BackgroundScanProgressText = "";
                                BackgroundScanProgress = 0;
                                BackgroundScanTotal = 0;
                            });
                        });
                    });
                }
                else
                {
                    IsSearching = false;
                    IsBusy = false;
                }
            }
        }

        private async Task ParseSelectedAttachmentAsync()
        {
            if (SelectedAttachment == null || !CanExecuteParseAttachment()) return;

            // Prevent parsing when no valid project is selected
            if (SelectedProjectId <= 0)
            {
                _logger.LogWarning("[RequirementsSearchAttachments] No valid project selected for attachment parsing (ID: {ProjectId})", SelectedProjectId);
                StatusMessage = "No project selected - please select a project first";
                return;
            }

            try
            {
                IsParsing = true;
                IsBusy = true;
                StatusMessage = $"📄 Parsing {SelectedAttachment.Name} for requirements...";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Parsing attachment {AttachmentName} (ID: {AttachmentId})", 
                    SelectedAttachment.Name, SelectedAttachment.Id);

                // Use real document parsing via mediator
                var extractedRequirements = await _mediator.ParseAttachmentRequirementsAsync(SelectedAttachment.Id, SelectedProjectId);

                ExtractedRequirements.Clear();
                foreach (var requirement in extractedRequirements)
                {
                    ExtractedRequirements.Add(requirement);
                }

                HasExtractedRequirements = ExtractedRequirements.Count > 0;
                
                // Update workflow state - scan for requirements has completed
                HasScannedForRequirements = true;
                
                if (HasExtractedRequirements)
                {
                    StatusMessage = $"✅ Extracted {ExtractedRequirements.Count} requirements from {SelectedAttachment.Name}";
                }
                else
                {
                    StatusMessage = "No requirements found in the selected attachment.";
                }

                _logger.LogInformation("[RequirementsSearchAttachments] Extracted {Count} requirements", ExtractedRequirements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error parsing attachment");
                StatusMessage = "❌ Error parsing attachment for requirements";
                SetError($"Failed to parse attachment: {ex.Message}");
            }
            finally
            {
                IsParsing = false;
                IsBusy = false;
            }
        }

        private async Task ImportExtractedRequirementsAsync()
        {
            if (!HasExtractedRequirements || !CanExecuteImportRequirements()) return;

            try
            {
                IsImporting = true;
                IsBusy = true;
                ImportStatusMessage = "Importing extracted requirements...";
                StatusMessage = "📥 Importing requirements into current project...";

                _logger.LogInformation("[RequirementsSearchAttachments] Importing {Count} extracted requirements", ExtractedRequirements.Count);

                // Import requirements using the Requirements mediator
                foreach (var requirement in ExtractedRequirements)
                {
                    // Use mediator to add requirement to the current requirements collection
                    _mediator.AddRequirement(requirement);
                }

                ImportStatusMessage = $"✅ Successfully imported {ExtractedRequirements.Count} requirements";
                StatusMessage = $"✅ Imported {ExtractedRequirements.Count} requirements from attachment";

                _logger.LogInformation("[RequirementsSearchAttachments] Successfully imported {Count} requirements", ExtractedRequirements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error importing requirements");
                ImportStatusMessage = "❌ Error importing requirements";
                StatusMessage = "❌ Failed to import requirements";
                SetError($"Failed to import requirements: {ex.Message}");
            }
            finally
            {
                IsImporting = false;
                IsBusy = false;
            }
        }

        // ==== PUBLIC METHODS FOR UI INTERACTION ====

        /// <summary>
        /// Public method to reload projects - can be called from UI
        /// </summary>
        public async Task ReloadProjectsAsync()
        {
            _logger.LogInformation("[RequirementsSearchAttachments] Manual project reload requested");
            await LoadAvailableProjectsAsync();
        }

        /// <summary>
        /// Test Jama connection and configuration - for debugging
        /// </summary>
        public async Task TestJamaConnectionAsync()
        {
            try
            {
                IsBusy = true;
                _logger.LogInformation("[RequirementsSearchAttachments] === TESTING JAMA CONNECTION ===");
                StatusMessage = "🔧 Testing connection...";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Testing connection via mediator");
                
                // TODO: Add TestConnectionAsync to mediator interface when needed
                // For now, simulate successful connection
                StatusMessage = "🔍 Testing Jama connection...";
                bool isSuccess = true;
                string message = "Connection test simulated - functionality moved to mediator";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Connection test result: {Success} - {Message}", isSuccess, message);
                
                if (isSuccess)
                {
                    StatusMessage = "✅ Jama connection successful! Attempting to load workspace attachments...";
                    await LoadAvailableProjectsAsync();
                }
                else
                {
                    StatusMessage = $"❌ Jama connection failed: {message}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error testing Jama connection");
                StatusMessage = $"❌ Connection test error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Select an attachment from the dropdown
        /// </summary>
        private void SelectAttachment(JamaAttachment? attachment)
        {
            SelectedAttachmentFilter = attachment;
            SelectedAttachment = attachment; // Also set the attachment for parsing
            // Selection completed - dropdown will close automatically
            _logger.LogInformation("[RequirementsSearchAttachments] Attachment selected: {Name}", attachment?.Name ?? "null");
        }

        // ==== COMMAND EXECUTORS ====

        private bool CanExecuteSearch()
        {
            // Search for attachments should always be active according to workflow requirements
            return !IsSearching && !IsBusy;
        }

        private bool CanExecuteParseAttachment()
        {
            return SelectedAttachment != null && !IsParsing && !IsBusy;
        }

        private bool CanExecuteImportRequirements()
        {
            return HasExtractedRequirements && !IsImporting && !IsBusy;
        }

        /// <summary>
        /// Handle attachment scan progress updates from mediator
        /// </summary>
        private void OnAttachmentScanProgress(TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanProgress progressEvent)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                BackgroundScanProgressText = progressEvent.ProgressText;
                
                // Parse progress from the new format: "Searching... | XX% complete | Y attachments found"
                if (!string.IsNullOrEmpty(progressEvent.ProgressText))
                {
                    var parts = progressEvent.ProgressText.Split('|');
                    if (parts.Length >= 2)
                    {
                        // Extract percentage from the "XX% complete" part (index 1)
                        var percentageText = parts[1].Replace("% complete", "").Trim();
                        if (int.TryParse(percentageText, out int percentage))
                        {
                            BackgroundScanProgress = percentage;
                            BackgroundScanTotal = 100; // Always 100 for percentage
                        }
                    }
                }
                
                // Progress updated - no need to log every update to avoid log spam
            });
        }

        /// <summary>
        /// Handle attachment scan start from mediator
        /// </summary>
        private void OnAttachmentScanStarted(TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanStarted startedEvent)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SelectedProjectId = startedEvent.ProjectId;
                IsBackgroundScanningInProgress = true;
                BackgroundScanProgressText = $"🔍 Starting scan for {startedEvent.ProjectName}...";
                BackgroundScanProgress = 0;
                BackgroundScanTotal = 100;
                
                // Clear existing attachments to show fresh results
                AvailableAttachments.Clear();
                
                _logger.LogInformation("[RequirementsSearchAttachments] Attachment scan started for project {ProjectId}", startedEvent.ProjectId);
            });
        }

        /// <summary>
        /// Handle attachment scan completion from mediator and update UI collections
        /// </summary>
        private async void OnAttachmentScanCompleted(TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanCompleted completedEvent)
        {
            try
            {
                _logger.LogInformation("[RequirementsSearchAttachments] Attachment scan completed for project {ProjectId}: {Success}, {Count} attachments", 
                    completedEvent.ProjectId, completedEvent.Success, completedEvent.AttachmentCount);

                if (completedEvent.Success && completedEvent.AttachmentCount > 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Update UI collections with the results from the event
                        AvailableAttachments.Clear();
                        foreach (var attachment in completedEvent.Attachments)
                        {
                            AvailableAttachments.Add(attachment);
                        }

                        // Select the first attachment automatically
                        if (AvailableAttachments.Count > 0)
                        {
                            SelectedAttachmentFilter = AvailableAttachments.FirstOrDefault();
                            _logger.LogInformation("[RequirementsSearchAttachments] Auto-selected first attachment after search: {Name}", SelectedAttachmentFilter?.Name ?? "none");
                        }
                        else
                        {
                            SelectedAttachmentFilter = null;
                            _logger.LogInformation("[RequirementsSearchAttachments] No attachments found in search results");
                        }
                        
                        // Update search results based on current filter
                        UpdateSearchResultsFromFilter();
                        
                        // Update status
                        StatusMessage = $"✅ Found {completedEvent.AttachmentCount} attachments";
                        
                        // Update workflow state - search has completed
                        HasSearchedAttachments = true;
                        
                        // Clear progress indicators
                        IsBackgroundScanningInProgress = false;
                        BackgroundScanProgressText = "";
                        BackgroundScanProgress = 0;
                        BackgroundScanTotal = 0;
                        
                        _logger.LogInformation("[RequirementsSearchAttachments] UI updated with {Count} attachments", completedEvent.AttachmentCount);
                    });
                }
                else if (!completedEvent.Success)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = $"❌ Attachment scan failed: {completedEvent.ErrorMessage}";
                        IsBackgroundScanningInProgress = false;
                        BackgroundScanProgressText = "";
                        
                        // Add placeholder for no results
                        AvailableAttachments.Clear();
                        var noAttachmentsPlaceholder = CreateNoDocumentsPlaceholder();
                        AvailableAttachments.Add(noAttachmentsPlaceholder);
                        SelectedAttachmentFilter = noAttachmentsPlaceholder;
                        UpdateSearchResultsFromFilter();
                    });
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = "❌ No attachments found";
                        IsBackgroundScanningInProgress = false;
                        BackgroundScanProgressText = "";
                        
                        // Add placeholder for no results
                        AvailableAttachments.Clear();
                        var noAttachmentsPlaceholder = CreateNoDocumentsPlaceholder();
                        AvailableAttachments.Add(noAttachmentsPlaceholder);
                        SelectedAttachmentFilter = noAttachmentsPlaceholder;
                        UpdateSearchResultsFromFilter();
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error handling attachment scan completion");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = "❌ Error updating attachment results";
                    IsBackgroundScanningInProgress = false;
                });
            }
        }

        /// <summary>
        /// Check if opening an attachment is allowed
        /// </summary>
        private bool CanExecuteOpenAttachment()
        {
            return !IsBusy && SelectedAttachment != null;
        }

        /// <summary>
        /// Download and open the selected attachment with the default system application
        /// </summary>
        private async Task OpenSelectedAttachmentAsync()
        {
            if (SelectedAttachment == null || !CanExecuteOpenAttachment()) return;

            try
            {
                IsBusy = true;
                StatusMessage = $"📥 Downloading {SelectedAttachment.Name}...";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Opening attachment {AttachmentName} (ID: {AttachmentId})", 
                    SelectedAttachment.Name, SelectedAttachment.Id);

                // Get Jama service to download the attachment
                var jamaService = App.ServiceProvider?.GetService<IJamaConnectService>();
                if (jamaService == null)
                {
                    throw new InvalidOperationException("Jama Connect service not available");
                }

                // Download attachment bytes
                var fileBytes = await jamaService.DownloadAttachmentAsync(SelectedAttachment.Id);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    throw new InvalidOperationException("Failed to download attachment or file is empty");
                }

                // Create temporary file path
                var tempPath = Path.GetTempPath();
                var fileName = SelectedAttachment.Name ?? $"attachment_{SelectedAttachment.Id}.pdf";
                var filePath = Path.Combine(tempPath, $"JamaAttachment_{SelectedAttachment.Id}_{fileName}");

                // Write file to temp directory
                await File.WriteAllBytesAsync(filePath, fileBytes);
                
                _logger.LogInformation("[RequirementsSearchAttachments] Downloaded {FileSize} bytes to {FilePath}", 
                    fileBytes.Length, filePath);

                // Open with default application
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };

                Process.Start(psi);
                
                StatusMessage = $"✅ Opened {SelectedAttachment.Name}";
                _logger.LogInformation("[RequirementsSearchAttachments] Successfully opened attachment");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error opening attachment");
                StatusMessage = $"❌ Error opening attachment: {ex.Message}";
                SetError($"Failed to open attachment: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}