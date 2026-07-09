using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Enums;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Utils;
using EditableDataControl.ViewModels;
using RequirementsEvents = TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Unified ViewModel for Requirements display - source-agnostic architecture.
    /// Combines the best features from both Jama and General requirements paths
    /// with adaptive content rendering based on data availability.
    /// Follows clean 4-tab architecture: Details, Tables, Analysis, RequirementsScraper.
    /// </summary>
    public partial class UnifiedRequirementsMainViewModel : BaseDomainViewModel, IDisposable
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly IPersistenceService _persistence;
        private readonly ITextEditingDialogService _textEditingDialogService;
        private readonly IWorkspaceDiagnosticsService _workspaceDiagnosticsService;
        private UserSettingsViewModel? _userSettingsVm;
        private bool _batchSelectionInitialized;

        private const string BatchSelectionPersistencePrefix = "requirements.batch-selection";

        #region Navigation & State Properties

        [ObservableProperty]
        private RequirementViewMode selectedViewMode = RequirementViewMode.Details;

        [ObservableProperty]
        private Requirement? currentRequirement;

        [ObservableProperty]
        private bool hasCurrentRequirement;

        #endregion

        #region Analysis Properties (from Jama path)

        [ObservableProperty]
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisElapsedTime = "";

        [ObservableProperty]
        private string qualityScoreDisplay = "Not analyzed";

        [ObservableProperty]
        private string analysisSummary = "No analysis performed yet.";

        [ObservableProperty]
        private bool hasAnalysis;

        [ObservableProperty]
        private bool canGenerateTests;

        [ObservableProperty]
        private string jamaProbeStatus = "Jama probe idle.";

        [ObservableProperty]
        private bool isBatchQueueRunning;

        [ObservableProperty]
        private string batchQueueStatus = "Select requirements to batch analyze.";

        [ObservableProperty]
        private int deleteItemSuffixThreshold = 1930;

        // Analysis timer (from Jama path)
        private System.Timers.Timer? _analysisTimer;
        private DateTime _analysisStartTime;

        #endregion

        #region Content Collections (from General path)

        /// <summary>
        /// Tables associated with selected requirement
        /// </summary>
        public ObservableCollection<LooseTableViewModel> SelectedTableVMs { get; } = new();

        /// <summary>
        /// Paragraphs associated with selected requirement  
        /// </summary>
        public ObservableCollection<ParagraphViewModel> SelectedParagraphVMs { get; } = new();

        /// <summary>
        /// Live preview of REQ_RC requirements that will be deleted with the current threshold.
        /// </summary>
        public ObservableCollection<string> DeletionPreviewItems { get; } = new();

        #endregion

        #region Content Presence Flags

        /// <summary>
        /// True if requirement has any tables (drives Tables tab visibility)
        /// </summary>
        public bool HasTables => SelectedTableVMs?.Any() == true;

        /// <summary>
        /// True if requirement has any paragraphs (used for content analysis)
        /// </summary>
        public bool HasParagraphs => SelectedParagraphVMs?.Any() == true;

        /// <summary>
        /// True if bulk actions should be visible
        /// </summary>
        public bool BulkActionsVisible => CanSelectAllVisible();

        /// <summary>
        /// True if requirement has metadata for chips display
        /// </summary>
        public bool HasMeta => VisibleChips?.Any() == true;

        public bool HasDeletionPreviewItems => DeletionPreviewItems.Count > 0;

        public string DeletionPreviewStatus =>
            $"{DeletionPreviewItems.Count} REQ_RC requirement(s) selected for deletion (ID > {DeleteItemSuffixThreshold})";

        #endregion

        #region Chip System (from General path)

        /// <summary>
        /// Nested class for chip view model
        /// </summary>
        public class ChipViewModel
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public bool IsCore { get; set; } = false;
            public int DisplayOrder { get; set; } = 999; // For stable positioning
        }

        /// <summary>
        /// Primary chips for requirement metadata
        /// </summary>
        public ObservableCollection<ChipViewModel> VisibleChips
        {
            get => _visibleChips;
            private set
            {
                if (_visibleChips != value)
                {
                    _visibleChips = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OptionalChips));
                    OnPropertyChanged(nameof(VisibleChipsWithValuesCount));
                }
            }
        }
        private ObservableCollection<ChipViewModel> _visibleChips = new();

        /// <summary>
        /// Non-core chips only (for the pills grid) - computed from VisibleChips
        /// </summary>
        public ObservableCollection<ChipViewModel> OptionalChips
        {
            get => new ObservableCollection<ChipViewModel>(VisibleChips.Where(c => !c.IsCore));
        }

        /// <summary>
        /// Date-related chips
        /// </summary>
        public ObservableCollection<ChipViewModel> DateChips
        {
            get => _dateChips;
            private set
            {
                if (_dateChips != value)
                {
                    _dateChips = value;
                    OnPropertyChanged();
                }
            }
        }
        private ObservableCollection<ChipViewModel> _dateChips = new();

        /// <summary>
        /// Count of chips with actual values (not placeholders)
        /// </summary>
        public int VisibleChipsWithValuesCount
        {
            get
            {
                if (VisibleChips == null) return 0;
                return VisibleChips.Count(c => !string.IsNullOrWhiteSpace(c.Value) && c.Value != "(not set)");
            }
        }

        #endregion

        #region Integrated Components

        /// <summary>
        /// Analysis ViewModel for LLM analysis functionality
        /// </summary>
        public RequirementAnalysisViewModel? RequirementAnalysisVM { get; private set; }

        /// <summary>
        /// ViewModel for Requirements Search in Attachments feature
        /// </summary>
        public RequirementsSearchAttachmentsViewModel RequirementsSearchAttachmentsViewModel { get; }

        #endregion

        #region Commands

        // Tab Navigation Commands
        public ICommand SelectDetailsCommand { get; private set; } = null!;
        public ICommand SelectTablesCommand { get; private set; } = null!;
        public ICommand SelectAnalysisCommand { get; private set; } = null!;
        public ICommand SelectRequirementsScraperCommand { get; private set; } = null!;

        // Content Management Commands
        public ICommand AddRequirementCommand { get; private set; } = null!;
        public ICommand RemoveRequirementCommand { get; private set; } = null!;
        public ICommand DeleteRequirementsAboveThresholdCommand { get; private set; } = null!;

        // Table Operations Commands
        public ICommand SelectAllTablesCommand { get; private set; } = null!;
        public ICommand ClearAllTablesCommand { get; private set; } = null!;

        // Paragraph Operations Commands
        public ICommand SelectAllParagraphsCommand { get; private set; } = null!;
        public ICommand ClearAllParagraphsCommand { get; private set; } = null!;
        public ICommand ToggleParagraphCommand { get; private set; } = null!;

        // Content Editing Commands
        public ICommand EditSupplementalInfoCommand { get; private set; } = null!;

        // Bulk Operations Commands
        public ICommand SelectAllVisibleCommand { get; private set; } = null!;
        public ICommand ClearAllVisibleCommand { get; private set; } = null!;

        // Analysis & Test Generation Commands (from Jama path)
        public ICommand QuickAnalyzeCommand { get; private set; } = null!;
        public IAsyncRelayCommand AnalyzeSelectedBatchCommand { get; private set; } = null!;
        public ICommand GenerateTestsCommand { get; private set; } = null!;
        public ICommand ViewInTestGenCommand { get; private set; } = null!;

        // Workspace Navigation Commands (replaces removed side menu navigation)
        public ICommand NavigateToTestCaseCreationCommand { get; private set; } = null!;
        public ICommand NavigateToMainCommand { get; private set; } = null!;

        // Requirement Navigation Commands (in-view replacement for removed workspace navigation)
        public ICommand PreviousRequirementCommand { get; private set; } = null!;
        public ICommand NextRequirementCommand { get; private set; } = null!;

        public ObservableCollection<Requirement> Requirements => _mediator.Requirements;

        public ObservableCollection<BatchRequirementSelectionItem> BatchRequirementSelections { get; } = new();

        public int SelectedBatchCount => BatchRequirementSelections.Count(r => r.IsSelected);

        public bool HasBatchSelections => BatchRequirementSelections.Count > 0;

        public string RequirementTestLinkStatus =>
            CurrentRequirement == null
                ? "No requirement selected"
                : CurrentRequirement.HasGeneratedTestCase
                    ? $"{CurrentRequirement.GeneratedTestCases.Count} linked generated test case(s)"
                    : "No generated test case linked";

        public string RequirementReviewStatus =>
            CurrentRequirement?.Analysis != null ? "Reviewed by LLM analysis" : "Not yet reviewed";

        public string RequirementQualityStatus =>
            CurrentRequirement?.Analysis != null
                ? $"Quality score: {CurrentRequirement.Analysis.OriginalQualityScore}/10"
                : "Quality score unavailable";

        public string RequirementTraceStatus =>
            CurrentRequirement == null
                ? "Traceability unavailable"
                : $"Upstream: {CurrentRequirement.NumberOfUpstreamRelationships}  Downstream: {CurrentRequirement.NumberOfDownstreamRelationships}  Links: {CurrentRequirement.NumberOfLinks}";

        public Requirement? SelectedRequirement
        {
            get => CurrentRequirement;
            set
            {
                if (value == null || CurrentRequirement?.GlobalId == value.GlobalId)
                {
                    return;
                }

                _mediator.SelectRequirement(value);
            }
        }

        public string RequirementPositionDisplay
        {
            get
            {
                var total = _mediator.Requirements.Count;
                if (total == 0)
                {
                    return "0 / 0";
                }

                var currentIndex = _mediator.GetCurrentRequirementIndex();
                if (currentIndex < 0)
                {
                    return $"0 / {total}";
                }

                return $"{currentIndex + 1} / {total}";
            }
        }

        #endregion

        #region Fields

        private readonly INavigationMediator _navigationMediator;

        #endregion

        #region Constructor

        public UnifiedRequirementsMainViewModel(
            IRequirementsMediator mediator,
            ILogger<UnifiedRequirementsMainViewModel> logger,
            IPersistenceService persistence,
            ITextEditingDialogService textEditingDialogService,
            RequirementsSearchAttachmentsViewModel requirementsSearchAttachmentsViewModel,
            INavigationMediator navigationMediator,
            IWorkspaceDiagnosticsService workspaceDiagnosticsService,
            IRequirementAnalysisService? analysisService = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _textEditingDialogService = textEditingDialogService ?? throw new ArgumentNullException(nameof(textEditingDialogService));
            RequirementsSearchAttachmentsViewModel = requirementsSearchAttachmentsViewModel ?? throw new ArgumentNullException(nameof(requirementsSearchAttachmentsViewModel));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _workspaceDiagnosticsService = workspaceDiagnosticsService ?? throw new ArgumentNullException(nameof(workspaceDiagnosticsService));

            // Initialize commands
            InitializeCommands();

            // Wire collection-changed handlers and initial notifications
            WirePresenceNotifications();

            // Subscribe to Requirements domain events
            SubscribeToEvents();

            // Initialize Analysis ViewModel with proper dependency injection
            InitializeAnalysisViewModel(analysisService);

            // Track SelectedViewMode changes for property notifications
            this.PropertyChanged += OnViewModePropertyChanged;

            // Bridge Settings probe state into Requirements UI for visible feedback.
            AttachUserSettingsProbeBridge();

            _logger.LogInformation("[UnifiedRequirementsMainVM] Constructor completed - Instance ID: {InstanceId}", GetHashCode());
        }

        #endregion

        #region Initialization

        private new void InitializeCommands()
        {
            // Tab Navigation
            SelectDetailsCommand = new RelayCommand(() => SelectedViewMode = RequirementViewMode.Details);
            SelectTablesCommand = new RelayCommand(() => SelectedViewMode = RequirementViewMode.Tables);
            SelectAnalysisCommand = new RelayCommand(() => SelectedViewMode = RequirementViewMode.Analysis);
            SelectRequirementsScraperCommand = new RelayCommand(() => SelectedViewMode = RequirementViewMode.RequirementsScraper);



            // Content Management
            AddRequirementCommand = new RelayCommand(AddRequirement);
            RemoveRequirementCommand = new RelayCommand(RemoveSelectedRequirement, () => CurrentRequirement != null);
            DeleteRequirementsAboveThresholdCommand = new RelayCommand(DeleteRequirementsAboveThreshold);

            // Table Operations
            SelectAllTablesCommand = new RelayCommand(SelectAllTables, () => HasTables);
            ClearAllTablesCommand = new RelayCommand(ClearAllTables, () => HasTables);

            // Paragraph Operations
            SelectAllParagraphsCommand = new RelayCommand(SelectAllParagraphs, () => HasParagraphs);
            ClearAllParagraphsCommand = new RelayCommand(ClearAllParagraphs, () => HasParagraphs);
            ToggleParagraphCommand = new RelayCommand<ParagraphViewModel>(ToggleParagraph);

            // Content Editing
            EditSupplementalInfoCommand = new RelayCommand(EditSupplementalInfo, () => SelectedParagraphVMs?.Any(p => p.IsSelected) == true);

            // Bulk Operations
            SelectAllVisibleCommand = new RelayCommand(SelectAllVisible, CanSelectAllVisible);
            ClearAllVisibleCommand = new RelayCommand(ClearAllVisible, CanClearAllVisible);

            // Analysis & Test Generation
            QuickAnalyzeCommand = new RelayCommand(ExecuteQuickAnalyze, () => HasCurrentRequirement);
            AnalyzeSelectedBatchCommand = new AsyncRelayCommand(ExecuteAnalyzeSelectedBatchAsync, CanExecuteAnalyzeSelectedBatch);
            GenerateTestsCommand = new RelayCommand(ExecuteGenerateTests, () => CanGenerateTests);
            ViewInTestGenCommand = new RelayCommand(ExecuteViewInTestGen, () => HasCurrentRequirement);

            // Workspace Navigation (replaces removed side menu navigation)
            NavigateToTestCaseCreationCommand = new RelayCommand(ExecuteNavigateToTestCaseCreation);
            NavigateToMainCommand = new RelayCommand(ExecuteNavigateToMain);

            // In-view requirement navigation
            PreviousRequirementCommand = new RelayCommand(ExecutePreviousRequirement, CanExecutePreviousRequirement);
            NextRequirementCommand = new RelayCommand(ExecuteNextRequirement, CanExecuteNextRequirement);
        }

        private void SubscribeToEvents()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Subscribing to Requirements domain events");
            
            // Subscribe to requirement selection events
            _mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            _logger.LogInformation("[UnifiedRequirementsMainVM] Subscribed to RequirementSelected events");
            
            // Subscribe to navigation events
            _mediator.Subscribe<RequirementsEvents.NavigateToAttachmentSearch>(OnNavigateToAttachmentSearch);
            _logger.LogInformation("[UnifiedRequirementsMainVM] Subscribed to NavigateToAttachmentSearch events");
            
            // Initialize to Details view, not RequirementsScraper
            SelectedViewMode = RequirementViewMode.Details;
            _logger.LogInformation("[UnifiedRequirementsMainVM] Initialized to Details view mode");
            
            // CRITICAL: Get the current requirement if one is already selected (handles timing issue)
            // The RequirementSelected event may have been published before this ViewModel was created
            var currentRequirement = _mediator.CurrentRequirement;
            if (currentRequirement != null)
            {
                _logger.LogInformation("[UnifiedRequirementsMainVM] Found existing current requirement: {RequirementName}", currentRequirement.Name);
                OnRequirementSelected(new RequirementsEvents.RequirementSelected 
                { 
                    Requirement = currentRequirement,
                    SelectedBy = "ViewModel_Initialization"
                });
            }

            _mediator.Requirements.CollectionChanged += OnRequirementsCollectionUpdated;
            SynchronizeBatchSelections();
            RefreshDeletionPreview();
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Event subscriptions completed");
        }

        private void OnRequirementsCollectionUpdated(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Requirements));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            SynchronizeBatchSelections();
            RefreshDeletionPreview();
            NotifyCommandsCanExecuteChanged();
        }

        private void SynchronizeBatchSelections()
        {
            foreach (var existingItem in BatchRequirementSelections)
            {
                existingItem.PropertyChanged -= OnBatchSelectionItemPropertyChanged;
            }

            var selectedIds = BatchRequirementSelections
                .Where(s => s.IsSelected)
                .Select(s => s.Requirement.GlobalId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Only load persisted selections once, and only if we have requirements loaded
            // This ensures we don't load stale selections from different projects
            if (!_batchSelectionInitialized && selectedIds.Count == 0 && _mediator.Requirements.Count > 0)
            {
                selectedIds = LoadPersistedBatchSelectionIds();
                _batchSelectionInitialized = true;
                _logger.LogDebug("[UnifiedRequirementsMainVM] Loaded persisted batch selections: {Count} items", selectedIds.Count);
            }

            BatchRequirementSelections.Clear();

            foreach (var requirement in _mediator.Requirements)
            {
                var isSelected = !string.IsNullOrWhiteSpace(requirement.GlobalId) && selectedIds.Contains(requirement.GlobalId);
                var item = new BatchRequirementSelectionItem(requirement, isSelected);
                item.PropertyChanged += OnBatchSelectionItemPropertyChanged;
                BatchRequirementSelections.Add(item);
            }

            // Apply default selection only if:
            // 1. No items are currently selected AND
            // 2. We have requirements available AND
            // 3. Either we haven't initialized yet OR we just cleared persisted selections
            if (!BatchRequirementSelections.Any(s => s.IsSelected) && BatchRequirementSelections.Count > 0)
            {
                var itemsToSelect = BatchRequirementSelections.Take(5).ToList();
                foreach (var item in itemsToSelect)
                {
                    item.IsSelected = true;
                }
                _logger.LogDebug("[UnifiedRequirementsMainVM] Applied default batch selection to first {Count} items", itemsToSelect.Count);
            }

            PersistSelectedBatchRequirementIds();

            OnPropertyChanged(nameof(SelectedBatchCount));
            OnPropertyChanged(nameof(HasBatchSelections));
            ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();
        }

        private void OnBatchSelectionItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BatchRequirementSelectionItem.IsSelected))
            {
                return;
            }

            PersistSelectedBatchRequirementIds();
            OnPropertyChanged(nameof(SelectedBatchCount));
            ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();
        }

        private string GetBatchSelectionPersistenceKey()
        {
            var projectKey = _mediator.CurrentProjectId > 0
                ? _mediator.CurrentProjectId.ToString()
                : string.IsNullOrWhiteSpace(_mediator.CurrentProjectName)
                    ? "default"
                    : _mediator.CurrentProjectName;

            return $"{BatchSelectionPersistencePrefix}.{projectKey}";
        }

        private HashSet<string> LoadPersistedBatchSelectionIds()
        {
            try
            {
                var key = GetBatchSelectionPersistenceKey();
                var persisted = _persistence.Load<List<string>>(key) ?? new List<string>();

                var persistedIds = persisted
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Validate that persisted selections still exist in current requirements
                // If none of the persisted IDs exist in current project, assume it's a new/different project
                var currentRequirementIds = _mediator.Requirements
                    .Where(r => !string.IsNullOrWhiteSpace(r.GlobalId))
                    .Select(r => r.GlobalId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var validIds = persistedIds.Where(id => currentRequirementIds.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Only return persisted selections if we can validate at least some exist in current project
                // If no persisted selections are valid, return empty to trigger default 5-item selection
                return validIds.Count > 0 ? validIds : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[UnifiedRequirementsMainVM] Failed to load batch selection state");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void PersistSelectedBatchRequirementIds()
        {
            try
            {
                var key = GetBatchSelectionPersistenceKey();
                var selectedIds = BatchRequirementSelections
                    .Where(s => s.IsSelected && !string.IsNullOrWhiteSpace(s.Requirement.GlobalId))
                    .Select(s => s.Requirement.GlobalId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _persistence.Save(key, selectedIds);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[UnifiedRequirementsMainVM] Failed to persist batch selection state");
            }
        }

        private void InitializeAnalysisViewModel(IRequirementAnalysisService? analysisService)
        {
            var analysisEngine = App.ServiceProvider?.GetService<IRequirementAnalysisEngine>();
            if (analysisEngine != null)
            {
                var analysisLogger = App.ServiceProvider?.GetService<ILogger<RequirementAnalysisViewModel>>() ?? 
                    new LoggerFactory().CreateLogger<RequirementAnalysisViewModel>();
                
                var editDetectionService = App.ServiceProvider?.GetService<IEditDetectionService>();
                var learningService = App.ServiceProvider?.GetService<ILLMLearningService>();
                
                RequirementAnalysisVM = new RequirementAnalysisViewModel(analysisEngine, analysisLogger, editDetectionService, learningService);
                OnPropertyChanged(nameof(RequirementAnalysisVM));
            }
            else
            {
                RequirementAnalysisVM = null;
                OnPropertyChanged(nameof(RequirementAnalysisVM));
                _logger.LogWarning("[UnifiedRequirementsMainVM] No IRequirementAnalysisEngine service available - analysis features disabled");
            }
        }

        private void WirePresenceNotifications()
        {
            SelectedTableVMs.CollectionChanged += (_, __) => 
            {
                OnPropertyChanged(nameof(HasTables));
                OnPropertyChanged(nameof(BulkActionsVisible));
                ((RelayCommand)SelectAllTablesCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllTablesCommand).NotifyCanExecuteChanged();
            };

            SelectedParagraphVMs.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(HasParagraphs));
                OnPropertyChanged(nameof(BulkActionsVisible));
                ((RelayCommand)SelectAllParagraphsCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllParagraphsCommand).NotifyCanExecuteChanged();
            };
        }

        #endregion

        #region Event Handlers

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected eventArg)
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Processing RequirementSelected event - Requirement: {RequirementName}", eventArg.Requirement?.Name ?? "None");
            System.Diagnostics.Debug.WriteLine($"[OnRequirementSelected] CLEARING TABLE VMs - Event for requirement: {eventArg.Requirement?.Name ?? "None"}");
            
            var requirement = eventArg.Requirement;
            CurrentRequirement = requirement;
            HasCurrentRequirement = requirement != null;
            
            // Clear previous content
            System.Diagnostics.Debug.WriteLine($"[OnRequirementSelected] About to clear SelectedTableVMs.Count = {SelectedTableVMs.Count}");
            SelectedTableVMs.Clear();
            SelectedParagraphVMs.Clear();
            System.Diagnostics.Debug.WriteLine($"[OnRequirementSelected] SelectedTableVMs cleared, Count now = {SelectedTableVMs.Count}");

            // Clear previous chips
            VisibleChips.Clear();
            DateChips.Clear();

            if (requirement != null)
            {
                PopulateContentCollections(requirement);
                PopulateChips(requirement);
                
                // Reset analysis state  
                HasAnalysis = requirement.Analysis != null;
                
                if (HasAnalysis && requirement.Analysis != null)
                {
                    var analysis = requirement.Analysis;
                    QualityScoreDisplay = $"{analysis.OriginalQualityScore:F1}/10";
                    AnalysisSummary = analysis.FreeformFeedback ?? "No analysis feedback available.";
                    CanGenerateTests = analysis.OriginalQualityScore >= 7; // Consider good quality if score >= 7
                }
                else
                {
                    QualityScoreDisplay = "Not analyzed";
                    AnalysisSummary = "No analysis performed yet.";
                    CanGenerateTests = false;
                }
                
                _logger.LogInformation("[UnifiedRequirementsMainVM] Content populated - Tables: {TableCount}, Paragraphs: {ParagraphCount}, Chips: {ChipCount}", 
                    SelectedTableVMs.Count, SelectedParagraphVMs.Count, VisibleChips.Count);
            }
            else
            {
                _logger.LogInformation("[UnifiedRequirementsMainVM] No requirement selected - content cleared");
            }

            // Notify commands
            OnPropertyChanged(nameof(SelectedRequirement));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            OnPropertyChanged(nameof(RequirementTestLinkStatus));
            OnPropertyChanged(nameof(RequirementReviewStatus));
            OnPropertyChanged(nameof(RequirementQualityStatus));
            OnPropertyChanged(nameof(RequirementTraceStatus));
            NotifyCommandsCanExecuteChanged();
        }

        private void PopulateContentCollections(Requirement requirement)
        {
            _logger.LogDebug("[UnifiedRequirementsMainVM] PopulateContentCollections - LooseTables: {LooseTableCount}", 
                requirement.LooseContent?.Tables?.Count ?? 0);

            // Clear existing collections
            SelectedTableVMs.Clear();
            SelectedParagraphVMs.Clear();

            // Populate tables from LooseContent (this is where the actual table data is)
            if (requirement.LooseContent?.Tables?.Any() == true)
            {
                foreach (var looseTable in requirement.LooseContent.Tables)
                {
                    try
                    {
                        _logger.LogInformation("[UnifiedRequirementsMainVM] Processing LooseTable: Title='{Title}', Rows={RowCount}, ColumnKeys={ColumnKeyCount}, ColumnHeaders={ColumnHeaderCount}", 
                            looseTable.EditableTitle, 
                            looseTable.Rows?.Count ?? 0, 
                            looseTable.ColumnKeys?.Count ?? 0, 
                            looseTable.ColumnHeaders?.Count ?? 0);

                        // Debug actual row data
                        if (looseTable.Rows?.Any() == true)
                        {
                            for (int i = 0; i < Math.Min(looseTable.Rows.Count, 3); i++) // Log first 3 rows
                            {
                                var row = looseTable.Rows[i];
                                _logger.LogInformation("[UnifiedRequirementsMainVM] LooseTable Row {RowIndex}: {CellCount} cells = [{Cells}]", 
                                    i, row?.Count ?? 0, string.Join(", ", row?.Select(cell => $"'{cell}'") ?? new[] { "null" }));
                            }
                        }
                        // Create column definitions
                        var columns = new ObservableCollection<EditableDataControl.ViewModels.ColumnDefinitionModel>();
                        if (looseTable.ColumnKeys?.Any() == true && looseTable.ColumnHeaders?.Any() == true)
                        {
                            // Use existing column metadata
                            for (int i = 0; i < Math.Min(looseTable.ColumnKeys.Count, looseTable.ColumnHeaders.Count); i++)
                            {
                                columns.Add(new EditableDataControl.ViewModels.ColumnDefinitionModel
                                {
                                    BindingPath = looseTable.ColumnKeys[i],
                                    Header = looseTable.ColumnHeaders[i]
                                });
                            }
                        }
                        else if (looseTable.Rows?.Any() == true)
                        {
                            // Generate column definitions from data structure
                            var maxColumns = looseTable.Rows.Max(row => row?.Count ?? 0);
                            for (int i = 0; i < maxColumns; i++)
                            {
                                columns.Add(new EditableDataControl.ViewModels.ColumnDefinitionModel
                                {
                                    BindingPath = $"Col{i}",
                                    Header = $"Column {i + 1}"
                                });
                            }
                        }

                        // Create row data
                        var rows = new ObservableCollection<EditableDataControl.ViewModels.TableRowModel>();
                        if (looseTable.Rows?.Any() == true)
                        {
                            foreach (var looseRow in looseTable.Rows)
                            {
                                var rowModel = new EditableDataControl.ViewModels.TableRowModel();
                                
                                for (int colIndex = 0; colIndex < Math.Min(looseRow.Count, columns.Count); colIndex++)
                                {
                                    rowModel[columns[colIndex].BindingPath] = looseRow[colIndex] ?? "";
                                }
                                
                                rows.Add(rowModel);
                            }
                        }

                        var tableVM = new LooseTableViewModel(
                            requirementId: requirement.GlobalId ?? requirement.Item ?? "",
                            tableKey: Guid.NewGuid().ToString(), // Generate unique key since LooseTable doesn't have ID
                            title: looseTable.EditableTitle ?? "Untitled Table",
                            columns: columns,
                            rows: rows
                        );

                        SelectedTableVMs.Add(tableVM);
                        _logger.LogDebug("[UnifiedRequirementsMainVM] Added LooseTable: {Title} with {ColumnCount} columns and {RowCount} rows", 
                            looseTable.EditableTitle, columns.Count, rows.Count);
                        
                        // Additional debug for actual data content
                        _logger.LogInformation("[UnifiedRequirementsMainVM] LooseTableViewModel created - Title: '{Title}', DisplayName: '{DisplayName}', Columns: {ColumnCount}, Rows: {RowCount}",
                            tableVM.Title, tableVM.DisplayName, tableVM.Columns.Count, tableVM.Rows.Count);
                        _logger.LogInformation("[UnifiedRequirementsMainVM] EditorViewModel status - IsNull: {IsNull}, Title: '{EditorTitle}', Columns: {EditorColumns}, Rows: {EditorRows}",
                            tableVM.EditorViewModel == null, tableVM.EditorViewModel?.Title ?? "null", 
                            tableVM.EditorViewModel?.Columns?.Count ?? -1, tableVM.EditorViewModel?.Rows?.Count ?? -1);
                        
                        foreach (var row in rows)
                        {
                            _logger.LogInformation("[UnifiedRequirementsMainVM] Row data: {CellCount} cells", row.Cells.Count);
                            foreach (var cell in row.Cells)
                            {
                                _logger.LogInformation("[UnifiedRequirementsMainVM] Cell: {Key} = '{Value}'", cell.Key, cell.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[UnifiedRequirementsMainVM] Failed to create LooseTableViewModel from LooseTable: {Title}", looseTable.EditableTitle);
                    }
                }
            }

            _logger.LogInformation("[UnifiedRequirementsMainVM] Content populated - Tables: {TableCount}", 
                SelectedTableVMs.Count);
        }

        private void PopulateChips(Requirement requirement)
        {
            var chips = new List<ChipViewModel>();

            // Core chips (always visible when available)
            if (!string.IsNullOrEmpty(requirement.Item))
                chips.Add(new ChipViewModel { Label = "ID", Value = requirement.Item, IsCore = true, DisplayOrder = 1 });
            
            if (!string.IsNullOrEmpty(requirement.ItemType))
                chips.Add(new ChipViewModel { Label = "Type", Value = requirement.ItemType, IsCore = true, DisplayOrder = 2 });

            if (!string.IsNullOrEmpty(requirement.RelationshipStatus))
                chips.Add(new ChipViewModel { Label = "Status", Value = requirement.RelationshipStatus, IsCore = true, DisplayOrder = 3 });

            // Optional chips
            if (!string.IsNullOrEmpty(requirement.Project))
                chips.Add(new ChipViewModel { Label = "Project", Value = requirement.Project, IsCore = false, DisplayOrder = 12 });

            if (!string.IsNullOrEmpty(requirement.Release))
                chips.Add(new ChipViewModel { Label = "Release", Value = requirement.Release, IsCore = false, DisplayOrder = 14 });

            if (!string.IsNullOrEmpty(requirement.CreatedBy))
                chips.Add(new ChipViewModel { Label = "Created By", Value = requirement.CreatedBy, IsCore = false, DisplayOrder = 11 });

            // Sort chips by display order for consistent positioning
            VisibleChips = new ObservableCollection<ChipViewModel>(chips.OrderBy(c => c.DisplayOrder));

            // Date chips (handled separately for temporal context)
            var dateChips = new List<ChipViewModel>();
            
            if (requirement.CreatedDate != null)
                dateChips.Add(new ChipViewModel { Label = "Created", Value = requirement.CreatedDate.Value.ToString("MMM dd, yyyy"), IsCore = false, DisplayOrder = 20 });
            
            if (requirement.ModifiedDate != null)
                dateChips.Add(new ChipViewModel { Label = "Modified", Value = requirement.ModifiedDate.Value.ToString("MMM dd, yyyy"), IsCore = false, DisplayOrder = 21 });
            
            DateChips = new ObservableCollection<ChipViewModel>(dateChips);
        }

        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged eventArg)
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Requirements collection changed - Count: {Count}", eventArg.AffectedRequirements?.Count ?? 0);
            
            // If current requirement no longer exists, clear selection
            if (CurrentRequirement != null && eventArg.AffectedRequirements?.Any(r => r.GlobalId == CurrentRequirement.GlobalId) != true)
            {
                CurrentRequirement = null;
                HasCurrentRequirement = false;
                SelectedTableVMs.Clear();
                SelectedParagraphVMs.Clear();
                VisibleChips.Clear();
                DateChips.Clear();
                _logger.LogInformation("[UnifiedRequirementsMainVM] Current requirement no longer exists - selection cleared");
            }
            
            NotifyCommandsCanExecuteChanged();
        }

        private void OnWorkflowStateChanged(RequirementsEvents.WorkflowStateChanged eventArg)
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Workflow state changed");
            NotifyCommandsCanExecuteChanged();
        }

        private void OnRequirementUpdated(RequirementsEvents.RequirementUpdated eventArg)
        {
            if (CurrentRequirement?.GlobalId == eventArg.Requirement?.GlobalId)
            {
                _logger.LogInformation("[UnifiedRequirementsMainVM] Current requirement updated - Requirement: {RequirementName}", eventArg.Requirement?.Name);
                
                var updatedRequirement = eventArg.Requirement;
                if (updatedRequirement != null)
                {
                    CurrentRequirement = updatedRequirement;
                    PopulateContentCollections(updatedRequirement);
                    PopulateChips(updatedRequirement);
                }
            }
        }

        private void OnRequirementAnalyzed(RequirementsEvents.RequirementAnalyzed eventArg)
        {
            if (CurrentRequirement?.GlobalId == eventArg.Requirement?.GlobalId)
            {
                _logger.LogInformation("[UnifiedRequirementsMainVM] Current requirement analyzed - Requirement: {RequirementName}", eventArg.Requirement?.Name);
                
                var analysis = eventArg.Analysis;
                if (analysis != null)
                {
                    HasAnalysis = true;
                    QualityScoreDisplay = $"{analysis.OriginalQualityScore:F1}/10";
                    AnalysisSummary = analysis.FreeformFeedback ?? "Analysis completed with no feedback.";
                    CanGenerateTests = analysis.OriginalQualityScore >= 7; // Consider good quality if score >= 7
                    OnPropertyChanged(nameof(RequirementReviewStatus));
                    OnPropertyChanged(nameof(RequirementQualityStatus));
                }
            }
        }

        private void OnNavigateToAttachmentSearch(RequirementsEvents.NavigateToAttachmentSearch eventArg)
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Navigate to attachment search triggered");
            SelectedViewMode = RequirementViewMode.RequirementsScraper;
        }

        private void OnViewModePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedViewMode))
            {
                _logger.LogDebug("[UnifiedRequirementsMainVM] SelectedViewMode changed to: {ViewMode}", SelectedViewMode);
            }
        }

        private void AttachUserSettingsProbeBridge()
        {
            var resolved = App.ServiceProvider?.GetService<UserSettingsViewModel>();
            if (resolved == null)
            {
                return;
            }

            if (!ReferenceEquals(_userSettingsVm, resolved))
            {
                if (_userSettingsVm != null)
                {
                    _userSettingsVm.PropertyChanged -= OnUserSettingsProbePropertyChanged;
                }

                _userSettingsVm = resolved;
                _userSettingsVm.PropertyChanged += OnUserSettingsProbePropertyChanged;
            }

            if (!string.IsNullOrWhiteSpace(_userSettingsVm.StatusMessage))
            {
                JamaProbeStatus = _userSettingsVm.StatusMessage;
            }
        }

        private void OnUserSettingsProbePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_userSettingsVm == null)
            {
                return;
            }

            if (e.PropertyName == nameof(UserSettingsViewModel.StatusMessage))
            {
                if (!string.IsNullOrWhiteSpace(_userSettingsVm.StatusMessage) &&
                    (_userSettingsVm.StatusMessage.Contains("Jama relationship probe", StringComparison.OrdinalIgnoreCase) ||
                     _userSettingsVm.StatusMessage.Contains("Jama probe", StringComparison.OrdinalIgnoreCase)))
                {
                    JamaProbeStatus = _userSettingsVm.StatusMessage;
                }
            }

            if (e.PropertyName == nameof(UserSettingsViewModel.LastJamaProbeReport) &&
                !string.IsNullOrWhiteSpace(_userSettingsVm.LastJamaProbeReport))
            {
                if (_userSettingsVm.LastJamaProbeReport.Contains("[JAMA RELATIONSHIP PROBE SUCCESS]", StringComparison.Ordinal))
                {
                    JamaProbeStatus = "Jama relationship probe completed successfully.";
                }
                else if (_userSettingsVm.LastJamaProbeReport.Contains("[JAMA RELATIONSHIP PROBE ERROR]", StringComparison.Ordinal) ||
                         _userSettingsVm.LastJamaProbeReport.Contains("[JAMA RELATIONSHIP PROBE EXCEPTION]", StringComparison.Ordinal))
                {
                    JamaProbeStatus = "Jama relationship probe failed. Open Settings for details.";
                }
            }
        }

        #endregion

        #region Command Implementations

        // Content Management
        private void AddRequirement()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] AddRequirement command executed");
            // TODO: Integrate with mediator pattern for requirement creation
        }

        private void RemoveSelectedRequirement()
        {
            if (CurrentRequirement == null) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] RemoveSelectedRequirement command executed - Requirement: {RequirementName}", CurrentRequirement.Name);
            
            // Use mediator to remove requirement
            _mediator.RemoveRequirement(CurrentRequirement);
        }

        private void DeleteRequirementsAboveThreshold()
        {
            var threshold = DeleteItemSuffixThreshold;
            var matches = GetDeleteCandidates(threshold);

            if (matches.Count == 0)
            {
                MessageBox.Show(
                    $"No REQ_RC requirements found with trailing ID number greater than {threshold}.",
                    "Bulk Delete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete {matches.Count} REQ_RC requirement(s) with trailing ID number greater than {threshold}?\n\nThis cannot be undone.",
                "Confirm Bulk Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var requirement in matches)
            {
                _mediator.RemoveRequirement(requirement);
            }

            _logger.LogInformation(
                "[UnifiedRequirementsMainVM] Bulk deleted {Count} REQ_RC requirements with trailing ID number > {Threshold}",
                matches.Count,
                threshold);

            OnPropertyChanged(nameof(RequirementPositionDisplay));
            RefreshDeletionPreview();
            NotifyCommandsCanExecuteChanged();
        }

        partial void OnDeleteItemSuffixThresholdChanged(int value)
        {
            RefreshDeletionPreview();
            NotifyCommandsCanExecuteChanged();
        }

        private List<Requirement> GetDeleteCandidates(int threshold)
        {
            return _mediator.Requirements
                .Where(r => TryGetRequirementRcSuffix(r.Item, out var itemSuffix) && itemSuffix > threshold)
                .Distinct()
                .OrderBy(r => r.Item)
                .ToList();
        }

        private void RefreshDeletionPreview()
        {
            var candidates = GetDeleteCandidates(DeleteItemSuffixThreshold);

            DeletionPreviewItems.Clear();
            foreach (var requirement in candidates)
            {
                var id = string.IsNullOrWhiteSpace(requirement.Item) ? "<no-item-id>" : requirement.Item;
                var name = string.IsNullOrWhiteSpace(requirement.Name) ? "<no-name>" : requirement.Name;
                DeletionPreviewItems.Add($"{id} - {name}");
            }

            OnPropertyChanged(nameof(HasDeletionPreviewItems));
            OnPropertyChanged(nameof(DeletionPreviewStatus));
        }

        private static bool TryGetRequirementRcSuffix(string? text, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // Scope bulk deletion to requirement IDs in the REQ_RC family.
            // Example: MFD268C4B-REQ_RC-1931 -> 1931.
            var match = Regex.Match(text.Trim(), @"-REQ_RC-(\d+)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            return int.TryParse(match.Groups[1].Value, out value);
        }

        // Table Operations
        private void SelectAllTables()
        {
            if (!HasTables) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Selecting all tables - Count: {Count}", SelectedTableVMs.Count);
            
            foreach (var tableVM in SelectedTableVMs)
            {
                tableVM.IsSelected = true;
            }
        }

        private void ClearAllTables()
        {
            if (!HasTables) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Clearing all table selections - Count: {Count}", SelectedTableVMs.Count);
            
            foreach (var tableVM in SelectedTableVMs)
            {
                tableVM.IsSelected = false;
            }
        }

        // Paragraph Operations
        private void SelectAllParagraphs()
        {
            if (!HasParagraphs) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Selecting all paragraphs - Count: {Count}", SelectedParagraphVMs.Count);
            
            foreach (var paragraphVM in SelectedParagraphVMs)
            {
                paragraphVM.IsSelected = true;
            }
        }

        private void ClearAllParagraphs()
        {
            if (!HasParagraphs) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Clearing all paragraph selections - Count: {Count}", SelectedParagraphVMs.Count);
            
            foreach (var paragraphVM in SelectedParagraphVMs)
            {
                paragraphVM.IsSelected = false;
            }
        }

        private void ToggleParagraph(ParagraphViewModel? paragraphVM)
        {
            if (paragraphVM == null) return;
            
            paragraphVM.IsSelected = !paragraphVM.IsSelected;
            _logger.LogDebug("[UnifiedRequirementsMainVM] Toggled paragraph selection - IsSelected: {IsSelected}", paragraphVM.IsSelected);
        }

        // Content Editing
        private void EditSupplementalInfo()
        {
            var selectedParagraphs = SelectedParagraphVMs?.Where(p => p.IsSelected).ToList();
            if (selectedParagraphs?.Any() != true) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] EditSupplementalInfo command executed - Selected paragraphs: {Count}", selectedParagraphs.Count);
            
            // TODO: Implement supplemental info editing dialog
            // This would typically open a dialog for editing the combined supplemental information
        }

        // Bulk Operations
        private void SelectAllVisible()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Selecting all visible content");
            
            switch (SelectedViewMode)
            {
                case RequirementViewMode.Tables:
                    SelectAllTables();
                    break;
                case RequirementViewMode.Details:
                default:
                    SelectAllParagraphs();
                    break;
            }
        }

        private void ClearAllVisible()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Clearing all visible content selections");
            
            switch (SelectedViewMode)
            {
                case RequirementViewMode.Tables:
                    ClearAllTables();
                    break;
                case RequirementViewMode.Details:
                default:
                    ClearAllParagraphs();
                    break;
            }
        }

        private bool CanSelectAllVisible()
        {
            return SelectedViewMode switch
            {
                RequirementViewMode.Tables => HasTables,
                RequirementViewMode.Details => HasParagraphs,
                _ => HasParagraphs || HasTables
            };
        }

        private bool CanClearAllVisible()
        {
            return CanSelectAllVisible();
        }

        private void ExecutePreviousRequirement()
        {
            _mediator.NavigateToPrevious();
            OnPropertyChanged(nameof(RequirementPositionDisplay));
        }

        private bool CanExecutePreviousRequirement()
        {
            return _mediator.GetCurrentRequirementIndex() > 0;
        }

        private void ExecuteNextRequirement()
        {
            _mediator.NavigateToNext();
            OnPropertyChanged(nameof(RequirementPositionDisplay));
        }

        private bool CanExecuteNextRequirement()
        {
            var idx = _mediator.GetCurrentRequirementIndex();
            var total = _mediator.Requirements.Count;
            return idx >= 0 && idx < total - 1;
        }

        // Analysis & Test Generation Commands
        private async void ExecuteQuickAnalyze()
        {
            if (CurrentRequirement == null) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Quick analyze started - Requirement: {RequirementName}", CurrentRequirement.Name);
            
            IsAnalyzing = true;
            _analysisStartTime = DateTime.Now;
            
            // Start analysis timer
            _analysisTimer = new System.Timers.Timer(1000);
            _analysisTimer.Elapsed += (_, __) => UpdateAnalysisTimer();
            _analysisTimer.Start();
            
            try
            {
                // Execute analysis through the mediator
                await _mediator.AnalyzeRequirementAsync(CurrentRequirement);
            }
            finally
            {
                IsAnalyzing = false;
                _analysisTimer?.Stop();
                _analysisTimer?.Dispose();
                _analysisTimer = null;
                AnalysisElapsedTime = "";
            }
        }

        private bool CanExecuteAnalyzeSelectedBatch()
        {
            return !IsBatchQueueRunning && BatchRequirementSelections.Any(r => r.IsSelected);
        }

        private async Task ExecuteAnalyzeSelectedBatchAsync()
        {
            var selected = BatchRequirementSelections
                .Where(r => r.IsSelected)
                .Select(r => r.Requirement)
                .ToList();

            if (!selected.Any())
            {
                BatchQueueStatus = "Select at least one requirement for batch analysis.";
                return;
            }

            IsBatchQueueRunning = true;
            BatchQueueStatus = $"Starting analysis for {selected.Count} selected requirement(s)...";
            ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();

            var frontLoaded = selected.Take(5).ToList();
            var remaining = selected.Skip(frontLoaded.Count).ToList();

            try
            {
                if (frontLoaded.Any())
                {
                    BatchQueueStatus = $"Analyzing first {frontLoaded.Count} requirement(s)...";
                    await _mediator.AnalyzeBatchRequirementsAsync(frontLoaded.AsReadOnly());
                }

                if (remaining.Any())
                {
                    BatchQueueStatus = $"Continuing in background with {remaining.Count} remaining requirement(s). You can keep working.";

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _mediator.AnalyzeBatchRequirementsAsync(remaining.AsReadOnly());
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                BatchQueueStatus = $"Batch analysis complete for {selected.Count} requirement(s).";
                                IsBatchQueueRunning = false;
                                ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[UnifiedRequirementsMainVM] Background selected-batch analysis failed");
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                BatchQueueStatus = "Background analysis encountered an error. Check logs for details.";
                                IsBatchQueueRunning = false;
                                ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();
                            });
                        }
                    });

                    return;
                }

                BatchQueueStatus = $"Batch analysis complete for {selected.Count} requirement(s).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UnifiedRequirementsMainVM] Selected-batch analysis failed");
                BatchQueueStatus = "Batch analysis failed. Check logs for details.";
            }
            finally
            {
                if (!remaining.Any())
                {
                    IsBatchQueueRunning = false;
                    ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();
                }
            }
        }

        private void ExecuteGenerateTests()
        {
            if (CurrentRequirement == null || !CanGenerateTests) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Generate tests executed - Requirement: {RequirementName}", CurrentRequirement.Name);
            
            // Navigate to test generation through workspace coordinator
            // This would typically trigger a domain change event
        }

        private void ExecuteViewInTestGen()
        {
            if (CurrentRequirement == null) return;
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] View in test generation executed - Requirement: {RequirementName}", CurrentRequirement.Name);
            
            // Navigate to test generation for viewing through workspace coordinator  
        }

        private void ExecuteNavigateToTestCaseCreation()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Navigating to TestCase Creation workspace");
            _navigationMediator.NavigateToSection("testcasecreation");
        }

        private void ExecuteNavigateToMain()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Navigating to main/startup view");
            _navigationMediator.NavigateToSection("startup");
        }



        private void UpdateAnalysisTimer()
        {
            var elapsed = DateTime.Now - _analysisStartTime;
            AnalysisElapsedTime = $"Analyzing... {elapsed:mm\\:ss}";
        }

        private void NotifyCommandsCanExecuteChanged()
        {
            ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)SelectAllTablesCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearAllTablesCommand).NotifyCanExecuteChanged();
            ((RelayCommand)SelectAllParagraphsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearAllParagraphsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditSupplementalInfoCommand).NotifyCanExecuteChanged();
            ((RelayCommand)SelectAllVisibleCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearAllVisibleCommand).NotifyCanExecuteChanged();
            ((RelayCommand)QuickAnalyzeCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)AnalyzeSelectedBatchCommand).NotifyCanExecuteChanged();
            ((RelayCommand)GenerateTestsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ViewInTestGenCommand).NotifyCanExecuteChanged();
            ((RelayCommand)PreviousRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextRequirementCommand).NotifyCanExecuteChanged();
        }

        #endregion

        #region BaseDomainViewModel Implementation

        protected override bool CanRefresh() => true;

        protected override bool CanSave() => false; // Requirements are read-only in this view

        protected override bool CanCancel() => false;

        protected override async Task RefreshAsync()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Refresh requested");
            // Refresh would typically reload requirements from source
            await Task.CompletedTask;
        }

        protected override Task SaveAsync()
        {
            // No-op for read-only requirements view
            return Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // No-op for requirements view
        }

        #endregion

        #region Disposal

        public new void Dispose()
        {
            _logger.LogInformation("[UnifiedRequirementsMainVM] Disposing instance - ID: {InstanceId}", GetHashCode());
            
            // Clean up timer
            _analysisTimer?.Stop();
            _analysisTimer?.Dispose();
            
            // Dispose RequirementAnalysisVM
            RequirementAnalysisVM?.Dispose();

            if (_userSettingsVm != null)
            {
                _userSettingsVm.PropertyChanged -= OnUserSettingsProbePropertyChanged;
            }

            foreach (var item in BatchRequirementSelections)
            {
                item.PropertyChanged -= OnBatchSelectionItemPropertyChanged;
            }

            _mediator.Requirements.CollectionChanged -= OnRequirementsCollectionUpdated;
            
            this.PropertyChanged -= OnViewModePropertyChanged;
            
            base.Dispose(); // Call base disposal
            
            _logger.LogInformation("[UnifiedRequirementsMainVM] Disposal completed");
        }

        #endregion

        public sealed class BatchRequirementSelectionItem : ObservableObject
        {
            public BatchRequirementSelectionItem(Requirement requirement, bool isSelected)
            {
                Requirement = requirement;
                _isSelected = isSelected;
            }

            public Requirement Requirement { get; }

            public string DisplayText => string.IsNullOrWhiteSpace(Requirement.Item)
                ? Requirement.Name
                : $"{Requirement.Item} - {Requirement.Name}";

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }
        }
    }
}