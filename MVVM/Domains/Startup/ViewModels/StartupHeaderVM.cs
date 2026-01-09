using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// Header workspace content for application startup state
    /// </summary>
    public partial class StartupHeaderVM : ObservableObject
    {
        [ObservableProperty]
        private string headerText = "";
        
        [ObservableProperty]
        private bool isVisible = false;
    }
}