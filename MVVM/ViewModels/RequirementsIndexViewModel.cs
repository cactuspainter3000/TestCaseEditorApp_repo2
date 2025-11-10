using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Small navigator VM for Requirements collection.
    /// - Keeps a SelectedRequirement (bound to the UI)
    /// - Exposes Prev/Next commands and a method to re-evaluate when the host's CurrentRequirement changes.
    /// Designed to be lightweight and safe to construct from MainViewModel.
    /// </summary>
    public class RequirementsIndexViewModel : ObservableObject
    {
        private readonly ObservableCollection<Requirement> _requirements;
        private readonly Func<Requirement?> _getCurrentRequirement;
        private readonly Action<Requirement?> _setCurrentRequirement;
        private readonly Action? _commitPendingEdits;
        private readonly ILogger<RequirementsIndexViewModel>? _logger;

        public RequirementsIndexViewModel(
            ObservableCollection<Requirement> requirements,
            Func<Requirement?> getCurrentRequirement,
            Action<Requirement?> setCurrentRequirement,
            Action? commitPendingEdits = null,
            ILogger<RequirementsIndexViewModel>? logger = null)
        {
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setCurrentRequirement = setCurrentRequirement ?? throw new ArgumentNullException(nameof(setCurrentRequirement));
            _commitPendingEdits = commitPendingEdits;
            _logger = logger;

            // Commands - expose names the view expects (PreviousRequirementCommand / NextRequirementCommand)
            PreviousRequirementCommand = new RelayCommand(ExecutePrev, CanExecutePrev);
            NextRequirementCommand = new RelayCommand(ExecuteNext, CanExecuteNext);

            // Keep collection changes observed so command availability and position display update
            _requirements.CollectionChanged += (_, __) =>
            {
                NotifyCommands();
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            };
        }

        // Exposed collection reference for the view to bind ItemsSource to.
        public ObservableCollection<Requirement> Requirements => _requirements;

        // Exposed ICommand properties matching XAML bindings
        public IRelayCommand PreviousRequirementCommand { get; }
        public IRelayCommand NextRequirementCommand { get; }

        // SelectedRequirement is mirror of host's current requirement for binding convenience.
        // When set locally, we call back into the host via _setCurrentRequirement.
        private Requirement? _selectedRequirement;
        public Requirement? SelectedRequirement
        {
            get => _selectedRequirement ?? _getCurrentRequirement();
            set
            {
                if (Equals(_selectedRequirement, value)) return;
                _selectedRequirement = value;
                OnPropertyChanged(nameof(SelectedRequirement));

                // Commit pending edits in host before changing selection
                _commitPendingEdits?.Invoke();
                _setCurrentRequirement?.Invoke(value);

                _logger?.LogDebug("[NAV] SelectedRequirement set -> {RequirementItem}", value?.Item ?? "<null>");
                NotifyCommands();
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            }
        }

        // Readable position display for the UI (e.g. "3 / 31")
        public string RequirementPositionDisplay
        {
            get
            {
                var cur = _getCurrentRequirement();
                if (cur == null || _requirements.Count == 0) return "—";
                var idx = _requirements.IndexOf(cur);
                if (idx < 0) return "—";
                return $"{idx + 1} / {_requirements.Count}";
            }
        }

        // Called by the host (MainViewModel) when it sets its CurrentRequirement so the navigator can sync.
        public void NotifyCurrentRequirementChanged()
        {
            // Clear any cached selection so getter pulls from host delegate,
            // then raise property changed so bindings update.
            _selectedRequirement = null;
            OnPropertyChanged(nameof(SelectedRequirement));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            NotifyCommands();

            _logger?.LogDebug("[NAV] NotifyCurrentRequirementChanged invoked. Current={Current}, Count={Count}",
                _getCurrentRequirement()?.Item ?? "<null>", _requirements.Count);
        }

        private void ExecutePrev()
        {
            var cur = _getCurrentRequirement();
            if (cur == null)
            {
                if (_requirements.Count > 0)
                {
                    _commitPendingEdits?.Invoke();
                    _setCurrentRequirement(_requirements[0]);
                    _logger?.LogDebug("[NAV] ExecutePrev -> wrapped to first item: {Item}", _requirements[0].Item);
                    return;
                }
                return;
            }

            var idx = _requirements.IndexOf(cur);
            if (idx > 0)
            {
                _commitPendingEdits?.Invoke();
                _setCurrentRequirement(_requirements[idx - 1]);
                _logger?.LogDebug("[NAV] ExecutePrev -> moved to index {Index} ({Item})", idx - 1, _requirements[idx - 1].Item);
            }
        }

        private bool CanExecutePrev() => _getCurrentRequirement() != null && _requirements.IndexOf(_getCurrentRequirement()!) > 0;

        private void ExecuteNext()
        {
            var cur = _getCurrentRequirement();
            if (cur == null)
            {
                if (_requirements.Count > 0)
                {
                    _commitPendingEdits?.Invoke();
                    _setCurrentRequirement(_requirements[0]);
                    _logger?.LogDebug("[NAV] ExecuteNext -> wrapped to first item: {Item}", _requirements[0].Item);
                    return;
                }
                return;
            }

            var idx = _requirements.IndexOf(cur);
            if (idx >= 0 && idx < _requirements.Count - 1)
            {
                _commitPendingEdits?.Invoke();
                _setCurrentRequirement(_requirements[idx + 1]);
                _logger?.LogDebug("[NAV] ExecuteNext -> moved to index {Index} ({Item})", idx + 1, _requirements[idx + 1].Item);
            }
        }

        private bool CanExecuteNext() => _getCurrentRequirement() != null && _requirements.IndexOf(_getCurrentRequirement()!) < _requirements.Count - 1;

        private void NotifyCommands()
        {
            PreviousRequirementCommand?.NotifyCanExecuteChanged();
            NextRequirementCommand?.NotifyCanExecuteChanged();
        }
    }
}