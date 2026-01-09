using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// Title workspace content for application startup state
    /// </summary>
    public partial class StartupTitleVM : ObservableObject
    {
        [ObservableProperty]
        private string applicationName = "Systems ATE APP";
    }
}