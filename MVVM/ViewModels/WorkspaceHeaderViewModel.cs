using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Strongly-typed WorkspaceHeaderViewModel wired to your Requirement model.
    /// Uses the actual Requirement properties (Name, Item, Description, Status) you provided.
    /// </summary>
    public class WorkspaceHeaderViewModel : ObservableObject
    {
        private readonly MainViewModel _main;

        public WorkspaceHeaderViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));

            // Optional quick-action commands -- no-op by default
            EditRequirementCommand = new RelayCommand(() => { /* hook for future */ }, () => true);
            OpenRequirementCommand = new RelayCommand(() => { /* hook for future */ }, () => true);

            if (_main is INotifyPropertyChanged pc)
            {
                pc.PropertyChanged += MainOnPropertyChanged;
            }
        }

        private void MainOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Propagate changes for the bindings used in the view
            if (e.PropertyName == nameof(MainViewModel.WorkspacePath) ||
                e.PropertyName == nameof(MainViewModel.CurrentRequirement) ||
                e.PropertyName == nameof(MainViewModel.HasUnsavedChanges) ||
                e.PropertyName == nameof(MainViewModel.WordFilePath))
            {
                OnPropertyChanged(nameof(WorkspaceName));
                OnPropertyChanged(nameof(CurrentRequirementTitle));
                OnPropertyChanged(nameof(CurrentRequirementSummary));
                OnPropertyChanged(nameof(CurrentRequirementId));
                OnPropertyChanged(nameof(CurrentRequirementStatus));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(SourceInfo));
            }
        }

        // Workspace display name
        public string WorkspaceName =>
            string.IsNullOrWhiteSpace(_main.WorkspacePath)
                ? "(unsaved workspace)"
                : System.IO.Path.GetFileNameWithoutExtension(_main.WorkspacePath);

        // Primary requirement title: uses Requirement.Name
        public string CurrentRequirementTitle =>
            _main.CurrentRequirement != null && !string.IsNullOrWhiteSpace(_main.CurrentRequirement.Name)
                ? _main.CurrentRequirement.Name
                : "(no requirement selected)";

        // Short summary / description: uses Requirement.Description
        public string? CurrentRequirementSummary => string.IsNullOrWhiteSpace(_main.CurrentRequirement?.Description)
            ? null
            : _main.CurrentRequirement!.Description;

        // Identifier (ID): uses Requirement.Item (e.g., C1XMA2405-REQ_RC-108)
        public string? CurrentRequirementId => string.IsNullOrWhiteSpace(_main.CurrentRequirement?.Item)
            ? null
            : _main.CurrentRequirement!.Item;

        // Status: uses Requirement.Status
        public string? CurrentRequirementStatus => string.IsNullOrWhiteSpace(_main.CurrentRequirement?.Status)
            ? null
            : _main.CurrentRequirement!.Status;

        // Unsaved flag forwarded from main viewmodel
        public bool HasUnsavedChanges => _main.HasUnsavedChanges;

        // Source file / workspace info
        public string? SourceInfo => _main.WorkspacePath is null ? _main.WordFilePath : System.IO.Path.GetFileName(_main.WorkspacePath);

        // Commands (optional hooks)
        public IRelayCommand EditRequirementCommand { get; }
        public IRelayCommand OpenRequirementCommand { get; }
    }
}