using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    public partial class DummyNewProjectTitleViewModel : ObservableObject
    {
        [ObservableProperty]
        private string pageTitle = "✨ Create New Project";
        
        [ObservableProperty]
        private string breadcrumb = "Home > New Project";
        
        public DummyNewProjectTitleViewModel()
        {
            // Simple constructor
        }
    }
}