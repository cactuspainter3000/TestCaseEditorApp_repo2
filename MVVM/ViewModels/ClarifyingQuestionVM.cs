using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for a single clarifying question (item-level).
    /// Combines observable fields, optional multiple-choice options, metadata, and helper flags.
    /// </summary>
    public class ClarifyingQuestionVM : ObservableObject
    {
        private readonly Action? _onChanged;
        private bool _isLoading = false;

        public ClarifyingQuestionVM(Action? onChanged = null)
        {
            _onChanged = onChanged;
            Id = Guid.NewGuid().ToString("N");
            Options.CollectionChanged += Options_CollectionChanged;
        }

        public ClarifyingQuestionVM(string textValue, IReadOnlyList<string>? options = null, Action? onChanged = null) : this(onChanged)
        {
            Text = textValue ?? string.Empty;
            if (options != null)
            {
                foreach (var o in options) Options.Add(o);
            }
        }

        // Unique id for tracking
        public string Id { get; }

        // Primary question text
        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        // User-provided answer (nullable)
        private string? _answer;
        public string? Answer
        {
            get => _answer;
            set
            {
                if (SetProperty(ref _answer, value))
                {
                    OnPropertyChanged(nameof(IsAnswered));
                    
                    // Mark workspace dirty when answer changes (skip if loading)
                    if (_onChanged != null && !_isLoading)
                    {
                        _onChanged();
                        TestCaseEditorApp.Services.Logging.Log.Debug("[Question] Answer changed - marked workspace dirty via delegate");
                    }
                }
            }
        }

        // Optional multiple-choice options
        public ObservableCollection<string> Options { get; } = new();

        // Convenience property for XAML triggers / template selection
        public bool HasOptions => Options.Count > 0;

        // Metadata: category/severity/rationale to help the UI and prompt building
        private string? _category;
        public string? Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        private string _severity = "OPTIONAL"; // MUST | SHOULD | OPTIONAL
        public string Severity
        {
            get => _severity;
            set => SetProperty(ref _severity, value);
        }

        private string? _rationale;
        public string? Rationale
        {
            get => _rationale;
            set => SetProperty(ref _rationale, value);
        }

        // Whether the user explicitly promoted this question into an assumption
        private bool _markedAsAssumption;
        public bool MarkedAsAssumption
        {
            get => _markedAsAssumption;
            set
            {
                if (SetProperty(ref _markedAsAssumption, value))
                {
                    OnPropertyChanged(nameof(IsAnswered));
                }
            }
        }

        // Controls fade-out animation when question is being replaced
        private bool _isFadingOut;
        public bool IsFadingOut
        {
            get => _isFadingOut;
            set => SetProperty(ref _isFadingOut, value);
        }

        // Indicates question has been submitted (for partial fade on answered questions)
        private bool _isSubmitted;
        public bool IsSubmitted
        {
            get => _isSubmitted;
            set => SetProperty(ref _isSubmitted, value);
        }

        // Derived property used by the UI to decide whether the question is "done"
        public bool IsAnswered => !string.IsNullOrWhiteSpace(Answer) || MarkedAsAssumption;

        /// <summary>
        /// Set properties without triggering dirty flag (used during loading).
        /// </summary>
        public void SetPropertiesForLoad(string text, string? answer, string? category, string severity, string? rationale, bool markedAsAssumption, bool isSubmitted = false)
        {
            _isLoading = true;
            try
            {
                Text = text;
                Answer = answer;
                Category = category;
                Severity = severity;
                Rationale = rationale;
                MarkedAsAssumption = markedAsAssumption;
                IsSubmitted = isSubmitted;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void Options_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Raise notification for HasOptions so DataTriggers update
            OnPropertyChanged(nameof(HasOptions));
        }

        // Safe replace helper that marshals to the UI thread if needed.
        public void ReplaceOptions(IReadOnlyList<string>? items)
        {
            void update()
            {
                Options.Clear();
                if (items == null) return;
                foreach (var s in items) Options.Add(s);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) update();
            else dispatcher.Invoke(update);
        }

        // Utility: create a DefaultItem using the project's model (uses Name/Description)
        public DefaultItem ToDefaultItem()
        {
            return new DefaultItem
            {
                Key = GenerateKeyFromText(Text),
                Name = Text,
                Description = Rationale, // use rationale as a short description if present
                IsEnabled = true
            };
        }

        private static string GenerateKeyFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Guid.NewGuid().ToString("N");
            var t = text.Trim().ToLowerInvariant();
            var arr = t.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
            var cleaned = new string(arr);
            var slug = string.Join('-', cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            slug = slug.Length > 40 ? slug.Substring(0, 40) : slug;
            return slug;
        }
    }
}