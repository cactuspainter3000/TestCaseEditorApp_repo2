using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    public partial class DummyNewProjectMainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string displayText = "🎯 New Project Main Workspace - Working!";
        
        [ObservableProperty]
        private string statusMessage = "This is the dummy main content area for New Project workflow.";
        
        public DummyNewProjectMainViewModel()
        {
            // Simple constructor - no dependencies needed for dummy implementation
        }
    }
}