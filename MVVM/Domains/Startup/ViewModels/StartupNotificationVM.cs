using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// Notification workspace content for application startup state
    /// </summary>
    public partial class StartupNotificationVM : ObservableObject
    {
        [ObservableProperty]
        private string statusText = "Ready";
        
        [ObservableProperty]
        private bool isVisible = true;
    }
}