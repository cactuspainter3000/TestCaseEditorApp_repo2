using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestCaseEditorApp.Models;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Import-first MainViewModel wired to the real IRequirementService and WorkspaceService.
    /// Keeps the UI-focused helpers minimal so it integrates with your existing services.
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // Core state
        [ObservableProperty] private ObservableCollection<Requirement> requirements = new();
        [ObservableProperty] private Requirement? currentRequirement;
        [ObservableProperty] private string? workspacePath;
        [ObservableProperty] private bool hasUnsavedChanges;
        [ObservableProperty] private string? statusMessage;

        // Import/source paths
        [ObservableProperty] private string? wordFilePath;
        [ObservableProperty] private string? currentSourcePath;

        // Left‑menu step list expected by MainWindow.xaml bindings
        // These were missing and caused the runtime binding errors.
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; } = new ObservableCollection<StepDescriptor>();

        private StepDescriptor? _selectedStep;
        public StepDescriptor? SelectedStep
        {
            get => _selectedStep;
            set => SetProperty(ref _selectedStep, value);
        }

        private readonly IRequirementService _requirementService;
        private readonly IFileDialogService _fileDialog;
        private DispatcherTimer? _statusTimer;

        public MainViewModel(IRequirementService requirementService, IFileDialogService? fileDialog = null)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _fileDialog = fileDialog ?? new FileDialogService();

            // Keep counts up-to-date
            Requirements.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(TotalRequirementsCount));
                OnPropertyChanged(nameof(RequirementPositionDisplay));
            };

            // Provide some initial menu items so the UI shows the menu immediately.
            // Replace or populate these from your app wiring if you have a central menu definition.
            TestCaseCreationSteps.Add(new StepDescriptor { DisplayName = "Requirements", Badge = "" });
            TestCaseCreationSteps.Add(new StepDescriptor { DisplayName = "Clarifying Questions", Badge = "" });
            TestCaseCreationSteps.Add(new StepDescriptor { DisplayName = "Test Case Creation", Badge = "" });

            // Optionally set a default selected step:
            SelectedStep = TestCaseCreationSteps.FirstOrDefault();
        }

        public int TotalRequirementsCount => Requirements?.Count ?? 0;
        public string RequirementPositionDisplay =>
            Requirements.Count == 0 || CurrentRequirement == null
                ? string.Empty
                : $"{Requirements.IndexOf(CurrentRequirement) + 1} of {Requirements.Count}";

        [RelayCommand]
        private async Task ImportWordAsync()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open requirements document (.docx)",
                Filter = "Word Documents (*.docx)|*.docx",
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() != true)
            {
                SetTransientStatus("Import cancelled.", 2);
                return;
            }
            await ImportFromPathAsync(dlg.FileName, replace: true);
        }

        [RelayCommand]
        private async Task ReloadAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentSourcePath))
            {
                SetTransientStatus("No source loaded to reload.", 3);
                return;
            }
            await ImportFromPathAsync(CurrentSourcePath!, replace: true);
        }

        [RelayCommand]
        private async Task SaveWorkspaceAsync()
        {
            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("Nothing to save.", 2);
                return;
            }

            var suggested = $"{(string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(WordFilePath)) ? "Workspace" : Path.GetFileNameWithoutExtension(WordFilePath))}.tcex.json";
            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestCaseEditorApp", "Sessions");
            Directory.CreateDirectory(defaultFolder);

            var chosen = _fileDialog.ShowSaveFile(
                title: "Save Workspace",
                suggestedFileName: suggested,
                filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                defaultExt: ".tcex.json",
                initialDirectory: defaultFolder);

            if (string.IsNullOrWhiteSpace(chosen))
            {
                SetTransientStatus("Save cancelled.", 2);
                return;
            }

            WorkspacePath = chosen;
            var ws = new Workspace
            {
                SourceDocPath = CurrentSourcePath,
                Requirements = Requirements.ToList()
            };

            try
            {
                WorkspaceService.Save(WorkspacePath!, ws);
                HasUnsavedChanges = false;
                SetTransientStatus($"Saved workspace: {Path.GetFileName(WorkspacePath)}", 4);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save workspace: {ex.Message}", "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadWorkspace()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Open Saved Session",
                Filter = "Test Case Editor Session|*.tcex.json",
                DefaultExt = ".tcex.json",
                RestoreDirectory = true,
                InitialDirectory = !string.IsNullOrWhiteSpace(WorkspacePath) ? Path.GetDirectoryName(WorkspacePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (ofd.ShowDialog() != true) return;

            WorkspacePath = ofd.FileName;
            try
            {
                var ws = WorkspaceService.Load(WorkspacePath!);
                if (ws == null)
                {
                    SetTransientStatus("Failed to load workspace (file empty or invalid).", 4);
                    return;
                }

                Requirements.Clear();
                foreach (var r in ws.Requirements ?? Enumerable.Empty<Requirement>())
                    Requirements.Add(r);

                CurrentRequirement = Requirements.FirstOrDefault();
                CurrentSourcePath = ws.SourceDocPath;
                SetTransientStatus($"Opened workspace: {Path.GetFileName(WorkspacePath)} • {Requirements.Count} requirements", 4);
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load workspace: {ex.Message}", "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // central import path used by Import/Reload
        private async Task ImportFromPathAsync(string path, bool replace)
        {
            if (replace && HasUnsavedChanges && Requirements.Count > 0)
            {
                var res = MessageBox.Show(
                    "You have unsaved changes. Replace the current requirements with the new import?",
                    "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes)
                {
                    SetTransientStatus("Import canceled.", 2);
                    return;
                }
            }

            try
            {
                SetTransientStatus($"Importing {Path.GetFileName(path)}…", 0);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                // Use the real service (synchronous work executed off UI thread)
                var reqs = await Task.Run(() => _requirementService.ImportRequirementsFromJamaAllDataDocx(path));
                sw.Stop();

                // Create workspace and save JSON immediately
                var ws = new Workspace
                {
                    SourceDocPath = path,
                    Requirements = reqs.ToList()
                };

                Requirements.Clear();
                foreach (var r in reqs) Requirements.Add(r);

                // Ask for the workspace file name before saving
                var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestCaseEditorApp", "Sessions");
                Directory.CreateDirectory(defaultFolder);
                var suggested = Path.GetFileNameWithoutExtension(path) + ".tcex.json";

                var chosen = _fileDialog.ShowSaveFile(
                    title: "Create Workspace",
                    suggestedFileName: suggested,
                    filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                    defaultExt: ".tcex.json",
                    initialDirectory: defaultFolder);

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    SetTransientStatus("Import canceled (no workspace name selected).", 2);
                    return;
                }

                WorkspacePath = chosen;
                WorkspaceService.Save(WorkspacePath!, ws);
                HasUnsavedChanges = false;
                CurrentSourcePath = path;
                WordFilePath = path;
                CurrentRequirement = Requirements.FirstOrDefault();

                SetTransientStatus($"Workspace created: {Path.GetFileName(WorkspacePath)} • {Requirements.Count} requirement(s) • {sw.ElapsedMilliseconds} ms", 6);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                SetTransientStatus("Import failed: file access error (close Word if it's open).", 5);
            }
            catch (Exception ex)
            {
                SetTransientStatus("Import failed: " + ex.Message, 6);
            }
        }

        private void SetTransientStatus(string message, int seconds = 3)
        {
            StatusMessage = message;
            _statusTimer?.Stop();
            if (seconds > 0)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                _statusTimer.Tick += (_, __) =>
                {
                    _statusTimer?.Stop();
                    StatusMessage = null;
                };
                _statusTimer.Start();
            }
        }

        public void Dispose()
        {
            // Nothing to dispose currently.
        }
    }
}