using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    public partial class TestCaseGeneratorSplashScreenViewModel : ObservableObject
    {
        [ObservableProperty]
        private string title = "Systems ATE APP";
        
        [ObservableProperty]
        private string description = "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";
    }
}