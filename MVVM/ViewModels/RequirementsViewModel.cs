using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class RequirementsViewModel : ObservableObject
    {
        public RequirementsViewModel()
        {
            Title = "Requirements Management";
            Description = "Import, analyze, and manage requirements for test case generation.";
        }
        
        [ObservableProperty]
        private string title = string.Empty;
        
        [ObservableProperty]
        private string description = string.Empty;
    }
}