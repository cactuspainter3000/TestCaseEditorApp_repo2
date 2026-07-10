using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels
{
    public partial class WorkshopShellViewModel : ObservableObject
    {
        public enum WorkshopArea
        {
            Requirements,
            TestCases
        }

        private readonly UnifiedRequirementsHubViewModel _requirementsHub;
        private readonly LLMTestCaseGeneratorViewModel _testCaseGenerator;

        [ObservableProperty]
        private WorkshopArea selectedArea = WorkshopArea.Requirements;

        [ObservableProperty]
        private object? currentAreaViewModel;

        public WorkshopShellViewModel(
            UnifiedRequirementsHubViewModel requirementsHub,
            LLMTestCaseGeneratorViewModel testCaseGenerator)
        {
            _requirementsHub = requirementsHub ?? throw new ArgumentNullException(nameof(requirementsHub));
            _testCaseGenerator = testCaseGenerator ?? throw new ArgumentNullException(nameof(testCaseGenerator));

            // Initial activation keeps the requirements shell visible without forcing heavy tab content creation.
            ActivateRequirementsArea(loadMainTabIfNeeded: false);
        }

        public bool IsRequirementsSelected => SelectedArea == WorkshopArea.Requirements;
        public bool IsTestCasesSelected => SelectedArea == WorkshopArea.TestCases;

        [RelayCommand]
        private void ShowRequirementsArea()
        {
            ActivateRequirementsArea(loadMainTabIfNeeded: true);
        }

        [RelayCommand]
        private void ShowTestCasesArea()
        {
            SelectedArea = WorkshopArea.TestCases;
            CurrentAreaViewModel = _testCaseGenerator;
            OnPropertyChanged(nameof(IsRequirementsSelected));
            OnPropertyChanged(nameof(IsTestCasesSelected));
        }

        private void ActivateRequirementsArea(bool loadMainTabIfNeeded)
        {
            SelectedArea = WorkshopArea.Requirements;

            // Load main requirements tab on explicit user action, not at initial open.
            if (loadMainTabIfNeeded)
            {
                _requirementsHub.EnsureMainTabSelected();
            }

            CurrentAreaViewModel = _requirementsHub;
            OnPropertyChanged(nameof(IsRequirementsSelected));
            OnPropertyChanged(nameof(IsTestCasesSelected));
        }
    }
}
