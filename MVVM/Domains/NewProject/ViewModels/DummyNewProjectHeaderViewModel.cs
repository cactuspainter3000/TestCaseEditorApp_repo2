using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels
{
    public partial class DummyNewProjectHeaderViewModel : ObservableObject
    {
        [ObservableProperty]
        private string title = "📋 New Project Header";
        
        [ObservableProperty]
        private string subtitle = "Dummy header workspace - context-specific headers";
        
        public DummyNewProjectHeaderViewModel()
        {
            // Simple constructor - no dependencies
        }
    }
}