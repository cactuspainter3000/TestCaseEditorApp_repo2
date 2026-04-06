using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    public partial class DummyNewProjectNavigationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string navigationTitle = "🧭 Navigation";
        
        [ObservableProperty]
        private string currentStep = "Step 1: Project Setup";
        
        public DummyNewProjectNavigationViewModel()
        {
            // Simple constructor
        }
    }
}