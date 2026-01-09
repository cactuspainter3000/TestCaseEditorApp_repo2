using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// Navigation workspace content for application startup state
    /// </summary>
    public partial class StartupNavigationVM : ObservableObject
    {
        [ObservableProperty]
        private string navigationText = "Get started by selecting an option from the side menu";
        
        [ObservableProperty]
        private bool isVisible = true;
    }
}