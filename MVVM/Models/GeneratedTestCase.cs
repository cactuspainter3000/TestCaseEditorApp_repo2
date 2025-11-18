using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.Models
{
    public class GeneratedTestCase : ObservableObject
    {
        private string _title = string.Empty;
        private string _preconditions = string.Empty;
        private string _steps = string.Empty;
        private string _expectedResults = string.Empty;
        private bool _isSelected;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Preconditions
        {
            get => _preconditions;
            set => SetProperty(ref _preconditions, value);
        }

        public string Steps
        {
            get => _steps;
            set => SetProperty(ref _steps, value);
        }

        public string ExpectedResults
        {
            get => _expectedResults;
            set => SetProperty(ref _expectedResults, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
