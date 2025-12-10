using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class ImportWorkflowViewModel : ObservableObject
    {
        public event EventHandler<ImportWorkflowCompletedEventArgs>? ImportWorkflowCompleted;
        public event EventHandler? ImportWorkflowCancelled;

        [ObservableProperty]
        private bool _isVisible;

        [ObservableProperty]
        private string _selectedDocumentPath = string.Empty;

        [ObservableProperty]
        private string _workspaceName = string.Empty;

        [ObservableProperty]
        private string _workspaceSavePath = string.Empty;

        [ObservableProperty]
        private bool _autoAnalyzeRequirements = true;

        [ObservableProperty]
        private bool _exportForChatGpt = false;

        [ObservableProperty]
        private bool _canProceed = false;

        // Commands
        public ICommand SelectDocumentCommand { get; }
        public ICommand ChooseWorkspaceLocationCommand { get; }
        public ICommand StartImportCommand { get; }
        public ICommand CancelCommand { get; }

        public ImportWorkflowViewModel()
        {
            SelectDocumentCommand = new RelayCommand(SelectDocument);
            ChooseWorkspaceLocationCommand = new RelayCommand(ChooseWorkspaceLocation);
            StartImportCommand = new RelayCommand(StartImport, CanStartImport);
            CancelCommand = new RelayCommand(Cancel);

            // Watch for changes that affect whether we can proceed
            PropertyChanged += OnPropertyChanged;
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SelectedDocumentPath) or nameof(WorkspaceName) or nameof(WorkspaceSavePath))
            {
                UpdateCanProceed();
                ((RelayCommand)StartImportCommand).NotifyCanExecuteChanged();
            }
        }

        private void UpdateCanProceed()
        {
            CanProceed = !string.IsNullOrWhiteSpace(SelectedDocumentPath) && 
                        File.Exists(SelectedDocumentPath) && 
                        !string.IsNullOrWhiteSpace(WorkspaceName) && 
                        !string.IsNullOrWhiteSpace(WorkspaceSavePath);
        }

        public void Show()
        {
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;
        }

        private void SelectDocument()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Document to Import",
                Filter = "Word Documents (*.docx)|*.docx|All Files (*.*)|*.*",
                DefaultExt = ".docx"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedDocumentPath = openFileDialog.FileName;
            }
        }

        private void ChooseWorkspaceLocation()
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Workspace As",
                Filter = "Workspace Files (*.workspace)|*.workspace|All Files (*.*)|*.*",
                DefaultExt = ".workspace",
                FileName = string.IsNullOrWhiteSpace(WorkspaceName) ? "My Workspace" : WorkspaceName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                WorkspaceSavePath = saveFileDialog.FileName;
                // Update workspace name if user changed it in the dialog
                if (!string.IsNullOrWhiteSpace(System.IO.Path.GetFileNameWithoutExtension(saveFileDialog.FileName)))
                {
                    WorkspaceName = System.IO.Path.GetFileNameWithoutExtension(saveFileDialog.FileName);
                }
            }
        }

        private bool CanStartImport()
        {
            return CanProceed;
        }

        private void StartImport()
        {
            if (!CanStartImport()) return;

            var args = new ImportWorkflowCompletedEventArgs
            {
                DocumentPath = SelectedDocumentPath,
                WorkspaceName = WorkspaceName,
                WorkspaceSavePath = WorkspaceSavePath,
                AutoAnalyzeRequirements = AutoAnalyzeRequirements,
                ExportForChatGpt = ExportForChatGpt
            };

            Hide();
            ImportWorkflowCompleted?.Invoke(this, args);
        }

        private void Cancel()
        {
            Hide();
            ImportWorkflowCancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    public class ImportWorkflowCompletedEventArgs : EventArgs
    {
        public string DocumentPath { get; set; } = string.Empty;
        public string WorkspaceName { get; set; } = string.Empty;
        public string WorkspaceSavePath { get; set; } = string.Empty;
        public bool AutoAnalyzeRequirements { get; set; }
        public bool ExportForChatGpt { get; set; }
    }
}