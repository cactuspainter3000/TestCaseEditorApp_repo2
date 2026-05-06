using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels
{
    public partial class OpenProject_TitleViewModel : ObservableObject
    {
        [ObservableProperty]
        private string pageTitle = "📂 Open Existing Project";
        
        [ObservableProperty]
        private string breadcrumb = "Home > Open Project";
        
        public OpenProject_TitleViewModel()
        {
            // Simple constructor
        }
    }
}