using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Helpers;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;
using System.Collections.Specialized;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Single-file MainViewModel implementation intended to be a drop-in replacement
    /// that compiles and runs without partial-method issues.
    /// - Uses CommunityToolkit's ObservableObject for property change helpers.
    /// - Provides an IRequirementsNavigator implementation for navigation UI.
    /// - Includes a DI-friendly constructor and a parameterless design-time constructor.
    /// - Minimal no-op service stubs are embedded so this file can compile standalone; remove them
    ///   if you already have implementations in the project.
    /// </summary>
    public class MainViewModel : ObservableObject, IDisposable, IRequirementsNavigator
    {
        // --- Services / collaborators ---
        private IRequirementService _requirementService;
        private IPersistenceService _persistence;
        private IFileDialogService _fileDialog;
        private IServiceProvider _services;

        // --- Logging ---
        private readonly ILogger<MainViewModel>? _logger;

        // --- Header / navigation / view state ---

        public TitleBarViewModel TitleBar { get; }
        private object? _headerViewModel;
        public object? HeaderViewModel
        {
            get => _headerViewModel;
            private set
            {
                if (_headerViewModel == value) return;
                _headerViewModel = value;
                OnPropertyChanged(nameof(HeaderViewModel));
            }
        }

        // Keep an explicit strongly-typed workspace header instance we update programmatically.
        private WorkspaceHeaderViewModel _workspaceHeaderViewModel;

        public NavigationViewModel Navigation { get; private set; }


        // --- Core observable collections / properties ---
        private ObservableCollection<Requirement> _requirements = new ObservableCollection<Requirement>();
        public ObservableCollection<Requirement> Requirements => _requirements;

        private Requirement? _currentRequirement;
        public Requirement? CurrentRequirement
        {
            get => _currentRequirement;
            set
            {
                if (SetProperty(ref _currentRequirement, value))
                {
                    OnCurrentRequirementChanged(value);
                }
            }
        }

        private string? _workspacePath;
        public string? WorkspacePath
        {
            get => _workspacePath;
            set => SetProperty(ref _workspacePath, value);
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        private string? _statusMessage;
        public string? StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string? _wordFilePath;
        public string? WordFilePath
        {
            get => _wordFilePath;
            set => SetProperty(ref _wordFilePath, value);
        }

        private string? _currentSourcePath;
        public string? CurrentSourcePath
        {
            get => _currentSourcePath;
            set => SetProperty(ref _currentSourcePath, value);
        }

        private ObservableCollection<LooseTableViewModel> _looseTables = new ObservableCollection<LooseTableViewModel>();
        public ObservableCollection<LooseTableViewModel> LooseTables => _looseTables;

        private ObservableCollection<string> _looseParagraphs = new ObservableCollection<string>();
        public ObservableCollection<string> LooseParagraphs => _looseParagraphs;

        // Steps UI
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; } = new ObservableCollection<StepDescriptor>();

        // Navigator VM (wraps Requirements collection)
        private RequirementsIndexViewModel? _requirementsNavigator;
        public RequirementsIndexViewModel RequirementsNavigator => _requirementsNavigator!;

        // Timer for transient status messages
        private DispatcherTimer? _statusTimer;

        // Minimal test case generator placeholder used by RequirementsViewModel instances
        private TestCaseGenViewModel? _testCaseGenerator = new TestCaseGenViewModel();

        // header adapter for test-case-creator UI
        private TestCaseCreatorHeaderViewModel? _testCaseCreatorHeader;
        private bool _headerSubscriptionsWired = false;
        private INotifyPropertyChanged? _linkedTestCaseGeneratorInpc;

        //public TitleBarViewModel TitleBar { get; }

        // Commands exposed directly (so bindings can reference them without source-generation)
        public ICommand NextRequirementCommand { get; }
        public ICommand PreviousRequirementCommand { get; }
        public ICommand NextWithoutTestCaseCommand { get; }
        public IAsyncRelayCommand ImportWordCommand { get; private set; }

        // Selected menu section (was referenced in UI/logic)
        private string? _selectedMenuSection;
        public string? SelectedMenuSection
        {
            get => _selectedMenuSection;
            set
            {
                if (SetProperty(ref _selectedMenuSection, value))
                {
                    OnSelectedMenuSectionChanged(value);
                }
            }
        }

        // WrapOnNextWithoutTestCase required by IRequirementsNavigator
        private bool _wrapOnNextWithoutTestCase;
        public bool WrapOnNextWithoutTestCase
        {
            get => _wrapOnNextWithoutTestCase;
            set => SetProperty(ref _wrapOnNextWithoutTestCase, value);
        }

        // Simple constructor for design-time / fallback scenarios.
        public MainViewModel()
            : this(
                  requirementService: new NoOpRequirementService(),
                  persistence: new NoOpPersistenceService(),
                  workspaceHeaderViewModel: new WorkspaceHeaderViewModel(),
                  navigationViewModel: new NavigationViewModel(),
                  fileDialog: new NoOpFileDialogService(),
                  services: new SimpleServiceProviderStub(),
                  logger: null,
                  requirementsIndexLogger: null)
        {
            // parameterless delegates to full constructor (no-op services)
        }

        // DI-friendly constructor. logger parameters are optional.
        // --- constructor (replacement snippet) ---
        public MainViewModel(
            IRequirementService requirementService,
            IPersistenceService persistence,
            WorkspaceHeaderViewModel workspaceHeaderViewModel,
            NavigationViewModel navigationViewModel,
            IFileDialogService fileDialog,
            IServiceProvider? services = null,
            ILogger<MainViewModel>? logger = null,
            ILogger<RequirementsIndexViewModel>? requirementsIndexLogger = null)
        {
            // Guard required services
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _workspaceHeaderViewModel = workspaceHeaderViewModel ?? throw new ArgumentNullException(nameof(workspaceHeaderViewModel));
            Navigation = navigationViewModel ?? throw new ArgumentNullException(nameof(navigationViewModel));
            _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
            _services = services ?? new SimpleServiceProviderStub();
            _logger = logger;
            TitleBar = new TitleBarViewModel();

            CreateAndAssignWorkspaceHeader();

            // Initialize state
            HeaderViewModel = _workspaceHeaderViewModel;

            // wire collection change notifications (preserve the ObservableCollection instance)
            Requirements.CollectionChanged += RequirementsOnCollectionChanged;

            // Initialize/ensure Import command exists before wiring header (so both menu and header share the same command)
            // Initialize/ensure Import command exists before wiring header (so both menu and header share the same command)
            ImportWordCommand = ImportWordCommand ?? new AsyncRelayCommand(ImportWordAsync);

            // Ensure the runtime HeaderViewModel reference points at the injected header (it already does, but set explicitly).
            HeaderViewModel = _workspaceHeaderViewModel;

            // Create navigator and pass child logger if available
            _requirementsNavigator = new RequirementsIndexViewModel(
                Requirements,
                () => CurrentRequirement,
                r => CurrentRequirement = r,
                () => CommitPendingEdits(),
                logger: requirementsIndexLogger);

            // Bind navigation commands to methods
            NextRequirementCommand = new RelayCommand(NextRequirement);
            PreviousRequirementCommand = new RelayCommand(PreviousRequirement);
            NextWithoutTestCaseCommand = new RelayCommand(NextWithoutTestCase);

            // Populate UI steps (factories create step VMs)
            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "requirements",
                DisplayName = "Requirements",
                Badge = string.Empty,
                CreateViewModel = svc =>
                {
                    var vm = new RequirementsViewModel(_persistence, this);
                    vm.TestCaseGenerator = _testCaseGenerator;
                    return vm;
                }
            });

            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "clarifying-questions",
                DisplayName = "Clarifying Questions",
                Badge = string.Empty,
                CreateViewModel = svc => new ClarifyingQuestionsViewModel(_persistence)
            });

            TestCaseCreationSteps.Add(new StepDescriptor
            {
                Id = "testcase-creation",
                DisplayName = "Test Case Creation",
                Badge = string.Empty,
                CreateViewModel = svc => new TestCaseCreationViewModel()
            });

            // default selected step
            SelectedStep = TestCaseCreationSteps.FirstOrDefault(s => s.CreateViewModel != null);

            _logger?.LogDebug("MainViewModel constructed");
        }

        // SelectedStep property (not source-generated; explicit implementation)
        private StepDescriptor? _selectedStep;
        public StepDescriptor? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (!SetProperty(ref _selectedStep, value)) return;

                _logger?.LogDebug("SelectedStep set: {Step}", value?.DisplayName);

                if (value?.CreateViewModel == null)
                {
                    CurrentStepViewModel = null;
                    return;
                }

                try
                {
                    var created = value.CreateViewModel(_services);
                    CurrentStepViewModel = created;

                    if (created is RequirementsViewModel reqVm)
                    {
                        reqVm.TestCaseGenerator = _testCaseGenerator;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "CreateViewModel failed for step {Step}", value?.Id);
                    CurrentStepViewModel = null;
                }
            }
        }

        // CurrentStepViewModel explicit backing
        private object? _currentStepViewModel;
        public object? CurrentStepViewModel
        {
            get => _currentStepViewModel;
            set => SetProperty(ref _currentStepViewModel, value);
        }

        // -----------------------------
        // Header wiring and helpers
        // -----------------------------

        private void CreateAndAssignWorkspaceHeader()
        {
            var workspaceHeader = new WorkspaceHeaderViewModel
            {
                WorkspaceName = this.Workspace?.Name,
                // populate other generic workspace properties if you have them
                // e.g. sourceInfo = Workspace?.Source ?? string.Empty
            };

            HeaderViewModel = workspaceHeader;
        }

        private void WireHeaderSubscriptions()
        {
            if (_headerSubscriptionsWired) return;
            _headerSubscriptionsWired = true;

            try
            {
                if (Requirements != null)
                {
                    Requirements.CollectionChanged += Header_Requirements_CollectionChanged;
                    foreach (var r in Requirements) TryWireRequirementForHeader(r);
                }
            }
            catch { /* best-effort */ }

            try
            {
                LlmConnectionManager.ConnectionChanged += LlmConnectionManager_ConnectionChanged;
            }
            catch { /* ignore */ }

            TryWireDynamicTestCaseGenerator();

            try { this.PropertyChanged += MainViewModel_PropertyChanged; } catch { }
        }

        private void UnwireHeaderSubscriptions()
        {
            if (!_headerSubscriptionsWired) return;
            _headerSubscriptionsWired = false;

            try
            {
                if (Requirements != null)
                {
                    Requirements.CollectionChanged -= Header_Requirements_CollectionChanged;
                    foreach (var r in Requirements) TryUnwireRequirementForHeader(r);
                }
            }
            catch { }

            try { LlmConnectionManager.ConnectionChanged -= LlmConnectionManager_ConnectionChanged; } catch { }
            try
            {
                if (_linkedTestCaseGeneratorInpc != null)
                {
                    _linkedTestCaseGeneratorInpc.PropertyChanged -= TestCaseGenerator_PropertyChanged;
                    _linkedTestCaseGeneratorInpc = null;
                }
            }
            catch { }
            try { this.PropertyChanged -= MainViewModel_PropertyChanged; } catch { }
        }

        private void LlmConnectionManager_ConnectionChanged(bool connected)
        {
            if (_testCaseCreatorHeader == null) return;
            try
            {
                _testCaseCreatorHeader.IsLlmConnected = connected;
            }
            catch { /* swallow */ }
        }

        private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CurrentRequirement) || e.PropertyName == nameof(WorkspacePath) || e.PropertyName == nameof(CurrentWorkspace))
            {
                UpdateTestCaseCreatorHeaderFromState();
            }
        }

        private void TestCaseGenerator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_testCaseCreatorHeader == null) return;

            if (string.Equals(e.PropertyName, "IsLlmAvailable", StringComparison.Ordinal)
                || string.Equals(e.PropertyName, "IsLlmBusy", StringComparison.Ordinal))
            {
                try
                {
                    bool connected = false;
                    try
                    {
                        var tcg = GetTestCaseGeneratorInstance();
                        if (tcg != null)
                        {
                            var avProp = tcg.GetType().GetProperty("IsLlmAvailable", BindingFlags.Public | BindingFlags.Instance);
                            var busyProp = tcg.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                            var isAvailable = avProp != null && avProp.GetValue(tcg) is bool bav && bav;
                            var isBusy = busyProp != null && busyProp.GetValue(tcg) is bool bbusy && bbusy;
                            connected = isAvailable && !isBusy;
                        }
                    }
                    catch { connected = false; }

                    _testCaseCreatorHeader.IsLlmConnected = connected;
                }
                catch { /* swallow */ }
            }
        }

        private void Header_Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Requirement r in e.OldItems) TryUnwireRequirementForHeader(r);
            }
            if (e.NewItems != null)
            {
                foreach (Requirement r in e.NewItems) TryWireRequirementForHeader(r);
            }
            UpdateTestCaseCreatorHeaderFromState();
        }

        private void TryWireRequirementForHeader(Requirement? r)
        {
            if (r == null) return;
            try { r.PropertyChanged += Requirement_ForHeader_PropertyChanged; } catch { }
        }

        private void TryUnwireRequirementForHeader(Requirement? r)
        {
            if (r == null) return;
            try { r.PropertyChanged -= Requirement_ForHeader_PropertyChanged; } catch { }
        }

        private void Requirement_ForHeader_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Requirement.CurrentResponse) ||
                e.PropertyName == nameof(Requirement.GeneratedTestCases))
            {
                UpdateTestCaseCreatorHeaderFromState();
            }
        }

        private void UpdateTestCaseCreatorHeaderFromState()
        {
            var h = _testCaseCreatorHeader;
            if (h == null) return;

            try
            {
                string? wsName = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(WorkspacePath))
                        wsName = Path.GetFileName(WorkspacePath);
                    else if (CurrentWorkspace?.SourceDocPath != null)
                        wsName = Path.GetFileName(CurrentWorkspace.SourceDocPath);
                }
                catch { wsName = null; }
                h.WorkspaceName = string.IsNullOrWhiteSpace(wsName) ? "Workspace" : wsName;

                h.CurrentRequirementName = CurrentRequirement?.Name ?? CurrentRequirement?.Item ?? string.Empty;

                int count = 0;
                try
                {
                    count = Requirements?.Count(r =>
                    {
                        try
                        {
                            return (r != null) && ((r.GeneratedTestCases != null && r.GeneratedTestCases.Count > 0) || r.HasGeneratedTestCase);
                        }
                        catch { return false; }
                    }) ?? 0;
                }
                catch { count = 0; }
                h.RequirementsWithTestCasesCount = count;

                try
                {
                    bool fallbackConnected = false;
                    try
                    {
                        var tcg = GetTestCaseGeneratorInstance();
                        if (tcg != null)
                        {
                            var avProp = tcg.GetType().GetProperty("IsLlmAvailable", BindingFlags.Public | BindingFlags.Instance);
                            var busyProp = tcg.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                            var isAvailable = avProp != null && avProp.GetValue(tcg) is bool bav && bav;
                            var isBusy = busyProp != null && busyProp.GetValue(tcg) is bool bbusy && bbusy;
                            fallbackConnected = isAvailable && !isBusy;
                        }
                    }
                    catch { fallbackConnected = false; }

                    h.IsLlmConnected = LlmConnectionManager.IsConnected || fallbackConnected;
                }
                catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }

        public void SetLlmConnection(bool connected)
        {
            LlmConnectionManager.SetConnected(connected);
        }

        // Try to invoke an ExportAllToJama command/method if present; otherwise show a transient message.
        private void TryInvokeExportAllToJama()
        {
            try
            {
                var cmdProp = this.GetType().GetProperty("ExportAllToJamaCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (cmdProp != null && cmdProp.GetValue(this) is ICommand cmd && cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                    return;
                }

                var m = this.GetType().GetMethod("ExportAllToJama", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    m.Invoke(this, Array.Empty<object>());
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "TryInvokeExportAllToJama failed");
            }

            TryInvokeSetTransientStatus("Export to Jama is not available.", 3);
        }

        // Try to invoke a help action (command or method) if present; otherwise show a transient message.
        private void TryInvokeHelp()
        {
            try
            {
                var cmdProp = this.GetType().GetProperty("HelpCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (cmdProp != null && cmdProp.GetValue(this) is ICommand cmd && cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                    return;
                }

                var m = this.GetType().GetMethod("ShowHelp", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? this.GetType().GetMethod("OpenHelp", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    m.Invoke(this, Array.Empty<object>());
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "TryInvokeHelp failed");
            }

            TryInvokeSetTransientStatus("Help is not available.", 3);
        }


        private void Header_OpenRequirements()
        {
            try
            {
                var reqStep = TestCaseCreationSteps.FirstOrDefault(s => string.Equals(s.Id, "requirements", StringComparison.OrdinalIgnoreCase));
                if (reqStep != null) SelectedStep = reqStep;
                SelectedMenuSection = "Requirements";
                TryInvokeSetTransientStatus("Opened requirements.", 2);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Header_OpenRequirements] failed");
                TryInvokeSetTransientStatus("Failed to open requirements.", 4);
            }
        }

        private void Header_OpenWorkspace()
        {
            try
            {
                TryInvokeLoadWorkspace();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Header_OpenWorkspace] failed");
                TryInvokeSetTransientStatus("Failed to open workspace.", 4);
            }
        }

        private Task Header_SaveAsync()
        {
            try
            {
                TryInvokeSaveWorkspace();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Header_SaveAsync] failed");
                TryInvokeSetTransientStatus("Failed to save workspace.", 4);
                return Task.CompletedTask;
            }
        }

        // ----------------- helpers that avoid compile-time coupling -----------------
        private object? GetTestCaseGeneratorInstance()
        {
            try
            {
                var prop = this.GetType().GetProperty("TestCaseGenerator", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                return prop?.GetValue(this);
            }
            catch { return null; }
        }

        private void TryWireDynamicTestCaseGenerator()
        {
            try
            {
                var tcg = GetTestCaseGeneratorInstance();
                if (tcg is INotifyPropertyChanged inpc && !ReferenceEquals(inpc, _linkedTestCaseGeneratorInpc))
                {
                    if (_linkedTestCaseGeneratorInpc != null)
                    {
                        _linkedTestCaseGeneratorInpc.PropertyChanged -= TestCaseGenerator_PropertyChanged;
                        _linkedTestCaseGeneratorInpc = null;
                    }

                    inpc.PropertyChanged += TestCaseGenerator_PropertyChanged;
                    _linkedTestCaseGeneratorInpc = inpc;
                }
            }
            catch { /* ignore */ }
        }

        private void TryInvokeSaveWorkspace()
        {
            try
            {
                var cmdProp = this.GetType().GetProperty("SaveWorkspaceCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                            ?? this.GetType().GetProperty("SaveCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (cmdProp != null && cmdProp.GetValue(this) is ICommand cmd && cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                    return;
                }

                var m = this.GetType().GetMethod("SaveWorkspace", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? this.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    m.Invoke(this, Array.Empty<object>());
                    return;
                }
            }
            catch { /* ignore */ }
        }

        private void TryInvokeLoadWorkspace()
        {
            try
            {
                var cmdProp = this.GetType().GetProperty("LoadWorkspaceCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                            ?? this.GetType().GetProperty("OpenWorkspaceCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (cmdProp != null && cmdProp.GetValue(this) is ICommand cmd && cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                    return;
                }

                var m = this.GetType().GetMethod("LoadWorkspace", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? this.GetType().GetMethod("OpenWorkspace", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    m.Invoke(this, Array.Empty<object>());
                    return;
                }
            }
            catch { /* ignore */ }
        }

        private void TryInvokeSetTransientStatus(string msg, int seconds)
        {
            try
            {
                var m = this.GetType().GetMethod("SetTransientStatus", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? this.GetType().GetMethod("ShowTransientStatus", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    m.Invoke(this, new object[] { msg, seconds });
                    return;
                }

                var prop = this.GetType().GetProperty("StatusMessage", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?? this.GetType().GetProperty("Status", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(this, msg);
                }
            }
            catch { /* ignore */ }
        }

        // -----------------------------
        // Collection / navigation helpers
        // -----------------------------
        private void RequirementsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        // --- Import / Save / Load implementations ---
        public async Task ImportWordAsync()
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

        public async Task ReloadAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentSourcePath))
            {
                SetTransientStatus("No source loaded to reload.", 3);
                return;
            }
            await ImportFromPathAsync(CurrentSourcePath!, replace: true);
        }

        public async Task SaveWorkspaceAsync()
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
                CurrentWorkspace = ws;
                HasUnsavedChanges = false;
                SetTransientStatus($"Saved workspace: {Path.GetFileName(WorkspacePath)}", 4);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save workspace: {ex.Message}", "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadWorkspace()
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
                var ws = WorkspaceService.Load<Workspace>(WorkspacePath!);
                if (ws == null)
                {
                    SetTransientStatus("Failed to load workspace (file empty or invalid).", 4);
                    return;
                }

                CurrentWorkspace = ws;
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

        // Navigation methods (ICommand-backed)
        private void NextRequirement()
        {
            CommitPendingEdits();
            if (Requirements.Count == 0 || CurrentRequirement == null) return;
            int idx = Requirements.IndexOf(CurrentRequirement);
            if (idx >= 0 && idx < Requirements.Count - 1) CurrentRequirement = Requirements[idx + 1];
        }

        private void PreviousRequirement()
        {
            CommitPendingEdits();
            if (Requirements.Count == 0 || CurrentRequirement == null) return;
            int idx = Requirements.IndexOf(CurrentRequirement);
            if (idx > 0) CurrentRequirement = Requirements[idx - 1];
        }

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
                try { return r != null && r.HasGeneratedTestCase; }
                catch { return false; }
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

        // Requirement wiring helpers
        private Requirement? _prevReq;
        private void UnhookOldRequirement()
        {
            if (_prevReq != null) _prevReq.PropertyChanged -= CurrentRequirement_PropertyChanged;
            _prevReq = null;
        }

        private void HookNewRequirement(Requirement? r)
        {
            if (r != null) { _prevReq = r; _prevReq.PropertyChanged += CurrentRequirement_PropertyChanged; }
        }

        // Called whenever CurrentRequirement is changed (setter triggers this)
        private void OnCurrentRequirementChanged(Requirement? value)
        {
            UnhookOldRequirement();
            HookNewRequirement(value);

            _requirementsNavigator?.NotifyCurrentRequirementChanged();

            try
            {
                _workspaceHeaderView_update(value);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[OnCurrentRequirementChanged] header update failed");
            }

            LooseTables.Clear();
            LooseParagraphs.Clear();
            OnPropertyChanged(nameof(RequirementPositionDisplay));

            if (value == null)
            {
                try
                {
                    _testCaseGenerator?.ResetForRequirement(null);
                    var _vcvm = _testCaseGenerator?.VerificationCaseVM;
                    if (_vcvm != null)
                    {
                        _vcvm.ReqId = string.Empty;
                        _vcvm.ReqName = string.Empty;
                        _vcvm.ReqDescription = string.Empty;
                        _vcvm.Methods = Array.Empty<Models.VerificationMethod>();
                        _vcvm.SelectedMethod = Models.VerificationMethod.Inspection;
                        _vcvm.ImportedRationale = null;
                        _vcvm.ImportedValidationEvidence = null;
                        _vcvm.ImportedSupportingNotes = null;
                        _vcvm.ImportedSupportingTables = null;
                        _vcvm.GenerationResult = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "[OnCurrentRequirementChanged] VCVM clear error");
                }
                return;
            }

            try { BuildSupportingInfoFromRequirement(value); } catch (Exception ex) { _logger?.LogDebug(ex, "[OnCurrentRequirementChanged] BuildSupportingInfo failed"); }

            try { _testCaseGenerator?.ResetForRequirement(value); WireGeneratorCallbacks(); } catch (Exception ex) { _logger?.LogDebug(ex, "[OnCurrentRequirementChanged] Reset/wire failed"); }

            var vcvm = _testCaseGenerator?.VerificationCaseVM;
            if (vcvm != null)
            {
                try
                {
                    vcvm.ReqId = value.Item ?? value.Name ?? string.Empty;
                    vcvm.ReqName = value.Name ?? string.Empty;
                    vcvm.ReqDescription = value.Description ?? string.Empty;

                    var list = value.VerificationMethods;
                    IReadOnlyList<Models.VerificationMethod> methods = (list != null) ? list.AsReadOnly() : Array.Empty<Models.VerificationMethod>();
                    vcvm.Methods = methods;
                    vcvm.SelectedMethod = value.Method != default ? value.Method : Models.VerificationMethod.Inspection;
                    vcvm.ImportedRationale = value.Rationale;
                    vcvm.ImportedValidationEvidence = value.ValidationEvidence;
                    vcvm.ImportedSupportingNotes = FormatSupportingNotes(value);
                    vcvm.ImportedSupportingTables = FormatSupportingTables(value);

                    vcvm.GenerationResult = GetLatestLlmDraftText(value);
                    _testCaseGenerator.LlmOutput = BuildStrictOutputFromSaved(value);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "[OnCurrentRequirementChanged] VCVM populate failed");
                }
            }

            OnPropertyChanged(nameof(RequirementPositionDisplay));
        }

        // Replace both old methods with this single helper:
        private void UpdateHeaderWithRequirement(Requirement? requirement)
        {
            // Prepare values once
            var title = requirement?.Name ?? string.Empty;
            var summary = ShortSummary(requirement?.Description);
            var id = requirement?.Item ?? string.Empty;
            var status = requirement?.Status ?? string.Empty;

            try
            {
                // Prefer updating the currently active HeaderViewModel (TestCaseCreatorHeaderViewModel)
                if (HeaderViewModel is TestCaseCreatorHeaderViewModel tcHeader)
                {
                    tcHeader.CurrentRequirementName = title;
                    tcHeader.CurrentRequirementSummary = summary;
                    // if your TestCaseCreatorHeaderViewModel uses different property names, update them here
                    // e.g. tcHeader.CurrentRequirementTitle = title;
                    // copy id/status if the VM exposes them
                    // tcHeader.CurrentRequirementId = id;
                    // tcHeader.CurrentRequirementStatus = status;
                    return;
                }

                // Fallback: if you still have an injected/workspace header VM, update it.
                if (_workspaceHeaderViewModel != null)
                {
                    // If WorkspaceHeaderViewModel still has requirement properties, set them
                    // (if you removed these from WorkspaceHeaderViewModel, this block can be removed)
                    _workspaceHeaderViewModel.CurrentRequirementTitle = title;
                    _workspaceHeaderViewModel.CurrentRequirementSummary = summary;
                    _workspaceHeaderViewModel.CurrentRequirementId = id;
                    _workspaceHeaderViewModel.CurrentRequirementStatus = status;
                    return;
                }

                // As a final fallback, attempt to update HeaderViewModel by reflection if it exposes the expected properties
                var header = HeaderViewModel;
                if (header != null)
                {
                    var t = header.GetType();

                    void TrySet(string propName, object? val)
                    {
                        var prop = t.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (prop != null && prop.CanWrite)
                        {
                            prop.SetValue(header, val);
                        }
                    }

                    TrySet("CurrentRequirementTitle", title);
                    TrySet("CurrentRequirementSummary", summary);
                    TrySet("CurrentRequirementId", id);
                    TrySet("CurrentRequirementStatus", status);
                    TrySet("CurrentRequirementName", title);
                    TrySet("CurrentRequirementStatus", status);
                }
            }
            catch
            {
                // best-effort: swallow exceptions to avoid breaking UI updates
            }
        }

        // Updated PropertyChanged handler to call the helper:
        private void CurrentRequirement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Requirement.Name) || e.PropertyName == nameof(Requirement.Description) || e.PropertyName == nameof(Requirement.Status))
            {
                if (sender is Requirement r)
                {
                    UpdateHeaderWithRequirement(r);
                }
            }
        }

        // If you previously used _workspaceHeader_view_update, replace its body with a call to the helper:
        private void _workspaceHeaderView_update(Requirement? value)
        {
            UpdateHeaderWithRequirement(value);
        }

        private void OnSelectedMenuSectionChanged(string? value)
        {
            try
            {
                if (string.Equals(value, "TestCase", StringComparison.OrdinalIgnoreCase))
                {
                    // Use or create the TestCaseCreator header VM (keeps a cached instance)
                    CreateAndAssignTestCaseCreatorHeader();
                }
                else if (string.Equals(value, "TestFlow", StringComparison.OrdinalIgnoreCase))
                {
                    // If you have a TestFlowHeaderViewModel, create or reuse it
                    HeaderViewModel = new TestFlowHeaderViewModel();
                }
                else
                {
                    // Default to the workspace header
                    HeaderViewModel = _workspaceHeaderViewModel ??= new WorkspaceHeaderViewModel();
                }
            }
            catch
            {
                // fallback to workspace header on any error
                HeaderViewModel = _workspaceHeaderViewModel ??= new WorkspaceHeaderViewModel();
            }
        }

        private void CreateAndAssignTestCaseCreatorHeader()
        {
            // Build a header VM and wire commands to existing MainViewModel behaviors (use TryInvoke* helpers where appropriate).
            var headerVm = new TestCaseCreatorHeaderViewModel
            {
                WorkspaceName = this.Workspace?.Name
            };

            // Compute RequirementsWithTestCasesCount without relying on an external helper method name.
            int count = 0;
            try
            {
                count = Requirements?.Count(r =>
                {
                    try
                    {
                        return (r != null) && ((r.GeneratedTestCases != null && r.GeneratedTestCases.Count > 0) || r.HasGeneratedTestCase);
                    }
                    catch { return false; }
                }) ?? 0;
            }
            catch { count = 0; }
            headerVm.RequirementsWithTestCasesCount = count;

            headerVm.StatusHint = "Test Case Creator";

            // Reuse existing Import command if present, else fall back to the local ImportWordAsync method.
            headerVm.ImportWordCommand = (ImportWordCommand as ICommand) ?? new AsyncRelayCommand(ImportWordAsync);

            // Wire the file/menu actions to the existing TryInvoke* helpers (these methods exist on MainViewModel).
            headerVm.LoadWorkspaceCommand = new RelayCommand(() => TryInvokeLoadWorkspace());
            headerVm.SaveWorkspaceCommand = new RelayCommand(() => TryInvokeSaveWorkspace());
            headerVm.ReloadCommand = new AsyncRelayCommand(ReloadAsync);
            headerVm.ExportAllToJamaCommand = new RelayCommand(() => TryInvokeExportAllToJama());
            headerVm.HelpCommand = new RelayCommand(() => TryInvokeHelp());

            // Optional header action commands used by the right-side buttons: reuse the Header_* helpers where appropriate.
            headerVm.OpenRequirementsCommand = new RelayCommand(() => Header_OpenRequirements());
            headerVm.OpenWorkspaceCommand = new RelayCommand(() => Header_OpenWorkspace());
            headerVm.SaveCommand = new RelayCommand(() => { TryInvokeSaveWorkspace(); });

            // Cache for update routines and publish header so the ContentPresenter/DataTemplate picks it up.
            _testCaseCreatorHeader = headerVm;
            HeaderViewModel = headerVm;

            // Ensure header-related subscriptions are wired (idempotent)
            try { WireHeaderSubscriptions(); } catch { /* best-effort */ }
        }

        // --- Import implementation (updated to preserve collection instance and use logger) ---
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
                    _logger?.LogInformation("Import canceled by user (unsaved changes).");
                    return;
                }
            }

            try
            {
                var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestCaseEditorApp", "Sessions");
                Directory.CreateDirectory(defaultFolder);

                var suggested = FileNameHelper.GenerateUniqueFileName(Path.GetFileNameWithoutExtension(path), ".tcex.json");

                var chosen = _file_dialog_show_save_helper(suggested, defaultFolder);

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    SetTransientStatus("Import canceled (no workspace name selected).", 2);
                    _logger?.LogInformation("Import canceled: no workspace name selected.");
                    return;
                }

                WorkspacePath = FileNameHelper.EnsureUniquePath(Path.GetDirectoryName(chosen)!, Path.GetFileName(chosen));

                SetTransientStatus($"Importing {Path.GetFileName(path)}…", 0);
                _logger?.LogInformation("Starting import of '{Path}'", path);

                var sw = Stopwatch.StartNew();

                _logger?.LogDebug("requirementService = {RequirementServiceType}", _requirementService?.GetType().FullName ?? "<null>");

                var reqs = await Task.Run(() => _requirement_service_call_for_import(path));
                _logger?.LogInformation("Parser returned {Count} requirement(s)", reqs?.Count ?? 0);

                sw.Stop();

                if (reqs == null || reqs.Count == 0)
                {
                    try
                    {
                        var tmp = Path.Combine(Path.GetTempPath(), $"tce_import_debug_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
                        using var swf = new StreamWriter(tmp, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                        swf.WriteLine("Import debug snapshot");
                        swf.WriteLine("Source DOCX: " + path);
                        swf.WriteLine("Parsed requirement count: 0");
                        swf.WriteLine("");

                        swf.WriteLine("Dumping first non-empty DOCX paragraphs for inspection (up to 120):");
                        try
                        {
                            using var wordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
                            var paragraphs = wordDoc.MainDocumentPart?.Document?.Body?.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                                                .Select(p => (p.InnerText ?? "").Trim()).Where(s => s.Length > 0).ToList() ?? new List<string>();

                            for (int i = 0; i < Math.Min(120, paragraphs.Count); i++)
                                swf.WriteLine($"[{i + 1}] {paragraphs[i]}");
                        }
                        catch (Exception ex)
                        {
                            swf.WriteLine("Failed to read DOCX paragraphs for debug: " + ex.Message);
                            _logger?.LogWarning(ex, "Failed to read DOCX paragraphs while building diagnostic snapshot for '{Path}'", path);
                        }

                        swf.Flush();
                        _logger?.LogInformation("Wrote parse debug to: {TempFile}", tmp);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Diagnostic snapshot failed while importing '{Path}'", path);
                    }
                }

                // Build workspace model
                CurrentWorkspace = new Workspace
                {
                    SourceDocPath = path,
                    Requirements = reqs.ToList()
                };

                // Seed defaults from project template (best-effort)
                object? template = null;
                try { template = DefaultsHelper.LoadProjectDefaultsTemplate(); } catch (Exception ex) { _logger?.LogWarning(ex, "DefaultsHelper failed"); }

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
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to adapt template object to DefaultsBlock; falling back to starter defaults");
                        CurrentWorkspace.Defaults = new DefaultsBlock { Version = 1, Catalog = null, State = new DefaultsState { SelectedPreset = "Bench (default)" } };
                    }
                }
                else
                {
                    CurrentWorkspace.Defaults = new DefaultsBlock { Version = 1, Catalog = null, State = new DefaultsState { SelectedPreset = "Bench (default)" } };
                }

                // Update UI-bound collection: preserve existing ObservableCollection instance so navigators stay wired.
                reqs = reqs ?? new List<Requirement>();

                try
                {
                    Requirements.CollectionChanged -= RequirementsOnCollectionChanged;

                    if (Application.Current?.Dispatcher?.CheckAccess() == true)
                    {
                        Requirements.Clear();
                        foreach (var r in reqs) Requirements.Add(r);
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            Requirements.Clear();
                            foreach (var r in reqs) Requirements.Add(r);
                        });
                    }
                }
                finally
                {
                    Requirements.CollectionChanged += RequirementsOnCollectionChanged;
                }

                CurrentWorkspace.Requirements = Requirements.ToList();

                try
                {
                    if (!string.IsNullOrWhiteSpace(WorkspacePath))
                        WorkspaceService.Save(WorkspacePath!, CurrentWorkspace);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "WorkspaceService.Save failed for path '{WorkspacePath}'", WorkspacePath);
                }

                HasUnsavedChanges = false;

                try { _testCaseGenerator?.LoadDefaultsFromWorking(CurrentWorkspace.Defaults ?? new DefaultsBlock()); } catch (Exception ex) { _logger?.LogWarning(ex, "TestCaseGenerator.LoadDefaultsFromWorking failed"); }

                CurrentRequirement = Requirements.FirstOrDefault();
                RefreshSupportingInfo();

                var db = CurrentWorkspace.Defaults ?? new DefaultsBlock();
                try
                {
                    if (db != null && db.Catalog?.Items != null)
                    {
                        foreach (var it in db.Catalog.Items)
                            _logger?.LogDebug("DefaultItem: Key='{Key}', Name='{Name}'", it?.Key, it?.Name);
                    }
                }
                catch (Exception ex) { _logger?.LogWarning(ex, "Exception while logging defaults"); }

                try { _testCaseGenerator?.LoadDefaultsFromWorking(db); } catch (Exception ex) { _logger?.LogWarning(ex, "TestCaseGenerator.LoadDefaultsFromWorking (2) failed"); }

                WordFilePath = null;
                CurrentSourcePath = null;

                ComputeDraftedCount();
                RaiseCounterChanges();

                // Single notification to navigator after full update
                _requirementsNavigator?.NotifyCurrentRequirementChanged();

                // Concise navigator diagnostic (single line) using safe reflection to check ReferenceEquals
                try
                {
                    var navType = _requirementsNavigator?.GetType().FullName ?? "<null>";
                    bool navigatorRefsRequirements = false;
                    try
                    {
                        object? navColObj = null;
                        var nav = _requirementsNavigator;
                        if (nav != null)
                        {
                            var p = nav.GetType().GetProperty("Requirements", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (p != null) navColObj = p.GetValue(nav);
                            else
                            {
                                var f = nav.GetType().GetField("_requirements", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                        ?? nav.GetType().GetField("requirements", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                if (f != null) navColObj = f.GetValue(nav);
                            }
                        }
                        navigatorRefsRequirements = ReferenceEquals(navColObj, Requirements);
                    }
                    catch (Exception ex) { _logger?.LogDebug(ex, "Navigator reflection check failed"); }
                    _logger?.LogInformation("[NAV DIAG] Requirements.Count={Count}; NavigatorType={Type}; NavigatorRefsRequirements={Refs}",
                        Requirements?.Count ?? -1, navType, navigatorRefsRequirements);
                }
                catch (Exception ex) { _logger?.LogDebug(ex, "Navigator diagnostic logging failed"); }

                if (Requirements.Count == 0)
                {
                    SetTransientStatus($"Import completed: 0 requirements — see logs in LocalAppData\\TestCaseEditorApp\\imports", 8);
                    try
                    {
                        var folder = Path.GetDirectoryName(WorkspacePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to open folder explorer");
                    }
                }
                else
                {
                    _logger?.LogInformation("CurrentRequirement set to: {Item} • {Name} (Total: {Total})",
                        CurrentRequirement?.Item, CurrentRequirement?.Name, Requirements.Count);
                }

                SetTransientStatus($"💾 Workspace created: {Path.GetFileName(WorkspacePath)} • {Requirements.Count} requirement(s) • {sw.ElapsedMilliseconds} ms", 6);
                _logger?.LogInformation("final status: {StatusMessage}", StatusMessage);
            }
            catch (NotSupportedException ex)
            {
                Status = ex.Message;
                _logger?.LogError(ex, "NotSupportedException during import");
            }
            catch (IOException ex)
            {
                Status = "Close the Word file and try again.";
                _logger?.LogError(ex, "IOException during import");
            }
            catch (Exception ex)
            {
                SaveSessionAuto();
                Status = "Import failed: " + ex.Message;
                _logger?.LogError(ex, "Exception during import");
            }
        }

        // Wrap requirement service call so it's easy to adapt if your real interface differs.
        private List<Requirement> _requirement_service_call_for_import(string path)
        {
            return _requirementService?.ImportRequirementsFromJamaAllDataDocx(path) ?? new List<Requirement>();
        }

        private string _file_dialog_show_save_helper(string suggestedFileName, string initialDirectory)
            => _fileDialog.ShowSaveFile(title: "Create Workspace", suggestedFileName: suggestedFileName, filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*", defaultExt: ".tcex.json", initialDirectory: initialDirectory);

        private void SetTransientStatus(string message, int seconds = 3)
        {
            StatusMessage = message;
            try { _statusTimer?.Stop(); } catch { }
            if (seconds > 0)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                _statusTimer.Tick += (_, __) => { try { _statusTimer?.Stop(); } catch { } StatusMessage = null; };
                _statusTimer.Start();
            }
        }

        // Helper properties / methods
        private IPersistenceService WorkspaceService => _persistence;

        private void SaveSessionAuto()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(WorkspacePath) && WorkspaceService != null && CurrentWorkspace != null)
                    WorkspaceService.Save(WorkspacePath!, CurrentWorkspace);
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "[SaveSessionAuto] failed"); }
        }

        private void RefreshSupportingInfo()
        {
            if (CurrentRequirement != null) BuildSupportingInfoFromRequirement(CurrentRequirement);
        }

        private void BuildSupportingInfoFromRequirement(Requirement req)
        {
            LooseTables.Clear();
            LooseParagraphs.Clear();

            try
            {
                if (req?.LooseContent?.Paragraphs != null)
                {
                    foreach (var p in req.LooseContent.Paragraphs) LooseParagraphs.Add(p);
                }

                if (req?.LooseContent?.Tables != null)
                {
                    foreach (var t in req.LooseContent.Tables)
                    {
                        var vm = new LooseTableViewModel { Title = t.EditableTitle };
                        LooseTables.Add(vm);
                    }
                }
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "[BuildSupportingInfoFromRequirement] failed"); }
        }

        private void ComputeDraftedCount() { /* app-specific */ }
        private void RaiseCounterChanges() { /* app-specific */ }
        private void WireGeneratorCallbacks() { /* wire generator events if needed */ }

        private string? FormatSupportingNotes(Requirement req) => req?.Description;
        private IEnumerable<TableDto>? FormatSupportingTables(Requirement req) => Enumerable.Empty<TableDto>();
        private string GetLatestLlmDraftText(Requirement req) => string.Empty;
        private string BuildStrictOutputFromSaved(Requirement req) => string.Empty;

        // Map simple Status property to StatusMessage
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

        private static string ShortSummary(string? description, int maxLength = 200)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var firstLine = description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault() ?? description;
            firstLine = firstLine.Trim();
            if (firstLine.Length <= maxLength) return firstLine;
            return firstLine.Substring(0, maxLength).Trim() + "…";
        }

        // Simple service provider stub
        private class SimpleServiceProviderStub : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        // Design-time / no-op stubs to compile standalone
        private class NoOpRequirementService : IRequirementService
        {
            public List<Requirement> ImportRequirementsFromJamaAllDataDocx(string path) => new List<Requirement>();
            public List<Requirement> ImportRequirementsFromWord(string path) => new List<Requirement>();
            public string ExportAllGeneratedTestCasesToCsv(IEnumerable<Requirement> requirements, string folderPath, string filePrefix, string extra) => string.Empty;
            public void ExportAllGeneratedTestCasesToExcel(IEnumerable<Requirement> requirements, string outputPath) { /* no-op */ }
        }

        private class NoOpPersistenceService : IPersistenceService
        {
            public void Save<T>(string key, T value) { }
            public T? Load<T>(string keyOrPath) => default;
            public void Save(string path, Workspace workspace) { }
            public bool Exists(string path) => false;
        }

        private class NoOpFileDialogService : IFileDialogService
        {
            public string ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string initialDirectory) => string.Empty;
        }

        // Dispose/unsubscribe
        public void Dispose()
        {
            try { if (Requirements != null) Requirements.CollectionChanged -= RequirementsOnCollectionChanged; } catch { }
            try { UnhookOldRequirement(); } catch { }
            try { if (_statusTimer != null) { _statusTimer.Stop(); _statusTimer = null; } } catch { }
            try { if (_requirementsNavigator is IDisposable d) d.Dispose(); } catch { }
            try { if (HeaderViewModel is IDisposable hd) hd.Dispose(); } catch { }
            try { UnwireHeaderSubscriptions(); } catch { }
        }

        // Explicit IRequirementsNavigator implementations (map to public ICommand props)
        ICommand? IRequirementsNavigator.NextRequirementCommand => this.NextRequirementCommand;
        ICommand? IRequirementsNavigator.PreviousRequirementCommand => this.PreviousRequirementCommand;
        ICommand? IRequirementsNavigator.NextWithoutTestCaseCommand => this.NextWithoutTestCaseCommand;

        // Current workspace (public for other parts of app)
        public Workspace? CurrentWorkspace { get; set; }
    }
}