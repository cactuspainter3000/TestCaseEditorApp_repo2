using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class SubMenuItemViewModel : ObservableObject
    {
        public SubMenuItemViewModel(string displayName, ICommand? command = null)
        {
            DisplayName = displayName;
            Command = command;
        }

        public string DisplayName { get; }

        // Command to execute when this submenu item is activated
        public ICommand? Command { get; }
    }
}