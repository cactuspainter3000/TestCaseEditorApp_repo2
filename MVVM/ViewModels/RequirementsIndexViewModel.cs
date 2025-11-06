using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
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

        public RequirementsIndexViewModel(
            ObservableCollection<Requirement> requirements,
            Func<Requirement?> getCurrentRequirement,
            Action<Requirement?> setCurrentRequirement,
            Action? commitPendingEdits = null)
        {
            _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setCurrentRequirement = setCurrentRequirement ?? throw new ArgumentNullException(nameof(setCurrentRequirement));
            _commitPendingEdits = commitPendingEdits;

            // Commands
            PrevCommand = new RelayCommand(ExecutePrev, CanExecutePrev);
            NextCommand = new RelayCommand(ExecuteNext, CanExecuteNext);

            // Keep collection changes observed so command availability updates
            _requirements.CollectionChanged += (_, __) => NotifyCommands();
        }

        // Exposed ICommand for UI
        public ICommand PrevCommand { get; }
        public ICommand NextCommand { get; }

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
                NotifyCommands();
            }
        }

        // Called by the host (MainViewModel) when it sets its CurrentRequirement so the navigator can sync.
        public void NotifyCurrentRequirementChanged()
        {
            // Clear any cached selection so getter pulls from host delegate,
            // then raise property changed so bindings update.
            _selectedRequirement = null;
            OnPropertyChanged(nameof(SelectedRequirement));
            NotifyCommands();
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
                    return;
                }
                return;
            }

            var idx = _requirements.IndexOf(cur);
            if (idx > 0)
            {
                _commitPendingEdits?.Invoke();
                _setCurrentRequirement(_requirements[idx - 1]);
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
                    return;
                }
                return;
            }

            var idx = _requirements.IndexOf(cur);
            if (idx >= 0 && idx < _requirements.Count - 1)
            {
                _commitPendingEdits?.Invoke();
                _setCurrentRequirement(_requirements[idx + 1]);
            }
        }

        private bool CanExecuteNext() => _getCurrentRequirement() != null && _requirements.IndexOf(_getCurrentRequirement()!) < _requirements.Count - 1;

        private void NotifyCommands()
        {
            (PrevCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (NextCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }
    }
}