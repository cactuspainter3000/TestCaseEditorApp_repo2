using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class ImportMethodSelectionViewModel : ObservableObject
    {
        public event EventHandler<ImportMethodSelectedEventArgs>? ImportMethodSelected;

        [ObservableProperty]
        private bool _isVisible;

        public ICommand WordImportCommand { get; }
        public ICommand WordImportNoAnalysisCommand { get; }
        public ICommand ImportWorkflowCommand { get; }
        public ICommand CloseCommand { get; }

        public ImportMethodSelectionViewModel()
        {
            WordImportCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.Word));
            WordImportNoAnalysisCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.WordNoAnalysis));
            ImportWorkflowCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.ImportWorkflow));
            CloseCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.Skip));
        }

        public void Show()
        {
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;
        }

        private void SelectImportMethod(ImportMethod method)
        {
            Hide();
            ImportMethodSelected?.Invoke(this, new ImportMethodSelectedEventArgs(method));
        }
    }

    public enum ImportMethod
    {
        Word,
        WordNoAnalysis,
        ImportWorkflow,
        Skip
    }

    public class ImportMethodSelectedEventArgs : EventArgs
    {
        public ImportMethod Method { get; }

        public ImportMethodSelectedEventArgs(ImportMethod method)
        {
            Method = method;
        }
    }
}