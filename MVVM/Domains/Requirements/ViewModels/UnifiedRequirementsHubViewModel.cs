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

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private object? currentContentViewModel;

        public UnifiedRequirementsHubViewModel(RequirementsTabSelectorViewModel tabSelector)
        {
            _tabSelector = tabSelector ?? throw new ArgumentNullException(nameof(tabSelector));
            CurrentContentViewModel = _tabSelector.CurrentContentViewModel;

            _tabSelector.PropertyChanged += OnTabSelectorPropertyChanged;
        }

        public bool HasCurrentContent => CurrentContentViewModel != null;

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
        private void ShowUtilities()
        {
            _tabSelector.SelectUtilitiesTabCommand.Execute(null);
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
