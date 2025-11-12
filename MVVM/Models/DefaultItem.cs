namespace TestCaseEditorApp.MVVM.Models
{
    using CommunityToolkit.Mvvm.ComponentModel;
    using System.Text.Json.Serialization;

    public sealed class DefaultItem : ObservableObject
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Category { get; init; }
        public string? Description { get; init; }
        public string? ContentLine { get; init; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        // true means “auto-selected by LLM” (render orange when enabled)
        private bool _isLlmSuggested;
        [JsonIgnore] // keep this purely UI state; don’t persist if you serialize DefaultItem
        public bool IsLlmSuggested
        {
            get => _isLlmSuggested;
            set => SetProperty(ref _isLlmSuggested, value);
        }

        public string PromptLine => string.IsNullOrWhiteSpace(ContentLine) ? Name : ContentLine!;
    }


}

