using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    public partial class TestCaseGeneratorSplashViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _welcomeMessage = "Welcome to Test Case Generator";
        
        [ObservableProperty]
        private string _description = "Generate comprehensive test cases from your requirements with AI assistance.";
        
        public TestCaseGeneratorSplashViewModel()
        {
            // Simple splash screen
        }
    }
}