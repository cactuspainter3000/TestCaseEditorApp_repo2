using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using RequirementsMediator = TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.RequirementsMediator;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Header ViewModel for Requirements domain.
    /// Provides contextual header information and quick stats.
    /// Following architectural guide patterns for header ViewModels.
    /// </summary>
    public partial class Requirements_HeaderViewModel : BaseDomainViewModel
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly IWorkspaceContext _workspaceContext;

        [ObservableProperty]
        private int totalRequirements;

        [ObservableProperty]
        private int analyzedRequirements;

        [ObservableProperty]
        private int pendingRequirements;

        [ObservableProperty]
        private string importSource = "None";

        [ObservableProperty]
        private bool hasRequirements;

        [ObservableProperty]
        private string analysisProgress = "0%";

        [ObservableProperty]
        private string requirementDescription = "Search for document attachments and scan them for requirements";

        // Workspace management properties
        [ObservableProperty]
        private string? workspaceFilePath;

        [ObservableProperty]
        private System.DateTime? lastSaveTimestamp;

        [ObservableProperty]
        private bool isDirty;

        [ObservableProperty]
        private bool canUndoLastSave;

        // Document parsing status properties
        [ObservableProperty]
        private bool isDocumentParsing;

        [ObservableProperty]
        private string documentParsingStatus = string.Empty;

        [ObservableProperty]
        private string parsingDocumentName = string.Empty;

        [ObservableProperty]
        private string parsingTimer = "0:00";

        [ObservableProperty]
        private int requirementsFound;

        // Attachment scanning status properties
        [ObservableProperty]
        private bool isAttachmentScanning;

        [ObservableProperty]
        private string attachmentScanningStatus = string.Empty;

        [ObservableProperty]
        private string scanningProjectName = string.Empty;

        [ObservableProperty]
        private int attachmentsFound;

        // Properties to maintain operation status visibility after completion
        [ObservableProperty]
        private bool showCompletionStatus;

        [ObservableProperty]
        private bool hasRecentResults;

        // Persistent attachment counter that remains visible after operations complete
        [ObservableProperty]
        private int persistentAttachmentCount;

        // Computed properties for unified status display
        public bool IsAnyOperationActive => IsDocumentParsing || IsAttachmentScanning || ShowCompletionStatus;
        
        public string CurrentOperationStatus => IsDocumentParsing ? DocumentParsingStatus : AttachmentScanningStatus;
        
        public int CurrentOperationCount => IsDocumentParsing ? RequirementsFound : AttachmentsFound;
        
        public bool ShouldShowCounter => (IsDocumentParsing && RequirementsFound > 0) || 
                                       (IsAttachmentScanning && AttachmentsFound > 0) || 
                                       (ShowCompletionStatus && HasRecentResults && (RequirementsFound > 0 || AttachmentsFound > 0));

        private DateTime _parsingStartTime;
        private System.Windows.Threading.DispatcherTimer? _parsingTimerDispatcher;

        public ICommand SaveWorkspaceCommand { get; }
        public ICommand UndoLastSaveCommand { get; }

        public Requirements_HeaderViewModel(
            IRequirementsMediator mediator,
            IWorkspaceContext workspaceContext,
            ILogger<Requirements_HeaderViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));
            _workspaceContext = workspaceContext ?? throw new System.ArgumentNullException(nameof(workspaceContext));

            // Initialize workspace commands
            SaveWorkspaceCommand = new RelayCommand(SaveWorkspace, () => IsDirty);
            UndoLastSaveCommand = new RelayCommand(UndoLastSave, () => CanUndoLastSave);

            // Initialize parsing timer
            InitializeParsingTimer();

            // Subscribe to Requirements events for real-time updates
            if (_mediator is RequirementsMediator concreteMediator)
            {
                _logger.LogInformation("[HeaderVM] === CONSTRUCTOR: Mediator cast to RequirementsMediator SUCCEEDED, subscribing to events");
                concreteMediator.Subscribe<RequirementsEvents.RequirementsImported>(OnRequirementsImported);
                concreteMediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
                concreteMediator.Subscribe<RequirementsEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
                concreteMediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
                concreteMediator.Subscribe<RequirementsEvents.RequirementUpdated>(OnRequirementUpdated);
                concreteMediator.Subscribe<RequirementsEvents.DocumentParsingStarted>(OnDocumentParsingStarted);
                concreteMediator.Subscribe<RequirementsEvents.DocumentParsingProgress>(OnDocumentParsingProgress);
                concreteMediator.Subscribe<RequirementsEvents.DocumentParsingCompleted>(OnDocumentParsingCompleted);
                concreteMediator.Subscribe<RequirementsEvents.AttachmentScanStarted>(OnAttachmentScanStarted);
                concreteMediator.Subscribe<RequirementsEvents.AttachmentScanProgress>(OnAttachmentScanProgress);
                concreteMediator.Subscribe<RequirementsEvents.AttachmentScanCompleted>(OnAttachmentScanCompleted);
                _logger.LogInformation("[HeaderVM] === CONSTRUCTOR: All subscriptions complete");
            }
            else
            {
                _logger.LogWarning("[HeaderVM] === CONSTRUCTOR: Mediator is NOT a RequirementsMediator instance! Type: {Type}, Mediator is null: {IsNull}", 
                    _mediator?.GetType().Name ?? "null", _mediator == null);
            }

            // Initialize values
            UpdateStatistics();

            _logger.LogDebug("Requirements_HeaderViewModel initialized");
        }

        private void OnRequirementsImported(RequirementsEvents.RequirementsImported e)
        {
            ImportSource = System.IO.Path.GetFileName(e.SourceFile) ?? "Unknown";
            UpdateStatistics();
        }

        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            // If requirements are being cleared (e.g., project close), reset all header state
            if (e.Action == "Clear" && e.NewCount == 0)
            {
                _logger.LogDebug("Requirements cleared - resetting header state");
                
                // Reset all project-specific properties
                ImportSource = "None";
                WorkspaceFilePath = null;
                LastSaveTimestamp = null;
                RequirementDescription = ""; // Empty instead of default text
                PersistentAttachmentCount = 0; // Clear attachment counter on project reset
                
                _logger.LogDebug("Header state reset: ImportSource={ImportSource}, WorkspaceFilePath={WorkspaceFilePath}", 
                    ImportSource, WorkspaceFilePath ?? "null");
            }
            
            UpdateStatistics();
        }

        private void OnRequirementAnalyzed(RequirementsEvents.RequirementAnalyzed e)
        {
            UpdateStatistics();
        }

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            _logger.LogInformation("[HeaderVM] === OnRequirementSelected called === GlobalId: {GlobalId}, Item: {Item}, SelectedBy: {SelectedBy}", 
                e?.Requirement?.GlobalId ?? "null", e?.Requirement?.Item ?? "null", e?.SelectedBy ?? "null");
            
            // Update header description based on selected requirement and ImportSource
            if (e.Requirement != null)
            {
                var workspace = _workspaceContext.CurrentWorkspace;
                var isJamaImport = !string.IsNullOrEmpty(workspace?.ImportSource) && 
                                 string.Equals(workspace.ImportSource, "Jama", System.StringComparison.OrdinalIgnoreCase);
                
                if (isJamaImport)
                {
                    // Jama Import: Show requirement name only (until better use is determined)
                    RequirementDescription = e.Requirement.Name ?? "Unnamed requirement";
                    _logger.LogInformation("[HeaderVM] === Jama import, set RequirementDescription to Name: {Name}", RequirementDescription);
                }
                else
                {
                    // Document Import: Show filtered requirement details (remove supplemental/table data)
                    var idPart = !string.IsNullOrEmpty(e.Requirement.Item) ? e.Requirement.Item : e.Requirement.GlobalId ?? "Unknown";
                    var namePart = e.Requirement.Name ?? "Unnamed requirement";
                    
                    // Filter out supplemental and table data - show clean description
                    var description = FilterDocumentRequirementDetails(e.Requirement);
                    RequirementDescription = !string.IsNullOrEmpty(description) ? 
                        $"{idPart}: {description}" : 
                        $"{idPart}: {namePart}";
                    _logger.LogInformation("[HeaderVM] === Document import, set RequirementDescription to: {Desc}", RequirementDescription);
                }
            }
            else
            {
                RequirementDescription = "Search for document attachments and scrape them for requirements";
                _logger.LogInformation("[HeaderVM] === No requirement, reset to default: {Desc}", RequirementDescription);
            }
        }

        private void OnRequirementUpdated(RequirementsEvents.RequirementUpdated e)
        {
            _logger.LogInformation("[HeaderVM] === OnRequirementUpdated RECEIVED === Event GlobalId: {EventGlobalId}, Current GlobalId: {CurrentGlobalId}, Mediator null: {MediatorNull}", 
                e?.Requirement?.GlobalId ?? "null", _mediator?.CurrentRequirement?.GlobalId ?? "null", _mediator == null);
            
            // If the updated requirement is currently selected, refresh the header description
            if (e.Requirement != null && _mediator != null && _mediator.CurrentRequirement != null && 
                e.Requirement.GlobalId == _mediator.CurrentRequirement.GlobalId)
            {
                _logger.LogInformation("[HeaderVM] === GlobalIds MATCH - Will call OnRequirementSelected to refresh ===");
                // Re-trigger requirement selected logic to update description with new data
                OnRequirementSelected(new RequirementsEvents.RequirementSelected 
                { 
                    Requirement = e.Requirement,
                    SelectedBy = "System",
                    SelectedAt = System.DateTime.Now
                });
                _logger.LogInformation("[HeaderVM] === After OnRequirementSelected, RequirementDescription is now: {Desc}", RequirementDescription);
            }
            else
            {
                _logger.LogInformation("[HeaderVM] === GlobalIds DO NOT MATCH or mediator null === Requirement null: {ReqNull}, Mediator null: {MedNull}, CurrentReq null: {CurNull}, IDs: {EventId} vs {CurrentId}", 
                    e?.Requirement == null, _mediator == null, _mediator?.CurrentRequirement == null, e?.Requirement?.GlobalId ?? "null", _mediator?.CurrentRequirement?.GlobalId ?? "null");
            }
        }

        private void UpdateStatistics()
        {
            TotalRequirements = _mediator.Requirements.Count;
            AnalyzedRequirements = _mediator.Requirements.Count(r => r.Analysis != null);
            PendingRequirements = TotalRequirements - AnalyzedRequirements;
            HasRequirements = TotalRequirements > 0;

            // Calculate analysis progress percentage
            if (TotalRequirements > 0)
            {
                var percentage = (double)AnalyzedRequirements / TotalRequirements * 100;
                AnalysisProgress = $"{percentage:F0}%";
            }
            else
            {
                AnalysisProgress = "0%";
            }

            _logger.LogDebug("Requirements statistics updated: {Total} total, {Analyzed} analyzed",
                TotalRequirements, AnalyzedRequirements);
        }

        // Computed properties for display
        public string RequirementsStatus =>
            TotalRequirements switch
            {
                0 => "No requirements loaded",
                1 => "1 requirement loaded",
                _ => $"{TotalRequirements} requirements loaded"
            };

        public string AnalysisStatus =>
            AnalyzedRequirements switch
            {
                0 when TotalRequirements > 0 => "Analysis not started",
                var count when count == TotalRequirements && TotalRequirements > 0 => "Analysis complete",
                var count when count > 0 => $"{count} of {TotalRequirements} analyzed",
                _ => ""
            };

        public List<(string Label, string Value)> GetQuickStats()
        {
            return new List<(string, string)>
            {
                ("Total", TotalRequirements.ToString()),
                ("Analyzed", AnalyzedRequirements.ToString()),
                ("Pending", PendingRequirements.ToString()),
                ("Progress", AnalysisProgress),
                ("Source", ImportSource)
            };
        }

        // Notify when computed properties change
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(TotalRequirements) || 
                e.PropertyName == nameof(AnalyzedRequirements) || 
                e.PropertyName == nameof(PendingRequirements))
            {
                OnPropertyChanged(nameof(RequirementsStatus));
                OnPropertyChanged(nameof(AnalysisStatus));
            }

            // Notify computed properties when their dependencies change
            if (e.PropertyName == nameof(IsDocumentParsing) || 
                e.PropertyName == nameof(IsAttachmentScanning) || 
                e.PropertyName == nameof(ShowCompletionStatus))
            {
                OnPropertyChanged(nameof(IsAnyOperationActive));
            }

            if (e.PropertyName == nameof(DocumentParsingStatus) || 
                e.PropertyName == nameof(AttachmentScanningStatus))
            {
                OnPropertyChanged(nameof(CurrentOperationStatus));
            }

            if (e.PropertyName == nameof(RequirementsFound) || 
                e.PropertyName == nameof(AttachmentsFound))
            {
                OnPropertyChanged(nameof(CurrentOperationCount));
            }

            if (e.PropertyName == nameof(IsDocumentParsing) || 
                e.PropertyName == nameof(IsAttachmentScanning) ||
                e.PropertyName == nameof(ShowCompletionStatus) ||
                e.PropertyName == nameof(HasRecentResults) ||
                e.PropertyName == nameof(RequirementsFound) || 
                e.PropertyName == nameof(AttachmentsFound))
            {
                OnPropertyChanged(nameof(ShouldShowCounter));
            }
        }

        /// <summary>
        /// Filter requirement details for document imports - remove supplemental and table data
        /// </summary>
        private string FilterDocumentRequirementDetails(TestCaseEditorApp.MVVM.Models.Requirement requirement)
        {
            // For document imports, show clean description by filtering out supplemental/table data
            var description = requirement.Description;
            
            if (string.IsNullOrEmpty(description))
            {
                return requirement.Name ?? "No description available";
            }
            
            // Filter logic: Remove common supplemental content patterns
            var filtered = description;
            
            // Remove table-like content (lines with multiple pipe characters)
            var lines = filtered.Split('\n', '\r').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var cleanLines = lines.Where(line => 
                !line.Contains("|") || // Remove table rows
                line.Split('|').Length < 3 // Keep lines that aren't table-formatted
            ).ToArray();
            
            // Remove supplemental markers and metadata
            cleanLines = cleanLines.Where(line =>
                !line.TrimStart().StartsWith("Supplemental:", System.StringComparison.OrdinalIgnoreCase) &&
                !line.TrimStart().StartsWith("Table:", System.StringComparison.OrdinalIgnoreCase) &&
                !line.TrimStart().StartsWith("Figure:", System.StringComparison.OrdinalIgnoreCase) &&
                !line.TrimStart().StartsWith("Note:", System.StringComparison.OrdinalIgnoreCase)
            ).ToArray();
            
            // Take first meaningful sentence/paragraph for header display
            var cleanDescription = string.Join(" ", cleanLines).Trim();
            
            // Limit length for header display
            if (cleanDescription.Length > 150)
            {
                cleanDescription = cleanDescription.Substring(0, 147) + "...";
            }
            
            return string.IsNullOrEmpty(cleanDescription) ? requirement.Name ?? "No description available" : cleanDescription;
        }

        // Workspace management methods
        private void SaveWorkspace()
        {
            // TODO: Implement save workspace functionality
            LastSaveTimestamp = System.DateTime.Now;
            IsDirty = false;
            CanUndoLastSave = true;
        }

        private void UndoLastSave()
        {
            // TODO: Implement undo last save functionality
            CanUndoLastSave = false;
        }

        // Abstract method implementations from BaseDomainViewModel
        protected override async Task SaveAsync()
        {
            SaveWorkspace();
            await Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // Reset any unsaved changes
            IsDirty = false;
        }

        protected override async Task RefreshAsync()
        {
            // Refresh requirements data and statistics when activated
            UpdateStatistics();
            await Task.CompletedTask;
        }

        protected override bool CanSave() => IsDirty && !IsBusy;
        protected override bool CanCancel() => IsDirty;
        protected override bool CanRefresh() => !IsBusy;

        // Document parsing timer and event handling
        private void InitializeParsingTimer()
        {
            _parsingTimerDispatcher = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _parsingTimerDispatcher.Tick += OnParsingTimerTick;
        }

        private void OnParsingTimerTick(object? sender, EventArgs e)
        {
            if (IsDocumentParsing || IsAttachmentScanning)
            {
                var elapsed = DateTime.Now - _parsingStartTime;
                ParsingTimer = $"{elapsed.Minutes}:{elapsed.Seconds:D2}";
            }
        }

        private void OnDocumentParsingStarted(RequirementsEvents.DocumentParsingStarted e)
        {
            _logger.LogInformation("[HeaderVM] Document parsing started: {DocumentName}", e.DocumentName);
            IsDocumentParsing = true;
            ParsingDocumentName = e.DocumentName;
            DocumentParsingStatus = $"Parsing {e.DocumentName}...";
            _parsingStartTime = e.StartTime;
            ParsingTimer = "0:00";
            RequirementsFound = 0;
            
            // Reset completion status when starting new operation
            ShowCompletionStatus = false;
            HasRecentResults = false;
            
            _parsingTimerDispatcher?.Start();
            
            // Notify computed properties
            OnPropertyChanged(nameof(IsAnyOperationActive));
            OnPropertyChanged(nameof(CurrentOperationStatus));
            OnPropertyChanged(nameof(CurrentOperationCount));
            OnPropertyChanged(nameof(ShouldShowCounter));
        }

        private void OnDocumentParsingProgress(RequirementsEvents.DocumentParsingProgress e)
        {
            _logger.LogDebug("[HeaderVM] Document parsing progress: {StatusMessage}", e.StatusMessage);
            DocumentParsingStatus = e.StatusMessage;
            OnPropertyChanged(nameof(CurrentOperationStatus));
        }

        private void OnDocumentParsingCompleted(RequirementsEvents.DocumentParsingCompleted e)
        {
            _logger.LogInformation("[HeaderVM] Document parsing completed: {DocumentName} - Found {RequirementsCount} requirements", 
                e.DocumentName, e.RequirementsFound);
            
            _parsingTimerDispatcher?.Stop();
            IsDocumentParsing = false;
            
            if (e.Success)
            {
                RequirementsFound = e.RequirementsFound;
                DocumentParsingStatus = $"Found {e.RequirementsFound} requirements in {e.DocumentName}";
                // Update statistics since new requirements were added
                UpdateStatistics();
                
                // Show completion status to maintain visibility of results
                if (e.RequirementsFound > 0)
                {
                    ShowCompletionStatus = true;
                    HasRecentResults = true;
                }
            }
            else
            {
                DocumentParsingStatus = $"Failed to parse {e.DocumentName}: {e.ErrorMessage}";
            }

            // Notify computed properties
            OnPropertyChanged(nameof(IsAnyOperationActive));
            OnPropertyChanged(nameof(CurrentOperationStatus));
            OnPropertyChanged(nameof(CurrentOperationCount));
            OnPropertyChanged(nameof(ShouldShowCounter));

            // Clear status after showing results for 8 seconds
            Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    ShowCompletionStatus = false;
                    HasRecentResults = false;
                    DocumentParsingStatus = string.Empty;
                    ParsingDocumentName = string.Empty;
                    ParsingTimer = "0:00";
                    OnPropertyChanged(nameof(IsAnyOperationActive));
                    OnPropertyChanged(nameof(ShouldShowCounter));
                });
            });
        }

        private void OnAttachmentScanStarted(RequirementsEvents.AttachmentScanStarted e)
        {
            _logger.LogInformation("[HeaderVM] Attachment scan started for project: {ProjectName}", e.ProjectName);
            IsAttachmentScanning = true;
            ScanningProjectName = e.ProjectName;
            AttachmentScanningStatus = $"Scanning {e.ProjectName} for attachments...";
            _parsingStartTime = e.StartTime;
            ParsingTimer = "0:00";
            AttachmentsFound = 0;
            
            // Reset completion status when starting new operation
            ShowCompletionStatus = false;
            HasRecentResults = false;
            // Clear persistent counter when starting new scan
            PersistentAttachmentCount = 0;
            
            _parsingTimerDispatcher?.Start();
            
            // Notify computed properties
            OnPropertyChanged(nameof(IsAnyOperationActive));
            OnPropertyChanged(nameof(CurrentOperationStatus));
            OnPropertyChanged(nameof(CurrentOperationCount));
            OnPropertyChanged(nameof(ShouldShowCounter));
        }

        private void OnAttachmentScanProgress(RequirementsEvents.AttachmentScanProgress e)
        {
            _logger.LogDebug("[HeaderVM] Attachment scan progress: {ProgressText}", e.ProgressText);
            AttachmentScanningStatus = e.ProgressText;
            OnPropertyChanged(nameof(CurrentOperationStatus));
        }

        private void OnAttachmentScanCompleted(RequirementsEvents.AttachmentScanCompleted e)
        {
            _logger.LogInformation("[HeaderVM] Attachment scan completed for project {ProjectId} - Found {AttachmentCount} attachments", 
                e.ProjectId, e.AttachmentCount);
            
            _parsingTimerDispatcher?.Stop();
            IsAttachmentScanning = false;
            
            if (e.Success)
            {
                AttachmentsFound = e.AttachmentCount;
                // Update persistent counter that stays visible after status message clears
                PersistentAttachmentCount = e.AttachmentCount;
                AttachmentScanningStatus = $"Found {e.AttachmentCount} attachments in {ScanningProjectName}";
                
                // Show completion status to maintain visibility of results
                if (e.AttachmentCount > 0)
                {
                    ShowCompletionStatus = true;
                    HasRecentResults = true;
                }
            }
            else
            {
                AttachmentScanningStatus = $"Failed to scan attachments: {e.ErrorMessage}";
            }

            // Notify computed properties
            OnPropertyChanged(nameof(IsAnyOperationActive));
            OnPropertyChanged(nameof(CurrentOperationStatus));
            OnPropertyChanged(nameof(CurrentOperationCount));
            OnPropertyChanged(nameof(ShouldShowCounter));

            // Clear status after showing results for 8 seconds (but keep PersistentAttachmentCount)
            Task.Delay(TimeSpan.FromSeconds(8)).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    ShowCompletionStatus = false;
                    HasRecentResults = false;
                    AttachmentScanningStatus = string.Empty;
                    ScanningProjectName = string.Empty;
                    ParsingTimer = "0:00";
                    AttachmentsFound = 0; // Reset temp count after display period (but keep PersistentAttachmentCount)
                    OnPropertyChanged(nameof(IsAnyOperationActive));
                    OnPropertyChanged(nameof(ShouldShowCounter));
                });
            });
        }
    }
}