using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProject_TitleViewModel : ObservableObject
    {
        [ObservableProperty]
        private string pageTitle = "📂 Open Existing Workshop";
        
        [ObservableProperty]
        private string breadcrumb = "Home > Open Workshop";
        
        public OpenProject_TitleViewModel()
        {
            // Simple constructor
        }
    }
}