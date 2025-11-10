using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Header VM for Test Flow Creator section.
    public class TestFlowHeaderViewModel : ObservableObject
    {
        public TestFlowHeaderViewModel()
        {
            NewFlowCommand = new RelayCommand(OnNewFlow);
            ImportFlowCommand = new RelayCommand(OnImportFlow);
        }

        public ICommand NewFlowCommand { get; }
        public ICommand ImportFlowCommand { get; }

        private void OnNewFlow()
        {
            // wire to navigation/logic in your app
        }

        private void OnImportFlow()
        {
            // wire to import logic
        }
    }
}