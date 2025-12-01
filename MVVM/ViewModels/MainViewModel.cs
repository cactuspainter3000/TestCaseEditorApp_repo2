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
using TestCaseEditorApp.MVVM.Utils;

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
        private readonly ToastNotificationService _toastService;

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

        // Toast notifications collection for UI binding
        public ObservableCollection<ToastNotification> ToastNotifications => _toastService.Toasts;

        private Requirement? _currentRequirement;
        public Requirement? CurrentRequirement
        {
            get => _currentRequirement;
            set
            {
                // Save BEFORE changing requirement so we can access the OLD requirement's data
                if (_currentRequirement != value && _isDirty)
                {
                    SavePillSelectionsBeforeNavigation();
                }

                if (SetProperty(ref _currentRequirement, value))
                {
                    // inside CurrentRequirement setter, immediately after a successful SetProperty(...)
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[CurrentRequirement] set -> Item='{value?.Item ?? "<null>"}' Name='{value?.Name ?? "<null>"}' Method='{value?.Method}' ActiveHeader={ActiveHeader?.GetType().Name ?? "<null>"}");
                    
                    // Save assumptions from previous requirement BEFORE switching
                    if (CurrentStepViewModel is TestCaseGenerator_AssumptionsVM currentAssumptionsVm && _currentRequirement != null)
                    {
                        currentAssumptionsVm.SaveAllAssumptionsData();
                        TestCaseEditorApp.Services.Logging.Log.Debug("[CurrentRequirement] Saved assumptions before switching requirement");
                    }
                    
                    // Update AssumptionsVM with new requirement for pill persistence FIRST
                    // This must happen before OnCurrentRequirementChanged so pills load from correct requirement
                    if (CurrentStepViewModel is TestCaseGenerator_AssumptionsVM assumptionsVm)
                    {
                        assumptionsVm.SetCurrentRequirement(value);
                        assumptionsVm.LoadPillsForRequirement(value);
                    }

                    // Update header and other requirement-related hooks
                    OnCurrentRequirementChanged(value);
                    
                    // Update workspace header CanReAnalyze state
                    _workspaceHeaderViewModel.CanReAnalyze = (value != null && !IsLlmBusy);
                    ((AsyncRelayCommand?)_workspaceHeaderViewModel.ReAnalyzeCommand)?.NotifyCanExecuteChanged();

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

        // Auto-save timer for periodic workspace saves
        private DispatcherTimer? _autoSaveTimer;
        private const int AutoSaveIntervalMinutes = 5;

        // Recent files service for tracking recently opened workspaces
        private RecentFilesService? _recentFilesService;
        
        // Analysis service for requirement quality analysis
        private RequirementAnalysisService? _analysisService;
        
        // Tracks if batch analysis is currently running (to prevent conflicts)
        private bool _isBatchAnalyzing = false;
        public bool IsBatchAnalyzing 
        { 
            get => _isBatchAnalyzing;
            private set
            {
                if (_isBatchAnalyzing != value)
                {
                    _isBatchAnalyzing = value;
                    OnPropertyChanged(nameof(IsBatchAnalyzing));
                }
            }
        }

        // Minimal test case generator placeholder used by TestCaseGenerator_VM instances
        private TestCaseGenerator_CoreVM? _testCaseGenerator = new TestCaseGenerator_CoreVM();

        // header adapter for test-case-creator UI
        private INotifyPropertyChanged? _linkedTestCaseGeneratorInpc;



        // Commands exposed directly (so bindings can reference them without source-generation)
        public IRelayCommand NextRequirementCommand { get; }
        public IRelayCommand PreviousRequirementCommand { get; }
        public IRelayCommand NextWithoutTestCaseCommand { get; }
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

        // Dirty state tracking for unsaved changes
        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] IsDirty changing from {_isDirty} to {value}. Stack: {Environment.StackTrace}");
                }
                if (SetProperty(ref _isDirty, value))
                {
                    UpdateWindowTitle();
                }
            }
        }

        private void UpdateWindowTitle()
        {
            // Update title bar to show dirty state (asterisk)
            if (TitleBar != null)
            {
                var baseName = string.IsNullOrEmpty(_workspaceHeaderViewModel?.WorkspaceName)
                    ? "Test Case Editor"
                    : _workspaceHeaderViewModel.WorkspaceName;
                TitleBar.Title = IsDirty ? $"{baseName} *" : baseName;
            }
        }

        // WrapOnNextWithoutTestCase required by ITestCaseGenerator_Navigator
        private bool _wrapOnNextWithoutTestCase;
        public bool WrapOnNextWithoutTestCase
        {
            get => _wrapOnNextWithoutTestCase;
            set => SetProperty(ref _wrapOnNextWithoutTestCase, value);
        }

        // IsLlmBusy required by ITestCaseGenerator_Navigator
        private bool _isLlmBusy;
        public bool IsLlmBusy
        {
            get => _isLlmBusy;
            set
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] IsLlmBusy changing from {_isLlmBusy} to {value}");
                if (SetProperty(ref _isLlmBusy, value))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] IsLlmBusy changed to {value}, notifying navigation commands");
                    // Notify navigation commands to re-evaluate CanExecute
                    NextRequirementCommand?.NotifyCanExecuteChanged();
                    PreviousRequirementCommand?.NotifyCanExecuteChanged();
                    NextWithoutTestCaseCommand?.NotifyCanExecuteChanged();
                    
                    // Update workspace header CanReAnalyze state
                    _workspaceHeaderViewModel.CanReAnalyze = (CurrentRequirement != null && !value);
                    ((AsyncRelayCommand?)_workspaceHeaderViewModel.ReAnalyzeCommand)?.NotifyCanExecuteChanged();
                }
            }
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

            // Initialize toast notification service
            _toastService = new ToastNotificationService(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);

            // Initialize header instances
            _testCaseGeneratorHeader = new TestCaseGenerator_HeaderVM(this) { TitleText = "Test Case Creator" };

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

            // Initialize auto-save timer
            InitializeAutoSave();

            // Initialize recent files service
            try { _recentFilesService = new RecentFilesService(); } catch { }
            
            // Initialize analysis service for auto-analysis during import
            try
            {
                var llmService = LlmFactory.Create();
                _analysisService = new RequirementAnalysisService(llmService);
            }
            catch
            {
                _analysisService = null; // LLM not available, analysis will be skipped
            }

            // Initialize/ensure Import command exists before wiring header (so both menu and header share the same command)
            ImportWordCommand = ImportWordCommand ?? new AsyncRelayCommand(ImportWordAsync);
            QuickImportCommand = new AsyncRelayCommand(QuickImportAsync);
            LoadWorkspaceCommand = new RelayCommand(() => LoadWorkspace());
            SaveWorkspaceCommand = new RelayCommand(() => SaveWorkspace());
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

            // Bind navigation commands to methods with CanExecute checks
            NextRequirementCommand = new RelayCommand(NextRequirement, CanNavigate);
            PreviousRequirementCommand = new RelayCommand(PreviousRequirement, CanNavigate);
            NextWithoutTestCaseCommand = new RelayCommand(NextWithoutTestCase, CanNavigate);
            
            // Wire Re-Analyze command to workspace header
            _workspaceHeaderViewModel.ReAnalyzeCommand = new AsyncRelayCommand(ReAnalyzeRequirementAsync, CanReAnalyze);

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
                    return new TestCaseGenerator_AssumptionsVM(_testCaseGeneratorHeader, this);
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
                HasFileMenu = true,
                IsSelectable = false,  // Only accessible via Questions workflow
                CreateViewModel = svc => new TestCaseGenerator_CreationVM(this)
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
                // Save BEFORE changing SelectedStep so CurrentStepViewModel still points to the old view
                if (_hasUnsavedChanges)
                {
                    SavePillSelectionsBeforeNavigation();
                }

                if (!SetProperty(ref _selectedStep, value)) return;

                _logger?.LogDebug("SelectedStep set: {Step}", value?.DisplayName);

                // Collapse any file menus when switching steps
                foreach (var step in TestCaseGeneratorSteps)
                {
                    if (step != value && step.IsFileMenuExpanded)
                    {
                        step.IsFileMenuExpanded = false;
                    }
                }

                if (value?.CreateViewModel == null)
                {
                    CurrentStepViewModel = null;
                    return;
                }

                try
                {
                    // Save assumptions from previous view BEFORE switching
                    if (CurrentStepViewModel is TestCaseGenerator_AssumptionsVM previousAssumptionsVm)
                    {
                        previousAssumptionsVm.SaveAllAssumptionsData();
                        TestCaseEditorApp.Services.Logging.Log.Debug("[SelectedStep] Saved assumptions before switching view");
                    }
                    
                    var created = value.CreateViewModel(_services);
                    CurrentStepViewModel = created;

                    // Set current requirement on AssumptionsVM when view is created
                    if (created is TestCaseGenerator_AssumptionsVM assumptionsVm)
                    {
                        assumptionsVm.SetCurrentRequirement(CurrentRequirement);
                        assumptionsVm.LoadPillsForRequirement(CurrentRequirement);
                    }

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
                _testCaseGeneratorHeader = new TestCaseGenerator_HeaderVM(this);

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
                    
                    // Subscribe to IsLlmBusy changes to update navigation command state
                    _testCaseGeneratorHeader.PropertyChanged += Header_PropertyChanged;
                }
            }
            catch { /* swallow */ }
        }
        
        private void Header_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseGenerator_HeaderVM.IsLlmBusy))
            {
                (NextRequirementCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (PreviousRequirementCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (NextWithoutTestCaseCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
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
                
                if (_testCaseGeneratorHeader != null)
                {
                    _testCaseGeneratorHeader.PropertyChanged -= Header_PropertyChanged;
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
                SetTransientStatus("Opened requirements.", 2);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Header_OpenRequirements] failed");
                SetTransientStatus($"Failed to open requirements: {ex.Message}", blockingError: true);
            }
        }

        private void Header_OpenWorkspace()
        {
            try { TryInvokeLoadWorkspace(); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Header_OpenWorkspace] failed");
                SetTransientStatus($"Failed to open workspace: {ex.Message}", blockingError: true);
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
                SetTransientStatus($"Failed to save workspace: {ex.Message}", blockingError: true);
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
                // Store dirty state to avoid recursive updates
                var wasDirty = _isDirty;
                
                var cmdProp = this.GetType().GetProperty("SaveWorkspaceCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                            ?? this.GetType().GetProperty("SaveCommand", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (cmdProp != null && cmdProp.GetValue(this) is ICommand cmd && cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                    if (wasDirty) _isDirty = false; // Clear dirty flag directly without triggering property change
                    return;
                }

                var m = this.GetType().GetMethod("SaveWorkspace", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                     ?? this.GetType().GetMethod("Save", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (m != null)
                {
                    m.Invoke(this, Array.Empty<object>());
                    if (wasDirty) _isDirty = false; // Clear dirty flag directly without triggering property change
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

            SetTransientStatus("Export to Jama is not available.", 3);
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

            SetTransientStatus("Help is not available.", 3);
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
            try
            {
                var ts = DateTime.UtcNow.ToString("o");
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var user = Environment.UserName;
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TestCaseEditorApp");

                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] invoked: {ts}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] PID={pid}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] Assembly={asm}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] BaseDir={baseDir}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] User={user}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] LocalAppData={localAppData}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] TempDir={tmpDir}");
            }
            catch (Exception ex)
            {
                try { TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] debug-probe failed: {ex.Message}"); } catch { }
            }
            const string fixedSourcePath = @"C:\Users\e10653214\Downloads\Decagon_Boundary Scan.docx";
            // Determine destination folder for quick-import saves.
            // Prefer the directory of an already-set WorkspacePath (user's previous choice),
            // otherwise fall back to the Desktop (may be redirected to OneDrive).
            var fixedDestinationFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            try
            {
                if (!string.IsNullOrWhiteSpace(WorkspacePath))
                {
                    var wpDir = Path.GetDirectoryName(WorkspacePath!);
                    if (!string.IsNullOrWhiteSpace(wpDir) && Directory.Exists(wpDir))
                    {
                        fixedDestinationFolder = wpDir;
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] Using WorkspacePath directory for quick-save: {fixedDestinationFolder}");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] Failed to determine preferred save folder: {ex.Message}");
            }

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

                // Diagnostic test: attempt a tiny write to the destination folder
                try
                {
                    var testName = $"tcex_write_test_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0,8)}.txt";
                    var testPath = Path.Combine(fixedDestinationFolder, testName);
                    File.WriteAllText(testPath, $"Test write from QuickImport at {DateTime.UtcNow:o} PID={System.Diagnostics.Process.GetCurrentProcess().Id}");
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] Wrote test file: {testPath}");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[QuickImport] Test write failed: {ex.Message}");
                }

                global::WorkspaceService.Save(WorkspacePath!, ws);
                global::WorkspaceService.Save(WorkspacePath!, ws);
                // Log detailed post-save diagnostics to help locate the written file
                LogPostSaveDiagnostics(WorkspacePath!);
                CurrentWorkspace = ws;
                HasUnsavedChanges = false;
                SetTransientStatus($"Quick import complete: {fileName}", 5);
            }
            catch (Exception ex)
            {
                SetTransientStatus($"Save failed: {ex.Message}", blockingError: true);
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

        // Quick save to existing workspace path
        public void SaveWorkspace()
        {
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                // No existing path, delegate to SaveAs
                _ = SaveWorkspaceAsync();
                return;
            }

            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("Nothing to save.", 2);
                return;
            }

            var ws = new Workspace
            {
                SourceDocPath = CurrentSourcePath,
                Requirements = Requirements.ToList()
            };

            try
            {
                global::WorkspaceService.Save(WorkspacePath!, ws);
                // Log detailed post-save diagnostics to help locate the written file
                LogPostSaveDiagnostics(WorkspacePath!);
                CurrentWorkspace = ws;
                IsDirty = false;
                HasUnsavedChanges = false;
                
                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
                
                SetTransientStatus($"Saved: {Path.GetFileName(WorkspacePath)}", 3);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save workspace: {ex.Message}", "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Save As - prompts for location
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

            // If the dialog returned a folder path (or a path without a filename),
            // append the suggested filename so we save a file rather than attempting
            // to write to a directory.
            try
            {
                if (Directory.Exists(chosen) || string.IsNullOrWhiteSpace(Path.GetFileName(chosen)))
                {
                    chosen = Path.Combine(chosen, suggested);
                }

                // Ensure a file extension is present - default to the expected extension
                if (string.IsNullOrWhiteSpace(Path.GetExtension(chosen)))
                {
                    chosen = Path.ChangeExtension(chosen, ".tcex.json");
                }
            }
            catch (Exception ex)
            {
                // Defensive: if path normalization fails, cancel the save and inform the user
                TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveWorkspaceAsync] Path normalization failed: {ex.Message}");
                MessageBox.Show($"Failed to determine save file path: {ex.Message}", "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // Log and show where we will save to help debug user-reported mismatches
                TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveWorkspaceAsync] Saving workspace to: {chosen}");
                SetTransientStatus($"Saving workspace to: {chosen}", 2);

                global::WorkspaceService.Save(WorkspacePath!, ws);
                // Log detailed post-save diagnostics to help locate the written file
                LogPostSaveDiagnostics(WorkspacePath!);
                CurrentWorkspace = ws;
                IsDirty = false;
                HasUnsavedChanges = false;
                
                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
                
                SetTransientStatus($"Saved workspace: {Path.GetFileName(WorkspacePath)}", 4);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveWorkspaceAsync] Save complete: {WorkspacePath}");
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
                var ws = global::WorkspaceService.Load(WorkspacePath!);
                if (ws == null)
                {
                    SetTransientStatus("Failed to load workspace (file empty or invalid).", blockingError: true);
                    return;
                }

                CurrentWorkspace = ws;
                
                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
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
                IsDirty = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load workspace: {ex.Message}", "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Navigation methods (ICommand-backed)
        private bool CanNavigate()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] CanNavigate() called. IsLlmBusy={IsLlmBusy}");
            
            // Don't allow navigation when LLM is busy
            if (IsLlmBusy)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[MainViewModel] CanNavigate() returning FALSE - IsLlmBusy is true");
                SetTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                return false;
            }

            try
            {
                if (_testCaseGeneratorHeader?.IsLlmBusy == true)
                {
                    SetTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                    return false;
                }
                    
                var tcg = GetTestCaseGeneratorInstance();
                if (tcg != null)
                {
                    var busyProp = tcg.GetType().GetProperty("IsLlmBusy", BindingFlags.Public | BindingFlags.Instance);
                    if (busyProp != null && busyProp.GetValue(tcg) is bool isBusy && isBusy)
                    {
                        SetTransientStatus("Please wait - the AI is working on your request. LLMs are powerful, but they need a moment!", 2);
                        return false;
                    }
                }
            }
            catch { /* ignore */ }
            
            return true;
        }
        
        private bool CanReAnalyze()
        {
            return CurrentRequirement != null && !IsLlmBusy;
        }
        
        private async Task ReAnalyzeRequirementAsync()
        {
            try
            {
                var tcg = GetTestCaseGeneratorInstance();
                if (tcg == null) return;
                
                // Get the AnalysisVM from TestCaseGenerator_VM
                var analysisVmProp = tcg.GetType().GetProperty("AnalysisVM", BindingFlags.Public | BindingFlags.Instance);
                if (analysisVmProp == null) return;
                
                var analysisVm = analysisVmProp.GetValue(tcg);
                if (analysisVm == null) return;
                
                // Switch to Analysis tab first
                var isAnalysisSelectedProp = tcg.GetType().GetProperty("IsAnalysisSelected", BindingFlags.Public | BindingFlags.Instance);
                isAnalysisSelectedProp?.SetValue(tcg, true);
                
                // Trigger analysis
                var analyzeCommandProp = analysisVm.GetType().GetProperty("AnalyzeRequirementCommand", BindingFlags.Public | BindingFlags.Instance);
                if (analyzeCommandProp?.GetValue(analysisVm) is ICommand analyzeCommand && analyzeCommand.CanExecute(null))
                {
                    analyzeCommand.Execute(null);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] Re-Analyze error: {ex}");
            }
        }
        
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
        /// <summary>
        /// Save data before navigating to a new requirement or view.
        /// Only saves if workspace is dirty (orange unsaved state).
        /// Checks the current view type and saves appropriate data.
        /// </summary>
        private void SavePillSelectionsBeforeNavigation()
        {
            try
            {
                // Check if current view is AssumptionsVM and save its data
                if (CurrentStepViewModel is TestCaseGenerator_AssumptionsVM assumptionsVm)
                {
                    assumptionsVm.SaveAllAssumptionsData();
                    TestCaseEditorApp.Services.Logging.Log.Debug("[Navigation] Saved assumptions data before navigation");
                }
                // Check if current view is QuestionsVM and save its data
                else if (CurrentStepViewModel is TestCaseGenerator_QuestionsVM questionsVm)
                {
                    questionsVm.SaveQuestionsForRequirement(CurrentRequirement, markDirty: false);
                    TestCaseEditorApp.Services.Logging.Log.Debug("[Navigation] Saved questions data before navigation");
                }
                // Check if current view is HeaderVM and save its data
                else if (CurrentStepViewModel is TestCaseGenerator_HeaderVM headerVm)
                {
                    // Header data saves automatically via requirement property binding
                    TestCaseEditorApp.Services.Logging.Log.Debug("[Navigation] Header data handled by requirement property");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Navigation] Error saving data: {ex.Message}");
            }
        }

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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ForwardReq] invoked ActiveHeader={ActiveHeader?.GetType().Name ?? "<null>"} ReqItem={req?.Item ?? "<null>"} Method='{req?.Method}' DescLen={(req?.Description?.Length ?? 0)}");

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
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[ForwardReq] wrote to _testCaseGeneratorHeader: Method='{methodStr}'");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ForwardReq] failed writing to _testCaseGeneratorHeader: {ex.Message}");
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
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ForwardReq] wrote to _workspaceHeaderViewModel");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ForwardReq] failed writing to _workspaceHeaderViewModel: {ex.Message}");
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
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ForwardReq] wrote to ActiveHeader via reflection");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ForwardReq] reflection fallback failed: {ex.Message}");
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
            // If the selected path is a saved workspace, load it directly instead of
            // treating it as a source document. This makes "Import In-Process" robust
            // when users select `.tcex.json` files by accident or intentionally.
            try
            {
                if (string.Equals(Path.GetExtension(path), ".tcex.json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var ws = global::WorkspaceService.Load(path);
                        if (ws == null || (ws.Requirements?.Count ?? 0) == 0)
                        {
                            SetTransientStatus("Failed to load workspace (file empty or invalid).", blockingError: true);
                            return;
                        }

                        WorkspacePath = path;
                        CurrentWorkspace = ws;

                        // Replace the observable collection contents without replacing the instance
                        try
                        {
                            Requirements.CollectionChanged -= RequirementsOnCollectionChanged;
                            Requirements.Clear();
                            foreach (var r in ws.Requirements ?? Enumerable.Empty<Requirement>()) Requirements.Add(r);
                        }
                        finally
                        {
                            Requirements.CollectionChanged += RequirementsOnCollectionChanged;
                        }

                        CurrentWorkspace.Requirements = Requirements.ToList();
                        HasUnsavedChanges = false;
                        IsDirty = false;

                        try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }

                        SetTransientStatus($"Opened workspace: {Path.GetFileName(WorkspacePath)}  {Requirements.Count} requirements", 4);
                        _requirementsNavigator?.NotifyCurrentRequirementChanged();
                        return;
                    }
                    catch (Exception ex)
                    {
                        SetTransientStatus($"Failed to load workspace: {ex.Message}", blockingError: true);
                        return;
                    }
                }
            }
            catch { /* best-effort only */ }

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

                SetTransientStatus($"Importing {Path.GetFileName(path)}...", 60); // Auto-dismiss after 60s or when next status appears
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

                // Auto-analyze requirements if LLM is available
                if (_analysisService != null && reqs.Any())
                {
                    _ = Task.Run(async () => await BatchAnalyzeRequirementsAsync(reqs));
                }

                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }

                Requirement? firstFromView = null;
                try
                {
                    firstFromView = _requirementsNavigator?.RequirementsView?.Cast<Requirement>().FirstOrDefault();
                }
                catch { firstFromView = null; }

                CurrentRequirement = firstFromView ?? Requirements.FirstOrDefault();
                HasUnsavedChanges = false;
                IsDirty = false;
                RefreshSupportingInfo();

                ComputeDraftedCount();
                RaiseCounterChanges();

                _requirementsNavigator?.NotifyCurrentRequirementChanged();

                SetTransientStatus($"Workspace created - {Requirements?.Count ?? 0} requirement(s)", 6);
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

        private void SetTransientStatus(string message, int seconds = 3, bool blockingError = false)
        {
            // For critical errors, show a blocking modal dialog
            if (blockingError)
            {
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Use toast notifications for non-blocking messages
            var toastType = ToastType.Info;
            
            // Detect error/warning messages and set appropriate toast type
            var lowerMsg = message.ToLowerInvariant();
            if (lowerMsg.Contains("fail") || lowerMsg.Contains("error"))
            {
                toastType = ToastType.Error;
            }
            else if (lowerMsg.Contains("cancel"))
            {
                toastType = ToastType.Warning;
            }
            else if (lowerMsg.Contains("saved") || lowerMsg.Contains("complete") || lowerMsg.Contains("created") || lowerMsg.Contains("opened"))
            {
                toastType = ToastType.Success;
            }
            
            // New toast triggers existing toasts to fade out gracefully
            _toastService.ShowToast(message, seconds, toastType);
        }

        private void InitializeAutoSave()
        {
            try
            {
                _autoSaveTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes)
                };
                _autoSaveTimer.Tick += (_, __) =>
                {
                    try
                    {
                        if (IsDirty && !string.IsNullOrWhiteSpace(WorkspacePath) && CurrentWorkspace != null)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug("[AutoSave] Saving workspace...");
                            SaveWorkspace();
                            SetTransientStatus($"Auto-saved at {DateTime.Now:HH:mm}", 2);
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AutoSave] Failed: {ex.Message}");
                    }
                };
                _autoSaveTimer.Start();
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AutoSave] Timer initialized ({AutoSaveIntervalMinutes} minute interval)");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AutoSave] Initialization failed: {ex.Message}");
            }
        }

        // Helper properties / methods
        private IPersistenceService WorkspaceService => _persistence;

        private void SaveSessionAuto()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(WorkspacePath) && WorkspaceService != null && CurrentWorkspace != null)
                    global::WorkspaceService.Save(WorkspacePath!, CurrentWorkspace);
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "[SaveSessionAuto] failed"); }
        }

        // Diagnostic helper: probe post-save artifacts so we can tell whether the
        // workspace file, meta file, marker file, audit logs or temp copies exist
        // immediately after a save completes.
        private void LogPostSaveDiagnostics(string path)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Probing saved path: {path}");

                try
                {
                    var fi = new FileInfo(path);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] File Exists={fi.Exists}, Length={(fi.Exists ? fi.Length.ToString() : "N/A")}, LastWriteUtc={(fi.Exists ? fi.LastWriteTimeUtc.ToString("o") : "N/A")} ");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] File probe failed: {ex.Message}");
                }

                var metaPath = path + ".meta.txt";
                try
                {
                    if (File.Exists(metaPath))
                    {
                        var lines = File.ReadAllLines(metaPath);
                        var preview = string.Join(Environment.NewLine, lines.Take(Math.Min(20, lines.Length)));
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Meta exists: {metaPath} (lines={lines.Length})");
                        TestCaseEditorApp.Services.Logging.Log.Debug(preview);
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Meta missing: {metaPath}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Meta probe failed: {ex.Message}");
                }

                var markerPath = path + ".saved.txt";
                try
                {
                    if (File.Exists(markerPath))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Marker exists: {markerPath}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Marker missing: {markerPath}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Marker probe failed: {ex.Message}");
                }

                try
                {
                    var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "where-saved.log");
                    if (File.Exists(logPath))
                    {
                        var all = File.ReadAllLines(logPath);
                        var last = all.Skip(Math.Max(0, all.Length - 20)).ToArray();
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Local where-saved.log last lines:\n{string.Join(Environment.NewLine, last)}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Local where-saved.log missing: {logPath}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] where-saved.log probe failed: {ex.Message}");
                }

                try
                {
                    var tmpDir = Path.Combine(Path.GetTempPath(), "TestCaseEditorApp");
                    var tmpCopy = Path.Combine(tmpDir, Path.GetFileName(path));
                    if (File.Exists(tmpCopy))
                    {
                        var tfi = new FileInfo(tmpCopy);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Temp copy exists: {tmpCopy} (Length={tfi.Length})");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Temp copy missing: {tmpCopy}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Temp-copy probe failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Unexpected diagnostic error: {ex.Message}");
            }
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

        /// <summary>
        /// Batch analyze requirements in background after import.
        /// Shows progress notifications and updates requirements with analysis results.
        /// After initial pass, processes any requirements queued for re-analysis.
        /// </summary>
        private async Task BatchAnalyzeRequirementsAsync(List<Requirement> requirements)
        {
            if (_analysisService == null || !requirements.Any())
                return;

            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsBatchAnalyzing = true;
                });
                
                await Task.Delay(500); // Brief delay to let UI settle after import

                int completed = 0;
                int total = requirements.Count;
                DateTime? firstAnalysisStart = null;
                TimeSpan? avgAnalysisTime = null;

                // Get requirements in display order (sorted view that user sees)
                var orderedRequirements = _requirementsNavigator?.RequirementsView?
                    .Cast<Requirement>()
                    .Where(r => requirements.Contains(r))
                    .ToList() ?? requirements;

                // Initial pass through all requirements in display order
                foreach (var req in orderedRequirements)
                {
                    try
                    {
                        // Skip if already queued for re-analysis (user edited during batch)
                        if (req.IsQueuedForReanalysis)
                        {
                            completed++;
                            continue;
                        }
                        
                        // Show progress message at the START of each analysis (except the first)
                        if (completed > 0 && avgAnalysisTime.HasValue)
                        {
                            var nextNumber = completed + 1;
                            var remaining = total - completed;
                            var estimatedTimeRemaining = TimeSpan.FromSeconds(avgAnalysisTime.Value.TotalSeconds * remaining);
                            
                            string progressMessage;
                            if (estimatedTimeRemaining.TotalMinutes >= 1)
                            {
                                progressMessage = $"Analyzing requirements... ({nextNumber}/{total}) - ~{Math.Ceiling(estimatedTimeRemaining.TotalMinutes)} min remaining";
                            }
                            else
                            {
                                progressMessage = $"Analyzing requirements... ({nextNumber}/{total}) - ~{Math.Ceiling(estimatedTimeRemaining.TotalSeconds)} sec remaining";
                            }
                            
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                SetTransientStatus(progressMessage, 180);
                            });
                        }
                        
                        // Track timing for first analysis
                        var analysisStart = DateTime.Now;
                        if (firstAnalysisStart == null)
                        {
                            firstAnalysisStart = analysisStart;
                            
                            // Show initial progress message immediately (before analysis completes)
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                SetTransientStatus($"Analyzing requirements... (1/{total})", 180);
                            });
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] Starting analysis {completed + 1}/{total} for requirement: {req.Item}");
                        
                        // Analyze the requirement
                        var analysis = await _analysisService.AnalyzeRequirementAsync(req, useFastMode: false);
                        
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] Completed analysis for {req.Item} - IsAnalyzed: {analysis.IsAnalyzed}, Score: {analysis.QualityScore}");
                        
                        // Calculate timing after first analysis
                        var analysisDuration = DateTime.Now - analysisStart;
                        if (completed == 0)
                        {
                            avgAnalysisTime = analysisDuration;
                        }
                        
                        // Update the requirement with analysis results
                        req.Analysis = analysis;
                        
                        completed++;
                        
                        // Notify via mediator that analysis was updated
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] Notifying mediator for requirement: {req.Item}");
                            AnalysisMediator.NotifyAnalysisUpdated(req);
                            OnPropertyChanged(nameof(Requirements));
                        });
                        
                        // Small delay between analyses to avoid overwhelming the LLM
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to analyze requirement {ReqId}", req.Item);
                        // Continue with next requirement even if one fails
                    }
                }

                // Process queued re-analyses
                var queuedRequirements = requirements.Where(r => r.IsQueuedForReanalysis).ToList();
                if (queuedRequirements.Any())
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        SetTransientStatus($"Re-analyzing {queuedRequirements.Count} edited requirement(s)...", 180);
                    });
                    
                    int reanalyzed = 0;
                    foreach (var req in queuedRequirements)
                    {
                        try
                        {
                            var analysis = await _analysisService.AnalyzeRequirementAsync(req, useFastMode: false);
                            req.Analysis = analysis;
                            req.IsQueuedForReanalysis = false;
                            
                            reanalyzed++;
                            
                            // Calculate remaining for queued items
                            string queuedProgressMessage;
                            if (avgAnalysisTime.HasValue)
                            {
                                var remaining = queuedRequirements.Count - reanalyzed;
                                var estimatedTimeRemaining = TimeSpan.FromSeconds(avgAnalysisTime.Value.TotalSeconds * remaining);
                                
                                if (estimatedTimeRemaining.TotalMinutes >= 1)
                                {
                                    queuedProgressMessage = $"Re-analyzing edited requirements... ({reanalyzed}/{queuedRequirements.Count}) - ~{Math.Ceiling(estimatedTimeRemaining.TotalMinutes)} min remaining";
                                }
                                else
                                {
                                    queuedProgressMessage = $"Re-analyzing edited requirements... ({reanalyzed}/{queuedRequirements.Count}) - ~{Math.Ceiling(estimatedTimeRemaining.TotalSeconds)} sec remaining";
                                }
                            }
                            else
                            {
                                queuedProgressMessage = $"Re-analyzing edited requirements... ({reanalyzed}/{queuedRequirements.Count})";
                            }
                            
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                SetTransientStatus(queuedProgressMessage, 180);
                                
                                // Notify via mediator that this requirement's analysis was updated
                                AnalysisMediator.NotifyAnalysisUpdated(req);
                                
                                OnPropertyChanged(nameof(Requirements));
                            });
                            
                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to re-analyze requirement {ReqId}", req.Item);
                            req.IsQueuedForReanalysis = false;
                        }
                    }
                }

                // Save the workspace with analysis results
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(WorkspacePath))
                    {
                        SaveWorkspace();
                    }
                    
                    var totalAnalyzed = completed - queuedRequirements.Count + queuedRequirements.Count(r => !r.IsQueuedForReanalysis);
                    SetTransientStatus($"Analysis complete - {totalAnalyzed} of {total} requirements analyzed", 5);
                    
                    // Refresh the current requirement view if we're looking at one
                    if (CurrentRequirement != null)
                    {
                        OnPropertyChanged(nameof(CurrentRequirement));
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Batch analysis failed");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SetTransientStatus("Analysis failed - see logs for details", 5);
                });
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsBatchAnalyzing = false;
                });
            }
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
            try { if (_autoSaveTimer != null) { _autoSaveTimer.Stop(); _autoSaveTimer = null; } } catch { }
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