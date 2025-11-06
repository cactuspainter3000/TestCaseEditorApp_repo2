using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Adapter VM for the Requirements view. The view binds to this VM (clean surface),
    /// which delegates to an IRequirementsNavigator (usually MainViewModel).
    ///
    /// Now also surfaces support content (selected requirement's loose tables and paragraphs)
    /// via provider delegates passed in by the host (MainViewModel/TestCaseGenerator).
    /// </summary>
    public partial class RequirementsViewModel : ObservableObject, IDisposable
    {
        private readonly IPersistenceService _persistence;
        private readonly IRequirementsNavigator _navigator;

        // Optional providers to obtain support content for a given requirement.
        // Host (MainViewModel) should pass these in when constructing this VM.
        private readonly Func<Requirement?, IEnumerable<LooseTableViewModel>>? _tableProvider;
        private readonly Func<Requirement?, IEnumerable<string>>? _paragraphProvider;

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

            // Forward collection notifications & navigator property changes
            _navigator.Requirements.CollectionChanged += Requirements_CollectionChanged;
            _navigator.PropertyChanged += Navigator_PropertyChanged;

            AddRequirementCommand = new RelayCommand(AddRequirement);
            RemoveRequirementCommand = new RelayCommand(RemoveSelectedRequirement, () => SelectedRequirement != null);

            // Support pane collections
            SelectedTableVMs = new ObservableCollection<LooseTableViewModel>();
            SelectedParagraphs = new ObservableCollection<string>();

            // Support pane commands
            SelectAllTablesCommand = new RelayCommand(SelectAllTables, () => SelectedTableVMs?.Any() == true);
            ClearAllTablesCommand = new RelayCommand(ClearAllTables, () => SelectedTableVMs?.Any() == true);

            // ensure initial population if a requirement already selected
            RefreshSupportContent();
        }

        private void Navigator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_navigator.CurrentRequirement))
            {
                OnPropertyChanged(nameof(SelectedRequirement));
                RefreshSupportContent();
            }
            else if (e.PropertyName == nameof(_navigator.RequirementPositionDisplay))
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            else if (e.PropertyName == nameof(_navigator.Requirements))
                OnPropertyChanged(nameof(Requirements));
        }

        private void Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Requirements));
            ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged();
        }

        // Expose core surface for binding
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
                    ((RelayCommand)RemoveRequirementCommand).NotifyCanExecuteChanged();
                    RefreshSupportContent();
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

        // Local add/remove commands
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
        public ObservableCollection<LooseTableViewModel> SelectedTableVMs { get; }

        // Loose paragraphs (strings) scoped to the SelectedRequirement
        public ObservableCollection<string> SelectedParagraphs { get; }

        // UI helpers for support pane
        [ObservableProperty]
        private int selectedLooseTabIndex;

        [ObservableProperty]
        private bool includeLooseParagraphs;

        // Commands for the support pane (Select/clear)
        public ICommand SelectAllTablesCommand { get; }
        public ICommand ClearAllTablesCommand { get; }

        private void SelectAllTables()
        {
            foreach (var t in SelectedTableVMs)
                t.IsSelected = true;
        }

        private void ClearAllTables()
        {
            foreach (var t in SelectedTableVMs)
                t.IsSelected = false;
        }

        // Refresh the support pane content from providers when the selected requirement changes
        private void RefreshSupportContent()
        {
            // Dispose / clear existing loose table VMs (if they need disposal; check your VM lifecycle)
            SelectedTableVMs.Clear();
            SelectedParagraphs.Clear();

            if (SelectedRequirement == null)
                return;

            // tables
            if (_tableProvider != null)
            {
                var tables = _tableProvider.Invoke(SelectedRequirement) ?? Enumerable.Empty<LooseTableViewModel>();
                foreach (var t in tables)
                    SelectedTableVMs.Add(t);
            }

            // paragraphs
            if (_paragraphProvider != null)
            {
                var paras = _paragraphProvider.Invoke(SelectedRequirement) ?? Enumerable.Empty<string>();
                foreach (var p in paras)
                    SelectedParagraphs.Add(p);
            }

            // Notify support-pane commands that availability changed
            ((RelayCommand)SelectAllTablesCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearAllTablesCommand).NotifyCanExecuteChanged();

            // Notify any binding consumers
            OnPropertyChanged(nameof(SelectedTableVMs));
            OnPropertyChanged(nameof(SelectedParagraphs));
            RefreshSupportContentFromProvider();
        }

        // Call this from RefreshSupportContent (or equivalent)

        private void RefreshSupportContentFromProvider()
        {
            try
            {
                var provider = this.TestCaseGenerator;

                // Get tables (defensive: fall back to empty)
                IEnumerable<LooseTableViewModel> tables = Enumerable.Empty<LooseTableViewModel>();
                if (provider != null)
                {
                    try
                    {
                        tables = provider.GetLooseTableVMsForRequirement(SelectedRequirement) ?? Enumerable.Empty<LooseTableViewModel>();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] GetLooseTableVMsForRequirement failed: {ex}");
                        tables = Enumerable.Empty<LooseTableViewModel>();
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[RefreshSupportContent] provider == null");
                }

                System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] req={SelectedRequirement?.Item} providerTables={tables.Count()}");

                // Assign into bound collection so UI updates
                SelectedTableVMs.Clear();
                foreach (var t in tables) SelectedTableVMs.Add(t);

                System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] SelectedTableVMs.Count after assign = {SelectedTableVMs.Count}");

                // Get paragraphs (defensive)
                IEnumerable<string> paras = Enumerable.Empty<string>();
                if (provider != null)
                {
                    try
                    {
                        paras = provider.GetLooseParagraphsForRequirement(SelectedRequirement) ?? Enumerable.Empty<string>();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] GetLooseParagraphsForRequirement failed: {ex}");
                        paras = Enumerable.Empty<string>();
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] providerParas={paras.Count()}");

                SelectedParagraphs.Clear();
                foreach (var p in paras) SelectedParagraphs.Add(p);

                System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] SelectedParagraphs.Count after assign = {SelectedParagraphs.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshSupportContent] unexpected exception: {ex}");
            }
        }

        public void Dispose()
        {
            try { _navigator.Requirements.CollectionChanged -= Requirements_CollectionChanged; } catch { }
            try { _navigator.PropertyChanged -= Navigator_PropertyChanged; } catch { }

            // Do not dispose table VMs here if MainViewModel/TestCaseGenerator owns them.
            // If RequirementsViewModel owns them, dispose their resources here as needed.
        }
    }
}