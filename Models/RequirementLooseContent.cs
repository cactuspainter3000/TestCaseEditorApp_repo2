using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

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
    }
}
