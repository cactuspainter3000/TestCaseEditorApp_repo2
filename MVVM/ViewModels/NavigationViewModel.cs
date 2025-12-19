using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class NavigationViewModel : ObservableObject
    {
        public NavigationViewModel()
        {
            // Example items using Segoe MDL2 glyph codepoints
            NavigationItems = new ObservableCollection<NavigationItem>
            {
                new NavigationItem("home", "Home", "\uE10F"),
                new NavigationItem("requirements", "Requirements", "\uE8A5"),
                new NavigationItem("tests", "Tests", "\uE7C3"),
                new NavigationItem("settings", "Settings", "\uE713")
            };

            _history = new System.Collections.Generic.List<string>();
            SelectedItem = NavigationItems.FirstOrDefault();
            UpdateIndexCounters();
        }

        private readonly System.Collections.Generic.List<string> _history;
        private int _historyIndex = -1;

        [ObservableProperty]
        private ObservableCollection<NavigationItem> navigationItems;

        [ObservableProperty]
        private NavigationItem? selectedItem;

        partial void OnSelectedItemChanged(NavigationItem? oldValue, NavigationItem? newValue)
        {
            // Whenever SelectedItem is changed (from UI selection or programmatically), update index counters.
            UpdateIndexCounters();
        }

        [ObservableProperty]
        private string? searchText;

        public bool CanGoBack => _historyIndex > 0;
        public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

        // New: expose 1-based current index and total count for the UI indicator
        [ObservableProperty]
        private int currentIndex;

        [ObservableProperty]
        private int totalCount;

        public event EventHandler<string?>? NavigationRequested;

        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void GoBack()
        {
            if (!CanGoBack) return;
            _historyIndex--;
            var id = _history[_historyIndex];
            SelectById(id);
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanGoForward))]
        private void GoForward()
        {
            if (!CanGoForward) return;
            _historyIndex++;
            var id = _history[_historyIndex];
            SelectById(id);
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void Navigate(NavigationItem? item)
        {
            if (item is null) return;

            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(item.Id);
            _historyIndex = _history.Count - 1;

            SelectedItem = item;
            NavigationRequested?.Invoke(this, item.Id);

            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
            UpdateIndexCounters();
        }

        [RelayCommand]
        private void Search()
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return;

            var q = SearchText!.Trim();
            var found = NavigationItems.FirstOrDefault(n => n.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                                         || n.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            if (found != null) Navigate(found);
        }

        public void SelectById(string id)
        {
            var item = NavigationItems.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                SelectedItem = item;
                NavigationRequested?.Invoke(this, item.Id);
                UpdateIndexCounters();
            }
        }

        private void UpdateIndexCounters()
        {
            // TotalCount is simple
            TotalCount = NavigationItems?.Count ?? 0;

            // CurrentIndex is 1-based index of SelectedItem in NavigationItems; zero if none selected
            if (SelectedItem is null || NavigationItems is null || NavigationItems.Count == 0)
            {
                CurrentIndex = 0;
            }
            else
            {
                var idx = NavigationItems.IndexOf(SelectedItem);
                CurrentIndex = (idx >= 0) ? (idx + 1) : 0;
            }
        }

        // NEW: adapter that holds the requirements navigation VM for binding in the NavigationView.
        // Set this from MainViewModel after you create the shared TestCaseGenerator_NavigationService.
        public TestCaseGenerator_NavigationVM? RequirementsNav { get; set; }
    }
}