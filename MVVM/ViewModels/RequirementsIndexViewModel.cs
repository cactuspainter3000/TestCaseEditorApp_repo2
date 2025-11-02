using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Small viewmodel that provides requirement-browsing commands and index display.
    /// Construct with the Requirements collection, a getter for CurrentRequirement, and a setter
    /// for CurrentRequirement (so it doesn't depend on MainViewModel internals).
    /// </summary>
    public class RequirementsIndexViewModel : ObservableObject, IDisposable
    {
        private readonly ObservableCollection<Requirement> _requirements;
        private readonly Func<Requirement?> _getCurrent;
        private readonly Action<Requirement?> _setCurrent;

        public RequirementsIndexViewModel(
            ObservableCollection<Requirement> requirements,
            Func<Requirement?> getCurrentRequirement,
            Action<Requirement?> setCurrentRequirement)
        {
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _getCurrent = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setCurrent = setCurrentRequirement ?? throw new ArgumentNullException(nameof(setCurrentRequirement));

            // subscribe
            _requirements.CollectionChanged += Requirements_CollectionChanged;

            PrevCommand = new RelayCommand(PrevImpl, CanPrevImpl);
            NextCommand = new RelayCommand(NextImpl, CanNextImpl);
            SelectCommand = new RelayCommand<object?>(SelectImpl);
            SearchCommand = new RelayCommand(SearchImpl);

            UpdateState();
        }

        // Commands
        public IRelayCommand PrevCommand { get; }
        public IRelayCommand NextCommand { get; }
        public IRelayCommand SelectCommand { get; }     // parameter: Requirement or string id
        public IRelayCommand SearchCommand { get; }

        // Search text (bind this on MainViewModel or on the view to pass via CommandParameter)
        private string? _searchText;
        public string? SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        // Read-only properties for display
        private int _selectedRequirementIndex;
        public int SelectedRequirementIndex
        {
            get => _selectedRequirementIndex;
            private set => SetProperty(ref _selectedRequirementIndex, value);
        }

        public int TotalRequirementsCount => _requirements?.Count ?? 0;

        public string RequirementPositionDisplay =>
            _requirements == null || _requirements.Count == 0 || _getCurrent() == null
                ? string.Empty
                : $"{_requirements.IndexOf(_getCurrent()!) + 1} of {_requirements.Count}";

        private void Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateState();
        }

        // Call when CurrentRequirement may have changed externally
        public void NotifyCurrentRequirementChanged()
        {
            UpdateState();
        }

        private void UpdateState()
        {
            var current = _getCurrent();
            if (_requirements == null || _requirements.Count == 0 || current == null)
            {
                SelectedRequirementIndex = 0;
            }
            else
            {
                var idx = _requirements.IndexOf(current);
                SelectedRequirementIndex = (idx >= 0) ? idx + 1 : 0;
            }

            OnPropertyChanged(nameof(TotalRequirementsCount));
            OnPropertyChanged(nameof(RequirementPositionDisplay));

            PrevCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
        }

        private bool CanPrevImpl() =>
            _getCurrent() != null && _requirements.IndexOf(_getCurrent()!) > 0;

        private bool CanNextImpl() =>
            _getCurrent() != null && _requirements.IndexOf(_getCurrent()!) < (_requirements.Count - 1);

        private void PrevImpl()
        {
            var cur = _getCurrent();
            if (cur == null) return;
            var i = _requirements.IndexOf(cur);
            if (i > 0) _setCurrent(_requirements[i - 1]);
            UpdateState();
        }

        private void NextImpl()
        {
            var cur = _getCurrent();
            if (cur == null) return;
            var i = _requirements.IndexOf(cur);
            if (i >= 0 && i < _requirements.Count - 1) _setCurrent(_requirements[i + 1]);
            UpdateState();
        }

        private void SelectImpl(object? param)
        {
            if (param is Requirement r)
            {
                _setCurrent(r);
                UpdateState();
                return;
            }

            if (param is string id)
            {
                var found = _requirements.FirstOrDefault(x =>
                    string.Equals(x.GlobalId, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Item, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    _setCurrent(found);
                    UpdateState();
                }
            }
        }

        private void SearchImpl()
        {
            var q = SearchText;
            if (string.IsNullOrWhiteSpace(q)) return;
            var found = _requirements.FirstOrDefault(r =>
                (!string.IsNullOrEmpty(r.Name) && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(r.Item) && r.Item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(r.GlobalId) && r.GlobalId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            if (found != null)
            {
                _setCurrent(found);
                UpdateState();
            }
        }

        public void Dispose()
        {
            _requirements.CollectionChanged -= Requirements_CollectionChanged;
        }
    }
}