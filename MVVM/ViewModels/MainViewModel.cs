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
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Single-file MainViewModel implementation intended to be a drop-in replacement.
    /// - Uses CommunityToolkit's ObservableObject for property change helpers.
    /// - Provides an ITestCaseGenerator_Navigator implementation for navigation UI.
    /// - Includes a DI-friendly constructor and a parameterless design-time constructor.
    /// - Minimal no-op service stubs are embedded so this file can compile standalone; remove them
    ///   if you already have implementations in the project.
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable, ITestCaseGenerator_Navigator
    {
        // --- Services / collaborators ---
        private readonly IRequirementService _requirementService;
        private readonly IPersistenceService _persistence;
        private readonly IFileDialogService _fileDialog;
        private readonly IServiceProvider _services;

        // Optional/managed runtime services
        private LlmProbeService? _llmProbeService;

        // --- Logging ---
        private readonly ILogger<MainViewModel>? _logger;

        // --- Header / navigation / view state ---
        public TitleBarViewModel TitleBar { get; }

        // Strongly-typed header instances
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private WorkspaceHeaderViewModel? _workspaceHeaderViewModel;

        // Active header slot: the UI binds to ActiveHeader (ContentControl Content="{Binding ActiveHeader}")
        private object? _activeHeader;
        public object? ActiveHeader
        {
            get => _activeHeader;
            private set => SetProperty(ref _activeHeader, value);
        }

        // Backwards-compatible alias used in some places in the codebase
        public object? HeaderViewModel
        {
            get => ActiveHeader;
            set => ActiveHeader = value;
        }

        // Also expose the test-case header explicitly when callers want it
        public TestCaseGenerator_HeaderVM? TestCaseGeneratorHeader => _testCaseGeneratorHeader;

        // Navigation VM
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
                    // inside CurrentRequirement setter, immediately after a successful SetProperty(...)
                    System.Diagnostics.Debug.WriteLine($"[CurrentRequirement] set -> Item='{value?.Item ?? "<null>"}' Name='{value?.Name ?? "<null>"}' Method='{value?.Method}' ActiveHeader={ActiveHeader?.GetType().Name ?? "<null>"}");
                    // Update header and other requirement-related hooks
                    OnCurrentRequirementChanged(value);

                    // Defensive final step: always forward to header(s)
                    try
                    {
                        if (Application.Current?.Dispatcher?.CheckAccess() == true)
                            ForwardRequirementToActiveHeader(value);
                        else
                            Application.Current?.Dispatcher?.Invoke(() => ForwardRequirementToActiveHeader(value));
                    }
                    catch { /* swallow */ }

                    try { _requirementsNavigator?.NotifyCurrentRequirementChanged(); } catch { }

                    OnPropertyChanged(nameof(RequirementPositionDisplay));
                }
            }
        }

        // --- Other persisted/view state ---
        private string? _workspacePath;
        public string? WorkspacePath
        {
            get => _workspacePath;
            set => SetProperty(ref _workspacePath, value);
        }

        private bool _requirementsCollectionHooked;
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
        public ObservableCollection<StepDescriptor> TestCaseGeneratorSteps { get; } = new ObservableCollection<StepDescriptor>();

        // Test Flow steps (from side menu)
        public ObservableCollection<StepDescriptor> TestFlowSteps { get; } = new ObservableCollection<StepDescriptor>();

        // Display name for window title
        private string _displayName = "Test Case Editor";
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        // SAP status properties (from side menu)
        private string _sapStatus = string.Empty;
        public string SapStatus
        {
            get => _sapStatus;
            set
            {
                if (SetProperty(ref _sapStatus, value))
                {
                    OnPropertyChanged(nameof(SapForegroundStatus));
                }
            }
        }

        public System.Windows.Media.Brush SapForegroundStatus
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SapStatus)) return System.Windows.Media.Brushes.Transparent;
                return string.Equals(SapStatus, "OK", StringComparison.OrdinalIgnoreCase) || 
                       string.Equals(SapStatus, "Connected", StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.Media.Brushes.LightGreen
                    : System.Windows.Media.Brushes.Orange;
            }
        }

        // Navigator VM (wraps Requirements collection)
        private RequirementsIndexViewModel? _requirementsNavigator;
        public RequirementsIndexViewModel RequirementsNavigator => _requirementsNavigator!;

        // Timer for transient status messages
        private DispatcherTimer? _statusTimer;

        // Minimal test case generator placeholder used by TestCaseGenerator_VM instances
        private TestCaseGenerator_CoreVM? _testCaseGenerator = new TestCaseGenerator_CoreVM();

        // header adapter for test-case-creator UI
        private INotifyPropertyChanged? _linkedTestCaseGeneratorInpc;



        // Commands exposed directly (so bindings can reference them without source-generation)
        public ICommand NextRequirementCommand { get; }
        public ICommand PreviousRequirementCommand { get; }
        public ICommand NextWithoutTestCaseCommand { get; }
        public IAsyncRelayCommand ImportWordCommand { get; private set; }
        public IAsyncRelayCommand QuickImportCommand { get; private set; }
        public ICommand LoadWorkspaceCommand { get; private set; }
        public ICommand SaveWorkspaceCommand { get; private set; }
        public ICommand ReloadCommand { get; private set; }
        public ICommand ExportAllToJamaCommand { get; private set; }
        public ICommand HelpCommand { get; private set; }

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

        // WrapOnNextWithoutTestCase required by ITestCaseGenerator_Navigator
        private bool _wrapOnNextWithoutTestCase;
        public bool WrapOnNextWithoutTestCase
        {
            get => _wrapOnNextWithoutTestCase;
            set => SetProperty(ref _wrapOnNextWithoutTestCase, value);
        }

        // -------------------------
        // Constructors
        // -------------------------

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
            _requirement_service_guard(requirementService, persistence, workspaceHeaderViewModel, navigationViewModel, fileDialog);

            _requirementService = requirementService;
            _persistence = persistence;
            _workspaceHeaderViewModel = workspaceHeaderViewModel;
            Navigation = navigationViewModel;
            _fileDialog = fileDialog;
            _services = services ?? new SimpleServiceProviderStub();
            _logger = logger;
            TitleBar = new TitleBarViewModel();

            // Initialize header instances
            _testCaseGeneratorHeader = new TestCaseGenerator_HeaderVM { TitleText = "Test Case Creator" };

            if (_testCaseGeneratorHeader != null)
            {
                _testCaseGeneratorHeader.RequirementDescription = "DIAG: description visible";
                _testCaseGeneratorHeader.RequirementMethod = "DIAG: method (string)";
                // If the VM exposes RequirementMethodEnum and you don't know the enum type,
                // set it via reflection to the first enum value (best-effort):
                var prop = _testCaseGeneratorHeader.GetType().GetProperty("RequirementMethodEnum");
                if (prop != null)
                {
                    var enumType = prop.PropertyType;
                    if (enumType.IsEnum)
                    {
                        var first = Enum.GetValues(enumType).GetValue(0);
                        prop.SetValue(_testCaseGeneratorHeader, first);
                    }
                }
                ActiveHeader = _testCaseGeneratorHeader; // ensure it's visible while debugging
            }

            // Default active header is workspace header
            ActiveHeader = _workspaceHeaderViewModel;

            // Wire collection change notifications (preserve the ObservableCollection instance)
            Requirements.CollectionChanged += RequirementsOnCollectionChanged;
            _requirementsCollectionHooked = true;

            // Initialize/ensure Import command exists before wiring header (so both menu and header share the same command)
            ImportWordCommand = ImportWordCommand ?? new AsyncRelayCommand(ImportWordAsync);
            QuickImportCommand = new AsyncRelayCommand(QuickImportAsync);
            LoadWorkspaceCommand = new RelayCommand(() => TryInvokeLoadWorkspace());
            SaveWorkspaceCommand = new RelayCommand(() => TryInvokeSaveWorkspace());
            ReloadCommand = new AsyncRelayCommand(ReloadAsync);
            ExportAllToJamaCommand = new RelayCommand(() => TryInvokeExportAllToJama());
            HelpCommand = new RelayCommand(() => TryInvokeHelp());

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
            InitializeSteps();

            // Ensure header wiring is consistent
            TryWireDynamicTestCaseGenerator();
            WireHeaderSubscriptions();

            _logger?.LogDebug("MainViewModel constructed");
        }

        private void _requirement_service_guard(IRequirementService requirementService, IPersistenceService persistence, WorkspaceHeaderViewModel workspaceHeaderViewModel, NavigationViewModel navigationViewModel, IFileDialogService fileDialog)
        {
            if (requirementService == null) throw new ArgumentNullException(nameof(requirementService));
            if (persistence == null) throw new ArgumentNullException(nameof(persistence));
            if (workspaceHeaderViewModel == null) throw new ArgumentNullException(nameof(workspaceHeaderViewModel));
            if (navigationViewModel == null) throw new ArgumentNullException(nameof(navigationViewModel));
            if (fileDialog == null) throw new ArgumentNullException(nameof(fileDialog));
        }

        private void InitializeSteps()
        {
            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "requirements",
                DisplayName = "Requirements",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc =>
                {
                    var vm = new TestCaseGenerator_VM(_persistence, this);
                    vm.TestCaseGenerator = _testCaseGenerator;
                    return vm;
                }
            });

            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "test-assumptions",
                DisplayName = "Test Assumptions",
                Badge = string.Empty,
                HasFileMenu = false,
                CreateViewModel = svc =>
                {
                    return new TestCaseGenerator_AssumptionsVM(_testCaseGeneratorHeader);
                }
            });

            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "clarifying-questions",
                DisplayName = "Clarifying Questions",
                Badge = string.Empty,
                HasFileMenu = false,
                CreateViewModel = svc =>
                {
                    var llm = TestCaseEditorApp.Services.LlmFactory.Create();
                    return new TestCaseGenerator_QuestionsVM(_persistence, llm, _testCaseGeneratorHeader, this);
                }
                
            });

            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "testcase-creation",
                DisplayName = "Test Case Generator",
                Badge = string.Empty,
                HasFileMenu = false,
                CreateViewModel = svc => new TestCaseGenerator_CreationVM()
            });

            SelectedStep = TestCaseGeneratorSteps.FirstOrDefault(s => s.CreateViewModel != null);
        }

        // SelectedStep property
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

                    if (created is TestCaseGenerator_VM reqVm)
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

        // Insert this method into MainViewModel.cs (near other header helpers)
        private void OnSelectedMenuSectionChanged(string? value)
        {
            try
            {
                // Treat a few common labels as "Test Case Creator"
                if (string.Equals(value, "TestCase", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Test Case Creator", StringComparison.OrdinalIgnoreCase)
                    || (value?.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    // Defer expensive initialization to allow UI animation to start immediately
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        new Action(() => CreateAndAssignTestCaseGeneratorHeader()),
                        System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }

                // TestFlow uses workspace header (TestFlowHeaderViewModel stub was removed)
                if (string.Equals(value, "TestFlow", StringComparison.OrdinalIgnoreCase))
                {
                    CreateAndAssignWorkspaceHeader();
                    return;
                }

                // Default to workspace header
                CreateAndAssignWorkspaceHeader();
            }
            catch
            {
                // Best-effort fallback
                CreateAndAssignWorkspaceHeader();
            }
        }

        private void CreateAndAssignWorkspaceHeader()
        {
            if (_workspaceHeaderViewModel == null)
            {
                _workspaceHeaderViewModel = new WorkspaceHeaderViewModel();
            }

            // initialize some workspace header values if possible
            try { _workspaceHeaderViewModel.WorkspaceName = CurrentWorkspace?.Name ?? Path.GetFileName(WorkspacePath ?? string.Empty); } catch { }

            ActiveHeader = _workspaceHeaderViewModel;
        }

        private void CreateAndAssignTestCaseGeneratorHeader()
        {
            if (_testCaseGeneratorHeader == null)
                _testCaseGeneratorHeader = new TestCaseGenerator_HeaderVM();

            var ctx = new TestCaseGenerator_HeaderContext
            {
                WorkspaceName = string.IsNullOrWhiteSpace(WorkspacePath) ? string.Empty : Path.GetFileName(WorkspacePath),
                Requirements = this.Requirements,
                ImportCommand = ImportWordCommand,
                LoadWorkspaceCommand = LoadWorkspaceCommand,
                SaveWorkspaceCommand = SaveWorkspaceCommand,
                ReloadCommand = ReloadCommand,
                ExportAllToJamaCommand = ExportAllToJamaCommand,
                HelpCommand = HelpCommand,
                OpenRequirementsCommand = new RelayCommand(() => Header_OpenRequirements()),
                OpenWorkspaceCommand = new RelayCommand(() => Header_OpenWorkspace()),
                SaveCommand = new RelayCommand(() => TryInvokeSaveWorkspace())
            };

            _testCaseGeneratorHeader.Initialize(ctx);
            _testCaseGeneratorHeader.AttachConnectionManager();
            _testCaseGeneratorHeader.IsLlmBusy = false;

            // assign active header
            ActiveHeader = _testCaseGeneratorHeader;

            // ensure probe exists
            if (_llmProbeService == null)
            {
                try
                {
                    _llmProbeService = new LlmProbeService("http://localhost:11434/api/tags", TimeSpan.FromSeconds(10));
                    _llmProbeService.Start();
                }
                catch { /* best-effort */ }
            }

            WireHeaderSubscriptions();
        }

        private void WireHeaderSubscriptions()
        {
            try
            {
                if (!_requirementsCollectionHooked)
                {
                    _requirements.CollectionChanged += Requirements_CollectionChanged;
                    _requirementsCollectionHooked = true;
                }

                _testCaseGeneratorHeader?.UpdateRequirements(Requirements);

                if (_testCaseGeneratorHeader != null)
                {
                    _testCaseGeneratorHeader.SetCurrentRequirement(CurrentRequirement);
                }
            }
            catch { /* swallow */ }
        }

        private void UnwireHeaderSubscriptions()
        {
            try
            {
                if (_requirementsCollectionHooked)
                {
                    _requirements.CollectionChanged -= Requirements_CollectionChanged;
                    _requirementsCollectionHooked = false;
                }

                if (_linkedTestCaseGeneratorInpc != null)
                {
                    _linkedTestCaseGeneratorInpc.PropertyChanged -= TestCaseGenerator_PropertyChanged;
                    _linkedTestCaseGeneratorInpc = null;
                }
            }
            catch { /* swallow */ }
        }

        private void Requirements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                _testCaseGeneratorHeader?.UpdateRequirements(Requirements);
            }
            catch { /* swallow */ }
        }

        private void LlmConnectionManager_ConnectionChanged(bool connected)
        {
            if (_testCaseGeneratorHeader == null) return;
            try { _testCaseGeneratorHeader.IsLlmConnected = connected; } catch { }
        }

        private void TestCaseGenerator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_testCaseGeneratorHeader == null) return;

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

                    _testCaseGeneratorHeader.IsLlmConnected = connected;
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
            _testCaseGeneratorHeader?.UpdateRequirements(Requirements);
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
                _testCaseGeneratorHeader?.UpdateRequirements(Requirements);
            }
        }

        private void UpdateTestCaseGeneratorHeaderFromState()
        {
            var h = _testCaseGeneratorHeader;
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

        private void Header_OpenRequirements()
        {
            try
            {
                var reqStep = TestCaseGeneratorSteps.FirstOrDefault(s => string.Equals(s.Id, "requirements", StringComparison.OrdinalIgnoreCase));
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
            try { TryInvokeLoadWorkspace(); }
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
        public async Task QuickImportAsync()
        {
            const string fixedSourcePath = @"C:\Users\e10653214\Downloads\Decagon_Boundary Scan.docx";
            const string fixedDestinationFolder = @"C:\Users\e10653214\Desktop";

            if (!File.Exists(fixedSourcePath))
            {
                SetTransientStatus($"Source file not found: {fixedSourcePath}", 3);
                return;
            }

            // Import from fixed source
            await ImportFromPathAsync(fixedSourcePath, replace: true);

            // Auto-save to fixed destination
            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("Nothing to save after import.", 2);
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var guid = Guid.NewGuid().ToString("N").Substring(0, 8);
            var fileName = $"Decagon_Boundary Scan_{timestamp}_{guid}.tcex.json";
            var fullPath = Path.Combine(fixedDestinationFolder, fileName);

            try
            {
                WorkspacePath = fullPath;
                var ws = new Workspace
                {
                    SourceDocPath = CurrentSourcePath,
                    Requirements = Requirements.ToList()
                };

                WorkspaceService.Save(WorkspacePath!, ws);
                CurrentWorkspace = ws;
                HasUnsavedChanges = false;
                SetTransientStatus($"Quick import complete: {fileName}", 5);
            }
            catch (Exception ex)
            {
                SetTransientStatus($"Save failed: {ex.Message}", 5);
            }
        }

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

        public Task ReloadAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentSourcePath))
            {
                SetTransientStatus("No source loaded to reload.", 3);
                return Task.CompletedTask;
            }
            return ImportFromPathAsync(CurrentSourcePath!, replace: true);
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

                Requirement? firstFromView = null;
                try
                {
                    firstFromView = _requirementsNavigator?.RequirementsView?.Cast<Requirement>().FirstOrDefault();
                }
                catch { firstFromView = null; }

                CurrentRequirement = firstFromView ?? Requirements.FirstOrDefault();
                CurrentSourcePath = ws.SourceDocPath;
                SetTransientStatus($"Opened workspace: {Path.GetFileName(WorkspacePath)} � {Requirements.Count} requirements", 4);
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

        // CurrentRequirement change handling
        private void OnCurrentRequirementChanged(Requirement? newValue)
        {
            try
            {
                UnhookOldRequirement();
                HookNewRequirement(newValue);

                // Update header(s)
                if (_testCaseGeneratorHeader != null && ActiveHeader == _testCaseGeneratorHeader)
                {
                    _testCaseGeneratorHeader.SetCurrentRequirement(newValue);

                    return;
                }

                if (_workspaceHeaderViewModel != null && ActiveHeader == _workspaceHeaderViewModel)
                {
                    _workspaceHeaderViewModel.CurrentRequirementTitle = newValue?.Name ?? string.Empty;
                    _workspaceHeaderViewModel.CurrentRequirementSummary = ShortSummary(newValue?.Description);
                    _workspaceHeaderViewModel.CurrentRequirementId = newValue?.Item ?? string.Empty;
                    _workspaceHeaderViewModel.CurrentRequirementStatus = newValue?.Status ?? string.Empty;
                    return;
                }

                // fallback reflection update
                var header = ActiveHeader;
                if (header != null)
                {
                    var t = header.GetType();
                    void TrySet(string propName, object? val)
                    {
                        var prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                        if (prop != null && prop.CanWrite) prop.SetValue(header, val);
                    }

                    TrySet("CurrentRequirementTitle", newValue?.Name ?? string.Empty);
                    TrySet("CurrentRequirementSummary", ShortSummary(newValue?.Description));
                    TrySet("CurrentRequirementId", newValue?.Item ?? string.Empty);
                    TrySet("CurrentRequirementStatus", newValue?.Status ?? string.Empty);
                    TrySet("CurrentRequirementName", newValue?.Name ?? string.Empty);
                }
            }
            catch { /* best-effort */ }
        }

        private void ForwardRequirementToActiveHeader(Requirement? req)
        {
            System.Diagnostics.Debug.WriteLine($"[ForwardReq] invoked ActiveHeader={ActiveHeader?.GetType().Name ?? "<null>"} ReqItem={req?.Item ?? "<null>"} Method='{req?.Method}' DescLen={(req?.Description?.Length ?? 0)}");

            if (req == null)
            {
                // Clear both headers defensively when no requirement
                try
                {
                    if (_testCaseGeneratorHeader != null)
                    {
                        _testCaseGeneratorHeader.RequirementDescription = string.Empty;
                        _testCaseGeneratorHeader.RequirementMethod = string.Empty;
                    }
                }
                catch { }

                try
                {
                    if (_workspaceHeaderViewModel != null)
                        _workspaceHeaderViewModel.CurrentRequirementSummary = string.Empty;
                }
                catch { }

                return;
            }

            var description = req.Description ?? string.Empty;
            var methodStr = req.Method.ToString();

            // 1) Update the test-case header instance if it exists (always, regardless of ActiveHeader)
            try
            {
                if (_testCaseGeneratorHeader != null)
                {
                    _testCaseGeneratorHeader.RequirementDescription = description;
                    _testCaseGeneratorHeader.RequirementMethod = methodStr;
                    _testCaseGeneratorHeader.RequirementMethodEnum = req.Method;
                    _testCaseGeneratorHeader.CurrentRequirementName = req.Name ?? req.Item ?? string.Empty;
                    _testCaseGeneratorHeader.CurrentRequirementSummary = ShortSummary(req.Description);
                    System.Diagnostics.Debug.WriteLine($"[ForwardReq] wrote to _testCaseGeneratorHeader: Method='{methodStr}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForwardReq] failed writing to _testCaseGeneratorHeader: {ex.Message}");
            }

            // 2) Update the workspace header instance if it exists
            try
            {
                if (_workspaceHeaderViewModel != null)
                {
                    _workspaceHeaderViewModel.CurrentRequirementTitle = req.Name ?? string.Empty;
                    _workspaceHeaderViewModel.CurrentRequirementSummary = ShortSummary(req.Description);
                    _workspaceHeaderViewModel.CurrentRequirementId = req.Item ?? string.Empty;
                    _workspaceHeaderViewModel.CurrentRequirementStatus = req.Status ?? string.Empty;
                    System.Diagnostics.Debug.WriteLine("[ForwardReq] wrote to _workspaceHeaderViewModel");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForwardReq] failed writing to _workspaceHeaderViewModel: {ex.Message}");
            }

            // 3) Best-effort: set properties on the ActiveHeader object if it's some other header type
            try
            {
                var header = ActiveHeader;
                if (header != null && header != _testCaseGeneratorHeader && header != _workspaceHeaderViewModel)
                {
                    var t = header.GetType();
                    void TrySet(string propName, object? val)
                    {
                        var prop = t.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                        if (prop != null && prop.CanWrite) prop.SetValue(header, val);
                    }

                    TrySet("RequirementDescription", description);
                    TrySet("RequirementMethod", methodStr);
                    TrySet("RequirementMethodEnum", req.Method);
                    TrySet("CurrentRequirementName", req.Name ?? string.Empty);
                    TrySet("CurrentRequirementSummary", ShortSummary(req.Description));
                    System.Diagnostics.Debug.WriteLine("[ForwardReq] wrote to ActiveHeader via reflection");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ForwardReq] reflection fallback failed: {ex.Message}");
            }
        }

        private void CurrentRequirement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e?.PropertyName) ||
                e.PropertyName == nameof(Requirement.Description) ||
                e.PropertyName == nameof(Requirement.Method))
            {
                try
                {
                    if (Application.Current?.Dispatcher?.CheckAccess() == true)
                    {
                        ForwardRequirementToActiveHeader(CurrentRequirement);
                    }
                    else
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ForwardRequirementToActiveHeader(CurrentRequirement));
                    }
                }
                catch
                {
                    // best-effort - swallow exceptions
                }
            }
        }

        // -----------------
        // Import implementation (trimmed helper)
        // -----------------
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

                SetTransientStatus($"Importing {Path.GetFileName(path)}�", 0);
                _logger?.LogInformation("Starting import of '{Path}'", path);

                var sw = Stopwatch.StartNew();

                var reqs = await Task.Run(() => _requirement_service_call_for_import(path));
                _logger?.LogInformation("Parser returned {Count} requirement(s)", reqs?.Count ?? 0);

                sw.Stop();

                // Build workspace model
                CurrentWorkspace = new Workspace
                {
                    SourceDocPath = path,
                    Requirements = reqs.ToList()
                };

                // Update UI-bound collection: preserve existing ObservableCollection instance
                reqs = reqs ?? new List<Requirement>();
                try
                {
                    Requirements.CollectionChanged -= RequirementsOnCollectionChanged;
                    Requirements.Clear();
                    foreach (var r in reqs) Requirements.Add(r);
                }
                finally
                {
                    Requirements.CollectionChanged += RequirementsOnCollectionChanged;
                }

                CurrentWorkspace.Requirements = Requirements.ToList();

                HasUnsavedChanges = false;

                Requirement? firstFromView = null;
                try
                {
                    firstFromView = _requirementsNavigator?.RequirementsView?.Cast<Requirement>().FirstOrDefault();
                }
                catch { firstFromView = null; }

                CurrentRequirement = firstFromView ?? Requirements.FirstOrDefault();
                RefreshSupportingInfo();

                ComputeDraftedCount();
                RaiseCounterChanges();

                _requirementsNavigator?.NotifyCurrentRequirementChanged();

                SetTransientStatus($"?? Workspace created � {Requirements?.Count ?? 0} requirement(s)", 6);
                _logger?.LogInformation("final status: {StatusMessage}", StatusMessage);
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
            
            // Also update workspace header if it exists
            if (_workspaceHeaderViewModel != null)
            {
                _workspaceHeaderViewModel.StatusMessage = message;
            }
            
            try { _statusTimer?.Stop(); } catch { }
            if (seconds > 0)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                _statusTimer.Tick += (_, __) => 
                { 
                    try { _statusTimer?.Stop(); } catch { } 
                    StatusMessage = null;
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.StatusMessage = null;
                    }
                };
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
            return firstLine.Substring(0, maxLength).Trim() + "�";
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

            try
            {
                _llmProbeService?.Stop();
                _llmProbeService?.Dispose();
            }
            catch { }
            _llmProbeService = null;
        }

        // Explicit ITestCaseGenerator_Navigator implementations (map to public ICommand props)
        ICommand? ITestCaseGenerator_Navigator.NextRequirementCommand => this.NextRequirementCommand;
        ICommand? ITestCaseGenerator_Navigator.PreviousRequirementCommand => this.PreviousRequirementCommand;
        ICommand? ITestCaseGenerator_Navigator.NextWithoutTestCaseCommand => this.NextWithoutTestCaseCommand;

        // Current workspace (public for other parts of app)
        public Workspace? CurrentWorkspace { get; set; }

        public Workspace? Workspace
        {
            get => CurrentWorkspace;
            set => CurrentWorkspace = value;
        }
    }
}