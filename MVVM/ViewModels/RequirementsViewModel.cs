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
    public partial class RequirementsViewModel : ObservableObject, IDisposable
    {
        private readonly IPersistenceService _persistence;
        private readonly IRequirementsNavigator _navigator;

        // Optional lightweight providers
        private readonly Func<Requirement?, IEnumerable<LooseTableViewModel>>? _tableProvider;
        private readonly Func<Requirement?, IEnumerable<string>>? _paragraphProvider;

        // Optional richer provider that can provide VMs directly.
        internal TestCaseGenViewModel? TestCaseGenerator { get; set; }

        public RequirementsViewModel(
            IPersistenceService persistence,
            IRequirementsNavigator navigator,
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

            // routed parent-level commands (act on visible view)
            SelectAllVisibleCommand = new RelayCommand(SelectAllVisible, CanSelectAllVisible);
            ClearAllVisibleCommand = new RelayCommand(ClearAllVisible, CanClearAllVisible);

            // Collections
            SelectedTableVMs = new ObservableCollection<LooseTableViewModel>();
            SelectedParagraphVMs = new ObservableCollection<ParagraphViewModel>();

            // Wire collection-changed handlers and initial notifications
            WirePresenceNotifications();

            // Track SelectedSupportView changes via PropertyChanged so we don't rely on a generated partial hook
            this.PropertyChanged += RequirementsViewModel_PropertyChanged;

            // Initial population/state
            RefreshSupportContent();
            UpdateVisibleChipsFromRequirement(SelectedRequirement);
        }

        private void RequirementsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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

        // Support pane commands (per-type)
        public ICommand SelectAllTablesCommand { get; }
        public ICommand ClearAllTablesCommand { get; }

        // Routed parent-level commands (act on the currently visible view)
        public ICommand SelectAllVisibleCommand { get; }
        public ICommand ClearAllVisibleCommand { get; }

        // Replace these methods in RequirementsViewModel

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
                    System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] _tableProvider threw: {ex}");
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
                    System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] _paragraphProvider threw: {ex}");
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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RefreshSupportContentFromProvider] tables failed: {ex}"); }

                SelectedTableVMs.Clear();
                foreach (var t in tables) SelectedTableVMs.Add(t);

                IEnumerable<string> paras = Enumerable.Empty<string>();
                try { paras = provider.GetLooseParagraphsForRequirement(SelectedRequirement) ?? Enumerable.Empty<string>(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RefreshSupportContentFromProvider] paras failed: {ex}"); }

                SelectedParagraphVMs.Clear();
                foreach (var p in paras) SelectedParagraphVMs.Add(new ParagraphViewModel(p));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshSupportContentFromProvider] unexpected: {ex}");
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
            }
            catch { }
        }

        // ---------------- Meta chips ----------------

        public class ChipViewModel
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private ObservableCollection<ChipViewModel> _visibleChips = new();
        public ObservableCollection<ChipViewModel> VisibleChips
        {
            get => _visibleChips;
            set
            {
                _visibleChips = value ?? new ObservableCollection<ChipViewModel>();
                OnPropertyChanged();
            }
        }

        private void UpdateVisibleChipsFromRequirement(Requirement? r)
        {
            var list = new ObservableCollection<ChipViewModel>();
            if (r != null)
            {
                void AddIf(string label, string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(new ChipViewModel { Label = label, Value = value! });
                }

                AddIf("Project", r.Project);
                AddIf("Type", r.RequirementType);
                AddIf("Status", r.Status);
                AddIf("Set", r.SetName);
                AddIf("Version", r.Version);
                AddIf("Last Activity", r.LastActivityDate?.ToString("g") ?? string.Empty);
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
            }
        }

        // ---------------- Dispose / cleanup ----------------

        public void Dispose()
        {
            try { _navigator.Requirements.CollectionChanged -= Requirements_CollectionChanged; } catch { }
            try { _navigator.PropertyChanged -= Navigator_PropertyChanged; } catch { }

            try { SelectedTableVMs.CollectionChanged -= SelectedTableVMs_CollectionChanged; } catch { }
            try { SelectedParagraphVMs.CollectionChanged -= SelectedParagraphVMs_CollectionChanged; } catch { }

            try { this.PropertyChanged -= RequirementsViewModel_PropertyChanged; } catch { }
        }
    }

    public enum SupportView
    {
        Meta,
        Tables,
        Paragraphs
    }
}