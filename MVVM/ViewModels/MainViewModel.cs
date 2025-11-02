using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
        private readonly WorkspaceHeaderViewModel _headerViewModel;
        private readonly IRequirementService _requirementService;
        private readonly IPersistenceService _persistence;
        private readonly IFileDialogService _fileDialog;

        // Core state (CommunityToolkit source generators will produce backing fields)
        [ObservableProperty] private ObservableCollection<Requirement> requirements = new();
        [ObservableProperty] private Requirement? currentRequirement;
        [ObservableProperty] private string? workspacePath;
        [ObservableProperty] private bool hasUnsavedChanges;
        [ObservableProperty] private string? statusMessage;

        // Import/source paths
        [ObservableProperty] private string? wordFilePath;
        [ObservableProperty] private string? currentSourcePath;

        // Services and helpers (single declarations)
        private readonly IServiceProvider _services;
        private RequirementsIndexViewModel? _requirementsNavigator;

        // UI helpers
        private DispatcherTimer? _statusTimer;

        // Persistence key for selected step
        private const string SelectedStepKey = "Main.SelectedStep";

        // Content area viewmodel (bound from XAML ContentControl)
        private object? _currentStepViewModel;

        public object? CurrentStepViewModel
        {
            get => _currentStepViewModel;
            set => SetProperty(ref _currentStepViewModel, value);
        }

        public WorkspaceHeaderViewModel HeaderViewModel => _headerViewModel;
        public NavigationViewModel Navigation { get; }
        public RequirementsIndexViewModel RequirementsNavigator => _requirementsNavigator!;

        // Left‑menu step list expected by MainWindow.xaml bindings
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; } = new ObservableCollection<StepDescriptor>();

        // Selected step (factory invoked here to produce the content VM)
        private StepDescriptor? _selectedStep;
        public StepDescriptor? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (!SetProperty(ref _selectedStep, value)) return;

                System.Diagnostics.Debug.WriteLine($"[SelectedStep] invoked. DisplayName='{value?.DisplayName}', Id='{value?.Id}'");

                if (value?.CreateViewModel == null)
                {
                    System.Diagnostics.Debug.WriteLine("[SelectedStep] CreateViewModel is null — nothing to create.");
                    CurrentStepViewModel = null;
                    return;
                }

                try
                {
                    // Invoke factory. If your factories don't use IServiceProvider, it's okay to ignore it.
                    var created = value.CreateViewModel(_services);
                    System.Diagnostics.Debug.WriteLine($"[SelectedStep] factory returned: {(created == null ? "null" : created.GetType().FullName)}");

                    CurrentStepViewModel = created;
                    System.Diagnostics.Debug.WriteLine($"[SelectedStep] CurrentStepViewModel assigned: {(CurrentStepViewModel == null ? "null" : CurrentStepViewModel.GetType().FullName)}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SelectedStep] Exception invoking CreateViewModel: {ex}");
                    CurrentStepViewModel = null;
                }

                // persist selection if applicable
                try
                {
                    if (value != null)
                    {
                        _persistence?.Save(SelectedStepKey, value.Id);
                        System.Diagnostics.Debug.WriteLine($"[SelectedStep] Persisted SelectedStep.Id='{value.Id}'");
                    }
                }
                catch (Exception exPersist)
                {
                    System.Diagnostics.Debug.WriteLine($"[SelectedStep] Persistence save failed: {exPersist}");
                }
            }
        }

        // Constructor - accept services required by factories and viewmodels
        public MainViewModel(
            IRequirementService requirementService,
            IPersistenceService persistence,
            WorkspaceHeaderViewModel headerViewModel,
            NavigationViewModel navigationViewModel,
            IFileDialogService fileDialog)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));

            _headerViewModel = headerViewModel ?? throw new ArgumentNullException(nameof(headerViewModel));

            Navigation = navigationViewModel ?? throw new ArgumentNullException(nameof(navigationViewModel));

            // Keep counts up-to-date
            Requirements.CollectionChanged += RequirementsOnCollectionChanged;

            _requirementsNavigator = new RequirementsIndexViewModel(Requirements,
                () => CurrentRequirement,
                r => CurrentRequirement = r);

            // Populate the steps with factories that produce viewmodels.
            // Use services or captured concrete services as needed.
            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "requirements",
                DisplayName = "Requirements",
                Badge = "",
                CreateViewModel = svc => new RequirementsViewModel(_persistence)
            });

            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "clarifying-questions",
                DisplayName = "Clarifying Questions",
                Badge = "",
                CreateViewModel = svc => new ClarifyingQuestionsViewModel(_persistence)
            });

            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "testcase-creation",
                DisplayName = "Test Case Creation",
                Badge = "",
                CreateViewModel = svc => new TestCaseCreationViewModel()
            });

            // Select a step that actually has a factory
            SelectedStep = TestCaseCreationSteps.FirstOrDefault(s => s.CreateViewModel != null);
        }

        private void RequirementsOnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(TotalRequirementsCount));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
        }

        // Convenience properties
        public int TotalRequirementsCount => Requirements?.Count ?? 0;
        public string RequirementPositionDisplay =>
            Requirements == null || Requirements.Count == 0 || CurrentRequirement == null
                ? string.Empty
                : $"{Requirements.IndexOf(CurrentRequirement) + 1} of {Requirements.Count}";

        // Commands (existing logic left unchanged)
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

        partial void OnCurrentRequirementChanged(Requirement? oldValue, Requirement? newValue)
        {
            _requirementsNavigator?.NotifyCurrentRequirementChanged();
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
            // Unsubscribe events to avoid leaks
            if (Requirements != null)
                Requirements.CollectionChanged -= RequirementsOnCollectionChanged;

            _statusTimer?.Stop();
        }
    }
}