using System.Windows;

namespace TestCaseEditorApp.Services
{
    public interface ISettingsDialogService
    {
        bool ShowSettingsDialog(Window? owner = null, bool isRequired = false);
    }
}
