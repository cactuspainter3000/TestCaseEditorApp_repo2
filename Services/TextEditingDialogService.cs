using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TestCaseEditorApp.MVVM.Views;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of text editing dialog service using existing SupplementalInfoEditorWindow
    /// Follows architectural guidelines with proper dependency injection and error handling
    /// </summary>
    public class TextEditingDialogService : ITextEditingDialogService
    {
        private readonly ILogger<TextEditingDialogService> _logger;

        public TextEditingDialogService(ILogger<TextEditingDialogService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string?> ShowSupplementalInfoEditDialog(string title, string currentText, string separator = " ||| ")
        {
            if (string.IsNullOrEmpty(title))
                throw new ArgumentException("Title cannot be null or empty", nameof(title));

            try
            {
                _logger.LogDebug("Showing supplemental info edit dialog: {Title}", title);

                // Use Task to properly handle UI thread coordination
                var tcs = new TaskCompletionSource<string?>();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Convert current text to ParagraphViewModel collection for dialog constructor
                        var items = new ObservableCollection<ParagraphViewModel>();
                        if (!string.IsNullOrEmpty(currentText))
                        {
                            var textItems = currentText.Split(new[] { separator }, StringSplitOptions.None)
                                                     .Select(s => s.Trim())
                                                     .Where(s => !string.IsNullOrEmpty(s));
                            
                            foreach (var item in textItems)
                            {
                                items.Add(new ParagraphViewModel(item) { IsSelected = true });
                            }
                        }

                        var dialog = new SupplementalInfoEditorWindow(items)
                        {
                            Title = title,
                            Owner = Application.Current.MainWindow
                        };

                        var result = dialog.ShowDialog();
                        if (result == true)
                        {
                            var editedText = string.Join(separator, dialog.ResultItems);
                            _logger.LogDebug("Dialog confirmed with {Count} items", dialog.ResultItems.Count);
                            tcs.SetResult(editedText);
                        }
                        else
                        {
                            _logger.LogDebug("Dialog cancelled");
                            tcs.SetResult(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error showing supplemental info edit dialog");
                        tcs.SetException(ex);
                    }
                });

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show supplemental info edit dialog");
                throw;
            }
        }
    }
}