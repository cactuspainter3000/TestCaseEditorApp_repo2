using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Adapter hub that exposes a single requirements workspace surface.
    /// It reuses RequirementsTabSelectorViewModel internals while presenting
    /// a consolidated toolbar and content host.
    /// </summary>
    public partial class UnifiedRequirementsHubViewModel : ObservableObject
    {
        private readonly RequirementsTabSelectorViewModel _tabSelector;
        private readonly Requirements_NavigationViewModel _requirementsNavigation;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private object? currentContentViewModel;

        public UnifiedRequirementsHubViewModel(
            RequirementsTabSelectorViewModel tabSelector,
            Requirements_NavigationViewModel requirementsNavigation)
        {
            _tabSelector = tabSelector ?? throw new ArgumentNullException(nameof(tabSelector));
            _requirementsNavigation = requirementsNavigation ?? throw new ArgumentNullException(nameof(requirementsNavigation));
            CurrentContentViewModel = _tabSelector.CurrentContentViewModel;

            _tabSelector.PropertyChanged += OnTabSelectorPropertyChanged;
        }

        public bool HasCurrentContent => CurrentContentViewModel != null;

        [RelayCommand]
        private void ApplySearch()
        {
            _requirementsNavigation.SearchQuery = SearchText?.Trim();

            if (_requirementsNavigation.SearchCommand.CanExecute(null))
            {
                _requirementsNavigation.SearchCommand.Execute(null);
            }

            EnsureMainTabSelected();
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
            _requirementsNavigation.SearchQuery = string.Empty;
            EnsureMainTabSelected();
        }

        [RelayCommand]
        private void ShowRequirements()
        {
            EnsureMainTabSelected();
        }

        [RelayCommand]
        private void ShowCleanup()
        {
            _tabSelector.SelectCleanupTabCommand.Execute(null);
        }

        [RelayCommand]
        private void ShowAttachments()
        {
            _tabSelector.SelectAttachmentsTabCommand.Execute(null);
        }

        [RelayCommand]
        private void OpenAttachmentScraper()
        {
            ShowAttachments();
        }

        [RelayCommand]
        private void ShowUtilities()
        {
            _tabSelector.SelectUtilitiesTabCommand.Execute(null);
        }

        [RelayCommand]
        private void OpenUtilities()
        {
            ShowUtilities();
        }

        public void EnsureMainTabSelected()
        {
            if (_tabSelector.SelectedTab == RequirementsTabSelectorViewModel.WorkspaceTab.None)
            {
                _tabSelector.SelectMainTabCommand.Execute(null);
            }
            else if (_tabSelector.SelectedTab != RequirementsTabSelectorViewModel.WorkspaceTab.Main)
            {
                _tabSelector.SelectMainTabCommand.Execute(null);
            }
        }

        private void OnTabSelectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RequirementsTabSelectorViewModel.CurrentContentViewModel))
            {
                CurrentContentViewModel = _tabSelector.CurrentContentViewModel;
                OnPropertyChanged(nameof(HasCurrentContent));
            }
        }
    }
}
