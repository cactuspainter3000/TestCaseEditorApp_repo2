using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Main workspace content for TestCaseGenerator domain
    /// </summary>
    public partial class TestCaseGeneratorMainVM : ObservableObject
    {
        [ObservableProperty]
        private string title = "Test Case Generator";
        
        [ObservableProperty]
        private string description = "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";
    }
}