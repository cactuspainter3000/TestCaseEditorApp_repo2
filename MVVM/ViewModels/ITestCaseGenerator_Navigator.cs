using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Lightweight abstraction for the UI navigator surface that the Requirements view needs.
    /// Implemented by MainViewModel so TestCaseGenerator_VM can be view-model-only and testable.
    /// </summary>
    public interface ITestCaseGenerator_Navigator : INotifyPropertyChanged
    {
        ObservableCollection<Requirement> Requirements { get; }
        Requirement? CurrentRequirement { get; set; }

        // Navigation commands (these are produced by CommunityToolkit [RelayCommand])
        ICommand? NextRequirementCommand { get; }
        ICommand? PreviousRequirementCommand { get; }
        ICommand? NextWithoutTestCaseCommand { get; }

        // UI helpers
        string RequirementPositionDisplay { get; }
        bool WrapOnNextWithoutTestCase { get; set; }
        
        // LLM state
        bool IsLlmBusy { get; set; }
        
        // Batch analysis state (prevents user-initiated analysis during auto-import analysis)
        bool IsBatchAnalyzing { get; }
        
        // Modal editing functionality
        void ShowRequirementEditor(Requirement requirement);
    }
}