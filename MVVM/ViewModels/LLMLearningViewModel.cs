using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class LLMLearningViewModel : ObservableObject
    {
        public LLMLearningViewModel()
        {
            Title = "LLM Learning";
            Description = "Train and configure AI models for intelligent test case generation.";
        }
        
        [ObservableProperty]
        private string title = string.Empty;
        
        [ObservableProperty]
        private string description = string.Empty;
    }
}