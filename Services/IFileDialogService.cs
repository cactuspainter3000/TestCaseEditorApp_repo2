// Services/IFileDialogService.cs
using Microsoft.Win32;

namespace TestCaseEditorApp.Services
{
    public interface IFileDialogService
    {
        /// <summary>Shows a Save dialog and returns the chosen full path, or null if cancelled.</summary>
        string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null);
        
        /// <summary>Shows an Open dialog and returns the chosen full path, or null if cancelled.</summary>
        string? ShowOpenFile(string title, string filter, string? initialDirectory = null);
        
        /// <summary>Shows a Folder dialog and returns the chosen folder path, or null if cancelled.</summary>
        string? ShowFolderDialog(string title, string? initialDirectory = null);
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
        
        public string? ShowOpenFile(string title, string filter, string? initialDirectory = null)
        {
            var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                Multiselect = false,
                RestoreDirectory = true,
                InitialDirectory = initialDirectory ?? string.Empty
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
        
        public string? ShowFolderDialog(string title, string? initialDirectory = null)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title,
                InitialDirectory = initialDirectory ?? string.Empty
            };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }
    }
}

