using System;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Lightweight helper to provide requirement-browsing commands and index for the UI.
    /// Designed to be instantiated from MainViewModel (passes 'this' in constructor).
    /// Subscribes to Requirements.CollectionChanged and MainViewModel property changes.
    /// Exposes commands and read-only properties that should be bound from XAML.
    /// </summary>
    public class RequirementsNavigationService : ObservableObject, IDisposable
    {
        private readonly MainViewModel _main;

        public RequirementsNavigationService(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            // wire collection and selection changes
            if (_main.Requirements != null)
                _main.Requirements.CollectionChanged += Requirements_CollectionChanged;

            _main.PropertyChanged += Main_PropertyChanged;

            // create commands
            PrevRequirementCommand = new RelayCommand(PrevRequirement, CanPrevRequirement);
            NextRequirementCommand = new RelayCommand(NextRequirement, CanNextRequirement);
            SelectRequirementCommand = new RelayCommand<object?>(SelectRequirement);
            SearchRequirementCommand = new RelayCommand(SearchRequirement);

            // initial state
            UpdateIndex();
        }

        // Commands exposed for binding from XAML (bind to the service instance)
        public IRelayCommand PrevRequirementCommand { get; }
        public IRelayCommand NextRequirementCommand { get; }
        public IRelayCommand SelectRequirementCommand { get; }
        public IRelayCommand SearchRequirementCommand { get; }

        // Read-only properties for binding (service raises change notifications)
        public int SelectedRequirementIndex
        {
            get => _selectedRequirementIndex;
            private set => SetProperty(ref _selectedRequirementIndex, value);
        }
        private int _selectedRequirementIndex;

        public int TotalRequirementsCount => _main.Requirements?.Count ?? 0;

        public string RequirementPositionDisplay =>
            _main.Requirements == null || _main.Requirements.Count == 0 || _main.CurrentRequirement == null
                ? string.Empty
                : $"{_main.Requirements.IndexOf(_main.CurrentRequirement) + 1} of {_main.Requirements.Count}";

        private void Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateIndex();
        }

        private void Main_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentRequirement))
                UpdateIndex();
        }

        private void UpdateIndex()
        {
            if (_main.Requirements == null || _main.Requirements.Count == 0 || _main.CurrentRequirement == null)
                SelectedRequirementIndex = 0;
            else
            {
                var idx = _main.Requirements.IndexOf(_main.CurrentRequirement);
                SelectedRequirementIndex = (idx >= 0) ? idx + 1 : 0;
            }

            // Notify listeners that derived properties changed on the service
            OnPropertyChanged(nameof(TotalRequirementsCount));
            OnPropertyChanged(nameof(RequirementPositionDisplay));

            PrevRequirementCommand.NotifyCanExecuteChanged();
            NextRequirementCommand.NotifyCanExecuteChanged();
        }

        private bool CanPrevRequirement() =>
            _main.CurrentRequirement != null && _main.Requirements.IndexOf(_main.CurrentRequirement) > 0;

        private bool CanNextRequirement() =>
            _main.CurrentRequirement != null && _main.Requirements.IndexOf(_main.CurrentRequirement) < (_main.Requirements.Count - 1);

        private void PrevRequirement()
        {
            if (!CanPrevRequirement()) return;
            var i = _main.Requirements.IndexOf(_main.CurrentRequirement);
            _main.CurrentRequirement = _main.Requirements[i - 1];
        }

        private void NextRequirement()
        {
            if (!CanNextRequirement()) return;
            var i = _main.Requirements.IndexOf(_main.CurrentRequirement);
            _main.CurrentRequirement = _main.Requirements[i + 1];
        }

        private void SelectRequirement(object? param)
        {
            if (param is Requirement r)
            {
                _main.CurrentRequirement = r;
                return;
            }
            if (param is string id)
            {
                var found = _main.Requirements.FirstOrDefault(x =>
                    string.Equals(x.GlobalId, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Item, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase));
                if (found != null) _main.CurrentRequirement = found;
            }
        }

        private void SearchRequirement()
        {
            // If MainViewModel exposes a search text property use it; fall back to nothing if not present.
            var prop = _main.GetType().GetProperty("RequirementSearchText");
            string? q = prop?.GetValue(_main) as string;
            if (string.IsNullOrWhiteSpace(q)) return;

            var found = _main.Requirements.FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.Name) && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(r.Item) && r.Item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(r.GlobalId) && r.GlobalId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            if (found != null) _main.CurrentRequirement = found;
        }

        public void Dispose()
        {
            if (_main.Requirements != null)
                _main.Requirements.CollectionChanged -= Requirements_CollectionChanged;
            _main.PropertyChanged -= Main_PropertyChanged;
        }
    }
}