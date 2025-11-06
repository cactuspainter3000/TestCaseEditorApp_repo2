using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Models;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// View-model adapter used by NavigationView. It forwards to an IRequirementsNavigator.
    /// NavigationView.DataContext remains NavigationViewModel; bind controls to NavigationViewModel.RequirementsNav.*.
    /// </summary>
    public class RequirementsNavigationViewModel : ObservableObject, IDisposable
    {
        private readonly IRequirementsNavigator _navigator;

        public RequirementsNavigationViewModel(IRequirementsNavigator navigator)
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _navigator.PropertyChanged += Navigator_PropertyChanged;
        }

        private void Navigator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IRequirementsNavigator.CurrentRequirement))
                OnPropertyChanged(nameof(SelectedRequirement));
            else if (e.PropertyName == nameof(IRequirementsNavigator.Requirements))
                OnPropertyChanged(nameof(Requirements));
            else if (e.PropertyName == nameof(IRequirementsNavigator.RequirementPositionDisplay))
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            else if (e.PropertyName == nameof(IRequirementsNavigator.WrapOnNextWithoutTestCase))
                OnPropertyChanged(nameof(WrapOnNextWithoutTestCase));
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

        public void Dispose()
        {
            try { _navigator.PropertyChanged -= Navigator_PropertyChanged; } catch { }
        }

        // Add inside RequirementsNavigationViewModel class
        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        // Provide a search command that forwards to a SearchRequirementCommand on the navigator if present,
        // otherwise performs a basic search using the stored SearchQuery.
        public ICommand? SearchCommand => _searchCommand ??= new RelayCommand(ExecuteSearch);
        private ICommand? _searchCommand;

        private void ExecuteSearch()
        {
            try
            {
                // If the underlying navigator exposes a search command, prefer that.
                var navIfc = _navigator as IRequirementsNavigator;
                var searchCmdProperty = _navigator.GetType().GetProperty("SearchRequirementCommand");
                if (searchCmdProperty != null)
                {
                    var cmd = searchCmdProperty.GetValue(_navigator) as ICommand;
                    if (cmd != null && cmd.CanExecute(SearchQuery))
                    {
                        cmd.Execute(SearchQuery);
                        return;
                    }
                }

                // Fallback: do an inline search over the Requirements collection
                if (string.IsNullOrWhiteSpace(SearchQuery)) return;
                var q = SearchQuery.Trim();
                var found = Requirements.FirstOrDefault(r =>
                    (!string.IsNullOrEmpty(r.Name) && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(r.Item) && r.Item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(r.GlobalId) && r.GlobalId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
                if (found != null) SelectedRequirement = found;
            }
            catch { /* swallow to keep UI snappy */ }
        }
    }
}