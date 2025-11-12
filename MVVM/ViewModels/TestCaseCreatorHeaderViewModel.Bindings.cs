using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Minimal partial to add TitleText used by TestCaseCreatorHeaderView.xaml
    public partial class TestCaseCreatorHeaderViewModel : ObservableObject
    {
        [ObservableProperty]
        private string titleText = "Create Test Case";
    }
}