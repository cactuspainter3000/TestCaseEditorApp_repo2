using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    internal partial class RequirementsViewModel : ObservableObject
    {
        public TestCaseGenerator_VM TestCaseGeneratorVM { get; }
        
        public RequirementsViewModel(IPersistenceService persistence, ITestCaseGenerator_Navigator navigator, TestCaseGenerator_CoreVM? testCaseGenerator)
        {
            Title = "Requirements";
            Description = "View requirement details, tables, and supplemental information.";
            
            // Create the TestCaseGenerator_VM instance that will be used by the RequirementsView
            TestCaseGeneratorVM = new TestCaseGenerator_VM(persistence, navigator);
            
            // Set the TestCaseGenerator so it can load tables and paragraphs
            TestCaseGeneratorVM.TestCaseGenerator = testCaseGenerator;
        }
        
        [ObservableProperty]
        private string title = string.Empty;
        
        [ObservableProperty]
        private string description = string.Empty;
    }
}