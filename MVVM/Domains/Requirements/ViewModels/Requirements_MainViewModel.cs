using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Mediators;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Main ViewModel for Requirements domain - complete copy from TestCaseGenerator_VM.
    /// Provides requirement details display with chip-based UI, tables, and paragraphs.
    /// </summary>
    public partial class Requirements_MainViewModel : BaseDomainViewModel, IDisposable
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        private readonly IPersistenceService _persistence;
        private readonly ITextEditingDialogService _textEditingDialogService;

        // Optional lightweight providers
        private readonly Func<Requirement?, IEnumerable<LooseTableViewModel>>? _tableProvider;
        private readonly Func<Requirement?, IEnumerable<string>>? _paragraphProvider;

        // Optional richer provider that can provide VMs directly.
        internal object? RequirementsCore { get; set; }

        // Analysis VM for LLM-powered requirement analysis
        public object? AnalysisVM { get; private set; }

        // Expose mediator analysis state for UI binding
        public bool IsAnalyzing => _mediator.IsAnalyzing;

        // Local state management 
        private readonly ObservableCollection<Requirement> _requirements = new();
        private Requirement? _selectedRequirement;

        public Requirements_MainViewModel(
            IRequirementsMediator mediator,
            ITestCaseGenerationMediator testCaseGenerationMediator,
            IPersistenceService persistence,
            ITextEditingDialogService textEditingDialogService,
            ILogger<Requirements_MainViewModel> logger,
            IRequirementAnalysisService? analysisService = null,
            Func<Requirement?, IEnumerable<LooseTableViewModel>>? tableProvider = null,
            Func<Requirement?, IEnumerable<string>>? paragraphProvider = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _textEditingDialogService = textEditingDialogService ?? throw new ArgumentNullException(nameof(textEditingDialogService));
            _tableProvider = tableProvider;
            _paragraphProvider = paragraphProvider;

            // Subscribe to domain events - DUAL SUBSCRIPTION for cross-domain compatibility
            _mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            _mediator.Subscribe<RequirementsEvents.WorkflowStateChanged>(OnWorkflowStateChanged);
            
            // CRITICAL: Also subscribe to TestCaseGenerationEvents since navigation publishes those
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnTestCaseGenerationRequirementSelected);

            // Commands
            AddRequirementCommand = new RelayCommand(AddRequirement);
            RemoveRequirementCommand = new RelayCommand(RemoveSelectedRequirement, () => SelectedRequirement != null);

            // per-type commands (still available if child controls want to bind directly)
            SelectAllTablesCommand = new RelayCommand(SelectAllTables, () => SelectedTableVMs?.Any() == true);
            ClearAllTablesCommand = new RelayCommand(ClearAllTables, () => SelectedTableVMs?.Any() == true);
            SelectAllParagraphsCommand = new RelayCommand(SelectAllParagraphs, () => SelectedParagraphVMs?.Any() == true);
            ClearAllParagraphsCommand = new RelayCommand(ClearAllParagraphs, () => SelectedParagraphVMs?.Any() == true);
            ToggleParagraphCommand = new RelayCommand<ParagraphViewModel>(ToggleParagraph);
            EditSupplementalInfoCommand = new RelayCommand(EditSupplementalInfo, () => SelectedParagraphVMs?.Any(p => p.IsSelected) == true);

            // routed parent-level commands (act on visible view)
            SelectAllVisibleCommand = new RelayCommand(SelectAllVisible, CanSelectAllVisible);
            ClearAllVisibleCommand = new RelayCommand(ClearAllVisible, CanClearAllVisible);

            // Collections
            SelectedTableVMs = new ObservableCollection<LooseTableViewModel>();
            SelectedParagraphVMs = new ObservableCollection<ParagraphViewModel>();

            // Wire collection-changed handlers and initial notifications
            WirePresenceNotifications();

            // Create Analysis VM placeholder (we'll skip the complex analysis setup for now)
            AnalysisVM = null; // TODO: Add analysis functionality if needed
            
            // CRITICAL: Initialize with current requirement from TestCaseGeneration mediator
            InitializeWithCurrentRequirement();
            
            // Initial data load
            UpdateVisibleChipsFromRequirement(SelectedRequirement);
        }

        /// <summary>
        /// Initialize with current requirement from TestCaseGeneration mediator
        /// </summary>
        private void InitializeWithCurrentRequirement()
        {
            try
            {
                // Get current requirement from TestCaseGeneration mediator
                var currentRequirement = _testCaseGenerationMediator.CurrentRequirement;
                if (currentRequirement != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Initializing with current requirement: {currentRequirement.GlobalId}");
                    _selectedRequirement = currentRequirement;
                    OnPropertyChanged(nameof(SelectedRequirement));
                    
                    // Load the requirement content
                    UpdateVisibleChipsFromRequirement(_selectedRequirement);
                    LoadRequirementContent(_selectedRequirement);
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] No current requirement found during initialization");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Requirements_MainViewModel with current requirement");
            }
        }

        /// <summary>
        /// Handle requirement selection events from domain mediator
        /// </summary>
        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] OnRequirementSelected called with: {e.Requirement?.GlobalId ?? "NULL"}");
            Console.WriteLine($"*** [Requirements_MainViewModel] OnRequirementSelected: {e.Requirement?.GlobalId ?? "NULL"} ***");
            if (!ReferenceEquals(_selectedRequirement, e.Requirement))
            {
                _selectedRequirement = e.Requirement;
                OnPropertyChanged(nameof(SelectedRequirement));
                
                // Re-populate chips from requirement data
                UpdateVisibleChipsFromRequirement(_selectedRequirement);
                
                // Update analysis state
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
                
                // Load requirement content
                LoadRequirementContent(_selectedRequirement);
            }
        }

        /// <summary>
        /// Handle requirement selection events from TestCaseGeneration domain (cross-domain compatibility)
        /// </summary>
        private void OnTestCaseGenerationRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] OnTestCaseGenerationRequirementSelected called with: {e.Requirement?.GlobalId ?? "NULL"}");
            Console.WriteLine($"*** [Requirements_MainViewModel] OnTestCaseGenerationRequirementSelected: {e.Requirement?.GlobalId ?? "NULL"} ***");
            if (!ReferenceEquals(_selectedRequirement, e.Requirement))
            {
                _selectedRequirement = e.Requirement;
                OnPropertyChanged(nameof(SelectedRequirement));
                
                // Re-populate chips from requirement data
                UpdateVisibleChipsFromRequirement(_selectedRequirement);
                
                // Update analysis state
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
                
                // Load requirement content
                LoadRequirementContent(_selectedRequirement);
            }
        }

        /// <summary>
        /// Handle requirements collection changes
        /// </summary>
        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            // Update local requirements collection
            _requirements.Clear();
            foreach (var req in e.AffectedRequirements)
            {
                _requirements.Add(req);
            }
            
            // Update UI
            OnPropertyChanged(nameof(Requirements));
        }

        /// <summary>
        /// Handle workflow state changes
        /// </summary>
        private void OnWorkflowStateChanged(RequirementsEvents.WorkflowStateChanged e)
        {
            // Handle workflow changes if needed
        }

        // ==== PROPERTIES ====

        /// <summary>
        /// Collection of requirements
        /// </summary>
        public ObservableCollection<Requirement> Requirements => _requirements;

        /// <summary>
        /// Currently selected requirement
        /// </summary>
        public Requirement? SelectedRequirement
        {
            get => _selectedRequirement;
            set
            {
                if (!ReferenceEquals(_selectedRequirement, value))
                {
                    _selectedRequirement = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    
                    // Notify other components
                    if (_selectedRequirement != null)
                    {
                        _mediator.PublishEvent(new RequirementsEvents.RequirementSelected 
                        { 
                            Requirement = _selectedRequirement 
                        });
                    }
                    
                    // Update content based on new selection
                    UpdateVisibleChipsFromRequirement(value);
                }
            }
        }

        /// <summary>
        /// Display string for requirement position 
        /// </summary>
        public string RequirementPositionDisplay => "— / —"; // TODO: Implement from mediator state

        /// <summary>
        /// Controls wrapping behavior for next navigation
        /// </summary>
        public bool WrapOnNextWithoutTestCase { get; set; } = false;

        // ==== COMMANDS ====

        public ICommand AddRequirementCommand { get; }
        public ICommand RemoveRequirementCommand { get; }

        // Navigation commands (implement via mediator)
        public ICommand? PreviousRequirementCommand => null; // TODO: Implement via mediator
        public ICommand? NextRequirementCommand => null; // TODO: Implement via mediator  
        public ICommand? NextWithoutTestCaseCommand => null; // TODO: Implement via mediator

        // per-type commands
        public ICommand SelectAllTablesCommand { get; }
        public ICommand ClearAllTablesCommand { get; }
        public ICommand SelectAllParagraphsCommand { get; }
        public ICommand ClearAllParagraphsCommand { get; }
        public ICommand ToggleParagraphCommand { get; }
        public ICommand EditSupplementalInfoCommand { get; }
        public ICommand SelectAllVisibleCommand { get; }
        public ICommand ClearAllVisibleCommand { get; }

        // ==== CONTENT COLLECTIONS ====

        /// <summary>
        /// Tables associated with selected requirement
        /// </summary>
        public ObservableCollection<LooseTableViewModel> SelectedTableVMs { get; } = new();

        /// <summary>
        /// Paragraphs associated with selected requirement  
        /// </summary>
        public ObservableCollection<ParagraphViewModel> SelectedParagraphVMs { get; } = new();

        // ==== CONTENT PRESENCE FLAGS ====

        /// <summary>
        /// True if requirement has any tables
        /// </summary>
        public bool HasTables => SelectedTableVMs?.Any() == true;

        /// <summary>
        /// True if requirement has any paragraphs
        /// </summary>
        public bool HasParagraphs => SelectedParagraphVMs?.Any() == true;

        /// <summary>
        /// True if bulk actions should be visible
        /// </summary>
        public bool BulkActionsVisible => CanSelectAllVisible();

        // ==== CHIP SYSTEM ====

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
                    OnPropertyChanged(nameof(OptionalChips)); // Notify OptionalChips when VisibleChips changes
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

        /// <summary>
        /// Update visible chips from requirement data - exact copy from TestCaseGenerator_VM
        /// </summary>
        private void UpdateVisibleChipsFromRequirement(Requirement? r)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] UpdateVisibleChipsFromRequirement called with: {r?.GlobalId ?? "NULL"}");
            var list = new ObservableCollection<ChipViewModel>();
            
            if (r != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Creating chips for requirement: {r.GlobalId}");
                int orderCounter = 10;
                
                // Helper: always add field, show placeholder if empty
                void AddAlways(string label, string? value, bool isCore = false)
                {
                    list.Add(new ChipViewModel 
                    { 
                        Label = label, 
                        Value = string.IsNullOrWhiteSpace(value) ? "(not set)" : value!,
                        IsCore = isCore,
                        DisplayOrder = orderCounter++
                    });
                }

                // === Always show (core identification) ===
                AddAlways("Global ID", r.GlobalId, isCore: true);
                AddAlways("Type", r.RequirementType, isCore: true);
                AddAlways("Status", r.Status, isCore: true);
                AddAlways("Version", r.Version, isCore: true);

                // === Always show optional fields (ordered by frequency from Jama analysis) ===
                // Row 1: High-frequency classification fields
                AddAlways("Safety", r.SafetyRequirement);
                AddAlways("Security", r.SecurityRequirement);
                AddAlways("Key Characteristics", r.KeyCharacteristics);
                
                // Row 2: V&V and compliance
                AddAlways("FDAL", r.Fdal);
                AddAlways("Derived", r.DerivedRequirement);
                AddAlways("Export Controlled", r.ExportControlled);
                
                // Row 3: Project organization
                AddAlways("Project", r.Project);
                AddAlways("Set", r.SetName);
                AddAlways("Heading", r.Heading);
                
                // Row 4: Traceability and IDs
                AddAlways("API ID", r.ApiId);
                AddAlways("Change Driver", r.ChangeDriver);
                
                // Row 5: People (lower frequency)
                AddAlways("Created By", r.CreatedBy);
                AddAlways("Modified By", r.ModifiedBy);
                AddAlways("Locked By", r.LastLockedBy);
                
                // Row 6: Relationships (always show counts)
                AddAlways("Upstream Links", r.NumberOfUpstreamRelationships.ToString());
                AddAlways("Downstream Links", r.NumberOfDownstreamRelationships.ToString());
                AddAlways("Comments", r.NumberOfComments.ToString());
                AddAlways("Attachments", r.NumberOfAttachments.ToString());
            
                // === Dates (separate collection for timeline) ===
                var dateList = new ObservableCollection<ChipViewModel>();
                if (r.CreatedDate.HasValue)
                    dateList.Add(new ChipViewModel { Label = "Created", Value = r.CreatedDate.Value.ToString("g") });
                if (r.ModifiedDate.HasValue)
                    dateList.Add(new ChipViewModel { Label = "Modified", Value = r.ModifiedDate.Value.ToString("g") });
                if (r.LastActivityDate.HasValue)
                    dateList.Add(new ChipViewModel { Label = "Last Activity", Value = r.LastActivityDate.Value.ToString("g") });
                
                DateChips = dateList;
            }
            else
            {
                DateChips = new ObservableCollection<ChipViewModel>();
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Setting VisibleChips with {list.Count} items");
            VisibleChips = list;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] VisibleChips now has {VisibleChips.Count} items");
        }

        // ===== SUPPORT VIEW SELECTION =====

        [ObservableProperty]
        private SupportView selectedSupportView = SupportView.Meta;

        // Tab selection properties for UI binding
        public bool IsMetaSelected
        {
            get => SelectedSupportView == SupportView.Meta;
            set
            {
                if (value)
                {
                    SelectedSupportView = SupportView.Meta;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsTablesSelected));
                    OnPropertyChanged(nameof(IsParagraphsSelected));
                    OnPropertyChanged(nameof(IsAnalysisSelected));
                }
            }
        }

        public bool IsTablesSelected
        {
            get => SelectedSupportView == SupportView.Tables;
            set
            {
                if (value)
                {
                    SelectedSupportView = SupportView.Tables;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMetaSelected));
                    OnPropertyChanged(nameof(IsParagraphsSelected));
                    OnPropertyChanged(nameof(IsAnalysisSelected));
                }
            }
        }

        public bool IsParagraphsSelected
        {
            get => SelectedSupportView == SupportView.Paragraphs;
            set
            {
                if (value)
                {
                    SelectedSupportView = SupportView.Paragraphs;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMetaSelected));
                    OnPropertyChanged(nameof(IsTablesSelected));
                    OnPropertyChanged(nameof(IsAnalysisSelected));
                }
            }
        }

        public bool IsAnalysisSelected
        {
            get => SelectedSupportView == SupportView.Analysis;
            set
            {
                if (value)
                {
                    SelectedSupportView = SupportView.Analysis;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMetaSelected));
                    OnPropertyChanged(nameof(IsTablesSelected));
                    OnPropertyChanged(nameof(IsParagraphsSelected));
                }
            }
        }

        // Analysis properties
        public bool HasAnalysis => AnalysisVM != null; // TODO: Implement proper analysis check
        public string AnalysisQualityScore => "—"; // TODO: Implement analysis quality
        public bool HasMeta => VisibleChips?.Any() == true;

        // ==== COMMAND IMPLEMENTATIONS ====

        private void AddRequirement()
        {
            // TODO: Implement add requirement
        }

        private void RemoveSelectedRequirement()
        {
            if (SelectedRequirement != null)
            {
                // TODO: Implement remove requirement
            }
        }

        private void SelectAllTables()
        {
            foreach (var table in SelectedTableVMs)
            {
                table.IsSelected = true;
            }
        }

        private void ClearAllTables()
        {
            foreach (var table in SelectedTableVMs)
            {
                table.IsSelected = false;
            }
        }

        private void SelectAllParagraphs()
        {
            foreach (var paragraph in SelectedParagraphVMs)
            {
                paragraph.IsSelected = true;
            }
        }

        private void ClearAllParagraphs()
        {
            foreach (var paragraph in SelectedParagraphVMs)
            {
                paragraph.IsSelected = false;
            }
        }

        private void ToggleParagraph(ParagraphViewModel? paragraph)
        {
            if (paragraph != null)
            {
                paragraph.IsSelected = !paragraph.IsSelected;
            }
        }

        private void EditSupplementalInfo()
        {
            // TODO: Implement edit supplemental info
        }

        private void SelectAllVisible()
        {
            if (IsTablesSelected)
                SelectAllTables();
            else if (IsParagraphsSelected)
                SelectAllParagraphs();
        }

        private void ClearAllVisible()
        {
            if (IsTablesSelected)
                ClearAllTables();
            else if (IsParagraphsSelected)
                ClearAllParagraphs();
        }

        private bool CanSelectAllVisible()
        {
            if (IsTablesSelected)
                return SelectedTableVMs?.Any() == true;
            else if (IsParagraphsSelected)
                return SelectedParagraphVMs?.Any() == true;
            return false;
        }

        private bool CanClearAllVisible()
        {
            return CanSelectAllVisible();
        }

        /// <summary>
        /// Load requirement content (tables, paragraphs, etc.)
        /// </summary>
        private void LoadRequirementContent(Requirement? requirement)
        {
            // Clear existing content
            SelectedTableVMs.Clear();
            SelectedParagraphVMs.Clear();

            if (requirement == null) return;

            // Load tables if provider available
            if (_tableProvider != null)
            {
                var tables = _tableProvider(requirement);
                foreach (var table in tables)
                {
                    SelectedTableVMs.Add(table);
                }
            }

            // Load paragraphs if provider available
            if (_paragraphProvider != null)
            {
                var paragraphs = _paragraphProvider(requirement);
                foreach (var paragraph in paragraphs)
                {
                    SelectedParagraphVMs.Add(new ParagraphViewModel(paragraph) 
                    { 
                        IsSelected = false 
                    });
                }
            }

            // Update presence flags
            OnPropertyChanged(nameof(HasTables));
            OnPropertyChanged(nameof(HasParagraphs));
            OnPropertyChanged(nameof(BulkActionsVisible));
        }

        /// <summary>
        /// Wire up collection change notifications
        /// </summary>
        private void WirePresenceNotifications()
        {
            SelectedTableVMs.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasTables));
                OnPropertyChanged(nameof(BulkActionsVisible));
            };

            SelectedParagraphVMs.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasParagraphs));
                OnPropertyChanged(nameof(BulkActionsVisible));
            };
        }

        // ==== ABSTRACT METHOD IMPLEMENTATIONS ====

        protected override async Task SaveAsync()
        {
            // Save logic here
            await Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // Cancel any ongoing operations
        }

        protected override async Task RefreshAsync()
        {
            // Refresh logic here
            await Task.CompletedTask;
        }

        protected override bool CanSave()
        {
            return true;
        }

        protected override bool CanCancel()
        {
            return false;
        }

        protected override bool CanRefresh()
        {
            return true;
        }

        // ==== DISPOSAL ====

        public override void Dispose()
        {
            // Unsubscribe from domain events
            try
            {
                // Cast to BaseDomainMediator to access Unsubscribe methods
                if (_mediator is TestCaseEditorApp.MVVM.Utils.BaseDomainMediator<RequirementsEvents> baseMediator)
                {
                    baseMediator.Unsubscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
                    baseMediator.Unsubscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
                    baseMediator.Unsubscribe<RequirementsEvents.WorkflowStateChanged>(OnWorkflowStateChanged);
                }
                
                // Unsubscribe from cross-domain TestCaseGeneration events
                _testCaseGenerationMediator.Unsubscribe<TestCaseGenerationEvents.RequirementSelected>(OnTestCaseGenerationRequirementSelected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Requirements_MainViewModel disposal");
            }
            
            base.Dispose();
        }
    }

    /// <summary>
    /// Support view enumeration
    /// </summary>
    public enum SupportView
    {
        Meta,
        Tables, 
        Paragraphs,
        Analysis
    }
}