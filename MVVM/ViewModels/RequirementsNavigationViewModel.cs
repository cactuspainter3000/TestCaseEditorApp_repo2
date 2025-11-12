using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Models;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public class RequirementsNavigationViewModel : ObservableObject, IDisposable
    {
        private readonly IRequirementsNavigator _navigator;
        private bool _suppressNavigatorUpdate;
        private readonly DispatcherTimer? _pollTimer;
        private int _selectedRequirementIndex = -1;
        private Requirement? _lastObservedNavigatorCurrent;
        private RelayCommand? _previousCommand;
        private RelayCommand? _nextCommand;
        private RelayCommand? _nextWithoutTestCaseCommand;
        private string? _searchQuery;
        private ICommand? _searchCommand;

        public RequirementsNavigationViewModel(IRequirementsNavigator navigator)
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _navigator.PropertyChanged += Navigator_PropertyChanged;

            _selectedRequirementIndex = (_navigator.CurrentRequirement != null) ? _navigator.Requirements.IndexOf(_navigator.CurrentRequirement) : -1;
            _lastObservedNavigatorCurrent = _navigator.CurrentRequirement;

            SubscribeToUnderlyingCommandChanges();

            // optional poll guard
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();
        }

        private void PollTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var navCurrent = _navigator.CurrentRequirement;
                if (!ReferenceEquals(navCurrent, _lastObservedNavigatorCurrent))
                {
                    _lastObservedNavigatorCurrent = navCurrent;
                    UpdateIndexFromNavigator();
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    RefreshNavCommands();
                }
            }
            catch (Exception ex) { Debug.WriteLine($"PollTimer_Tick error: {ex}"); }
        }

        private void Navigator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(IRequirementsNavigator.CurrentRequirement))
                {
                    UpdateIndexFromNavigator();
                    OnPropertyChanged(nameof(SelectedRequirement));
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    RefreshNavCommands();
                }
                else if (e.PropertyName == nameof(IRequirementsNavigator.Requirements))
                {
                    OnPropertyChanged(nameof(Requirements));
                    UpdateIndexFromNavigator();
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    RefreshNavCommands();
                }
                else if (e.PropertyName == nameof(IRequirementsNavigator.RequirementPositionDisplay))
                {
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                }
                else if (e.PropertyName == nameof(IRequirementsNavigator.WrapOnNextWithoutTestCase))
                {
                    OnPropertyChanged(nameof(WrapOnNextWithoutTestCase));
                }
            }
            catch (Exception ex) { Debug.WriteLine($"Navigator_PropertyChanged error: {ex}"); }
        }

        private void UpdateIndexFromNavigator()
        {
            var newIndex = (_navigator.CurrentRequirement != null) ? _navigator.Requirements.IndexOf(_navigator.CurrentRequirement) : -1;

            _suppressNavigatorUpdate = true;
            try
            {
                _selectedRequirementIndex = newIndex;
                _lastObservedNavigatorCurrent = _navigator.CurrentRequirement;
                OnPropertyChanged(nameof(SelectedRequirementIndex));
                OnPropertyChanged(nameof(SelectedRequirement));
            }
            finally { _suppressNavigatorUpdate = false; }
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
                    var idx = value != null ? Requirements.IndexOf(value) : -1;
                    _suppressNavigatorUpdate = true;
                    try
                    {
                        _selectedRequirementIndex = idx;
                        _lastObservedNavigatorCurrent = value;
                        OnPropertyChanged(nameof(SelectedRequirementIndex));
                    }
                    finally { _suppressNavigatorUpdate = false; }

                    OnPropertyChanged(nameof(SelectedRequirement));
                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                    RefreshNavCommands();
                }
            }
        }

        public ICommand? PreviousRequirementCommand => _previousCommand ??= new RelayCommand(
            () =>
            {
                try { _navigator.PreviousRequirementCommand?.Execute(null); }
                finally { UpdateIndexFromNavigator(); OnPropertyChanged(nameof(RequirementPositionDisplay)); RefreshNavCommands(); }
            },
            () => _navigator.PreviousRequirementCommand?.CanExecute(null) ?? false
        );

        public ICommand? NextRequirementCommand => _nextCommand ??= new RelayCommand(
            () =>
            {
                try { _navigator.NextRequirementCommand?.Execute(null); }
                finally { UpdateIndexFromNavigator(); OnPropertyChanged(nameof(RequirementPositionDisplay)); RefreshNavCommands(); }
            },
            () => _navigator.NextRequirementCommand?.CanExecute(null) ?? false
        );

        public ICommand? NextWithoutTestCaseCommand => _nextWithoutTestCaseCommand ??= new RelayCommand(
            () =>
            {
                try { _navigator.NextWithoutTestCaseCommand?.Execute(null); }
                finally { UpdateIndexFromNavigator(); OnPropertyChanged(nameof(RequirementPositionDisplay)); RefreshNavCommands(); }
            },
            () => _navigator.NextWithoutTestCaseCommand?.CanExecute(null) ?? false
        );

        public int SelectedRequirementIndex
        {
            get => _selectedRequirementIndex;
            set
            {
                if (_selectedRequirementIndex == value) return;
                _selectedRequirementIndex = value;

                if (!_suppressNavigatorUpdate)
                {
                    if (value >= 0 && value < Requirements.Count)
                    {
                        var item = Requirements[value];
                        if (!ReferenceEquals(_navigator.CurrentRequirement, item))
                            _navigator.CurrentRequirement = item;
                    }
                    else
                    {
                        _navigator.CurrentRequirement = null;
                    }
                }

                OnPropertyChanged(nameof(SelectedRequirementIndex));
                OnPropertyChanged(nameof(SelectedRequirement));
                OnPropertyChanged(nameof(RequirementPositionDisplay));
                RefreshNavCommands();
            }
        }

        public string RequirementPositionDisplay
        {
            get
            {
                var total = Requirements?.Count ?? 0;
                var pos = (_selectedRequirementIndex >= 0 && total > 0) ? (_selectedRequirementIndex + 1).ToString() : "—";
                return $"{pos} / {total}";
            }
        }

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

        public void Dispose()
        {
            try { _navigator.PropertyChanged -= Navigator_PropertyChanged; } catch { }
            UnsubscribeFromUnderlyingCommandChanges();
            try
            {
                if (_pollTimer != null) { _pollTimer.Tick -= PollTimer_Tick; _pollTimer.Stop(); }
            }
            catch { }
        }

        private void SubscribeToUnderlyingCommandChanges()
        {
            try { if (_navigator.PreviousRequirementCommand is ICommand prevCmd) prevCmd.CanExecuteChanged += UnderlyingCommand_CanExecuteChanged; } catch { }
            try { if (_navigator.NextRequirementCommand is ICommand nextCmd) nextCmd.CanExecuteChanged += UnderlyingCommand_CanExecuteChanged; } catch { }
            try { if (_navigator.NextWithoutTestCaseCommand is ICommand nextWCmd) nextWCmd.CanExecuteChanged += UnderlyingCommand_CanExecuteChanged; } catch { }
        }

        private void UnsubscribeFromUnderlyingCommandChanges()
        {
            try { if (_navigator.PreviousRequirementCommand is ICommand prevCmd) prevCmd.CanExecuteChanged -= UnderlyingCommand_CanExecuteChanged; } catch { }
            try { if (_navigator.NextRequirementCommand is ICommand nextCmd) nextCmd.CanExecuteChanged -= UnderlyingCommand_CanExecuteChanged; } catch { }
            try { if (_navigator.NextWithoutTestCaseCommand is ICommand nextWCmd) nextWCmd.CanExecuteChanged -= UnderlyingCommand_CanExecuteChanged; } catch { }
        }

        private void UnderlyingCommand_CanExecuteChanged(object? sender, EventArgs e) => RefreshNavCommands();

        private void RefreshNavCommands()
        {
            try
            {
                (_previousCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (_nextCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (_nextWithoutTestCaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
                CommandManager.InvalidateRequerySuggested();
            }
            catch { }
        }

        public string? SearchQuery { get => _searchQuery; set => SetProperty(ref _searchQuery, value); }

        public ICommand? SearchCommand => _searchCommand ??= new RelayCommand(ExecuteSearch);

        private void ExecuteSearch()
        {
            try
            {
                var searchCmdProperty = _navigator.GetType().GetProperty("SearchRequirementCommand");
                if (searchCmdProperty != null)
                {
                    var cmd = searchCmdProperty.GetValue(_navigator) as ICommand;
                    if (cmd != null && cmd.CanExecute(SearchQuery)) { cmd.Execute(SearchQuery); return; }
                }

                if (string.IsNullOrWhiteSpace(SearchQuery)) return;
                var q = SearchQuery.Trim();
                var found = Requirements.FirstOrDefault(r =>
                    (!string.IsNullOrEmpty(r.Name) && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(r.Item) && r.Item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(r.GlobalId) && r.GlobalId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                if (found != null) SelectedRequirement = found;
            }
            catch { }
        }
    }
}