using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProject_HeaderViewModel : ObservableObject
    {
        [ObservableProperty]
        private string headerTitle = "📂 Open Project";
        
        [ObservableProperty]
        private string description = "Select an existing project to open";
        
        public OpenProject_HeaderViewModel()
        {
            // Simple constructor
        }
    }
}