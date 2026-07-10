using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels
{
    public partial class WorkshopShellViewModel : ObservableObject
    {
        public enum WorkshopArea
        {
            Requirements,
            TestCases
        }

        private readonly RequirementsTabSelectorViewModel _requirementsTabSelector;
        private readonly LLMTestCaseGeneratorViewModel _testCaseGenerator;

        [ObservableProperty]
        private WorkshopArea selectedArea = WorkshopArea.Requirements;

        [ObservableProperty]
        private object? currentAreaViewModel;

        public WorkshopShellViewModel(
            RequirementsTabSelectorViewModel requirementsTabSelector,
            LLMTestCaseGeneratorViewModel testCaseGenerator)
        {
            _requirementsTabSelector = requirementsTabSelector ?? throw new ArgumentNullException(nameof(requirementsTabSelector));
            _testCaseGenerator = testCaseGenerator ?? throw new ArgumentNullException(nameof(testCaseGenerator));

            ActivateRequirementsArea();
        }

        public bool IsRequirementsSelected => SelectedArea == WorkshopArea.Requirements;
        public bool IsTestCasesSelected => SelectedArea == WorkshopArea.TestCases;

        [RelayCommand]
        private void ShowRequirementsArea()
        {
            ActivateRequirementsArea();
        }

        [RelayCommand]
        private void ShowTestCasesArea()
        {
            SelectedArea = WorkshopArea.TestCases;
            CurrentAreaViewModel = _testCaseGenerator;
            OnPropertyChanged(nameof(IsRequirementsSelected));
            OnPropertyChanged(nameof(IsTestCasesSelected));
        }

        private void ActivateRequirementsArea()
        {
            SelectedArea = WorkshopArea.Requirements;

            // Ensure a predictable default tab when opening a workshop.
            if (_requirementsTabSelector.SelectedTab == RequirementsTabSelectorViewModel.WorkspaceTab.None)
            {
                _requirementsTabSelector.SelectMainTabCommand.Execute(null);
            }

            CurrentAreaViewModel = _requirementsTabSelector;
            OnPropertyChanged(nameof(IsRequirementsSelected));
            OnPropertyChanged(nameof(IsTestCasesSelected));
        }
    }
}
