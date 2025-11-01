using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace TestCaseEditorApp.MVVM.Models
{
    public partial class TestCase : ObservableObject
    {

        [ObservableProperty]
        private string id = string.Empty;

        [ObservableProperty]
        private string apiId = string.Empty;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string testCaseText = string.Empty;

        [ObservableProperty]
        private string assigned = string.Empty;

        [ObservableProperty]
        private string stepNumber = string.Empty;

        [ObservableProperty]
        private string stepAction = string.Empty;

        [ObservableProperty]
        private string stepExpectedResult = string.Empty;

        [ObservableProperty]
        private string stepNotes = string.Empty;

        [ObservableProperty]
        private string status = string.Empty;

        [ObservableProperty]
        private string tags = string.Empty;

        [ObservableProperty]
        private ObservableCollection<TestStep> steps = new();
    }
}


