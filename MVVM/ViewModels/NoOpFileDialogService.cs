using System;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// No-op IFileDialogService used for design-time or when DI isn't configured.
    /// </summary>
    internal class NoOpFileDialogService : IFileDialogService
    {
        public string ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string initialDirectory)
        {
            // Return empty string to indicate "no file chosen" in the design-time scenario.
            return string.Empty;
        }
    }
}