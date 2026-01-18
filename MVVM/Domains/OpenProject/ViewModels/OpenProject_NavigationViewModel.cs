using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProject_NavigationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string navigationTitle = "🧭 Project Browser";
        
        [ObservableProperty]
        private string currentAction = "Browse Projects";
        
        public OpenProject_NavigationViewModel()
        {
            // Simple constructor
        }
    }
}