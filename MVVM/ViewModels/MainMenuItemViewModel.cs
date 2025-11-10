using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class MainMenuItemViewModel : ObservableObject
    {
        public MainMenuItemViewModel(string displayName)
        {
            DisplayName = displayName;
            Children = new ObservableCollection<SubMenuItemViewModel>();
            ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        }

        public string DisplayName { get; }

        public ObservableCollection<SubMenuItemViewModel> Children { get; }

        // Optional: bind ToggleButton.IsChecked to this instead of ListBoxItem.IsSelected
        [ObservableProperty]
        private bool isExpanded;

        // Command to toggle IsExpanded (optional depending on template)
        public ICommand ToggleExpandCommand { get; }
    }
}