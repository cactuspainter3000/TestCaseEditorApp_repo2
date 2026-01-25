using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Unstructured content associated with a requirement.
    /// </summary>
    public partial class RequirementLooseContent : ObservableObject
    {
        [ObservableProperty]
        private List<string> paragraphs = new();

        [ObservableProperty]
        private List<LooseTable> tables = new();

        [ObservableProperty]
        private string? cleanedDescription;
    }
}
