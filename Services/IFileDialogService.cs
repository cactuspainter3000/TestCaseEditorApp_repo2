// Services/IFileDialogService.cs
using Microsoft.Win32;

namespace TestCaseEditorApp.Services
{
    public interface IFileDialogService
    {
        /// <summary>Shows a Save dialog and returns the chosen full path, or null if cancelled.</summary>
        string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null);
    }

    public sealed class FileDialogService : IFileDialogService
    {
        public string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null)
        {
            var dlg = new SaveFileDialog
            {
                Title = title,
                FileName = suggestedFileName,
                Filter = filter,                 // e.g., "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*"
                DefaultExt = defaultExt,         // e.g., ".tcex.json"
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true,         // Remember the last used directory
                InitialDirectory = initialDirectory ?? string.Empty
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}

