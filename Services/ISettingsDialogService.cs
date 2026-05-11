using System.Windows;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    public interface ISettingsDialogService
    {
        bool ShowSettingsDialog(Window? owner = null, bool isRequired = false);
        bool ShowSettingsDialog(Func<Task<(bool Success, List<string> Issues)>>? validationCallback, Window? owner = null, bool isRequired = false);
    }
}
