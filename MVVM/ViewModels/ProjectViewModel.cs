using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class ProjectViewModel : ObservableObject
    {
        public ProjectViewModel()
        {
            Title = "Project Management";
            Description = "Configure your test case generation projects and workspace settings.";
        }
        
        [ObservableProperty]
        private string title = string.Empty;
        
        [ObservableProperty]
        private string description = string.Empty;
    }
}