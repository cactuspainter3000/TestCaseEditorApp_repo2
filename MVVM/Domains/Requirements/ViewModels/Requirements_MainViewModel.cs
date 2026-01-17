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
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnTestCaseGenerationRequirementsCollectionChanged);

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

            // Track SelectedSupportView changes for BulkActionsVisible updates
            this.PropertyChanged += Requirements_MainViewModel_PropertyChanged;

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
                    
                    // **CRITICAL**: Update mediator's CurrentRequirement to match initial selection
                    _mediator.CurrentRequirement = currentRequirement;
                    
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
        /// Save any pending table edits back to the requirement before switching requirements
        /// </summary>
        /// <summary>
        /// Handle requirement selection events from domain mediator
        /// </summary>
        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] OnRequirementSelected called with: {e.Requirement?.GlobalId ?? "NULL"}");
            Console.WriteLine($"*** [Requirements_MainViewModel] OnRequirementSelected: {e.Requirement?.GlobalId ?? "NULL"} ***");
            if (!ReferenceEquals(_selectedRequirement, e.Requirement))
            {
                // Save any dirty table changes before navigating away
                SaveDirtyTableChanges();
                
                _selectedRequirement = e.Requirement;
                OnPropertyChanged(nameof(SelectedRequirement));
                
                // **CRITICAL**: Update mediator's CurrentRequirement to match local selection
                _mediator.CurrentRequirement = e.Requirement;
                
                // Re-populate chips from requirement data
                UpdateVisibleChipsFromRequirement(_selectedRequirement);
                
                // Update analysis state
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
                
                // Load requirement content (simple clear/reload like TestCaseGenerator)
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
            OnRequirementSelected(new RequirementsEvents.RequirementSelected { Requirement = e.Requirement });
        }
        
        /// <summary>
        /// Save any dirty table changes before navigating to a new requirement.
        /// </summary>
        private void SaveDirtyTableChanges()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] SaveDirtyTableChanges called. TableProvider: {_tableProvider != null}, SelectedReq: {_selectedRequirement?.GlobalId ?? "NULL"}");
                
                if (_tableProvider != null && _selectedRequirement != null)
                {
                    var tables = _tableProvider(_selectedRequirement);
                    var tableList = tables?.ToList() ?? new List<LooseTableViewModel>();
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Found {tableList.Count} tables for requirement {_selectedRequirement.GlobalId}");
                    
                    var dirtyTables = tableList.OfType<LooseTableViewModel>().Where(t => t.IsDirty).ToList();
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Found {dirtyTables.Count} dirty tables");
                    
                    foreach (var table in dirtyTables)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Saving dirty table: {table.Title} (ID: {table.RequirementId})");
                        table.SaveToSourceRequirement();
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Cannot save dirty tables - TableProvider: {_tableProvider != null}, SelectedReq: {_selectedRequirement != null}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving dirty table changes during navigation");
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Error saving dirty table changes during navigation");
            }
        }
        
        
        /// <summary>
        /// Handle requirements collection changes from TestCaseGeneration mediator (for project close events)
        /// </summary>
        private void OnTestCaseGenerationRequirementsCollectionChanged(TestCaseGenerationEvents.RequirementsCollectionChanged e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] OnTestCaseGenerationRequirementsCollectionChanged: Action={e.Action}, NewCount={e.NewCount}");
            Console.WriteLine($"*** [Requirements_MainViewModel] TestCaseGeneration collection changed: {e.Action}, Count={e.NewCount} ***");
            
            // Clear the main workspace when requirements are cleared
            if (e.Action == "Clear" && e.NewCount == 0)
            {
                Console.WriteLine("*** [Requirements_MainViewModel] Clearing workspace due to project close ***");
                TestCaseEditorApp.Services.Logging.Log.Debug("[Requirements_MainViewModel] Clearing workspace due to project close");
                
                // Clear selection and content (simple clear like TestCaseGenerator)
                _selectedRequirement = null;
                OnPropertyChanged(nameof(SelectedRequirement));
                
                // Generate empty chips showing "(not set)" for all fields instead of clearing completely
                UpdateVisibleChipsFromRequirement(null);
                
                // Reset support view to default (Meta tab)
                SelectedSupportView = SupportView.Meta;
                
                // Clear all table and paragraph content
                SelectedTableVMs.Clear();
                
                // Unwire PropertyChanged events before clearing
                foreach (var para in SelectedParagraphVMs)
                {
                    para.PropertyChanged -= ParagraphViewModel_PropertyChanged;
                }
                SelectedParagraphVMs.Clear();
                OnPropertyChanged(nameof(HasTables));
                OnPropertyChanged(nameof(HasParagraphs));
                
                // Clear content (this will handle the empty case properly)
                LoadRequirementContent(null);
                
                // Force refresh of all property bindings to ensure meta data is cleared
                OnPropertyChanged(nameof(IsMetaSelected));
                OnPropertyChanged(nameof(IsTablesSelected));
                OnPropertyChanged(nameof(IsParagraphsSelected));
                OnPropertyChanged(nameof(IsAnalysisSelected));
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
                
                // Force update of any computed properties that might be cached
                OnPropertyChanged(nameof(Requirements));
            }
            
            // Convert to Requirements domain event and handle normally
            var requirementsEvent = new RequirementsEvents.RequirementsCollectionChanged
            {
                Action = e.Action,
                AffectedRequirements = e.AffectedRequirements,
                NewCount = e.NewCount
            };
            OnRequirementsCollectionChanged(requirementsEvent);
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
                    
                    // **CRITICAL**: Update mediator's CurrentRequirement to match local selection
                    _mediator.CurrentRequirement = value;
                    
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
                    
                    // Update content based on new selection (simple clear/reload)
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
            if (r != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Requirement details - GlobalId: {r.GlobalId}, Type: {r.RequirementType}, Status: {r.Status}, Project: {r.Project}");
            }
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
                // When requirement is null, still show all fields with "(not set)" values
                TestCaseEditorApp.Services.Logging.Log.Debug("[Requirements_MainViewModel] Creating empty chips for cleared requirement");
                int orderCounter = 10;
                
                // Helper: add empty field
                void AddEmpty(string label, bool isCore = false)
                {
                    list.Add(new ChipViewModel 
                    { 
                        Label = label, 
                        Value = "(not set)",
                        IsCore = isCore,
                        DisplayOrder = orderCounter++
                    });
                }

                // === Same field structure as when requirement is loaded ===
                AddEmpty("Global ID", isCore: true);
                AddEmpty("Type", isCore: true);
                AddEmpty("Status", isCore: true);
                AddEmpty("Version", isCore: true);

                // === Optional fields ===
                AddEmpty("Safety");
                AddEmpty("Security");
                AddEmpty("Key Characteristics");
                AddEmpty("FDAL");
                AddEmpty("Derived");
                AddEmpty("Export Controlled");
                AddEmpty("Project");
                AddEmpty("Set");
                AddEmpty("Heading");
                AddEmpty("API ID");
                AddEmpty("Change Driver");
                AddEmpty("Created By");
                AddEmpty("Modified By");
                AddEmpty("Locked By");
                AddEmpty("Upstream Links");
                AddEmpty("Downstream Links");
                AddEmpty("Comments");
                AddEmpty("Attachments");
                
                // Clear date chips as well
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

        private async void EditSupplementalInfo()
        {
            try
            {
                if (SelectedRequirement == null || SelectedParagraphVMs?.Any(p => p.IsSelected) != true)
                {
                    return;
                }

                var selectedParagraphs = SelectedParagraphVMs
                    .Where(p => p.IsSelected)
                    .Select(p => p.Text)
                    .ToList();

                var existingText = string.Join(" ||| ", selectedParagraphs);

                var editedText = await _textEditingDialogService.ShowSupplementalInfoEditDialog(
                    "Edit Supplemental Information",
                    existingText);

                if (editedText != null && !string.IsNullOrWhiteSpace(editedText))
                {
                    var newParagraphs = editedText
                        .Split(new[] { " ||| " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();

                    // Update the LooseContent with the new paragraphs
                    if (SelectedRequirement.LooseContent == null)
                    {
                        SelectedRequirement.LooseContent = new RequirementLooseContent
                        {
                            Paragraphs = new List<string>()
                        };
                    }
                    else if (SelectedRequirement.LooseContent.Paragraphs == null)
                    {
                        SelectedRequirement.LooseContent.Paragraphs = new List<string>();
                    }

                    // Remove the old selected paragraphs from the collection
                    var paragraphsToRemove = SelectedParagraphVMs
                        .Where(p => p.IsSelected)
                        .Select(p => p.Text)
                        .ToList();

                    var paragraphList = SelectedRequirement.LooseContent.Paragraphs.ToList();
                    foreach (var paragraphToRemove in paragraphsToRemove)
                    {
                        paragraphList.Remove(paragraphToRemove);
                    }

                    // Add the new paragraphs
                    paragraphList.AddRange(newParagraphs);

                    // Update the collection
                    SelectedRequirement.LooseContent.Paragraphs = paragraphList;

                    // Refresh the UI
                    LoadRequirementContent(SelectedRequirement);

                    _logger.LogInformation("Successfully updated supplemental information for requirement {Id}. " +
                                         "Edited {OldCount} paragraphs into {NewCount} paragraphs.",
                                         SelectedRequirement?.GlobalId ?? "Unknown",
                                         selectedParagraphs.Count,
                                         newParagraphs.Count);

                    // Mark the requirement as modified
                    OnPropertyChanged(nameof(SelectedRequirement));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error editing supplemental information for requirement {Id}",
                               SelectedRequirement?.GlobalId ?? "Unknown");
            }
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
            return (IsTablesSelected && HasTables)
                || (IsParagraphsSelected && HasParagraphs);
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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] LoadRequirementContent called for: {requirement?.GlobalId ?? "NULL"}");
            
            // Clear existing content
            SelectedTableVMs.Clear();
            
            // Unwire PropertyChanged events before clearing
            foreach (var para in SelectedParagraphVMs)
            {
                para.PropertyChanged -= ParagraphViewModel_PropertyChanged;
            }
            SelectedParagraphVMs.Clear();

            if (requirement == null) return;

            // Load tables if provider available
            if (_tableProvider != null)
            {
                try
                {
                    var tables = _tableProvider(requirement);
                    foreach (var table in tables)
                    {
                        SelectedTableVMs.Add(table);
                    }
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Loaded {SelectedTableVMs.Count} tables for requirement {requirement.GlobalId}");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[LoadRequirementContent] Table provider failed");
                    // Continue loading other content even if table loading fails
                }
            }

            // Load paragraphs if provider available
            if (_paragraphProvider != null)
            {
                try
                {
                    var paragraphs = _paragraphProvider(requirement);
                    foreach (var paragraph in paragraphs)
                    {
                        var paraVM = new ParagraphViewModel(paragraph) 
                        { 
                            IsSelected = false 
                        };
                        paraVM.PropertyChanged += ParagraphViewModel_PropertyChanged;
                        SelectedParagraphVMs.Add(paraVM);
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[LoadRequirementContent] Paragraph provider failed");
                    // Continue with the rest of the method
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

        /// <summary>
        /// Handle SelectedSupportView changes for BulkActionsVisible updates
        /// </summary>
        private void Requirements_MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == nameof(SelectedSupportView))
            {
                // Update routed command availability and visibility
                try
                {
                    ((RelayCommand)SelectAllVisibleCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)ClearAllVisibleCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)EditSupplementalInfoCommand).NotifyCanExecuteChanged();
                }
                catch { /* ignore if not RelayCommand */ }

                OnPropertyChanged(nameof(BulkActionsVisible));
            }
        }

        /// <summary>
        /// Handle ParagraphViewModel property changes for command CanExecute updates
        /// </summary>
        private void ParagraphViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParagraphViewModel.IsSelected))
            {
                try
                {
                    ((RelayCommand)EditSupplementalInfoCommand).NotifyCanExecuteChanged();
                }
                catch { }
            }
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
                _testCaseGenerationMediator.Unsubscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnTestCaseGenerationRequirementsCollectionChanged);
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