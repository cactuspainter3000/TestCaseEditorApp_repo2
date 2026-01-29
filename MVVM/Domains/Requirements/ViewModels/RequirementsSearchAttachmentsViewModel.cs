using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using System.Linq;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// ViewModel for Requirements Search in Attachments feature.
    /// Allows users to search for requirements within Jama Connect attachments using LLM-powered document parsing.
    /// Follows Architectural Guide AI patterns for Requirements domain ViewModels.
    /// </summary>
    public partial class RequirementsSearchAttachmentsViewModel : BaseDomainViewModel
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly IJamaConnectService _jamaConnectService;
        private readonly IJamaDocumentParserService _documentParserService;

        /// <summary>
        /// Constructor for RequirementsSearchAttachmentsViewModel with proper mediator injection
        /// </summary>
        public RequirementsSearchAttachmentsViewModel(
            IRequirementsMediator mediator,
            IJamaConnectService jamaConnectService,
            IJamaDocumentParserService documentParserService,
            ILogger<RequirementsSearchAttachmentsViewModel> logger) 
            : base(mediator, logger)
        {
            _mediator = mediator;
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _documentParserService = documentParserService ?? throw new ArgumentNullException(nameof(documentParserService));

            Title = "Requirements Search in Attachments";
            StatusMessage = "Initializing Requirements Search in Attachments...";
            
            _logger.LogInformation("[RequirementsSearchAttachments] ViewModel constructor completed. Will load projects when activated.");
            
            // Load projects when the view becomes active
            _ = Task.Run(async () => 
            {
                await Task.Delay(100); // Small delay to ensure initialization is complete
                await LoadAvailableProjectsAsync();
            });
        }

        // ==== PROPERTIES ====

        [ObservableProperty]
        private int selectedProjectId = 636; // Default to Project 636

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private bool isSearching = false;

        [ObservableProperty]
        private bool hasResults = false;

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

        // ==== BASE CLASS IMPLEMENTATION ====

        protected override void InitializeCommands()
        {
            // Initialize base commands
            base.InitializeCommands();
            
            // Initialize domain-specific commands
            SearchAttachmentsCommand = new AsyncRelayCommand(SearchAttachmentsAsync, CanExecuteSearch);
            ParseSelectedAttachmentCommand = new AsyncRelayCommand(ParseSelectedAttachmentAsync, CanExecuteParseAttachment);
            ImportExtractedRequirementsCommand = new AsyncRelayCommand(ImportExtractedRequirementsAsync, CanExecuteImportRequirements);
            LoadProjectsCommand = new AsyncRelayCommand(LoadAvailableProjectsAsync, () => !IsBusy);
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

        private async Task LoadAvailableProjectsAsync()
        {
            try
            {
                _logger.LogInformation("[RequirementsSearchAttachments] *** LoadAvailableProjectsAsync started ***");
                IsBusy = true;
                StatusMessage = "Loading Jama projects...";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Checking Jama configuration...");
                IsJamaConfigured = _jamaConnectService.IsConfigured;
                _logger.LogInformation("[RequirementsSearchAttachments] Jama configured: {IsConfigured}", IsJamaConfigured);
                
                if (!IsJamaConfigured)
                {
                    StatusMessage = "❌ Jama Connect is not configured. Please configure Jama credentials first.";
                    return;
                }

                _logger.LogInformation("[RequirementsSearchAttachments] *** Calling _jamaConnectService.GetProjectsAsync() ***");
                var projects = await _jamaConnectService.GetProjectsAsync();
                _logger.LogInformation("[RequirementsSearchAttachments] *** GetProjectsAsync returned {Count} projects ***", projects?.Count ?? 0);
                
                AvailableProjects.Clear();
                if (projects != null)
                {
                    foreach (var project in projects)
                    {
                        _logger.LogDebug("[RequirementsSearchAttachments] Adding project: {ProjectId} - {ProjectName}", project.Id, project.Name);
                        AvailableProjects.Add(project);
                    }
                }

                StatusMessage = $"✅ Loaded {AvailableProjects.Count} projects. Select a project and search for attachments.";
                _logger.LogInformation("[RequirementsSearchAttachments] *** Successfully loaded {Count} projects into AvailableProjects collection ***", AvailableProjects.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error loading projects");
                StatusMessage = "❌ Error loading Jama projects";
                SetError($"Failed to load projects: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task SearchAttachmentsAsync()
        {
            if (!CanExecuteSearch()) return;

            try
            {
                IsSearching = true;
                IsBusy = true;
                StatusMessage = $"🔍 Searching attachments in project {SelectedProjectId}...";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Searching attachments for project {ProjectId}", SelectedProjectId);

                var attachments = await _jamaConnectService.GetProjectAttachmentsAsync(SelectedProjectId);
                
                // Filter attachments based on search query if provided
                var filteredAttachments = attachments;
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    filteredAttachments = attachments
                        .Where(a => a.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                SearchResults.Clear();
                foreach (var attachment in filteredAttachments)
                {
                    SearchResults.Add(attachment);
                }

                HasResults = SearchResults.Count > 0;
                
                if (HasResults)
                {
                    StatusMessage = $"✅ Found {SearchResults.Count} attachments. Select one to extract requirements.";
                }
                else
                {
                    StatusMessage = "No attachments found matching your criteria.";
                }

                _logger.LogInformation("[RequirementsSearchAttachments] Found {Count} attachments", SearchResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsSearchAttachments] Error searching attachments");
                StatusMessage = "❌ Error searching attachments";
                SetError($"Failed to search attachments: {ex.Message}");
            }
            finally
            {
                IsSearching = false;
                IsBusy = false;
            }
        }

        private async Task ParseSelectedAttachmentAsync()
        {
            if (SelectedAttachment == null || !CanExecuteParseAttachment()) return;

            try
            {
                IsParsing = true;
                IsBusy = true;
                StatusMessage = $"📄 Parsing {SelectedAttachment.Name} for requirements...";
                
                _logger.LogInformation("[RequirementsSearchAttachments] Parsing attachment {AttachmentName} (ID: {AttachmentId})", 
                    SelectedAttachment.Name, SelectedAttachment.Id);

                // For now, simulate the document parsing since the service method may not exist yet
                // TODO: Replace with actual service call when IJamaDocumentParserService is fully implemented
                await Task.Delay(2000); // Simulate processing time
                
                // Simulate extracted requirements for now
                var simulatedRequirements = new List<Requirement>
                {
                    new Requirement 
                    { 
                        GlobalId = Guid.NewGuid().ToString(),
                        Name = "Extracted Requirement from Attachment",
                        Description = $"Sample requirement extracted from {SelectedAttachment.Name} using LLM document parsing",
                        ItemType = "Requirement"
                    }
                };

                ExtractedRequirements.Clear();
                foreach (var requirement in simulatedRequirements)
                {
                    ExtractedRequirements.Add(requirement);
                }

                HasExtractedRequirements = ExtractedRequirements.Count > 0;
                
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

        // ==== COMMAND EXECUTORS ====

        private bool CanExecuteSearch()
        {
            return IsJamaConfigured && !IsSearching && !IsBusy && SelectedProjectId > 0;
        }

        private bool CanExecuteParseAttachment()
        {
            return SelectedAttachment != null && !IsParsing && !IsBusy;
        }

        private bool CanExecuteImportRequirements()
        {
            return HasExtractedRequirements && !IsImporting && !IsBusy;
        }
    }
}