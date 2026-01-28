using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels
{
    /// <summary>
    /// Navigation workspace ViewModel for requirement selection in LLM test case generation.
    /// Wraps LLMTestCaseGeneratorViewModel to expose selection-related properties.
    /// </summary>
    public partial class TestCaseCreation_NavigationViewModel : ObservableObject
    {
        private readonly LLMTestCaseGeneratorViewModel _mainViewModel;

        [ObservableProperty]
        private bool isDropdownOpen = false;

        public TestCaseCreation_NavigationViewModel(LLMTestCaseGeneratorViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel ?? throw new System.ArgumentNullException(nameof(mainViewModel));
            
            // Forward property change notifications from main ViewModel
            _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        }

        private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Forward relevant property changes to update the navigation UI
            switch (e.PropertyName)
            {
                case nameof(LLMTestCaseGeneratorViewModel.SelectedCount):
                    OnPropertyChanged(nameof(SelectedCount));
                    break;
                case nameof(LLMTestCaseGeneratorViewModel.TotalCount):
                    OnPropertyChanged(nameof(TotalCount));
                    break;
                case nameof(LLMTestCaseGeneratorViewModel.AvailableRequirements):
                    OnPropertyChanged(nameof(AvailableRequirements));
                    break;
            }
        }

        // Expose selection-related properties from main ViewModel
        public ObservableCollection<SelectableRequirement> AvailableRequirements => _mainViewModel.AvailableRequirements;
        public int SelectedCount => _mainViewModel.SelectedCount;
        public int TotalCount => _mainViewModel.TotalCount;

        // Expose selection commands
        public IRelayCommand SelectAllCommand => _mainViewModel.SelectAllCommand;
        public IRelayCommand ClearSelectionCommand => _mainViewModel.ClearSelectionCommand;
    }
}
