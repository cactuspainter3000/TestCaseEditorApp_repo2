using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Small wrapper for paragraph text so we can store IncludeInPrompt state per-paragraph.
    public partial class ParagraphViewModel : ObservableObject
    {
        public ParagraphViewModel(string text)
        {
            Text = text ?? string.Empty;
        }

        [ObservableProperty]
        private string text = string.Empty;

        // Whether this paragraph should be included in the LLM prompt
        [ObservableProperty]
        private bool includeInPrompt = true;

        // Optional selection flag if you want inline actions on selection
        [ObservableProperty]
        private bool isSelected;
    }
}