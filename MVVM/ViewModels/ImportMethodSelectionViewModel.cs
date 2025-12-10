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
        public ICommand QuickImportCommand { get; }
        public ICommand SkipImportCommand { get; }
        public ICommand CloseCommand { get; }

        public ImportMethodSelectionViewModel()
        {
            WordImportCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.Word));
            QuickImportCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.Quick));
            SkipImportCommand = new RelayCommand(() => SelectImportMethod(ImportMethod.Skip));
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
        Quick,
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