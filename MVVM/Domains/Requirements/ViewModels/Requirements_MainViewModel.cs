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
using EditableDataControl.ViewModels;
using RequirementsEvents = TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Helpers;
// COMPLETED: IRequirementAnalysisService moved to Requirements domain
using TestCaseEditorApp.MVVM.Domains.Requirements.Services; // IRequirementAnalysisService now in Requirements domain
using TestCaseEditorApp.MVVM.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Main ViewModel for Requirements domain - complete copy from TestCaseGenerator_VM.
    /// Provides requirement details display with chip-based UI, tables, and paragraphs.
    /// </summary>
    public partial class Requirements_MainViewModel : BaseDomainViewModel, IDisposable
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly IPersistenceService _persistence;
        private readonly ITextEditingDialogService _textEditingDialogService;

        // Optional lightweight providers
        // Direct table management - no provider pattern
        private readonly Dictionary<string, List<LooseTableViewModel>> _tableViewModelCache = new Dictionary<string, List<LooseTableViewModel>>();

        // Optional richer provider that can provide VMs directly.
        internal object? RequirementsCore { get; set; }

        // Analysis VM for LLM-powered requirement analysis (NEW: Focused architecture)
        public RequirementAnalysisViewModel? RequirementAnalysisVM { get; private set; }

        /// <summary>
        /// ViewModel for Requirements Search in Attachments feature.
        /// Resolved via DI following architectural patterns.
        /// </summary>
        public RequirementsSearchAttachmentsViewModel RequirementsSearchAttachmentsViewModel { get; }

        // Expose mediator analysis state for UI binding
        public bool IsAnalyzing => _mediator.IsAnalyzing;

        // Local state management 
        private readonly ObservableCollection<Requirement> _requirements = new();
        // REMOVED: _selectedRequirement - ViewModels should read directly from mediator.CurrentRequirement

        public Requirements_MainViewModel(
            IRequirementsMediator mediator,
            IPersistenceService persistence,
            ITextEditingDialogService textEditingDialogService,
            ILogger<Requirements_MainViewModel> logger,
            RequirementsSearchAttachmentsViewModel requirementsSearchAttachmentsViewModel,
            IRequirementAnalysisService? analysisService = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            // REMOVED: TestCaseGenerationMediator dependency - Requirements domain now independent
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _textEditingDialogService = textEditingDialogService ?? throw new ArgumentNullException(nameof(textEditingDialogService));
            RequirementsSearchAttachmentsViewModel = requirementsSearchAttachmentsViewModel ?? throw new ArgumentNullException(nameof(requirementsSearchAttachmentsViewModel));

            // Subscribe to Requirements domain events ONLY - Requirements domain independence
            _logger.LogInformation("[Requirements_MainVM] === CONSTRUCTOR: Subscribing to mediator events, mediator type: {MediatorType}, is null: {IsNull}", 
                _mediator?.GetType().Name ?? "null", _mediator == null);
            _mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            _mediator.Subscribe<RequirementsEvents.WorkflowStateChanged>(OnWorkflowStateChanged);
            _mediator.Subscribe<RequirementsEvents.RequirementUpdated>(OnRequirementUpdated);
            _mediator.Subscribe<RequirementsEvents.NavigateToAttachmentSearch>(OnNavigateToAttachmentSearch);
            _logger.LogInformation("[Requirements_MainVM] === CONSTRUCTOR: NavigateToAttachmentSearch subscription completed for instance {InstanceId} ===", GetHashCode());
            _logger.LogInformation("[Requirements_MainVM] === CONSTRUCTOR: All subscriptions complete ===");

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
            
            // Tab selection commands
            SelectDocumentScraperCommand = new RelayCommand(() => SelectedSupportView = SupportView.DocumentScraper);
            SelectAttachmentScraperCommand = new RelayCommand(() => SelectedSupportView = SupportView.AttachmentScraper);

            // Collections
            SelectedTableVMs = new ObservableCollection<LooseTableViewModel>();
            SelectedParagraphVMs = new ObservableCollection<ParagraphViewModel>();

            // Wire collection-changed handlers and initial notifications
            WirePresenceNotifications();

            // Track SelectedSupportView changes for BulkActionsVisible updates
            this.PropertyChanged += Requirements_MainViewModel_PropertyChanged;

            // Create Analysis VM with proper dependency injection (NEW: Service-based architecture)
            var analysisEngine = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisEngine>();
            if (analysisEngine != null)
            {
                var analysisLogger = App.ServiceProvider?.GetService<ILogger<RequirementAnalysisViewModel>>() ?? 
                    new LoggerFactory().CreateLogger<RequirementAnalysisViewModel>();
                
                // Include learning services for proper LLM learning functionality
                var editDetectionService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.IEditDetectionService>();
                var learningService = App.ServiceProvider?.GetService<TestCaseEditorApp.Services.ILLMLearningService>();
                
                RequirementAnalysisVM = new RequirementAnalysisViewModel(analysisEngine, analysisLogger, editDetectionService, learningService);
            }
            else
            {
                RequirementAnalysisVM = null; // Fallback when no analysis engine available
                logger.LogWarning("[Requirements_MainVM] No IRequirementAnalysisEngine service available - analysis features disabled");
            }
            
            // Initialize Requirements domain - CurrentRequirement will be set via mediator events
            // No local state initialization needed - ViewModels read directly from mediator
            
            // Initial data load
            UpdateVisibleChipsFromRequirement(SelectedRequirement);
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
            
            // ENHANCED DEBUG: Log more details
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Current mediator requirement: {_mediator.CurrentRequirement?.GlobalId ?? "NULL"}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Event requirement matches current: {ReferenceEquals(_mediator.CurrentRequirement, e.Requirement)}");
            Console.WriteLine($"*** [Requirements_MainViewModel] Current: {_mediator.CurrentRequirement?.Item ?? "NULL"}, Event: {e.Requirement?.Item ?? "NULL"}, Match: {ReferenceEquals(_mediator.CurrentRequirement, e.Requirement)} ***");
            
            // ALWAYS UPDATE - don't skip updates even if same reference
            // Save any dirty table changes before navigating away
            SaveDirtyTableChanges();
            
            // Update via SelectedRequirement setter to ensure all synchronization happens
            // This ensures RequirementAnalysisVM gets updated properly
            SelectedRequirement = e.Requirement;
            
            Console.WriteLine($"*** [Requirements_MainViewModel] About to update UI: {e.Requirement?.Item} - {e.Requirement?.Name} ***");
            
            // Load requirement content (tables and paragraphs)
            LoadRequirementContent(SelectedRequirement);
            
            // Update analysis state properties
            OnPropertyChanged(nameof(HasAnalysis));
            OnPropertyChanged(nameof(AnalysisQualityScore));
            
            Console.WriteLine($"*** [Requirements_MainViewModel] UI update complete ***");
        }

        /// <summary>
        /// Save any dirty table changes before navigating to a new requirement.
        /// </summary>
        private void SaveDirtyTableChanges()
        {
            try
            {
                var selectedRequirement = _mediator.CurrentRequirement;
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] SaveDirtyTableChanges called. SelectedReq: {selectedRequirement?.GlobalId ?? "NULL"}");
                
                if (selectedRequirement != null)
                {
                    var tables = GetLooseTableVMsForRequirement(selectedRequirement);
                    var tableList = tables?.ToList() ?? new List<LooseTableViewModel>();
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Found {tableList.Count} tables for requirement {selectedRequirement.GlobalId}");
                    
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
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Cannot save dirty tables - SelectedReq: {selectedRequirement != null}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving dirty table changes during navigation");
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "Error saving dirty table changes during navigation");
            }
        }
        
        
        /// <summary>
        /// Handle requirements collection changes
        /// </summary>
        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] OnRequirementsCollectionChanged: Action={e.Action}, NewCount={e.NewCount}");
            
            // Handle project close scenario (clear all requirements)
            if (e.Action == "Clear" && e.NewCount == 0)
            {
                Console.WriteLine("*** [Requirements_MainViewModel] Clearing workspace due to project close ***");
                TestCaseEditorApp.Services.Logging.Log.Debug("[Requirements_MainViewModel] Clearing workspace due to project close");
                
                // Clear selection and content via mediator (single source of truth)
                _mediator.CurrentRequirement = null;
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

        /// <summary>
        /// Handle requirement updates (e.g., when description is changed via "Update with Re-write" button)
        /// </summary>
        private void OnRequirementUpdated(RequirementsEvents.RequirementUpdated e)
        {
            _logger.LogInformation("[Requirements_MainVM] === OnRequirementUpdated RECEIVED === Event GlobalId: {EventGlobalId}, Current GlobalId: {CurrentGlobalId}", 
                e?.Requirement?.GlobalId ?? "null", _mediator.CurrentRequirement?.GlobalId ?? "null");

            // If the updated requirement is the currently selected one, refresh the UI
            if (e.Requirement != null && _mediator.CurrentRequirement != null && 
                e.Requirement.GlobalId == _mediator.CurrentRequirement.GlobalId)
            {
                _logger.LogInformation("[Requirements_MainVM] === GlobalIds MATCH - Refreshing requirement content ===");
                
                // Reload the requirement content to display the updated description
                LoadRequirementContent(SelectedRequirement);
                
                // Notify UI of property changes
                OnPropertyChanged(nameof(SelectedRequirement));
                
                _logger.LogInformation("[Requirements_MainVM] === Content refresh complete ===");
            }
            else
            {
                _logger.LogInformation("[Requirements_MainVM] === GlobalIds DO NOT MATCH - Skipping refresh === Event: {EventId}, Current: {CurrentId}", 
                    e?.Requirement?.GlobalId ?? "null", _mediator.CurrentRequirement?.GlobalId ?? "null");
            }
        }

        /// <summary>
        /// Handle navigation to Requirements Search in Attachments feature
        /// </summary>
        private void OnNavigateToAttachmentSearch(RequirementsEvents.NavigateToAttachmentSearch e)
        {
            try
            {
                _logger.LogInformation("[Requirements_MainVM] *** NAVIGATION EVENT RECEIVED *** Navigation to Requirements Search in Attachments requested");
                _logger.LogInformation("[Requirements_MainVM] *** Event details: TargetView={TargetView}, Timestamp={Timestamp} ***", e?.TargetView ?? "null", e?.Timestamp.ToString() ?? "null");
                _logger.LogInformation("[Requirements_MainVM] *** Current SelectedSupportView: {CurrentView} ***", SelectedSupportView);
                
                // Switch to the attachment search view using the existing support view mechanism
                SelectedSupportView = SupportView.RequirementsSearchAttachments;
                
                _logger.LogInformation("[Requirements_MainVM] *** SelectedSupportView changed to: {NewView} ***", SelectedSupportView);
                _logger.LogInformation("[Requirements_MainVM] *** Successfully switched to Requirements Search in Attachments view ***");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Requirements_MainVM] *** ERROR *** Error handling navigation to attachment search");
            }
        }

        // ==== PROPERTIES ====

        /// <summary>
        /// Collection of requirements
        /// </summary>
        public ObservableCollection<Requirement> Requirements => _requirements;

        /// <summary>
        /// Currently selected requirement - reads directly from mediator state
        /// </summary>
        public Requirement? SelectedRequirement
        {
            get => _mediator.CurrentRequirement;
            set
            {
                if (!ReferenceEquals(_mediator.CurrentRequirement, value))
                {
                    // Set mediator's CurrentRequirement - this is the single source of truth
                    _mediator.CurrentRequirement = value;
                    
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    
                    // Notify other components
                    if (value != null)
                    {
                        _mediator.PublishEvent(new RequirementsEvents.RequirementSelected 
                        { 
                            Requirement = value 
                        });
                    }
                    
                    // Update content based on new selection (simple clear/reload)
                    UpdateVisibleChipsFromRequirement(value);
                    
                    // Synchronize requirement with Analysis VM (NEW: Service-based architecture)
                    if (RequirementAnalysisVM != null)
                    {
                        _logger.LogInformation("[Requirements_MainVM] Setting RequirementAnalysisVM.CurrentRequirement to {RequirementItem}", value?.Item ?? "null");
                        RequirementAnalysisVM.CurrentRequirement = value;
                    }
                    else
                    {
                        _logger.LogWarning("[Requirements_MainVM] RequirementAnalysisVM is null! Cannot set CurrentRequirement");
                    }
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
        
        // Tab selection commands
        public ICommand SelectDocumentScraperCommand { get; }
        public ICommand SelectAttachmentScraperCommand { get; }

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
                    OnPropertyChanged(nameof(IsDocumentScraperSelected));
                    OnPropertyChanged(nameof(IsAttachmentScraperSelected));
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
                    OnPropertyChanged(nameof(IsDocumentScraperSelected));
                    OnPropertyChanged(nameof(IsAttachmentScraperSelected));
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
                    OnPropertyChanged(nameof(IsDocumentScraperSelected));
                    OnPropertyChanged(nameof(IsAttachmentScraperSelected));
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
                    OnPropertyChanged(nameof(IsDocumentScraperSelected));
                    OnPropertyChanged(nameof(IsAttachmentScraperSelected));
                }
            }
        }

        public bool IsDocumentScraperSelected
        {
            get => SelectedSupportView == SupportView.DocumentScraper;
            set
            {
                if (value)
                {
                    SelectedSupportView = SupportView.DocumentScraper;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMetaSelected));
                    OnPropertyChanged(nameof(IsTablesSelected));
                    OnPropertyChanged(nameof(IsParagraphsSelected));
                    OnPropertyChanged(nameof(IsAnalysisSelected));
                    OnPropertyChanged(nameof(IsAttachmentScraperSelected));
                }
            }
        }
        
        public bool IsAttachmentScraperSelected
        {
            get => SelectedSupportView == SupportView.AttachmentScraper;
            set
            {
                if (value)
                {
                    SelectedSupportView = SupportView.AttachmentScraper;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMetaSelected));
                    OnPropertyChanged(nameof(IsTablesSelected));
                    OnPropertyChanged(nameof(IsParagraphsSelected));
                    OnPropertyChanged(nameof(IsAnalysisSelected));
                    OnPropertyChanged(nameof(IsDocumentScraperSelected));
                }
            }
        }

        // Analysis properties
        public bool HasAnalysis => RequirementAnalysisVM?.HasAnalysis == true;
        public string AnalysisQualityScore => HasAnalysis && RequirementAnalysisVM?.QualityScore > 0 ? 
            RequirementAnalysisVM.QualityScore.ToString() : "—"; // Show original requirement score
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

            // Load tables using direct management
            try
            {
                var tables = GetLooseTableVMsForRequirement(requirement);
                foreach (var table in tables)
                {
                    SelectedTableVMs.Add(table);
                }
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Requirements_MainViewModel] Loaded {SelectedTableVMs.Count} tables for requirement {requirement.GlobalId}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[LoadRequirementContent] Table loading failed");
                // Continue loading other content even if table loading fails
            }

            // Load paragraphs using direct management
            try
            {
                var paragraphs = GetLooseParagraphsForRequirement(requirement);
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
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[LoadRequirementContent] Paragraph loading failed");
                // Continue with the rest of the method
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Requirements_MainViewModel disposal");
            }
            
            base.Dispose();
        }

        /// <summary>
        /// Get table ViewModels for requirement - direct management without provider
        /// </summary>
        private IEnumerable<LooseTableViewModel> GetLooseTableVMsForRequirement(Requirement? req)
        {
            if (req == null) return Enumerable.Empty<LooseTableViewModel>();
            
            var requirementId = req.GlobalId ?? req.Item ?? string.Empty;
            
            // Return cached instances if available
            if (_tableViewModelCache.ContainsKey(requirementId))
            {
                return _tableViewModelCache[requirementId];
            }
            
            var outList = new List<LooseTableViewModel>();
            
            // Extract tables from requirement content (similar to TestCaseGenerator logic)
            if (req.LooseContent?.Tables != null && req.LooseContent.Tables.Count > 0)
            {
                int idx = 0;
                foreach (var looseTable in req.LooseContent.Tables)
                {
                    if (looseTable == null) continue;
                    
                    // Convert LooseTable to TableDto using the standard conversion service
                    var dto = TestCaseEditorApp.Services.TableConversionService.ConvertLooseTableToDto(looseTable);
                    
                    var tableKey = !string.IsNullOrWhiteSpace(dto.Title)
                        ? $"table:{idx}:{SanitizeKey(dto.Title)}"
                        : $"table:{idx}";
                        
                    var cols = new ObservableCollection<ColumnDefinitionModel>();
                    if (dto.Columns != null)
                    {
                        for (int i = 0; i < dto.Columns.Count; i++)
                        {
                            var header = dto.Columns[i] ?? $"Column {i + 1}";
                            var bp = string.IsNullOrWhiteSpace(dto.Columns[i]) ? $"c{i}" : dto.Columns[i];
                            cols.Add(new ColumnDefinitionModel { Header = header, BindingPath = bp });
                        }
                    }
                    
                    var rows = new ObservableCollection<TableRowModel>();
                    if (dto.Rows != null)
                    {
                        foreach (var rowData in dto.Rows)
                        {
                            var row = new TableRowModel();
                            if (rowData != null)
                            {
                                for (int colIdx = 0; colIdx < cols.Count && colIdx < rowData.Count; colIdx++)
                                {
                                    row[cols[colIdx].BindingPath ?? $"Column_{colIdx}"] = rowData[colIdx] ?? string.Empty;
                                }
                            }
                            rows.Add(row);
                        }
                    }
                    
                    var ltv = new LooseTableViewModel(requirementId, tableKey, dto.Title ?? $"Table {idx + 1}", cols, rows);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[REQUIREMENTS DOMAIN] Created table '{ltv.Title}' - IsEditing property removed, forcing read-only mode");
                    // Removed IsEditing assignment - tables will default to read-only mode
                    outList.Add(ltv);
                    idx++;
                }
                
                if (outList.Count > 0)
                {
                    // Cache the instances for reuse
                    _tableViewModelCache[requirementId] = outList;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[GetLooseTableVMsForRequirement] Created and cached {outList.Count} tables for requirement: {requirementId}");
                }
            }
            
            return outList;
        }
        
        /// <summary>
        /// Get paragraph content for requirement - direct management without provider  
        /// </summary>
        private IEnumerable<string> GetLooseParagraphsForRequirement(Requirement? req)
        {
            if (req?.LooseContent?.Paragraphs == null) return Enumerable.Empty<string>();
            return req.LooseContent.Paragraphs.Where(p => !string.IsNullOrWhiteSpace(p));
        }
        
        /// <summary>
        /// Sanitize key for table identification
        /// </summary>
        private static string SanitizeKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(input.Trim().ToLowerInvariant(), @"[^\w\-_]", "_");
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
        Analysis,
        RichContent,
        Metadata,
        RequirementsSearchAttachments,
        DocumentScraper,
        AttachmentScraper
    }
}