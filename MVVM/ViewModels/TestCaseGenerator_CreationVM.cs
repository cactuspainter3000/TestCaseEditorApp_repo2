using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public class TestCaseGenerator_CreationVM : ObservableObject
    {
        public TestCaseGenerator_CreationVM()
        {
            TestCases = new ObservableCollection<string>
            {
                "TC-001: Login with valid credentials",
                "TC-002: Logout clears token"
            };

            AddTestCaseCommand = new RelayCommand(() => TestCases.Add($"TC-{TestCases.Count + 1:000}: New test case"));
            RemoveTestCaseCommand = new RelayCommand(() =>
            {
                if (SelectedTestCase != null)
                {
                    TestCases.Remove(SelectedTestCase);
                    SelectedTestCase = null;
                }
            });
        }

        public ObservableCollection<string> TestCases { get; }

        private string? _selectedTestCase;
        public string? SelectedTestCase
        {
            get => _selectedTestCase;
            set => SetProperty(ref _selectedTestCase, value);
        }

        public ICommand AddTestCaseCommand { get; }
        public ICommand RemoveTestCaseCommand { get; }
    }
}