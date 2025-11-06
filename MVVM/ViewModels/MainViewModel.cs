// Note: paste this file over your existing MainViewModel.cs (keep a backup).
// This variation adds the missing using-alias and explicit properties for
// CurrentStepViewModel and WrapOnNextWithoutTestCase so the file compiles even
// when source generators are temporarily out-of-sync.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestCaseEditorApp.MVVM.Models;

// Fix: alias used elsewhere in the file
using VMVerMethod = TestCaseEditorApp.MVVM.Models.VerificationMethod;
using TestCaseEditorApp.Helpers;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// MainViewModel — paste-ready version with a couple of explicit properties
    /// to avoid hard compile dependencies on source generators for these fields.
    /// Keep the rest of the implementation as-is in your project.
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable, IRequirementsNavigator
    {
        // Minimal TestCaseGenerator stub reference (if your real one exists, it will shadow this field type)
        private TestCaseGenViewModel? TestCaseGenerator { get; set; } = new TestCaseGenViewModel();

        // --- Services injected via constructor ---
        private readonly WorkspaceHeaderViewModel _headerViewModel;
        private readonly IRequirementService _requirementService;
        private readonly IPersistenceService _persistence;
        private readonly IFileDialogService _fileDialog;
        private readonly IServiceProvider _services;

        // --- Core state (CommunityToolkit may generate other properties) ---
        [ObservableProperty] private ObservableCollection<Requirement> requirements = new();
        [ObservableProperty] private Requirement? currentRequirement;
        [ObservableProperty] private string? workspacePath;
        [ObservableProperty] private bool hasUnsavedChanges;
        [ObservableProperty] private string? statusMessage;
        public Workspace? CurrentWorkspace { get; set; }

        // Import / source paths
        [ObservableProperty] private string? wordFilePath;
        [ObservableProperty] private string? currentSourcePath;

        // Header / Navigation
        public WorkspaceHeaderViewModel HeaderViewModel => _headerViewModel;
        public NavigationViewModel Navigation { get; }
        private RequirementsIndexViewModel? _requirementsNavigator;

        // --- UI helpers & timers ---
        private DispatcherTimer? _statusTimer;

        // NOTE: explicit backing field + property for CurrentStepViewModel to avoid build failures
        // when source generators haven't run; this will coexist with a generated property if/when
        // the generator runs — but avoid duplicating names in other partials.
        private object? _currentStepViewModel;
        public object? CurrentStepViewModel
        {
            get => _currentStepViewModel;
            set => SetProperty(ref _currentStepViewModel, value);
        }

        // Editor-side collections
        [ObservableProperty] private ObservableCollection<LooseTableViewModel> looseTables = new();
        [ObservableProperty] private ObservableCollection<string> looseParagraphs = new();

        // Test case creation steps / menu
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; } = new ObservableCollection<StepDescriptor>();

        // NOTE: explicit backing + property for WrapOnNextWithoutTestCase so XAML binding works even
        // when incremental generator state is stale.
        private bool _wrapOnNextWithoutTestCase;
        public bool WrapOnNextWithoutTestCase
        {
            get => _wrapOnNextWithoutTestCase;
            set => SetProperty(ref _wrapOnNextWithoutTestCase, value);
        }

        // Toggle used by the "Next empty" flow (if you prefer the source-generator attribute,
        // you can remove the explicit property once generator behavior is stable)
        // [ObservableProperty] private bool wrapOnNextWithoutTestCase; // removed in favor of explicit property

        // Convenience property for exposing navigator
        public RequirementsIndexViewModel RequirementsNavigator => _requirementsNavigator!;

        // Selected step (creates step view-model via factory)
        private StepDescriptor? _selectedStep;
        public StepDescriptor? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (!SetProperty(ref _selectedStep, value)) return;

                Debug.WriteLine($"[SelectedStep] invoked. DisplayName='{value?.DisplayName}', Id='{value?.Id}'");

                if (value?.CreateViewModel == null)
                {
                    Debug.WriteLine("[SelectedStep] CreateViewModel is null — nothing to create.");
                    CurrentStepViewModel = null;
                    return;
                }

                try
                {
                    var created = value.CreateViewModel(_services);
                    Debug.WriteLine($"[SelectedStep] factory returned: {(created == null ? "null" : created.GetType().FullName)}");

                    CurrentStepViewModel = created;

                    // insert immediately after CurrentStepViewModel = created;
                    if (created is RequirementsViewModel reqVm)
                    {
                        // assign the existing generator instance from MainViewModel
                        reqVm.TestCaseGenerator = this.TestCaseGenerator;

                        // If RequirementsViewModel has a refresh method to repopulate UI, call it now:
                        // (Replace with the actual method name your RequirementsViewModel implements.)
                        // e.g. reqVm.RefreshSupportContentFromProvider();
                        // or reqVm.RefreshSupportingInfo();
                        try
                        {
                            // Attempt to call a refresh method if present (uncomment and adjust the name if needed)
                            // reqVm.RefreshSupportContentFromProvider();
                        }
                        catch { /* ignore if method not present */ }
                    }

                    Debug.WriteLine($"[SelectedStep] CurrentStepViewModel assigned: {(CurrentStepViewModel == null ? "null" : CurrentStepViewModel.GetType().FullName)}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SelectedStep] Exception invoking CreateViewModel: {ex}");
                    CurrentStepViewModel = null;
                }

                try
                {
                    if (_persistence != null && value != null)
                    {
                        _persistence.Save("Main.SelectedStep", value.Id);
                        Debug.WriteLine($"[SelectedStep] Persisted SelectedStep.Id='{value.Id}'");
                    }
                }
                catch (Exception exPersist)
                {
                    Debug.WriteLine($"[SelectedStep] Persistence save failed: {exPersist}");
                }
            }
        }

        // --- Constructor ---
        public MainViewModel(
            IRequirementService requirementService,
            IPersistenceService persistence,
            WorkspaceHeaderViewModel headerViewModel,
            NavigationViewModel navigationViewModel,
            IFileDialogService fileDialog,
            IServiceProvider? services = null)
        {
            _requirement_service_guard(requirementService, persistence, headerViewModel, navigationViewModel, fileDialog);

            _requirementService = requirementService;
            _persistence = persistence;
            _fileDialog = fileDialog;
            _headerViewModel = headerViewModel;
            Navigation = navigationViewModel;
            _services = services ?? new SimpleServiceProviderStub();

            // Keep counts up-to-date
            Requirements.CollectionChanged += RequirementsOnCollectionChanged;

            // Navigator with commit callback (instance method)
            _requirementsNavigator = new RequirementsIndexViewModel(
                Requirements,
                () => CurrentRequirement,
                r => CurrentRequirement = r,
                () => CommitPendingEdits());

            // Populate the steps with factories that produce viewmodels.
            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "requirements",
                DisplayName = "Requirements",
                Badge = "",
                CreateViewModel = svc =>
                {
                    var vm = new RequirementsViewModel(_persistence, this);
                    vm.TestCaseGenerator = this.TestCaseGenerator;
                    // optionally call vm.RefreshSupportContentFromProvider(); // if exists
                    return vm;
                }
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

            // create single navigator service that forwards to this MainViewModel
            var reqNavService = new RequirementsNavigationService(this);

            var reqNavVm = new RequirementsNavigationViewModel(reqNavService);
            Navigation.RequirementsNav = reqNavVm;
            // Initialize any non-generator-based commands/properties if needed (none required here)
        }


        // Constructor guard
        private static void _requirement_service_guard(IRequirementService requirementService, IPersistenceService persistence, WorkspaceHeaderViewModel headerViewModel, NavigationViewModel navigationViewModel, IFileDialogService fileDialog)
        {
            _ = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _ = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _ = headerViewModel ?? throw new ArgumentNullException(nameof(headerViewModel));
            _ = navigationViewModel ?? throw new ArgumentNullException(nameof(navigationViewModel));
            _ = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        }

        // --- Navigator / collection helpers ---
        private void RequirementsOnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(TotalRequirementsCount));
            OnPropertyChanged(nameof(RequirementPositionDisplay));
            _requirementsNavigator?.NotifyCurrentRequirementChanged();
        }

        public int TotalRequirementsCount => Requirements?.Count ?? 0;
        public string RequirementPositionDisplay =>
            Requirements == null || Requirements.Count == 0 || CurrentRequirement == null
                ? string.Empty
                : $"{Requirements.IndexOf(CurrentRequirement) + 1} of {Requirements.Count}";

        private void CommitPendingEdits() => Keyboard.ClearFocus();

        // --- Commands (generator-backed where used) ---
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
            var ofd = new Microsoft.Win32.OpenFileDialog
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
                var ws = WorkspaceService.Load<Workspace>(WorkspacePath!);
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

        [RelayCommand]
        private void NextRequirement()
        {
            CommitPendingEdits();
            if (Requirements.Count == 0 || CurrentRequirement == null) return;
            int idx = Requirements.IndexOf(CurrentRequirement);
            if (idx >= 0 && idx < Requirements.Count - 1) CurrentRequirement = Requirements[idx + 1];
        }

        [RelayCommand]
        private void PreviousRequirement()
        {
            CommitPendingEdits();
            if (Requirements.Count == 0 || CurrentRequirement == null) return;
            int idx = Requirements.IndexOf(CurrentRequirement);
            if (idx > 0) CurrentRequirement = Requirements[idx - 1];
        }

        // Generates NextWithoutTestCaseCommand
        [RelayCommand]
        private void NextWithoutTestCase()
        {
            CommitPendingEdits();

            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("No requirements available.", 3);
                return;
            }

            int count = Requirements.Count;

            int startIdx = (CurrentRequirement == null) ? -1 : Requirements.IndexOf(CurrentRequirement);
            if (startIdx < -1) startIdx = -1;

            bool HasTestCase(Requirement r)
            {
                try
                {
                    return r != null && r.HasGeneratedTestCase;
                }
                catch
                {
                    return false;
                }
            }

            for (int step = 1; step <= count; step++)
            {
                int idx = startIdx + step;

                if (!WrapOnNextWithoutTestCase && idx >= count) break;

                int candidate = idx % count;
                var req = Requirements[candidate];
                if (!HasTestCase(req))
                {
                    CurrentRequirement = req;
                    return;
                }
            }

            SetTransientStatus("No next requirement without a test case found.", 4);
        }

        // --- Requirement wiring helpers ---
        private Requirement? _prevReq;

        private void UnhookOldRequirement()
        {
            if (_prevReq != null)
                _prevReq.PropertyChanged -= CurrentRequirement_PropertyChanged;
            _prevReq = null;
        }

        private void HookNewRequirement(Requirement? r)
        {
            if (r != null)
            {
                _prevReq = r;
                _prevReq.PropertyChanged += CurrentRequirement_PropertyChanged;
            }
        }

        // Called by the generated setter for CurrentRequirement
        partial void OnCurrentRequirementChanged(Requirement? value)
        {
            UnhookOldRequirement();
            HookNewRequirement(value);

            _requirementsNavigator?.NotifyCurrentRequirementChanged();

            // Update header
            try
            {
                _headerViewModel.CurrentRequirementTitle = value?.Name ?? string.Empty;
                _headerViewModel.CurrentRequirementSummary = ShortSummary(value?.Description);
                _headerViewModel.CurrentRequirementId = value?.Item ?? string.Empty;
                _headerViewModel.CurrentRequirementStatus = value?.Status ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[OnCurrentRequirementChanged] header update failed: " + ex);
            }

            // Clear left-side UI basics first
            LooseTables.Clear();
            LooseParagraphs.Clear();
            OnPropertyChanged(nameof(RequirementPositionDisplay));

            if (value == null)
            {
                // Clear generator state
                try
                {
                    TestCaseGenerator?.ResetForRequirement(null);
                    var _vcvm = TestCaseGenerator?.VerificationCaseVM;
                    if (_vcvm != null)
                    {
                        _vcvm.ReqId = string.Empty;
                        _vcvm.ReqName = string.Empty;
                        _vcvm.ReqDescription = string.Empty;
                        _vcvm.Methods = Array.Empty<VMVerMethod>();
                        _vcvm.SelectedMethod = VMVerMethod.Inspection;
                        _vcvm.ImportedRationale = null;
                        _vcvm.ImportedValidationEvidence = null;
                        _vcvm.ImportedSupportingNotes = null;
                        _vcvm.ImportedSupportingTables = null;
                        _vcvm.GenerationResult = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[OnCurrentRequirementChanged] VCVM clear error: " + ex);
                }
                return;
            }

            // 1) Populate the loose content FIRST (this feeds GetSelectedContext)
            try
            {
                BuildSupportingInfoFromRequirement(value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[OnCurrentRequirementChanged] BuildSupportingInfoFromRequirement failed: " + ex);
            }

            // 2) Reset the generator and wire callbacks
            try
            {
                TestCaseGenerator?.ResetForRequirement(value);
                WireGeneratorCallbacks();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[OnCurrentRequirementChanged] TestCaseGenerator reset/wire failed: " + ex);
            }

            var vcvm = TestCaseGenerator?.VerificationCaseVM;
            if (vcvm != null)
            {
                try
                {
                    vcvm.ReqId = value.Item ?? value.Name ?? string.Empty;
                    vcvm.ReqName = value.Name ?? string.Empty;
                    vcvm.ReqDescription = value.Description ?? string.Empty;

                    var list = value.VerificationMethods;
                    IReadOnlyList<VMVerMethod> methods =
                        (list != null) ? list.AsReadOnly() : Array.Empty<VMVerMethod>();
                    vcvm.Methods = methods;

                    vcvm.SelectedMethod = value.Method != default ? value.Method : VMVerMethod.Inspection;

                    vcvm.ImportedRationale = value.Rationale;
                    vcvm.ImportedValidationEvidence = value.ValidationEvidence;
                    vcvm.ImportedSupportingNotes = FormatSupportingNotes(value);
                    vcvm.ImportedSupportingTables = FormatSupportingTables(value);

                    var latestDraft = GetLatestLlmDraftText(value);
                    vcvm.GenerationResult = latestDraft;

                    TestCaseGenerator.LlmOutput = BuildStrictOutputFromSaved(value);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[OnCurrentRequirementChanged] VCVM populate failed: " + ex);
                }
            }

            OnPropertyChanged(nameof(RequirementPositionDisplay));
        }

        // Handler for property changes on the active Requirement instance
        private void CurrentRequirement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Requirement.Name) || e.PropertyName == nameof(Requirement.Description))
            {
                try
                {
                    if (sender is Requirement r)
                    {
                        _headerViewModel.CurrentRequirementTitle = r.Name ?? string.Empty;
                        _headerViewModel.CurrentRequirementSummary = ShortSummary(r.Description);
                        _headerViewModel.CurrentRequirementId = r.Item ?? string.Empty;
                    }
                }
                catch { /* ignore */ }
            }
        }

        // --- Import flow (DOCX) ---
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
                    Debug.WriteLine("[Import DEBUG] Import canceled by user (unsaved changes).");
                    return;
                }
            }

            try
            {
                var defaultFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "TestCaseEditorApp", "Sessions");
                Directory.CreateDirectory(defaultFolder);

                var suggested = FileNameHelper.GenerateUniqueFileName(
                    Path.GetFileNameWithoutExtension(path), ".tcex.json");

                var chosen = _fileDialog.ShowSaveFile(
                    title: "Create Workspace",
                    suggestedFileName: suggested,
                    filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                    defaultExt: ".tcex.json",
                    initialDirectory: defaultFolder);

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    SetTransientStatus("Import canceled (no workspace name selected).", 2);
                    Debug.WriteLine("[Import DEBUG] Import canceled: no workspace name selected.");
                    return;
                }

                WorkspacePath = FileNameHelper.EnsureUniquePath(
                    Path.GetDirectoryName(chosen)!, Path.GetFileName(chosen));

                SetTransientStatus($"Importing {Path.GetFileName(path)}…", 0);
                Debug.WriteLine($"[Import DEBUG] StatusMessage after starting import: '{StatusMessage}'");

                var sw = Stopwatch.StartNew();
                var reqs = await Task.Run(() =>
                    _requirementService?.ImportRequirementsFromJamaAllDataDocx(path) ?? new List<Requirement>());
                sw.Stop();

                CurrentWorkspace = new Workspace
                {
                    SourceDocPath = path,
                    Requirements = reqs.ToList()
                };

                // Seed defaults from project template (best-effort)
                object? template = null;
                try { template = DefaultsHelper.LoadProjectDefaultsTemplate(); } catch (Exception ex) { Debug.WriteLine("[Import] DefaultsHelper failed: " + ex); }

                if (template is DefaultsBlock dbTemplate)
                {
                    CurrentWorkspace.Defaults = dbTemplate;
                }
                else if (template is DefaultsCatalogDto catalogDto)
                {
                    CurrentWorkspace.Defaults = new DefaultsBlock
                    {
                        Version = 1,
                        Catalog = catalogDto,
                        State = new DefaultsState { SelectedPreset = "Bench (default)" }
                    };
                }
                else if (template != null)
                {
                    try
                    {
                        dynamic dyn = template;
                        var catalog = new DefaultsCatalogDto();
                        try { catalog.Items = dyn.Items; } catch { }
                        try { catalog.Presets = dyn.Presets; } catch { }

                        CurrentWorkspace.Defaults = new DefaultsBlock
                        {
                            Version = 1,
                            Catalog = catalog,
                            State = new DefaultsState { SelectedPreset = "Bench (default)" }
                        };
                    }
                    catch { CurrentWorkspace.Defaults = new DefaultsBlock { Version = 1, Catalog = null, State = new DefaultsState { SelectedPreset = "Bench (default)" } }; }
                }
                else
                {
                    CurrentWorkspace.Defaults = new DefaultsBlock { Version = 1, Catalog = null, State = new DefaultsState { SelectedPreset = "Bench (default)" } };
                }

                // Make the requirements collection live for the UI
                Requirements = new ObservableCollection<Requirement>(reqs);
                CurrentWorkspace.Requirements = Requirements.ToList();

                try
                {
                    WorkspaceService.Save(WorkspacePath!, CurrentWorkspace);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Import] WorkspaceService.Save failed: " + ex);
                }

                HasUnsavedChanges = false;

                try { TestCaseGenerator?.LoadDefaultsFromWorking(CurrentWorkspace.Defaults ?? new DefaultsBlock()); } catch (Exception ex) { Debug.WriteLine("[Import] TestCaseGenerator.LoadDefaults failed: " + ex); }

                CurrentRequirement = Requirements.FirstOrDefault();
                RefreshSupportingInfo();

                var db = CurrentWorkspace.Defaults ?? new DefaultsBlock();
                try
                {
                    Debug.WriteLine($"[MainViewModel] Loading defaults - db is {(db == null ? "NULL" : "not null")}");
                    if (db != null)
                    {
                        Debug.WriteLine($"[MainViewModel] DefaultsBlock: catalog.items={db.Catalog?.Items?.Count ?? 0}, presets={db.Catalog?.Presets?.Count ?? 0}, enabledKeys={db.State?.EnabledKeys?.Count ?? 0}");
                        if (db.Catalog?.Items != null)
                        {
                            foreach (var it in db.Catalog.Items)
                                Debug.WriteLine($"[MainViewModel] DefaultItem: Key='{it?.Key}', Name='{it?.Name}', IsEnabled={(it?.IsEnabled.ToString() ?? "<null>")}', ContentLine='{(it?.ContentLine ?? "<null>")}'");
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[MainViewModel] Exception while logging defaults: " + ex); }

                try { TestCaseGenerator?.LoadDefaultsFromWorking(db); } catch (Exception ex) { Debug.WriteLine("[Import] TestCaseGenerator.LoadDefaultsFromWorking (2) failed: " + ex); }

                WordFilePath = null;
                CurrentSourcePath = null;

                ComputeDraftedCount();
                RaiseCounterChanges();

                if (Requirements.Count == 0)
                {
                    Debug.WriteLine($"[Import] Parsed 0 requirements from '{path}'. Check diagnostics in %LOCALAPPDATA%\\TestCaseEditorApp\\imports");
                    SetTransientStatus($"Import completed: 0 requirements — see logs in LocalAppData\\TestCaseEditorApp\\imports", 8);
                    Debug.WriteLine($"[Import DEBUG] zero-results StatusMessage='{StatusMessage}'");
                }
                else
                {
                    _requirementsNavigator?.NotifyCurrentRequirementChanged();
                    Debug.WriteLine($"[Import] CurrentRequirement set to: {CurrentRequirement?.Item} • {CurrentRequirement?.Name} (Total: {Requirements.Count})");
                }

                SetTransientStatus($"💾 Workspace created: {Path.GetFileName(WorkspacePath)} • {Requirements.Count} requirement(s) • {sw.ElapsedMilliseconds} ms", 6);
                Debug.WriteLine($"[Import DEBUG] final StatusMessage='{StatusMessage}'");
            }
            catch (NotSupportedException ex)
            {
                Status = ex.Message;
                Debug.WriteLine("[Import ERROR] NotSupportedException: " + ex.Message);
            }
            catch (IOException)
            {
                Status = "Close the Word file and try again.";
                Debug.WriteLine("[Import ERROR] IOException during import.");
            }
            catch (Exception ex)
            {
                SaveSessionAuto();
                Status = "Import failed: " + ex.Message;
                Debug.WriteLine("[Import ERROR] Exception during import: " + ex.ToString());
            }
        }

        public string? Status
        {
            get => StatusMessage;
            set
            {
                if (StatusMessage == value) return;
                StatusMessage = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusMessage));
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

        // Explicit IRequirementsNavigator property implementations so the generated
        // IRelayCommand properties satisfy the interface (IRelayCommand implements ICommand).
        // Keeps MainViewModel generator-based commands as-is and satisfies the interface.
        System.Windows.Input.ICommand? IRequirementsNavigator.NextRequirementCommand => this.NextRequirementCommand;
        System.Windows.Input.ICommand? IRequirementsNavigator.PreviousRequirementCommand => this.PreviousRequirementCommand;
        System.Windows.Input.ICommand? IRequirementsNavigator.NextWithoutTestCaseCommand => this.NextWithoutTestCaseCommand;

        // Dispose/unsubscribe
        public void Dispose()
        {
            try { if (Requirements != null) Requirements.CollectionChanged -= RequirementsOnCollectionChanged; } catch { }
            try { UnhookOldRequirement(); } catch { }
            try { if (_statusTimer != null) { _statusTimer.Stop(); _statusTimer = null; } } catch { }
            try { if (_requirementsNavigator is IDisposable d) d.Dispose(); } catch { }
        }

        // --- Small guards / helpers referenced above ---
        private IPersistenceService WorkspaceService => _persistence;

        private void SaveSessionAuto()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(WorkspacePath) && WorkspaceService != null && CurrentWorkspace != null)
                    WorkspaceService.Save(WorkspacePath!, CurrentWorkspace);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[SaveSessionAuto] " + ex);
            }
        }

        private void RefreshSupportingInfo()
        {
            if (CurrentRequirement != null)
                BuildSupportingInfoFromRequirement(CurrentRequirement);
        }

        private void BuildSupportingInfoFromRequirement(Requirement req)
        {
            LooseTables.Clear();
            LooseParagraphs.Clear();

            // If your Requirement.LooseContent contains Paragraphs/Tables, hydrate those into the collections here.
            try
            {
                if (req?.LooseContent?.Paragraphs != null)
                {
                    foreach (var p in req.LooseContent.Paragraphs)
                        LooseParagraphs.Add(p);
                }

                if (req?.LooseContent?.Tables != null)
                {
                    foreach (var t in req.LooseContent.Tables)
                    {
                        var vm = new LooseTableViewModel { Title = t.EditableTitle };
                        // Optionally populate vm.Rows/Columns here if your LooseTableViewModel supports it.
                        LooseTables.Add(vm);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BuildSupportingInfoFromRequirement] " + ex);
            }
        }

        private void ComputeDraftedCount() { /* TODO: implement per-app behavior */ }
        private void RaiseCounterChanges() { /* TODO: implement per-app behavior */ }

        private void WireGeneratorCallbacks()
        {
            try
            {
                // TODO: wire TestCaseGenerator events/callbacks into the UI
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WireGeneratorCallbacks] " + ex);
            }
        }

        private string? FormatSupportingNotes(Requirement req) => req?.Description;
        private IEnumerable<TableDto>? FormatSupportingTables(Requirement req) => Enumerable.Empty<TableDto>();
        private string GetLatestLlmDraftText(Requirement req) => string.Empty;
        private string BuildStrictOutputFromSaved(Requirement req) => string.Empty;

        partial void OnWorkspacePathChanged(string? value)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _headerViewModel.WorkspaceName = Path.GetFileName(value) ?? value;
                    _headerViewModel.SourceInfo = value;
                }
                else
                {
                    _headerViewModel.WorkspaceName = "Workspace";
                    _headerViewModel.SourceInfo = null;
                }
            }
            catch { /* ignore */ }
        }

        partial void OnHasUnsavedChangesChanged(bool value)
        {
            try { _headerViewModel.HasUnsavedChanges = value; } catch { }
        }

        private static string ShortSummary(string? description, int maxLength = 200)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var firstLine = description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault() ?? description;
            firstLine = firstLine.Trim();
            if (firstLine.Length <= maxLength) return firstLine;
            return firstLine.Substring(0, maxLength).Trim() + "…";
        }

        // WorkspaceService facade: delegates to the persistence service (project may implement differently)
        private IPersistenceService WorkspaceServiceFacade => _persistence;

        // --- Helpers / local lightweight stubs (kept private to avoid type collisions with real project types) ---
        // If your project already contains real implementations, remove these or keep them out of compilation.
        private class SimpleServiceProviderStub : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        //#region Local lightweight compile-time stubs
        //public class LooseTableViewModel { public string? Title { get; set; } public ObservableCollection<object>? Rows { get; set; } }
        //public class TableDto { public string Title = ""; public List<string>? Columns; public List<List<string>>? Rows; }
        //#endregion
    }

}