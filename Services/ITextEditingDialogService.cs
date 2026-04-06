using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for handling text editing dialogs with architectural compliance
    /// Follows dependency injection and domain coordination patterns
    /// </summary>
    public interface ITextEditingDialogService
    {
        /// <summary>
        /// Show dialog for editing supplemental information text with separator support
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="currentText">Current text to edit</param>
        /// <param name="separator">Separator used to split text into items</param>
        /// <returns>Edited text if confirmed, null if cancelled</returns>
        Task<string?> ShowSupplementalInfoEditDialog(string title, string currentText, string separator = " ||| ");
    }
}