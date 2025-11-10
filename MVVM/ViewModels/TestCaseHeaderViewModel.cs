using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Header VM for Test Case Creator section. Add properties/commands that the header view will bind to.
    public class TestCaseHeaderViewModel : ObservableObject
    {
        public TestCaseHeaderViewModel()
        {
            NewTestCaseCommand = new RelayCommand(OnNewTestCase);
            OpenRequirementsCommand = new RelayCommand(OnOpenRequirements);
            GenerateTestCasesCommand = new RelayCommand(OnGenerateTestCases);
        }

        public ICommand NewTestCaseCommand { get; }
        public ICommand OpenRequirementsCommand { get; }
        public ICommand GenerateTestCasesCommand { get; }

        private void OnNewTestCase()
        {
            // wire to navigation/logic in your app
        }

        private void OnOpenRequirements()
        {
            // wire to navigation/logic in your app
        }

        private void OnGenerateTestCases()
        {
            // wire to generation logic
        }
    }
}