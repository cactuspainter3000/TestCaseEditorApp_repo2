using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the Requirements support pane (Meta / Tables / Paragraphs).
    /// Single-file, self-contained implementation to replace the previous fragmented versions.
    /// </summary>
    public partial class TestCaseGenerator_VM : ObservableObject, IDisposable
    {
        private readonly IPersistenceService _persistence;
        private readonly ITestCaseGenerator_Navigator _navigator;

        // Optional lightweight providers
        private readonly Func<Requirement?, IEnumerable<LooseTableViewModel>>? _tableProvider;
        private readonly Func<Requirement?, IEnumerable<string>>? _paragraphProvider;

        // Optional richer provider that can provide VMs directly.
        internal TestCaseGenerator_CoreVM? TestCaseGenerator { get; set; }

        // Analysis VM for LLM-powered requirement analysis
        public TestCaseGenerator_AnalysisVM? AnalysisVM { get; private set; }

        public TestCaseGenerator_VM(
            IPersistenceService persistence,
            ITestCaseGenerator_Navigator navigator,
            Func<Requirement?, IEnumerable<LooseTableViewModel>>? tableProvider = null,
            Func<Requirement?, IEnumerable<string>>? paragraphProvider = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _tableProvider = tableProvider;
            _paragraphProvider = paragraphProvider;

            // Subscribe to navigator change events
            _navigator.Requirements.CollectionChanged += Requirements_CollectionChanged;
            _navigator.PropertyChanged += Navigator_PropertyChanged;

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
            AnalysisVM = new TestCaseGenerator_AnalysisVM(_navigator, llmService: null);

            // Track SelectedSupportView changes via PropertyChanged so we don't rely on a generated partial hook
            this.PropertyChanged += TestCaseGenerator_VM_PropertyChanged;

            // Initial population/state
            RefreshSupportContent();
            UpdateVisibleChipsFromRequirement(SelectedRequirement);
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
                    var method = AnalysisVM.GetType().GetMethod("RefreshAnalysisDisplay", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(AnalysisVM, null);
                }
            }
        }

        // ---------------- Navigator / selection ----------------

        private void Navigator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_navigator.CurrentRequirement))
            {
                OnPropertyChanged(nameof(SelectedRequirement));
                RefreshSupportContent();
                UpdateVisibleChipsFromRequirement(SelectedRequirement);
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
            }
            else if (e.PropertyName == nameof(_navigator.RequirementPositionDisplay))
            {
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            }
            else if (e.PropertyName == nameof(_navigator.Requirements))
            {
                OnPropertyChanged(nameof(Requirements));
            }
        }

        private void Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Requirements));
            try { ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged(); } catch { }
        }

        public ObservableCollection<Requirement> Requirements => _navigator.Requirements;

        public Requirement? SelectedRequirement
        {
            get => _navigator.CurrentRequirement;
            set
            {
                if (_navigator.CurrentRequirement != value)
                {
                    _navigator.CurrentRequirement = value;
                    OnPropertyChanged();

                    try { ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged(); } catch { }
                    RefreshSupportContent();
                    UpdateVisibleChipsFromRequirement(_navigator.CurrentRequirement);
                }
            }
        }

        public ICommand? PreviousRequirementCommand => _navigator.PreviousRequirementCommand;
        public ICommand? NextRequirementCommand => _navigator.NextRequirementCommand;
        public ICommand? NextWithoutTestCaseCommand => _navigator.NextWithoutTestCaseCommand;

        public string RequirementPositionDisplay => _navigator.RequirementPositionDisplay;

        public bool WrapOnNextWithoutTestCase
        {
            get => _navigator.WrapOnNextWithoutTestCase;
            set
            {
                if (_navigator.WrapOnNextWithoutTestCase != value)
                {
                    _navigator.WrapOnNextWithoutTestCase = value;
                    OnPropertyChanged();
                }
            }
        }

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
        }

        private void RemoveSelectedRequirement()
        {
            if (SelectedRequirement != null)
            {
                Requirements.Remove(SelectedRequirement);
                SelectedRequirement = Requirements.Count > 0 ? Requirements[0] : null;
            }
        }

        // ---------------- Support pane surface ----------------

        // Loose table VMs scoped to the SelectedRequirement
        public ObservableCollection<LooseTableViewModel> SelectedTableVMs { get; private set; }

        // Paragraph VMs (wrapping strings) scoped to the SelectedRequirement
        public ObservableCollection<ParagraphViewModel> SelectedParagraphVMs { get; private set; }

        // UI helpers
        [ObservableProperty]
        private int selectedLooseTabIndex;

        [ObservableProperty]
        private bool includeLooseParagraphs;

        // HasMeta used by the view (Border visibility / style)
        [ObservableProperty]
        private bool hasMeta;

        // Support pane commands (per-type)
        public ICommand SelectAllTablesCommand { get; }
        public ICommand ClearAllTablesCommand { get; }
        public ICommand SelectAllParagraphsCommand { get; }
        public ICommand ClearAllParagraphsCommand { get; }
        public ICommand ToggleParagraphCommand { get; }
        public ICommand EditSupplementalInfoCommand { get; }

        // Routed parent-level commands (act on the currently visible view)
        public ICommand SelectAllVisibleCommand { get; }
        public ICommand ClearAllVisibleCommand { get; }

        // Replace these methods in TestCaseGenerator_VM

        private void SelectAllTables()
        {
            foreach (var t in SelectedTableVMs)
            {
                t.IsSelected = true;
                t.IncludeInPrompt = true; // keep both properties in sync
            }
        }

        private void ClearAllTables()
        {
            foreach (var t in SelectedTableVMs)
            {
                t.IsSelected = false;
                t.IncludeInPrompt = false; // keep both properties in sync
            }
        }

        private void SelectAllParagraphs()
        {
            foreach (var p in SelectedParagraphVMs)
                p.IncludeInPrompt = true;
        }

        private void ClearAllParagraphs()
        {
            foreach (var p in SelectedParagraphVMs)
                p.IncludeInPrompt = false;
        }

        private void ToggleParagraph(ParagraphViewModel? paragraph)
        {
            if (paragraph != null)
                paragraph.IncludeInPrompt = !paragraph.IncludeInPrompt;
        }

        private void EditSupplementalInfo()
        {
            var editor = new TestCaseEditorApp.MVVM.Views.SupplementalInfoEditorWindow(SelectedParagraphVMs)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            if (editor.ShowDialog() == true && editor.ResultItems != null)
            {
                // Replace the collection with edited items
                SelectedParagraphVMs.Clear();
                foreach (var text in editor.ResultItems)
                {
                    SelectedParagraphVMs.Add(new ParagraphViewModel(text) { IncludeInPrompt = true });
                }

                // Persist changes to the requirement
                if (SelectedRequirement != null)
                {
                    SelectedRequirement.LooseContent.Paragraphs = new System.Collections.Generic.List<string>(editor.ResultItems);
                }
            }
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

        // ---------------- Refresh / providers ----------------

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
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RefreshSupportContent] _tableProvider threw: {ex}");
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
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[RefreshSupportContent] _paragraphProvider threw: {ex}");
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
                catch (Exception ex) { TestCaseEditorApp.Services.Logging.Log.Debug($"[RefreshSupportContentFromProvider] tables failed: {ex}"); }

                SelectedTableVMs.Clear();
                foreach (var t in tables) SelectedTableVMs.Add(t);

                IEnumerable<string> paras = Enumerable.Empty<string>();
                try { paras = provider.GetLooseParagraphsForRequirement(SelectedRequirement) ?? Enumerable.Empty<string>(); }
                catch (Exception ex) { TestCaseEditorApp.Services.Logging.Log.Debug($"[RefreshSupportContentFromProvider] paras failed: {ex}"); }

                SelectedParagraphVMs.Clear();
                foreach (var p in paras) SelectedParagraphVMs.Add(new ParagraphViewModel(p));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RefreshSupportContentFromProvider] unexpected: {ex}");
            }
        }

        // ---------------- Presence helpers (badges etc.) ----------------

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

        // ---------------- Meta chips ----------------

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

        // ---------------- Support view selection helpers ----------------

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

        /// <summary>
        /// Whether the current requirement has analysis data available.
        /// </summary>
        public bool HasAnalysis => _navigator?.CurrentRequirement?.Analysis?.IsAnalyzed == true;

        /// <summary>
        /// The quality score from the analysis (1-10), or empty string if no analysis.
        /// </summary>
        public string AnalysisQualityScore
        {
            get
            {
                var analysis = _navigator?.CurrentRequirement?.Analysis;
                if (analysis?.IsAnalyzed == true)
                {
                    return analysis.QualityScore.ToString();
                }
                return string.Empty;
            }
        }

        // ---------------- Dispose / cleanup ----------------

        public void Dispose()
        {
            try { _navigator.Requirements.CollectionChanged -= Requirements_CollectionChanged; } catch { }
            try { _navigator.PropertyChanged -= Navigator_PropertyChanged; } catch { }

            try { SelectedTableVMs.CollectionChanged -= SelectedTableVMs_CollectionChanged; } catch { }
            try { SelectedParagraphVMs.CollectionChanged -= SelectedParagraphVMs_CollectionChanged; } catch { }

            try { this.PropertyChanged -= TestCaseGenerator_VM_PropertyChanged; } catch { }
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