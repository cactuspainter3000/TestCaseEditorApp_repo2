using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Lightweight placeholder VM used for menu items that don't have a dedicated ViewModel yet.
    public class PlaceholderViewModel : ObservableObject
    {
        public PlaceholderViewModel(string title)
        {
            Title = title;
        }

        public string Title { get; }
    }
}