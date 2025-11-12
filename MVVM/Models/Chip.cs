using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Lightweight observable model representing a single assumption "chip".
    /// Use this for UI-friendly toggles and suggestion state.
    /// </summary>
    public class Chip : ObservableObject
    {
        public Chip() { }

        public Chip(string name, string? description = null, bool isEnabled = false)
        {
            Name = name;
            Description = description;
            IsEnabled = isEnabled;
        }

        private string? _name;
        public string? Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private string? _description;
        public string? Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private bool _isLlmSuggested;
        public bool IsLlmSuggested
        {
            get => _isLlmSuggested;
            set => SetProperty(ref _isLlmSuggested, value);
        }
    }
}