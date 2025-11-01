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

        public string PromptLine => string.IsNullOrWhiteSpace(ContentLine) ? Name : ContentLine!;
    }

}

