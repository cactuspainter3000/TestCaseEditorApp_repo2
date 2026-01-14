using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for text splitting modal.
    /// Allows users to split text using ||| delimiters.
    /// </summary>
    public partial class SplitTextEditorViewModel : ObservableObject
    {
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private string _editedText = string.Empty;

        [ObservableProperty]
        private List<string> _previewItems = new();

        [ObservableProperty]
        private string _statusMessage = "Use ||| to mark where you want to split the text.";

        /// <summary>
        /// Event raised when the user completes the split operation
        /// </summary>
        public event EventHandler<TextSplitCompletedEventArgs>? SplitCompleted;

        /// <summary>
        /// Event raised when the user cancels the operation
        /// </summary>
        public event EventHandler? Cancelled;

        public ICommand SplitCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand InsertSplitCommand { get; }

        public SplitTextEditorViewModel(string originalText, NotificationService notificationService)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            EditedText = originalText ?? string.Empty;
            
            // Initialize commands
            SplitCommand = new RelayCommand(PerformSplit);
            CancelCommand = new RelayCommand(Cancel);
            InsertSplitCommand = new RelayCommand(InsertSplit);

            // Update preview immediately
            UpdatePreview();
        }

        private void PerformSplit()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EditedText))
                {
                    StatusMessage = "Please enter some text to split.";
                    return;
                }

                if (!EditedText.Contains("|||"))
                {
                    StatusMessage = "Please add ||| markers where you want to split the text.";
                    return;
                }

                var splitResults = EditedText.Split(new[] { "|||" }, StringSplitOptions.None)
                                           .Select(s => s.Trim())
                                           .Where(s => !string.IsNullOrWhiteSpace(s))
                                           .ToList();

                if (splitResults.Count < 2)
                {
                    StatusMessage = "Split must result in at least 2 items.";
                    return;
                }

                // Raise event with results
                SplitCompleted?.Invoke(this, new TextSplitCompletedEventArgs(splitResults));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[SplitTextEditor] Error performing split");
                _notificationService.ShowError($"Failed to split text: {ex.Message}");
            }
        }

        private void Cancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        private void InsertSplit()
        {
            // Simple implementation - just append to the end for now
            // In a real implementation, you'd need cursor position from the view
            if (!EditedText.EndsWith(" "))
            {
                EditedText += " ";
            }
            EditedText += "||| ";
            
            UpdatePreview();
        }

        partial void OnEditedTextChanged(string value)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EditedText))
                {
                    PreviewItems = new List<string>();
                    StatusMessage = "Enter text above to see preview.";
                    return;
                }

                var parts = EditedText.Split(new[] { "|||" }, StringSplitOptions.None)
                                     .Select(s => s.Trim())
                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                     .ToList();

                PreviewItems = parts.Count > 0 ? parts : new List<string> { EditedText };

                if (EditedText.Contains("|||"))
                {
                    StatusMessage = $"Will create {PreviewItems.Count} items after split.";
                }
                else
                {
                    StatusMessage = "Use ||| to mark where you want to split the text.";
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[SplitTextEditor] Error updating preview");
                PreviewItems = new List<string>();
                StatusMessage = "Error updating preview.";
            }
        }
    }

    /// <summary>
    /// Event args for split completed event
    /// </summary>
    public class TextSplitCompletedEventArgs : EventArgs
    {
        public List<string> SplitResults { get; }

        public TextSplitCompletedEventArgs(List<string> splitResults)
        {
            SplitResults = splitResults ?? new List<string>();
        }
    }
}