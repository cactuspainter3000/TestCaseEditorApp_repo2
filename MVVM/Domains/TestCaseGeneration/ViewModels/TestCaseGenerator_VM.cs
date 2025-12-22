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
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel for the Requirements support pane (Meta / Tables / Paragraphs).
    /// Refactored to use domain mediator instead of bridge interface.
    /// </summary>
    public partial class TestCaseGenerator_VM : BaseDomainViewModel, IDisposable
    {
        private new readonly ITestCaseGenerationMediator _mediator;
        private readonly IPersistenceService _persistence;

        // Optional lightweight providers
        private readonly Func<Requirement?, IEnumerable<LooseTableViewModel>>? _tableProvider;
        private readonly Func<Requirement?, IEnumerable<string>>? _paragraphProvider;

        // Optional richer provider that can provide VMs directly.
        internal TestCaseGenerator_CoreVM? TestCaseGenerator { get; set; }

        // Analysis VM for LLM-powered requirement analysis
        public TestCaseGenerator_AnalysisVM? AnalysisVM { get; private set; }

        // Local state management (replacing navigator dependencies)
        private readonly ObservableCollection<Requirement> _requirements = new();
        private Requirement? _selectedRequirement;

        public TestCaseGenerator_VM(
            ITestCaseGenerationMediator mediator,
            IPersistenceService persistence,
            ILogger<TestCaseGenerator_VM> logger,
            Func<Requirement?, IEnumerable<LooseTableViewModel>>? tableProvider = null,
            Func<Requirement?, IEnumerable<string>>? paragraphProvider = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _tableProvider = tableProvider;
            _paragraphProvider = paragraphProvider;

            // Subscribe to domain events
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);

            // Commands
            AddRequirementCommand = new RelayCommand(AddRequirement);
            RemoveRequirementCommand = new RelayCommand(RemoveSelectedRequirement, () => SelectedRequirement != null);

            // per-type commands (still available if child controls want to bind directly)
            SelectAllTablesCommand = new RelayCommand(SelectAllTables, () => SelectedTableVMs?.Any() == true);
            ClearAllTablesCommand = new RelayCommand(ClearAllTables, () => SelectedTableVMs?.Any() == true);
            SelectAllParagraphsCommand = new RelayCommand(SelectAllParagraphs, () => SelectedParagraphVMs?.Any() == true);
            ClearAllParagraphsCommand = new RelayCommand(ClearAllParagraphs, () => SelectedParagraphVMs?.Any() == true);
            ToggleParagraphCommand = new RelayCommand<ParagraphViewModel>(ToggleParagraph);
            EditSupplementalInfoCommand = new RelayCommand(EditSupplementalInfo, () => SelectedParagraphVMs?.Any() == true);

            // routed parent-level commands (act on visible view)
            SelectAllVisibleCommand = new RelayCommand(SelectAllVisible, CanSelectAllVisible);
            ClearAllVisibleCommand = new RelayCommand(ClearAllVisible, CanClearAllVisible);

            // Collections
            SelectedTableVMs = new ObservableCollection<LooseTableViewModel>();
            SelectedParagraphVMs = new ObservableCollection<ParagraphViewModel>();

            // Wire collection-changed handlers and initial notifications
            WirePresenceNotifications();

            // Create Analysis VM (will create its own LLM service via LlmFactory if needed)
            var analysisLogger = new LoggerFactory().CreateLogger<TestCaseGenerator_AnalysisVM>();
            AnalysisVM = new TestCaseGenerator_AnalysisVM(_mediator, analysisLogger, llmService: null);

            // Track SelectedSupportView changes via PropertyChanged so we don't rely on a generated partial hook
            this.PropertyChanged += TestCaseGenerator_VM_PropertyChanged;

            // Initial population/state
            RefreshSupportContent();
            UpdateVisibleChipsFromRequirement(SelectedRequirement);
            
            Title = "Requirements Support";
            _logger.LogDebug("TestCaseGenerator_VM created with domain mediator");
        }

        private void TestCaseGenerator_VM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == nameof(SelectedSupportView))
            {
                // Update routed command availability and visibility
                try
                {
                    ((RelayCommand)SelectAllVisibleCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)ClearAllVisibleCommand).NotifyCanExecuteChanged();
                }
                catch { /* ignore if not RelayCommand */ }

                OnPropertyChanged(nameof(BulkActionsVisible));
                
                // When switching to Analysis view, ensure AnalysisVM refreshes its display
                if (SelectedSupportView == SupportView.Analysis)
                {
                    // Use reflection to call the private RefreshAnalysisDisplay method
                    var analysis = AnalysisVM;
                    if (analysis != null)
                    {
                        var method = analysis.GetType().GetMethod("RefreshAnalysisDisplay",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        method?.Invoke(analysis, null);
                    }
                }
            }
        }

        // ===== DOMAIN EVENT HANDLERS =====

        /// <summary>
        /// Handle requirement selection from mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            if (!ReferenceEquals(_selectedRequirement, e.Requirement))
            {
                _selectedRequirement = e.Requirement;
                OnPropertyChanged(nameof(SelectedRequirement));
                RefreshSupportContent();
                UpdateVisibleChipsFromRequirement(_selectedRequirement);
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
                
                try { ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged(); } catch { }
            }
        }

        /// <summary>
        /// Handle requirements collection changes from mediator
        /// </summary>
        private void OnRequirementsCollectionChanged(TestCaseGenerationEvents.RequirementsCollectionChanged e)
        {
            // Update local requirements collection
            _requirements.Clear();
            foreach (var req in e.AffectedRequirements)
            {
                _requirements.Add(req);
            }
            
            OnPropertyChanged(nameof(Requirements));
            try { ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged(); } catch { }
        }

        // ===== PROPERTIES =====

        /// <summary>
        /// Requirements collection from mediator state
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
                    
                    // Notify mediator of selection
                    if (value != null)
                    {
                        _mediator.SelectRequirement(value);
                    }
                    
                    OnPropertyChanged();
                    try { ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged(); } catch { }
                    RefreshSupportContent();
                    UpdateVisibleChipsFromRequirement(value);
                }
            }
        }

        /// <summary>
        /// Navigation commands - TODO: Implement via mediator when available
        /// For now, return null to avoid bridge dependencies
        /// </summary>
        public ICommand? PreviousRequirementCommand => null; // TODO: Implement via mediator
        public ICommand? NextRequirementCommand => null; // TODO: Implement via mediator  
        public ICommand? NextWithoutTestCaseCommand => null; // TODO: Implement via mediator

        /// <summary>
        /// Requirement position display - TODO: Implement via mediator state
        /// </summary>
        public string RequirementPositionDisplay => "— / —"; // TODO: Implement from mediator state

        /// <summary>
        /// Wrap on next without test case - local state for now
        /// </summary>
        public bool WrapOnNextWithoutTestCase { get; set; } = false;

        // ===== REQUIREMENT MANAGEMENT COMMANDS =====

        public ICommand AddRequirementCommand { get; }
        public ICommand RemoveRequirementCommand { get; }

        private void AddRequirement()
        {
            var n = new Requirement
            {
                Item = $"AUTOGEN-{Guid.NewGuid():N}".Substring(0, 12),
                Name = "New requirement",
                Description = string.Empty
            };
            Requirements.Add(n);
            SelectedRequirement = n;
            
            // TODO: Notify mediator of new requirement when API is available
        }

        private void RemoveSelectedRequirement()
        {
            if (SelectedRequirement != null)
            {
                Requirements.Remove(SelectedRequirement);
                SelectedRequirement = Requirements.Count > 0 ? Requirements[0] : null;
                
                // TODO: Notify mediator of requirement removal when API is available
            }
        }

        // ===== SUPPORT VIEW COMMANDS =====

        public ICommand SelectAllTablesCommand { get; }
        public ICommand ClearAllTablesCommand { get; }
        public ICommand SelectAllParagraphsCommand { get; }
        public ICommand ClearAllParagraphsCommand { get; }
        public ICommand ToggleParagraphCommand { get; }
        public ICommand EditSupplementalInfoCommand { get; }
        public ICommand SelectAllVisibleCommand { get; }
        public ICommand ClearAllVisibleCommand { get; }

        // Command implementations - unchanged from original
        private void SelectAllTables()
        {
            foreach (var table in SelectedTableVMs ?? Enumerable.Empty<LooseTableViewModel>())
                table.IsSelected = true;
        }

        private void ClearAllTables()
        {
            foreach (var table in SelectedTableVMs ?? Enumerable.Empty<LooseTableViewModel>())
                table.IsSelected = false;
        }

        private void SelectAllParagraphs()
        {
            foreach (var para in SelectedParagraphVMs ?? Enumerable.Empty<ParagraphViewModel>())
                para.IsSelected = true;
        }

        private void ClearAllParagraphs()
        {
            foreach (var para in SelectedParagraphVMs ?? Enumerable.Empty<ParagraphViewModel>())
                para.IsSelected = false;
        }

        private void ToggleParagraph(ParagraphViewModel? para)
        {
            if (para != null) para.IsSelected = !para.IsSelected;
        }

        private void EditSupplementalInfo()
        {
            // TODO: Implement supplemental info editing via mediator
            _logger.LogDebug("EditSupplementalInfo requested - not yet implemented");
        }

        private void SelectAllVisible()
        {
            if (SelectedSupportView == SupportView.Tables)
                SelectAllTables();
            else if (SelectedSupportView == SupportView.Paragraphs)
                SelectAllParagraphs();
        }

        private void ClearAllVisible()
        {
            if (SelectedSupportView == SupportView.Tables)
                ClearAllTables();
            else if (SelectedSupportView == SupportView.Paragraphs)
                ClearAllParagraphs();
        }

        private bool CanSelectAllVisible()
        {
            return (SelectedSupportView == SupportView.Tables && HasTables)
                || (SelectedSupportView == SupportView.Paragraphs && HasParagraphs);
        }

        private bool CanClearAllVisible() => CanSelectAllVisible();

        // ===== SUPPORT CONTENT MANAGEMENT =====

        public ObservableCollection<LooseTableViewModel> SelectedTableVMs { get; } = new();
        public ObservableCollection<ParagraphViewModel> SelectedParagraphVMs { get; } = new();

        private void RefreshSupportContent()
        {
            SelectedTableVMs.Clear();
            SelectedParagraphVMs.Clear();

            if (SelectedRequirement == null)
                return;

            if (_tableProvider != null)
            {
                try
                {
                    var tables = _tableProvider.Invoke(SelectedRequirement) ?? Enumerable.Empty<LooseTableViewModel>();
                    foreach (var t in tables) SelectedTableVMs.Add(t);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "tableProvider failed for requirement {RequirementId}", SelectedRequirement.GlobalId);
                }
            }

            if (_paragraphProvider != null)
            {
                try
                {
                    var paras = _paragraphProvider.Invoke(SelectedRequirement) ?? Enumerable.Empty<string>();
                    foreach (var p in paras) SelectedParagraphVMs.Add(new ParagraphViewModel(p));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "paragraphProvider failed for requirement {RequirementId}", SelectedRequirement.GlobalId);
                }
            }

            // Fallback to TestCaseGenerator if present
            RefreshSupportContentFromProvider();
        }

        private void RefreshSupportContentFromProvider()
        {
            try
            {
                var provider = this.TestCaseGenerator;
                if (provider == null) return;

                IEnumerable<LooseTableViewModel> tables = Enumerable.Empty<LooseTableViewModel>();
                try { tables = provider.GetLooseTableVMsForRequirement(SelectedRequirement) ?? Enumerable.Empty<LooseTableViewModel>(); }
                catch (Exception ex) { _logger.LogDebug(ex, "provider.GetLooseTableVMsForRequirement failed"); }

                SelectedTableVMs.Clear();
                foreach (var t in tables) SelectedTableVMs.Add(t);

                IEnumerable<string> paras = Enumerable.Empty<string>();
                try { paras = provider.GetLooseParagraphsForRequirement(SelectedRequirement) ?? Enumerable.Empty<string>(); }
                catch (Exception ex) { _logger.LogDebug(ex, "provider.GetLooseParagraphsForRequirement failed"); }

                SelectedParagraphVMs.Clear();
                foreach (var p in paras) SelectedParagraphVMs.Add(new ParagraphViewModel(p));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RefreshSupportContentFromProvider unexpected error");
            }
        }

        // ===== PRESENCE HELPERS =====

        public bool HasTables => SelectedTableVMs?.Any() == true;
        public bool HasParagraphs => SelectedParagraphVMs?.Any() == true;

        // Bulk action visibility (parent toolbar shows actions only when applicable)
        public bool BulkActionsVisible => CanSelectAllVisible();

        private void WirePresenceNotifications()
        {
            if (SelectedTableVMs == null) throw new InvalidOperationException("SelectedTableVMs must be initialized before wiring presence notifications.");
            if (SelectedParagraphVMs == null) throw new InvalidOperationException("SelectedParagraphVMs must be initialized before wiring presence notifications.");

            SelectedTableVMs.CollectionChanged -= SelectedTableVMs_CollectionChanged;
            SelectedTableVMs.CollectionChanged += SelectedTableVMs_CollectionChanged;

            SelectedParagraphVMs.CollectionChanged -= SelectedParagraphVMs_CollectionChanged;
            SelectedParagraphVMs.CollectionChanged += SelectedParagraphVMs_CollectionChanged;

            OnPropertyChanged(nameof(HasTables));
            OnPropertyChanged(nameof(HasParagraphs));
            OnPropertyChanged(nameof(BulkActionsVisible));

            try
            {
                ((RelayCommand)SelectAllTablesCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllTablesCommand).NotifyCanExecuteChanged();
            }
            catch { }

            try
            {
                ((RelayCommand)SelectAllVisibleCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllVisibleCommand).NotifyCanExecuteChanged();
            }
            catch { }
        }

        private void SelectedTableVMs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasTables));
            OnPropertyChanged(nameof(BulkActionsVisible));
            try
            {
                ((RelayCommand)SelectAllTablesCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllTablesCommand).NotifyCanExecuteChanged();
            }
            catch { }

            try
            {
                ((RelayCommand)SelectAllVisibleCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllVisibleCommand).NotifyCanExecuteChanged();
            }
            catch { }
        }

        private void SelectedParagraphVMs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasParagraphs));
            OnPropertyChanged(nameof(BulkActionsVisible));
            try
            {
                ((RelayCommand)SelectAllVisibleCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearAllVisibleCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditSupplementalInfoCommand).NotifyCanExecuteChanged();
            }
            catch { }
        }

        // ===== META CHIPS =====

        public class ChipViewModel
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public bool IsCore { get; set; } = false;
            public int DisplayOrder { get; set; } = 999; // For stable positioning
        }

        private ObservableCollection<ChipViewModel> _visibleChips = new();
        public ObservableCollection<ChipViewModel> VisibleChips
        {
            get => _visibleChips;
            set
            {
                _visibleChips = value ?? new ObservableCollection<ChipViewModel>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(OptionalChips));
            }
        }

        // Non-core chips only (for the pills grid)
        public ObservableCollection<ChipViewModel> OptionalChips
        {
            get => new ObservableCollection<ChipViewModel>(VisibleChips.Where(c => !c.IsCore));
        }

        private ObservableCollection<ChipViewModel> _dateChips = new();
        public ObservableCollection<ChipViewModel> DateChips
        {
            get => _dateChips;
            set
            {
                _dateChips = value ?? new ObservableCollection<ChipViewModel>();
                OnPropertyChanged();
            }
        }

        private void UpdateVisibleChipsFromRequirement(Requirement? r)
        {
            var list = new ObservableCollection<ChipViewModel>();
            if (r != null)
            {
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

            VisibleChips = list;
        }

        // ===== SUPPORT VIEW SELECTION =====

        [ObservableProperty]
        private SupportView selectedSupportView = SupportView.Meta;

        public bool IsMetaSelected
        {
            get => SelectedSupportView == SupportView.Meta;
            set
            {
                if (value) SelectedSupportView = SupportView.Meta;
                OnPropertyChanged(nameof(IsMetaSelected));
                OnPropertyChanged(nameof(IsTablesSelected));
                OnPropertyChanged(nameof(IsParagraphsSelected));
                OnPropertyChanged(nameof(IsAnalysisSelected));
            }
        }

        public bool IsTablesSelected
        {
            get => SelectedSupportView == SupportView.Tables;
            set
            {
                if (value) SelectedSupportView = SupportView.Tables;
                OnPropertyChanged(nameof(IsMetaSelected));
                OnPropertyChanged(nameof(IsTablesSelected));
                OnPropertyChanged(nameof(IsParagraphsSelected));
                OnPropertyChanged(nameof(IsAnalysisSelected));
            }
        }

        public bool IsParagraphsSelected
        {
            get => SelectedSupportView == SupportView.Paragraphs;
            set
            {
                if (value) SelectedSupportView = SupportView.Paragraphs;
                OnPropertyChanged(nameof(IsMetaSelected));
                OnPropertyChanged(nameof(IsTablesSelected));
                OnPropertyChanged(nameof(IsParagraphsSelected));
                OnPropertyChanged(nameof(IsAnalysisSelected));
            }
        }

        public bool IsAnalysisSelected
        {
            get => SelectedSupportView == SupportView.Analysis;
            set
            {
                if (value) SelectedSupportView = SupportView.Analysis;
                OnPropertyChanged(nameof(IsMetaSelected));
                OnPropertyChanged(nameof(IsTablesSelected));
                OnPropertyChanged(nameof(IsParagraphsSelected));
                OnPropertyChanged(nameof(IsAnalysisSelected));
            }
        }

        // ===== ANALYSIS SUPPORT =====

        /// <summary>
        /// Whether current requirement has analysis data
        /// </summary>
        public bool HasAnalysis => SelectedRequirement?.Analysis != null;

        /// <summary>
        /// Analysis quality score for current requirement
        /// </summary>
        public double AnalysisQualityScore => SelectedRequirement?.Analysis?.QualityScore ?? 0.0;

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override bool CanSave() => false; // Support VM doesn't save directly
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => true;
        protected override async Task RefreshAsync()
        {
            _logger.LogDebug("Refreshing support content");
            RefreshSupportContent();
            await Task.CompletedTask;
        }
        protected override bool CanCancel() => false;
        protected override void Cancel() { /* No-op */ }

        // ===== DISPOSE =====

        public new void Dispose()
        {
            // Unsubscribe from mediator events
            try
            {
                _mediator.Unsubscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
                _mediator.Unsubscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from mediator events during dispose");
            }

            try { SelectedTableVMs.CollectionChanged -= SelectedTableVMs_CollectionChanged; } catch { }
            try { SelectedParagraphVMs.CollectionChanged -= SelectedParagraphVMs_CollectionChanged; } catch { }
            try { this.PropertyChanged -= TestCaseGenerator_VM_PropertyChanged; } catch { }

            base.Dispose();
        }
    }

    public enum SupportView
    {
        Meta,
        Tables,
        Paragraphs,
        Analysis
    }
}