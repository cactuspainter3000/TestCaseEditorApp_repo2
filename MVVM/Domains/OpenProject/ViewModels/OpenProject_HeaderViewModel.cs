using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProject_HeaderViewModel : ObservableObject
    {
        [ObservableProperty]
        private string headerTitle = "📂 Open Workshop";
        
        [ObservableProperty]
        private string description = "Select an existing workshop to open";
        
        public OpenProject_HeaderViewModel()
        {
            // Simple constructor
        }
    }
}