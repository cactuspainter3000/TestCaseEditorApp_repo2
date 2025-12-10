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
        private readonly IPersistenceService _persistence = new NoOpPersistenceService();
        private readonly IFileDialogService _fileDialog;
        private readonly IServiceProvider _services;

        // Optional/managed runtime services
        private LlmProbeService? _llmProbeService;
        private readonly ToastNotificationService _toastService;
        private readonly ChatGptExportService _chatGptExportService;
        private readonly AnythingLLMService _anythingLLMService;

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
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.CanReAnalyze = (value != null && !IsLlmBusy);
                        ((AsyncRelayCommand?)_workspaceHeaderViewModel.ReAnalyzeCommand)?.NotifyCanExecuteChanged();
                    }
                    
                    // Update ChatGPT export command state
                    ((RelayCommand?)ExportForChatGptCommand)?.NotifyCanExecuteChanged();

                    // Defensive final step: always forward to header(s)
                    try
                    {
                        if (Application.Current?.Dispatcher?.CheckAccess() == true)
                        {
                            ForwardRequirementToActiveHeader(value);
                            
                            // Update test case step selectability based on new requirement
                            UpdateTestCaseStepSelectability();
                        }
                        else
                        {
                            Application.Current?.Dispatcher?.Invoke(() => 
                            {
                                ForwardRequirementToActiveHeader(value);
                                UpdateTestCaseStepSelectability();
                            });
                        }
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

        private string? _currentAnythingLLMWorkspaceSlug;
        public string? CurrentAnythingLLMWorkspaceSlug
        {
            get => _currentAnythingLLMWorkspaceSlug;
            set => SetProperty(ref _currentAnythingLLMWorkspaceSlug, value);
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

        // User setting: auto-run requirement analysis after importing a workspace/source
        private bool _autoAnalyzeOnImport = true;
        public bool AutoAnalyzeOnImport
        {
            get => _autoAnalyzeOnImport;
            set
            {
                if (SetProperty(ref _autoAnalyzeOnImport, value))
                {
                    try { _persistence?.Save("AutoAnalyzeOnImport", value); } catch { }
                }
            }
        }

        private bool _autoExportForChatGpt = false;
        public bool AutoExportForChatGpt
        {
            get => _autoExportForChatGpt;
            set
            {
                if (SetProperty(ref _autoExportForChatGpt, value))
                {
                    try { _persistence?.Save("AutoExportForChatGpt", value); } catch { }
                }
            }
        }

        private string? _lastChatGptExportFilePath;
        public string? LastChatGptExportFilePath
        {
            get => _lastChatGptExportFilePath;
            set
            {
                if (SetProperty(ref _lastChatGptExportFilePath, value))
                {
                    ((RelayCommand?)OpenChatGptExportCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        // Command to toggle auto-analysis (useful for binding to a settings checkbox/menu)
        public ICommand ToggleAutoAnalyzeCommand { get; }

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

        // Auto-save service for periodic workspace saves
        private TestCaseEditorApp.Services.AutoSaveService? _autoSaveService;
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
        public ICommand ExportForChatGptCommand { get; private set; }
        public ICommand ExportSelectedForChatGptCommand { get; private set; }
        public ICommand ToggleAutoExportCommand { get; private set; }
        public ICommand OpenChatGptExportCommand { get; private set; }

        // Project Management Commands
        public ICommand NewProjectCommand { get; private set; }
        public ICommand OpenProjectCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand CloseProjectCommand { get; private set; }
        
        // Analysis Commands
        public ICommand AnalyzeUnanalyzedCommand { get; private set; }
        public ICommand ReAnalyzeModifiedCommand { get; private set; }
        public ICommand ImportAdditionalCommand { get; private set; }
        
        // ChatGPT Analysis Import Commands
        public ICommand ImportStructuredAnalysisCommand { get; private set; }
        public ICommand PasteChatGptAnalysisCommand { get; private set; }
        public ICommand GenerateLearningPromptCommand { get; private set; }
        public ICommand SetupLlmWorkspaceCommand { get; private set; }
        public ICommand GenerateAnalysisCommandCommand { get; private set; }
        public ICommand GenerateTestCaseCommandCommand { get; private set; }

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
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.CanReAnalyze = (CurrentRequirement != null && !value);
                        ((AsyncRelayCommand?)_workspaceHeaderViewModel.ReAnalyzeCommand)?.NotifyCanExecuteChanged();
                    }
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
            
            // Initialize ChatGPT export service
            _chatGptExportService = new ChatGptExportService();
            
            // Initialize AnythingLLM service
            _anythingLLMService = new AnythingLLMService();

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
            
            // Load persisted user preference for auto-analysis on import (default: true)
            try
            {
                if (_persistence != null && _persistence.Exists("AutoAnalyzeOnImport"))
                {
                    var val = _persistence.Load<bool>("AutoAnalyzeOnImport");
                    _autoAnalyzeOnImport = val;
                }
            }
            catch { /* ignore persistence errors */ }

            // Load persisted user preference for auto-export for ChatGPT (default: false)
            try
            {
                if (_persistence != null && _persistence.Exists("AutoExportForChatGpt"))
                {
                    var val = _persistence.Load<bool>("AutoExportForChatGpt");
                    _autoExportForChatGpt = val;
                }
            }
            catch { /* ignore persistence errors */ }

            // Initialize toggle commands for UI binding
            ToggleAutoAnalyzeCommand = new RelayCommand(() => AutoAnalyzeOnImport = !AutoAnalyzeOnImport);
            ToggleAutoExportCommand = new RelayCommand(() => AutoExportForChatGpt = !AutoExportForChatGpt);
            OpenChatGptExportCommand = new RelayCommand(() => OpenChatGptExportFile(), () => !string.IsNullOrEmpty(LastChatGptExportFilePath) && System.IO.File.Exists(LastChatGptExportFilePath));
            
            // Initialize project management commands
            NewProjectCommand = new RelayCommand(() => CreateNewProject());
            OpenProjectCommand = new RelayCommand(() => OpenProject());
            SaveProjectCommand = new RelayCommand(() => SaveProject());
            CloseProjectCommand = new RelayCommand(() => CloseProject());
            
            // Initialize analysis commands
            AnalyzeUnanalyzedCommand = new RelayCommand(() => AnalyzeUnanalyzed());
            ReAnalyzeModifiedCommand = new RelayCommand(() => ReAnalyzeModified());
            ImportAdditionalCommand = new RelayCommand(() => ImportAdditional());
            
            // Initialize ChatGPT analysis import commands  
            ImportStructuredAnalysisCommand = new RelayCommand(() => ImportStructuredAnalysis());
            PasteChatGptAnalysisCommand = new RelayCommand(() => PasteChatGptAnalysis());
            GenerateLearningPromptCommand = new RelayCommand(() => GenerateLearningPrompt());
            SetupLlmWorkspaceCommand = new RelayCommand(() => SetupLlmWorkspace());
            GenerateAnalysisCommandCommand = new RelayCommand(() => GenerateAnalysisCommand(), () => CurrentRequirement != null);
            GenerateTestCaseCommandCommand = new RelayCommand(() => GenerateTestCaseCommand(), () => CurrentRequirement != null);

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
            ExportForChatGptCommand = new RelayCommand(() => ExportCurrentRequirementForChatGpt(), () => CurrentRequirement != null);
            ExportSelectedForChatGptCommand = new RelayCommand(() => ExportSelectedRequirementsForChatGpt());

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
            // Add Project step first
            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "project",
                DisplayName = "Project",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc =>
                {
                    // Return a simple placeholder for now
                    return new TestCaseGenerator_VM(_persistence, this);
                }
            });

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
                Id = "llm-learning",
                DisplayName = "LLM Learning",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc =>
                {
                    return new TestCaseGenerator_VM(_persistence, this);
                }
            });

            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "test-assumptions",
                DisplayName = "Verification Method Assumptions",
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
                IsSelectable = true,  // Allow selection when test cases exist
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

        private void ExportCurrentRequirementForChatGpt()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    SetTransientStatus("No requirement selected for export.", 3);
                    return;
                }

                // Export to clipboard
                bool clipboardSuccess = _chatGptExportService.ExportAndCopy(CurrentRequirement, includeAnalysisRequest: true);
                
                // Also save to file
                string formattedText = _chatGptExportService.ExportSingleRequirement(CurrentRequirement, includeAnalysisRequest: true);
                string fileName = $"Requirement_{CurrentRequirement.Item?.Replace("/", "_").Replace("\\", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                try
                {
                    System.IO.File.WriteAllText(filePath, formattedText);
                    LastChatGptExportFilePath = filePath;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved single requirement to file: {filePath}");
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save single requirement file: {fileEx.Message}");
                }
                
                if (clipboardSuccess)
                {
                    SetTransientStatus($"✅ Requirement {CurrentRequirement.Item} exported to clipboard and saved to {fileName}!", 5);
                }
                else
                {
                    SetTransientStatus($"⚠️ Export saved to {fileName} but clipboard copy failed.", 4);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export current requirement for ChatGPT");
                SetTransientStatus("Error exporting requirement for ChatGPT.", 3);
            }
        }

        private void ExportSelectedRequirementsForChatGpt()
        {
            try
            {
                // For now, export all requirements - could be extended to support selection
                var requirementsToExport = Requirements.ToList();
                
                if (!requirementsToExport.Any())
                {
                    SetTransientStatus("No requirements available for export.", 3);
                    return;
                }

                bool success = _chatGptExportService.ExportAndCopyMultiple(requirementsToExport, includeAnalysisRequest: true);
                
                if (success)
                {
                    SetTransientStatus($"{requirementsToExport.Count} requirements exported to clipboard for ChatGPT analysis!", 4);
                }
                else
                {
                    SetTransientStatus("Failed to export requirements to clipboard.", 3);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export requirements for ChatGPT");
                SetTransientStatus("Error exporting requirements for ChatGPT.", 3);
            }
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
        
        /// <summary>
        /// Development convenience: Quick import with actual Decagon Boundary Scan requirements.
        /// Skips file dialogs and directly imports from the test file you use for development.
        /// </summary>
        public async Task QuickImportAsync()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[QuickImport] STARTING QuickImportAsync method");
                
                // Check if requirement service exists
                if (_requirementService == null)
                {
                    SetTransientStatus("Quick Import: Requirement service not available", 5);
                    TestCaseEditorApp.Services.Logging.Log.Warn("[QuickImport] _requirementService is null!");
                    return;
                }
                
                // Paths for your standard testing setup
                var testDocPath = @"C:\Users\e10653214\Downloads\Decagon_Boundary Scan.docx";
                var testWorkspaceFolder = @"C:\Users\e10653214\Desktop\testing import";
                
                SetTransientStatus("⚡ Quick Import from Decagon test file...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Starting import from: {testDocPath}");
                
                // Check if the test file exists
                if (!File.Exists(testDocPath))
                {
                    SetTransientStatus($"Quick Import: Test file not found at {testDocPath}", 5);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[QuickImport] Test file not found: {testDocPath}");
                    return;
                }
                
                // Ensure workspace directory exists
                Directory.CreateDirectory(testWorkspaceFolder);
                
                // Generate timestamped workspace file
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                WorkspacePath = Path.Combine(testWorkspaceFolder, $"QuickImport_Decagon_{timestamp}.tcex.json");
                
                SetTransientStatus($"Importing {Path.GetFileName(testDocPath)}...", 60);
                
                // Import requirements from the actual test file
                var sw = Stopwatch.StartNew();
                var reqs = await Task.Run(() => _requirement_service_call_for_import(testDocPath));
                sw.Stop();
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Parsed {reqs?.Count ?? 0} requirements in {sw.ElapsedMilliseconds}ms");
                
                if (reqs == null || !reqs.Any())
                {
                    SetTransientStatus("Quick Import: No requirements found in test file", 5);
                    TestCaseEditorApp.Services.Logging.Log.Info("[QuickImport] No requirements parsed from test file");
                    return;
                }
                
                // Build workspace model
                CurrentWorkspace = new Workspace
                {
                    SourceDocPath = testDocPath,
                    Requirements = reqs.ToList()
                };
                
                // Update UI collection
                try
                {
                    Requirements.CollectionChanged -= RequirementsOnCollectionChanged;
                    Requirements.Clear();
                    foreach (var req in reqs)
                        Requirements.Add(req);
                }
                finally
                {
                    Requirements.CollectionChanged += RequirementsOnCollectionChanged;
                }
                
                CurrentWorkspace.Requirements = Requirements.ToList();
                
                // Auto-process if enabled
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Imported {reqs.Count} requirements, AutoAnalyzeOnImport={AutoAnalyzeOnImport}, AutoExportForChatGpt={AutoExportForChatGpt}");
                
                if (reqs.Any() && AutoExportForChatGpt)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Exporting {reqs.Count} requirements for ChatGPT");
                    _ = Task.Run(() => BatchExportRequirementsForChatGpt(reqs));
                }
                else if (_analysisService != null && reqs.Any() && AutoAnalyzeOnImport)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Starting batch analysis for {reqs.Count} requirements");
                    _ = Task.Run(async () => await BatchAnalyzeRequirementsAsync(reqs));
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[QuickImport] Auto-processing NOT started - conditions not met");
                }
                
                // Set current requirement and finalize
                Requirement? firstFromView = null;
                try
                {
                    firstFromView = _requirementsNavigator?.RequirementsView?.Cast<Requirement>().FirstOrDefault();
                }
                catch { firstFromView = null; }
                
                CurrentRequirement = firstFromView ?? Requirements.FirstOrDefault();
                CurrentSourcePath = testDocPath;
                HasUnsavedChanges = false;
                IsDirty = false;
                RefreshSupportingInfo();
                ComputeDraftedCount();
                RaiseCounterChanges();
                _requirementsNavigator?.NotifyCurrentRequirementChanged();
                
                SetTransientStatus($"⚡ Quick Import complete - {Requirements.Count} Decagon requirements from test file", 4);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QuickImport] Complete - workspace: {WorkspacePath}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[QuickImport] FATAL ERROR during quick import: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Warn($"[QuickImport] Stack trace: {ex.StackTrace}");
                SetTransientStatus($"Quick Import failed: {ex.Message}", 10);
                
                // Also try to show a message box for immediate feedback
                try
                {
                    System.Windows.MessageBox.Show($"Quick Import failed with error:\n\n{ex.Message}\n\nCheck logs for details.", "Quick Import Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                catch { /* ignore message box errors */ }
            }
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
            TestCaseEditorApp.Services.Logging.Log.Info($"[SaveWorkspace] Quick save called. WorkspacePath='{WorkspacePath ?? "<null>"}'");
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[SaveWorkspace] No existing path, delegating to SaveAs");
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
                TestCaseEditorApp.Services.WorkspaceFileManager.Save(WorkspacePath!, ws);
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
            // Ensure async methods contain an await to satisfy analyzer when method is mostly synchronous
            await Task.CompletedTask;
            if (Requirements == null || Requirements.Count == 0)
            {
                SetTransientStatus("Nothing to save.", 2);
                return;
            }

            var suggested = $"{(string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(WordFilePath)) ? "Workspace" : Path.GetFileNameWithoutExtension(WordFilePath))}.tcex.json";
            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestCaseEditorApp", "Workspaces");
            Directory.CreateDirectory(defaultFolder);

            TestCaseEditorApp.Services.Logging.Log.Info($"[MainViewModel] Showing SaveFile dialog. initialDirectory={defaultFolder}, suggestedFileName={suggested}");
            var chosen = _fileDialog.ShowSaveFile(
                title: "Save Workspace",
                suggestedFileName: suggested,
                filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*",
                defaultExt: ".tcex.json",
                initialDirectory: defaultFolder);

            TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] File dialog returned: '{chosen ?? "NULL"}'");

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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] Set WorkspacePath to: '{WorkspacePath}'");
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

                TestCaseEditorApp.Services.WorkspaceFileManager.Save(WorkspacePath!, ws);
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
                InitialDirectory = !string.IsNullOrWhiteSpace(WorkspacePath) ? Path.GetDirectoryName(WorkspacePath) : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (ofd.ShowDialog() != true) return;

            WorkspacePath = ofd.FileName;
            try
            {
                var ws = TestCaseEditorApp.Services.WorkspaceFileManager.Load(WorkspacePath!);
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
            await Task.CompletedTask;
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

        private void UpdateTestCaseStepSelectability()
        {
            var testCaseStep = TestCaseGeneratorSteps?.FirstOrDefault(s => s.Id == "testcase-creation");
            if (testCaseStep != null)
            {
                var hasTestCases = CurrentRequirement?.GeneratedTestCases?.Count > 0 || CurrentRequirement?.HasGeneratedTestCase == true;
                TestCaseEditorApp.Services.Logging.Log.Info($"[MainViewModel] UpdateTestCaseStepSelectability: Current={testCaseStep.IsSelectable}, New={hasTestCases}, GeneratedTestCases.Count={CurrentRequirement?.GeneratedTestCases?.Count ?? 0}, HasGeneratedTestCase={CurrentRequirement?.HasGeneratedTestCase}");
                
                if (testCaseStep.IsSelectable != hasTestCases)
                {
                    testCaseStep.IsSelectable = hasTestCases;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[MainViewModel] Test Cases step IsSelectable changed to {hasTestCases} for requirement {CurrentRequirement?.Item}");
                    
                    // Force property change notifications
                    OnPropertyChanged(nameof(TestCaseGeneratorSteps));
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[MainViewModel] Test Cases step IsSelectable already {hasTestCases}, no change needed");
                }
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[MainViewModel] Test Cases step not found in TestCaseGeneratorSteps");
            }
        }

        private void CurrentRequirement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Update test case step selectability for any property change that might affect test cases
            UpdateTestCaseStepSelectability();
            
            if (string.IsNullOrEmpty(e?.PropertyName) ||
                e.PropertyName == nameof(Requirement.Description) ||
                e.PropertyName == nameof(Requirement.Method) ||
                e.PropertyName == nameof(Requirement.GeneratedTestCases))
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
                        var ws = TestCaseEditorApp.Services.WorkspaceFileManager.Load(path);
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
                var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestCaseEditorApp", "Workspaces");
                Directory.CreateDirectory(defaultFolder);

                var suggested = FileNameHelper.GenerateUniqueFileName(Path.GetFileNameWithoutExtension(path), ".tcex.json");

                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Showing Create Workspace dialog. defaultFolder={defaultFolder}, suggested={suggested}");
                var chosen = _file_dialog_show_save_helper(suggested, defaultFolder);
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Create Workspace dialog returned: '{chosen}'");

                if (string.IsNullOrWhiteSpace(chosen))
                {
                    SetTransientStatus("Import canceled (no workspace name selected).", 2);
                    _logger?.LogInformation("Import canceled: no workspace name selected.");
                    return;
                }

                WorkspacePath = FileNameHelper.EnsureUniquePath(Path.GetDirectoryName(chosen)!, Path.GetFileName(chosen));
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Set WorkspacePath to: '{WorkspacePath}'");

                SetTransientStatus($"Importing {Path.GetFileName(path)}...", 60); // Auto-dismiss after 60s or when next status appears
                _logger?.LogInformation("Starting import of '{Path}'", path);

                var sw = Stopwatch.StartNew();

                var reqs = await Task.Run(() => _requirement_service_call_for_import(path));
                _logger?.LogInformation("Parser returned {Count} requirement(s)", reqs?.Count ?? 0);

                sw.Stop();

                // Build workspace model (guard reqs if null)
                CurrentWorkspace = new Workspace
                {
                    SourceDocPath = path,
                    Requirements = reqs?.ToList() ?? new List<Requirement>()
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

                // Auto-process requirements if enabled
                TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Checking auto-processing: _analysisService={_analysisService != null}, reqs.Any()={reqs.Any()}, AutoAnalyzeOnImport={AutoAnalyzeOnImport}, AutoExportForChatGpt={AutoExportForChatGpt}");
                
                if (reqs.Any() && AutoExportForChatGpt)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Exporting {reqs.Count} requirements for ChatGPT");
                    _ = Task.Run(() => BatchExportRequirementsForChatGpt(reqs));
                }
                else if (_analysisService != null && reqs.Any() && AutoAnalyzeOnImport)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[IMPORT] Starting batch analysis for {reqs.Count} requirements");
                    _ = Task.Run(async () => await BatchAnalyzeRequirementsAsync(reqs));
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[IMPORT] Auto-processing NOT started - conditions not met");
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
            // Try both parsing methods and use the one that returns results
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] _requirement_service_call_for_import called with path: {path}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] _requirementService is null: {_requirementService == null}");
                
                if (_requirementService == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[Import] _requirementService is null - cannot import");
                    return new List<Requirement>();
                }
                
                // First try the Jama All Data parser
                TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying Jama All Data parser...");
                var jamaResults = _requirementService?.ImportRequirementsFromJamaAllDataDocx(path) ?? new List<Requirement>();
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Jama parser returned {jamaResults.Count} requirements");
                
                if (jamaResults.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Successfully parsed {jamaResults.Count} requirements using Jama All Data parser");
                    return jamaResults;
                }
                
                // If that didn't work, try the regular Word parser
                TestCaseEditorApp.Services.Logging.Log.Info("[Import] Trying regular Word parser...");
                var wordResults = _requirementService?.ImportRequirementsFromWord(path) ?? new List<Requirement>();
                TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Word parser returned {wordResults.Count} requirements");
                
                if (wordResults.Count > 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Successfully parsed {wordResults.Count} requirements using Word parser");
                    return wordResults;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[Import] No requirements found with either parser for file: {path}");
                return new List<Requirement>();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[Import] Error during import: {ex.Message}");
                return new List<Requirement>();
            }
        }

        private string _file_dialog_show_save_helper(string suggestedFileName, string initialDirectory)
            => _fileDialog.ShowSaveFile(title: "Create Workspace", suggestedFileName: suggestedFileName, filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*", defaultExt: ".tcex.json", initialDirectory: initialDirectory) ?? string.Empty;

        private void SetTransientStatus(string message, int seconds = 3, bool blockingError = false)
        {
            TestCaseEditorApp.Services.TransientStatus.Show(_toastService, message, seconds, blockingError);
        }

        private void InitializeAutoSave()
        {
            try
            {
                _autoSaveService = new TestCaseEditorApp.Services.AutoSaveService(
                    TimeSpan.FromMinutes(AutoSaveIntervalMinutes),
                    () => IsDirty && !string.IsNullOrWhiteSpace(WorkspacePath) && CurrentWorkspace != null,
                    () => { SaveWorkspace(); },
                    (msg, secs) => SetTransientStatus(msg, secs)
                );
                _autoSaveService.Start();
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AutoSave] Service initialized ({AutoSaveIntervalMinutes} minute interval)");
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
                    TestCaseEditorApp.Services.WorkspaceFileManager.Save(WorkspacePath!, CurrentWorkspace);
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
                TestCaseEditorApp.Services.SaveDiagnostics.Probe(path);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Probe delegation failed: {ex.Message}");
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

        // Batch analysis state tracking
        private readonly object _batchAnalysisLock = new object();
        private readonly HashSet<string> _currentlyAnalyzing = new HashSet<string>();
        private readonly HashSet<string> _alreadyAnalyzed = new HashSet<string>();
        private bool _batchAnalysisInProgress = false;

        /// <summary>
        /// Batch analyze requirements in background after import.
        /// Shows progress notifications and updates requirements with analysis results.
        /// Thread-safe with duplicate prevention.
        /// </summary>
        private async Task BatchAnalyzeRequirementsAsync(List<Requirement> requirements)
        {
            if (_analysisService == null || !requirements.Any())
                return;

            // Prevent concurrent batch analysis
            lock (_batchAnalysisLock)
            {
                if (_batchAnalysisInProgress)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Already in progress - skipping duplicate call for {requirements.Count} requirements");
                    return;
                }
                _batchAnalysisInProgress = true;
            }

            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsBatchAnalyzing = true;
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] === STARTING BATCH ANALYSIS FOR {requirements.Count} REQUIREMENTS ===");
                
                await Task.Delay(500); // Brief delay to let UI settle after import

                // Get requirements in the order they appear in the UI (sorted view)
                var orderedRequirements = new List<Requirement>();
                try
                {
                    var requirementsView = _requirementsNavigator?.RequirementsView;
                    if (requirementsView != null)
                    {
                        // Use the UI display order - this is the sorted view the user actually sees
                        foreach (Requirement req in requirementsView)
                        {
                            if (requirements.Contains(req))
                            {
                                orderedRequirements.Add(req);
                            }
                        }
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Using UI display order: [{string.Join(", ", orderedRequirements.Select(r => r.Item))}]");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[BATCH ANALYSIS] RequirementsView not available, using import order");
                        orderedRequirements = requirements;
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Error getting UI order: {ex.Message}, using import order");
                    orderedRequirements = requirements;
                }

                // Filter requirements to only those that need analysis
                var needAnalysis = orderedRequirements.Where(r => 
                {
                    if (string.IsNullOrWhiteSpace(r.Item)) return false;
                    
                    lock (_batchAnalysisLock)
                    {
                        if (_currentlyAnalyzing.Contains(r.Item) || _alreadyAnalyzed.Contains(r.Item))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] SKIPPING {r.Item} - already processing or analyzed");
                            return false;
                        }
                    }
                    
                    if (r.Analysis?.IsAnalyzed == true)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] SKIPPING {r.Item} - already has analysis");
                        lock (_batchAnalysisLock)
                        {
                            _alreadyAnalyzed.Add(r.Item);
                        }
                        return false;
                    }
                    
                    if (string.IsNullOrWhiteSpace(r.Description))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] SKIPPING {r.Item} - no description");
                        return false;
                    }
                    
                    return true;
                }).ToList();

                TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Filtered to {needAnalysis.Count} requirements needing analysis (in UI display order)");
                
                if (!needAnalysis.Any())
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[BATCH ANALYSIS] No requirements need analysis - completing");
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        SetTransientStatus("All requirements already analyzed", 3);
                    });
                    return;
                }

                // Mark all requirements as currently being analyzed
                lock (_batchAnalysisLock)
                {
                    foreach (var req in needAnalysis)
                    {
                        _currentlyAnalyzing.Add(req.Item);
                    }
                }

                int completed = 0;
                int total = needAnalysis.Count;
                TimeSpan? avgAnalysisTime = null;

                TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Processing {total} requirements in UI display order: [{string.Join(", ", needAnalysis.Select(r => r.Item))}]");

                // Process requirements sequentially to avoid overwhelming the LLM service
                const int maxRetries = 2; // Define retry limit for consistent analysis
                foreach (var req in needAnalysis)
                {
                    try
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] === PROCESSING REQUIREMENT {req.Item} ({completed + 1}/{total}) ===");
                        
                        // Show progress message
                        var progressMessage = total == 1 
                            ? "Analyzing requirement..." 
                            : avgAnalysisTime.HasValue
                                ? $"Analyzing requirements... ({completed + 1}/{total}) - ~{Math.Ceiling((total - completed) * avgAnalysisTime.Value.TotalMinutes)} min remaining"
                                : $"Analyzing requirements... ({completed + 1}/{total})";
                        
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            SetTransientStatus(progressMessage, 300); // Long timeout for analysis
                        });
                        
                        // Set UI state to show spinner for the requirement being analyzed
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            // If this is the current requirement being viewed, show the analyzing state
                            if (CurrentRequirement?.Item == req.Item)
                            {
                                try
                                {
                                    var tcg = GetTestCaseGeneratorInstance();
                                    var analysisVmProp = tcg?.GetType().GetProperty("AnalysisVM", BindingFlags.Public | BindingFlags.Instance);
                                    var analysisVm = analysisVmProp?.GetValue(tcg);
                                    
                                    if (analysisVm != null)
                                    {
                                        // Set IsAnalyzing = true to show orange spinner
                                        var isAnalyzingProp = analysisVm.GetType().GetProperty("IsAnalyzing", BindingFlags.Public | BindingFlags.Instance);
                                        isAnalyzingProp?.SetValue(analysisVm, true);
                                        
                                        // Set status message
                                        var statusProp = analysisVm.GetType().GetProperty("AnalysisStatusMessage", BindingFlags.Public | BindingFlags.Instance);
                                        statusProp?.SetValue(analysisVm, "Analyzing requirement quality...");
                                        
                                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Set analyzing UI state for current requirement {req.Item}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[BATCH ANALYSIS] Failed to set analyzing UI state for {req.Item}");
                                }
                            }
                        });
                        
                        // Perform analysis with retry logic for robustness
                        var analysisStart = DateTime.Now;
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] CALLING AnalyzeRequirementAsync for {req.Item} at {analysisStart:HH:mm:ss.fff}");
                        
                        RequirementAnalysis? analysis = null;
                        int retryCount = 0;
                        
                        while (retryCount <= maxRetries && (analysis?.IsAnalyzed != true))
                        {
                            if (retryCount > 0)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] RETRY {retryCount}/{maxRetries} for {req.Item}");
                                await Task.Delay(1000); // Brief delay before retry
                            }
                            
                            try
                            {
                                analysis = await _analysisService.AnalyzeRequirementAsync(req);
                                
                                if (analysis?.IsAnalyzed == true)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] SUCCESS on attempt {retryCount + 1} for {req.Item}");
                                    break; // Success - exit retry loop
                                }
                                else if (retryCount < maxRetries)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Failed analysis on attempt {retryCount + 1} for {req.Item}, will retry");
                                }
                            }
                            catch (Exception retryEx)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Error(retryEx, $"[BATCH ANALYSIS] Exception on attempt {retryCount + 1} for {req.Item}: {retryEx.Message}");
                                if (retryCount == maxRetries)
                                {
                                    throw; // Re-throw on final attempt
                                }
                            }
                            
                            retryCount++;
                        }
                        
                        var analysisEnd = DateTime.Now;
                        var analysisDuration = analysisEnd - analysisStart;
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] COMPLETED {req.Item} at {analysisEnd:HH:mm:ss.fff} (duration: {analysisDuration.TotalSeconds:F1}s, attempts: {retryCount + 1}) - IsAnalyzed: {analysis?.IsAnalyzed}, Score: {analysis?.QualityScore}");
                        
                        // Update timing estimate
                        if (completed == 0)
                        {
                            avgAnalysisTime = analysisDuration;
                        }
                        else
                        {
                            avgAnalysisTime = TimeSpan.FromTicks((avgAnalysisTime.Value.Ticks + analysisDuration.Ticks) / 2);
                        }
                        
                        // Update the requirement with analysis results
                        req.Analysis = analysis;
                        completed++;
                        
                        // Mark as analyzed and remove from currently processing
                        lock (_batchAnalysisLock)
                        {
                            _currentlyAnalyzing.Remove(req.Item);
                            _alreadyAnalyzed.Add(req.Item);
                        }
                        
                        // Notify UI and clear analyzing state
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            AnalysisMediator.NotifyAnalysisUpdated(req);
                            OnPropertyChanged(nameof(Requirements));
                            
                            // If this is the current requirement being viewed, clear the analyzing state
                            if (CurrentRequirement?.Item == req.Item)
                            {
                                try
                                {
                                    var tcg = GetTestCaseGeneratorInstance();
                                    var analysisVmProp = tcg?.GetType().GetProperty("AnalysisVM", BindingFlags.Public | BindingFlags.Instance);
                                    var analysisVm = analysisVmProp?.GetValue(tcg);
                                    
                                    if (analysisVm != null)
                                    {
                                        // Clear IsAnalyzing to hide spinner
                                        var isAnalyzingProp = analysisVm.GetType().GetProperty("IsAnalyzing", BindingFlags.Public | BindingFlags.Instance);
                                        isAnalyzingProp?.SetValue(analysisVm, false);
                                        
                                        // Trigger refresh of analysis display
                                        var refreshMethod = analysisVm.GetType().GetMethod("RefreshAnalysisDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                                        refreshMethod?.Invoke(analysisVm, null);
                                        
                                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Cleared analyzing UI state and refreshed display for current requirement {req.Item}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[BATCH ANALYSIS] Failed to clear analyzing UI state for {req.Item}");
                                }
                            }
                            
                            // If this is the current requirement being viewed, force immediate UI refresh
                            if (CurrentRequirement?.Item == req.Item)
                            {
                                OnPropertyChanged(nameof(CurrentRequirement));
                            }
                        });
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] Updated UI for requirement {req.Item}");
                        
                        // Small delay between analyses to avoid overwhelming the service
                        await Task.Delay(500); // Increased delay for better reliability
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[BATCH ANALYSIS] ANALYSIS FAILED PERMANENTLY for requirement {req.Item} after {maxRetries + 1} attempts: {ex.Message}");
                        
                        // Create error analysis with detailed information
                        req.Analysis = new RequirementAnalysis
                        {
                            IsAnalyzed = false,
                            ErrorMessage = $"Analysis failed after {maxRetries + 1} attempts: {ex.Message}",
                            Timestamp = DateTime.Now,
                            QualityScore = 0
                        };
                        
                        completed++;
                        
                        // Mark as processed (even though it failed)
                        lock (_batchAnalysisLock)
                        {
                            _currentlyAnalyzing.Remove(req.Item);
                            _alreadyAnalyzed.Add(req.Item);
                        }
                        
                        // Continue with next requirement even if one fails
                    }
                }

                // Final summary
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(WorkspacePath))
                    {
                        SaveWorkspace();
                    }
                    
                    var analyzedReqs = needAnalysis.Where(r => r.Analysis?.IsAnalyzed == true).ToList();
                    var failedReqs = needAnalysis.Where(r => r.Analysis?.IsAnalyzed != true).ToList();
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS FINAL SUMMARY]");
                    TestCaseEditorApp.Services.Logging.Log.Info($"Total processed: {completed}");
                    TestCaseEditorApp.Services.Logging.Log.Info($"Successfully analyzed: {analyzedReqs.Count} - [{string.Join(", ", analyzedReqs.Select(r => r.Item))}]");
                    TestCaseEditorApp.Services.Logging.Log.Info($"Failed: {failedReqs.Count} - [{string.Join(", ", failedReqs.Select(r => r.Item))}]");
                    
                    var successRate = total > 0 ? (analyzedReqs.Count * 100 / total) : 100;
                    SetTransientStatus($"Analysis complete - {analyzedReqs.Count}/{total} requirements analyzed ({successRate}% success)", 6);
                    
                    // Refresh the current requirement view
                    if (CurrentRequirement != null)
                    {
                        OnPropertyChanged(nameof(CurrentRequirement));
                    }
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[BATCH ANALYSIS] === COMPLETED BATCH ANALYSIS ===");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[BATCH ANALYSIS] Fatal error during batch analysis: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SetTransientStatus($"Batch analysis failed: {ex.Message}", 8);
                });
            }
            finally
            {
                lock (_batchAnalysisLock)
                {
                    _batchAnalysisInProgress = false;
                    // Clear the currently analyzing set in case of any issues
                    _currentlyAnalyzing.Clear();
                }
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsBatchAnalyzing = false;
                    
                    // Clear the spinner from the current requirement's analysis display
                    try
                    {
                        var tcg = GetTestCaseGeneratorInstance();
                        if (tcg != null)
                        {
                            var analysisVmProp = tcg.GetType().GetProperty("AnalysisVM", BindingFlags.Public | BindingFlags.Instance);
                            var analysisVm = analysisVmProp?.GetValue(tcg);
                            if (analysisVm != null)
                            {
                                // Clear IsAnalyzing state
                                var isAnalyzingProp = analysisVm.GetType().GetProperty("IsAnalyzing", BindingFlags.Public | BindingFlags.Instance);
                                isAnalyzingProp?.SetValue(analysisVm, false);
                                
                                // Refresh analysis display to update UI
                                var refreshMethod = analysisVm.GetType().GetMethod("RefreshAnalysisDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                                refreshMethod?.Invoke(analysisVm, null);
                                
                                TestCaseEditorApp.Services.Logging.Log.Info("[BATCH ANALYSIS] Cleared spinner and refreshed analysis display when batch completed");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[BATCH ANALYSIS] Error clearing spinner after batch completion: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Batch export requirements for ChatGPT analysis in background after import.
        /// Shows progress notifications and exports requirements in ChatGPT-ready format.
        /// </summary>
        private void BatchExportRequirementsForChatGpt(List<Requirement> requirements)
        {
            if (!requirements.Any())
                return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Starting export for {requirements.Count} requirements");
                
                // Show progress notification
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SetTransientStatus($"Exporting {requirements.Count} requirements for ChatGPT analysis...", 3);
                });

                // Export requirements using the service
                string formattedText = _chatGptExportService.ExportMultipleRequirements(requirements, includeAnalysisRequest: true);
                
                // Save to file and copy to clipboard
                bool clipboardSuccess = _chatGptExportService.CopyToClipboard(formattedText);
                
                // Optionally save to file as well
                string fileName = $"Requirements_Export_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                
                try
                {
                    System.IO.File.WriteAllText(filePath, formattedText);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Saved to file: {filePath}");
                    
                    // Update the last exported file path on UI thread
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        LastChatGptExportFilePath = filePath;
                    });
                }
                catch (Exception fileEx)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[CHATGPT EXPORT] Failed to save file, but clipboard export may have succeeded: {fileEx.Message}");
                }

                // Show completion notification
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (clipboardSuccess)
                    {
                        SetTransientStatus($"✅ {requirements.Count} requirements exported for ChatGPT! Copied to clipboard and saved to {fileName}", 6);
                    }
                    else
                    {
                        SetTransientStatus($"⚠️ Export completed but clipboard copy failed. File saved to {fileName}", 5);
                    }
                });

                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Completed export for {requirements.Count} requirements, clipboard={clipboardSuccess}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to export requirements: {ex.Message}");
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SetTransientStatus("❌ Failed to export requirements for ChatGPT.", 4);
                });
            }
        }

        /// <summary>
        /// Opens the last exported ChatGPT file in Notepad.
        /// </summary>
        private void OpenChatGptExportFile()
        {
            try
            {
                if (string.IsNullOrEmpty(LastChatGptExportFilePath) || !System.IO.File.Exists(LastChatGptExportFilePath))
                {
                    SetTransientStatus("❌ No recent ChatGPT export file found.", 3);
                    return;
                }

                // Open the file in Notepad
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{LastChatGptExportFilePath}\"",
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(processInfo);
                TestCaseEditorApp.Services.Logging.Log.Info($"[CHATGPT EXPORT] Opened file in Notepad: {LastChatGptExportFilePath}");
                SetTransientStatus("📝 Opened ChatGPT export file in Notepad", 3);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[CHATGPT EXPORT] Failed to open file in Notepad: {ex.Message}");
                SetTransientStatus("❌ Failed to open file in Notepad", 3);
            }
        }

        /// <summary>
        /// Creates a new project with AnythingLLM workspace integration.
        /// </summary>
        private async void CreateNewProject()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[PROJECT] CreateNewProject started");
                
                // Show workspace selection dialog for creating new workspace only
                var dialog = new TestCaseEditorApp.MVVM.Views.WorkspaceSelectionDialog()
                {
                    Owner = Application.Current.MainWindow
                };

                TestCaseEditorApp.Services.Logging.Log.Info("[PROJECT] Dialog created, showing...");
                
                var result = dialog.ShowDialog();
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Dialog result: {result}, SelectedWorkspace: {dialog.SelectedWorkspace?.Name ?? "null"}");

                if (result == true && dialog.SelectedWorkspace != null)
                {
                    var workspace = dialog.SelectedWorkspace;
                    
                    // Clear existing requirements and set workspace
                    Requirements.Clear();
                    CurrentAnythingLLMWorkspaceSlug = workspace.Slug;
                    
                    // Reset to first step
                    SelectedStep = TestCaseGeneratorSteps.FirstOrDefault();

                    if (dialog.WasCreated)
                    {
                        // New workspace was created - offer to import requirements
                        var importChoice = System.Windows.MessageBox.Show(
                            $"Successfully created workspace '{workspace.Name}'!\\n\\n" +
                            "Would you like to import requirements now?",
                            "Import Requirements",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                        
                        if (importChoice == System.Windows.MessageBoxResult.Yes)
                        {
                            // Show import options
                            var importType = System.Windows.MessageBox.Show(
                                "Choose import method:\\n\\n" +
                                "• Yes: Import from Word document with analysis\\n" +
                                "• No: Quick import (Decagon format)\\n" +
                                "• Cancel: Skip import for now",
                                "Import Method",
                                System.Windows.MessageBoxButton.YesNoCancel,
                                System.Windows.MessageBoxImage.Question);
                            
                            if (importType == System.Windows.MessageBoxResult.Yes)
                            {
                                // Import Word with analysis
                                ImportWordCommand.Execute(null);
                            }
                            else if (importType == System.Windows.MessageBoxResult.No)
                            {
                                // Quick import
                                QuickImportCommand.Execute(null);
                            }
                        }
                        
                        SetTransientStatus($"🆕 Created and opened project: {workspace.Name}", 4);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Created new project with workspace slug '{workspace.Slug}'");
                    }
                    else
                    {
                        // Existing workspace was selected
                        SetTransientStatus($"📂 Opened existing project: {workspace.Name}", 4);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Opened existing project with workspace slug '{workspace.Slug}'");
                        
                        // Optionally load local workspace file
                        var loadLocal = System.Windows.MessageBox.Show(
                            "Would you like to load a local workspace file (.tcex.json) with this project?",
                            "Load Local Workspace",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                        
                        if (loadLocal == System.Windows.MessageBoxResult.Yes)
                        {
                            LoadWorkspace();
                        }
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[PROJECT] Project creation cancelled or failed");
                    SetTransientStatus("⚠️ Project creation cancelled", 3);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[PROJECT] Failed to create new project: {ex.Message}");
                SetTransientStatus("❌ Failed to create project", 3);
            }
        }

        /// <summary>
        /// Opens an existing project by selecting from available AnythingLLM workspaces.
        /// </summary>
        private async void OpenProject()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[PROJECT] OpenProject started");
                
                // Show workspace selection dialog for selecting existing workspace
                var dialog = new TestCaseEditorApp.MVVM.Views.WorkspaceSelectionDialog(TestCaseEditorApp.MVVM.Views.WorkspaceSelectionDialog.DialogMode.SelectExisting)
                {
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true && dialog.SelectedWorkspace != null)
                {
                    var workspace = dialog.SelectedWorkspace;
                    
                    // Clear existing requirements and set workspace
                    Requirements.Clear();
                    CurrentAnythingLLMWorkspaceSlug = workspace.Slug;
                    
                    // Reset to first step
                    SelectedStep = TestCaseGeneratorSteps.FirstOrDefault();
                    
                    SetTransientStatus($"📂 Opened project: {workspace.Name}", 4);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Opened project with workspace slug '{workspace.Slug}'");
                    
                    // Optionally load local workspace file
                    var loadLocal = MessageBox.Show(
                        "Would you like to load a local workspace file (.tcex.json) with this project?",
                        "Load Local Workspace",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (loadLocal == MessageBoxResult.Yes)
                    {
                        LoadWorkspace();
                    }
                }
                else
                {
                    SetTransientStatus("⚠️ Project opening cancelled", 3);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[PROJECT] Failed to open project: {ex.Message}");
                MessageBox.Show(
                    $"Error loading workspaces: {ex.Message}\n\n" +
                    "Please check:\n" +
                    "• AnythingLLM is running and accessible\n" +
                    "• API key is properly configured\n" +
                    "• Network connectivity",
                    "Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                SetTransientStatus("❌ Failed to open project", 3);
            }
        }

        /// <summary>
        /// Analyzes requirements that haven't been analyzed yet.
        /// </summary>
        private void AnalyzeUnanalyzed()
        {
            try
            {
                var unanalyzedCount = Requirements.Count(r => r.Analysis == null);
                SetTransientStatus($"🔍 Analyzing {unanalyzedCount} unanalyzed requirements...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Analyze unanalyzed requested for {unanalyzedCount} requirements");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to analyze unanalyzed: {ex.Message}");
                SetTransientStatus("❌ Failed to analyze unanalyzed", 3);
            }
        }

        /// <summary>
        /// Re-analyzes requirements that have been modified.
        /// </summary>
        private void ReAnalyzeModified()
        {
            try
            {
                var modifiedCount = Requirements.Count(r => r.Analysis != null && r.IsQueuedForReanalysis);
                SetTransientStatus($"🔄 Re-analyzing {modifiedCount} modified requirements...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Re-analyze modified requested for {modifiedCount} requirements");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to re-analyze modified: {ex.Message}");
                SetTransientStatus("❌ Failed to re-analyze modified", 3);
            }
        }

        /// <summary>
        /// Imports additional requirements to the current project.
        /// </summary>
        private void ImportAdditional()
        {
            try
            {
                SetTransientStatus("📥 Import additional coming soon...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[IMPORT] Import additional requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[IMPORT] Failed to import additional: {ex.Message}");
                SetTransientStatus("❌ Failed to import additional", 3);
            }
        }

        /// <summary>
        /// Imports structured analysis from a file (JSON or other structured format).
        /// </summary>
        private void ImportStructuredAnalysis()
        {
            try
            {
                SetTransientStatus("📥 Import structured analysis coming soon...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[ANALYSIS] Structured analysis import requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to import structured analysis: {ex.Message}");
                SetTransientStatus("❌ Failed to import analysis", 3);
            }
        }

        /// <summary>
        /// Generates a learning prompt for ChatGPT based on current requirements and copies to clipboard.
        /// </summary>
        private void GenerateLearningPrompt()
        {
            try
            {
                if (Requirements?.Any() != true)
                {
                    SetTransientStatus("⚠️ No requirements to generate learning prompt from", 3);
                    return;
                }

                // Generate a comprehensive learning prompt
                var promptBuilder = new System.Text.StringBuilder();
                promptBuilder.AppendLine("I'm working with a requirement analysis project and would like you to learn from these requirements to help me with future analysis. Please analyze the following requirements and identify patterns, structures, and characteristics that would be useful for test case generation and requirement analysis:");
                promptBuilder.AppendLine();

                // Add requirement details
                int counter = 1;
                foreach (var req in Requirements.Take(20)) // Limit to avoid huge prompts
                {
                    promptBuilder.AppendLine($"**Requirement {counter}:**");
                    promptBuilder.AppendLine($"- Item: {req.Item ?? "N/A"}");
                    promptBuilder.AppendLine($"- Name: {req.Name ?? "N/A"}");
                    if (!string.IsNullOrEmpty(req.Description))
                    {
                        promptBuilder.AppendLine($"- Description: {req.Description}");
                    }
                    if (req.Analysis?.Issues?.Any() == true)
                    {
                        promptBuilder.AppendLine($"- Analysis Issues: {string.Join("; ", req.Analysis.Issues.Take(3).Select(i => i.Description))}");
                    }
                    if (req.Analysis?.Recommendations?.Any() == true)
                    {
                        promptBuilder.AppendLine($"- Recommendations: {string.Join("; ", req.Analysis.Recommendations.Take(3).Select(r => r.Description))}");
                    }
                    promptBuilder.AppendLine();
                    counter++;
                }

                if (Requirements.Count > 20)
                {
                    promptBuilder.AppendLine($"*(Plus {Requirements.Count - 20} additional requirements not shown)*");
                    promptBuilder.AppendLine();
                }

                promptBuilder.AppendLine("Based on these requirements, please learn our analysis patterns for future use:");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**ANALYSIS TRAINING:**");
                promptBuilder.AppendLine("When I send you a requirement for analysis, please always respond with EXACTLY this JSON format:");
                promptBuilder.AppendLine("{\"QualityScore\": <1-10>, \"Issues\": [{\"Category\": \"<category>\", \"Severity\": \"<High|Medium|Low>\", \"Description\": \"...\"}], \"Recommendations\": [{\"Category\": \"<category>\", \"Description\": \"...\", \"Example\": \"...\"}], \"FreeformFeedback\": \"...\"}");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**TEST CASE TRAINING:**");
                promptBuilder.AppendLine("When I send you a requirement for test case generation, please always respond with test cases in this format:");
                promptBuilder.AppendLine("- Objective: To verify [specific requirement aspect]");
                promptBuilder.AppendLine("- Input: [what will be provided/configured]");
                promptBuilder.AppendLine("- Expected Output: [what should happen - be specific]");
                promptBuilder.AppendLine("- Pass Criteria: [how to determine success/failure]");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**PATTERN ANALYSIS:**");
                promptBuilder.AppendLine("1. Identify common patterns and structures in the above requirements");
                promptBuilder.AppendLine("2. Note the types of analysis that would be most valuable for this domain");
                promptBuilder.AppendLine("3. Suggest test case generation strategies specific to these requirement types");
                promptBuilder.AppendLine("4. Highlight domain-specific terminology and concepts I use");
                promptBuilder.AppendLine("5. Remember these patterns for consistent future analysis");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("**FUTURE COMMUNICATION:**");
                promptBuilder.AppendLine("- When I paste a requirement and say 'ANALYZE', use the JSON format above");
                promptBuilder.AppendLine("- When I paste a requirement and say 'GENERATE TEST CASES', use the test case format above");
                promptBuilder.AppendLine("- Always be consistent with the formatting and approach you learn from this training data");

                string prompt = promptBuilder.ToString();

                // Copy to clipboard
                System.Windows.Clipboard.SetText(prompt);

                SetTransientStatus("📋 Learning prompt copied to clipboard - ready for ChatGPT!", 4);
                TestCaseEditorApp.Services.Logging.Log.Info($"[LEARNING] Generated learning prompt from {Requirements.Count} requirements");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[LEARNING] Failed to generate learning prompt: {ex.Message}");
                SetTransientStatus("❌ Failed to generate learning prompt", 3);
            }
        }

        /// <summary>
        /// Generates a standardized analysis command for the current requirement.
        /// </summary>
        private void GenerateAnalysisCommand()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    SetTransientStatus("⚠️ No requirement selected", 3);
                    return;
                }

                var command = $"ANALYZE: {CurrentRequirement.Item} - {CurrentRequirement.Name}\n\n{CurrentRequirement.Description}";
                System.Windows.Clipboard.SetText(command);
                
                SetTransientStatus($"📋 Analysis command copied for {CurrentRequirement.Item}", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QUICK_CMD] Generated analysis command for {CurrentRequirement.Item}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[QUICK_CMD] Failed to generate analysis command: {ex.Message}");
                SetTransientStatus("❌ Failed to generate command", 3);
            }
        }

        /// <summary>
        /// Generates a standardized test case generation command for the current requirement.
        /// </summary>
        private void GenerateTestCaseCommand()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    SetTransientStatus("⚠️ No requirement selected", 3);
                    return;
                }

                var command = $"GENERATE TEST CASES: {CurrentRequirement.Item} - {CurrentRequirement.Name}\n\n{CurrentRequirement.Description}";
                System.Windows.Clipboard.SetText(command);
                
                SetTransientStatus($"📋 Test case command copied for {CurrentRequirement.Item}", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[QUICK_CMD] Generated test case command for {CurrentRequirement.Item}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[QUICK_CMD] Failed to generate test case command: {ex.Message}");
                SetTransientStatus("❌ Failed to generate command", 3);
            }
        }

        /// <summary>
        /// Sets up integrated LLM workspace for streamlined communication with standardized formats.
        /// </summary>
        private void SetupLlmWorkspace()
        {
            try
            {
                // Generate comprehensive workspace setup instructions
                var setupBuilder = new System.Text.StringBuilder();
                setupBuilder.AppendLine("LLM WORKSPACE SETUP - REQUIREMENT ANALYSIS & TEST CASE GENERATION");
                setupBuilder.AppendLine("=".PadRight(80, '='));
                setupBuilder.AppendLine();
                setupBuilder.AppendLine("This document sets up standardized communication between the Test Case Editor App and your LLM workspace.");
                setupBuilder.AppendLine("Please save this as your workspace context for consistent, automated responses.");
                setupBuilder.AppendLine();
                
                setupBuilder.AppendLine("**COMMUNICATION PROTOCOL:**");
                setupBuilder.AppendLine();
                setupBuilder.AppendLine("1. REQUIREMENT ANALYSIS REQUESTS:");
                setupBuilder.AppendLine("   When I send: 'ANALYZE: [requirement text]'");
                setupBuilder.AppendLine("   You respond with EXACTLY this JSON structure (no markdown, no code blocks):");
                setupBuilder.AppendLine("   {");
                setupBuilder.AppendLine("     \"QualityScore\": <1-10>,");
                setupBuilder.AppendLine("     \"Issues\": [");
                setupBuilder.AppendLine("       {");
                setupBuilder.AppendLine("         \"Category\": \"<Clarity|Testability|Completeness|Atomicity|Actionability|Consistency>\",");
                setupBuilder.AppendLine("         \"Severity\": \"<High|Medium|Low>\",");
                setupBuilder.AppendLine("         \"Description\": \"<specific issue description>\"");
                setupBuilder.AppendLine("       }");
                setupBuilder.AppendLine("     ],");
                setupBuilder.AppendLine("     \"Recommendations\": [");
                setupBuilder.AppendLine("       {");
                setupBuilder.AppendLine("         \"Category\": \"<same categories as Issues>\",");
                setupBuilder.AppendLine("         \"Description\": \"<actionable recommendation>\",");
                setupBuilder.AppendLine("         \"Example\": \"<concrete rewrite example with [brackets] for placeholders>\"");
                setupBuilder.AppendLine("       }");
                setupBuilder.AppendLine("     ],");
                setupBuilder.AppendLine("     \"FreeformFeedback\": \"<additional insights not captured above>\"");
                setupBuilder.AppendLine("   }");
                setupBuilder.AppendLine();
                
                setupBuilder.AppendLine("2. TEST CASE GENERATION REQUESTS:");
                setupBuilder.AppendLine("   When I send: 'GENERATE TEST CASES: [requirement text]'");
                setupBuilder.AppendLine("   You respond with test cases in this format:");
                setupBuilder.AppendLine("   **Test Case 1:**");
                setupBuilder.AppendLine("   - Objective: To verify [specific aspect of the requirement]");
                setupBuilder.AppendLine("   - Environment: [test environment/configuration needed]");
                setupBuilder.AppendLine("   - Input: [specific inputs, parameters, or actions]");
                setupBuilder.AppendLine("   - Expected Output: [specific expected results]");
                setupBuilder.AppendLine("   - Pass Criteria: [clear pass/fail determination]");
                setupBuilder.AppendLine();
                
                setupBuilder.AppendLine("3. QUICK COMMANDS:");
                setupBuilder.AppendLine("   - 'ANALYZE: [requirement]' → JSON analysis only");
                setupBuilder.AppendLine("   - 'GENERATE TEST CASES: [requirement]' → Formatted test cases only");
                setupBuilder.AppendLine("   - 'QUICK SCORE: [requirement]' → Just the quality score (1-10)");
                setupBuilder.AppendLine();
                
                if (Requirements?.Any() == true)
                {
                    setupBuilder.AppendLine("**CURRENT PROJECT CONTEXT:**");
                    setupBuilder.AppendLine();
                    setupBuilder.AppendLine($"Project: {Requirements.Count} requirements loaded");
                    
                    // Add domain-specific context
                    var domains = Requirements
                        .Where(r => !string.IsNullOrEmpty(r.Name))
                        .Take(10)
                        .Select(r => r.Name?.Split(' ').FirstOrDefault())
                        .Where(d => !string.IsNullOrEmpty(d))
                        .Distinct()
                        .Take(5);
                    
                    if (domains.Any())
                    {
                        setupBuilder.AppendLine($"Domain indicators: {string.Join(", ", domains)}");
                    }
                    
                    setupBuilder.AppendLine();
                    setupBuilder.AppendLine("Sample requirements for context:");
                    var samples = Requirements.Take(3);
                    foreach (var req in samples)
                    {
                        var namePreview = req.Name != null ? req.Name.Substring(0, Math.Min(80, req.Name.Length)) : "No name";
                        setupBuilder.AppendLine($"- {req.Item}: {namePreview}...");
                    }
                    setupBuilder.AppendLine();
                }
                
                setupBuilder.AppendLine("**QUALITY GUIDELINES:**");
                setupBuilder.AppendLine("- Score 1-3: Poor (not testable, multiple critical issues)");
                setupBuilder.AppendLine("- Score 4-5: Fair (some clarity issues, limited testability)");
                setupBuilder.AppendLine("- Score 6-7: Good (minor issues, generally testable)");
                setupBuilder.AppendLine("- Score 8-9: Very good (clear, testable, minor improvements)");
                setupBuilder.AppendLine("- Score 10: Excellent (exemplary requirement)");
                setupBuilder.AppendLine();
                
                setupBuilder.AppendLine("**IMPORTANT:**");
                setupBuilder.AppendLine("- Always use the exact formats specified above");
                setupBuilder.AppendLine("- No extra commentary unless specifically requested");
                setupBuilder.AppendLine("- Be consistent with terminology and analysis approach");
                setupBuilder.AppendLine("- Focus on actionable, implementable feedback");
                
                string workspaceSetup = setupBuilder.ToString();

                // Copy to clipboard
                System.Windows.Clipboard.SetText(workspaceSetup);

                SetTransientStatus("🔧 LLM workspace setup copied to clipboard - paste into your LLM workspace!", 5);
                TestCaseEditorApp.Services.Logging.Log.Info("[LLM_SETUP] Generated workspace setup document");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[LLM_SETUP] Failed to generate workspace setup: {ex.Message}");
                SetTransientStatus("❌ Failed to generate workspace setup", 3);
            }
        }

        /// <summary>
        /// Pastes ChatGPT analysis from clipboard and applies it to current requirements.
        /// </summary>
        private void PasteChatGptAnalysis()
        {
            try
            {
                SetTransientStatus("📋 Paste ChatGPT analysis coming soon...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[ANALYSIS] ChatGPT analysis paste requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to paste ChatGPT analysis: {ex.Message}");
                SetTransientStatus("❌ Failed to paste analysis", 3);
            }
        }
        
        /// <summary>
        /// Shows a dialog for selecting an AnythingLLM workspace.
        /// </summary>
        private int ShowWorkspaceSelectionDialog(string[] workspaceNames)
        {
            // Simple implementation using built-in dialogs until we have a proper UI
            var selection = string.Join("\\n", workspaceNames.Select((name, index) => $"{index}: {name}"));
            var message = $"Available AnythingLLM Workspaces:\\n{selection}\\n\\nEnter the number of the workspace to open:";
            
            var input = System.Windows.MessageBox.Show(message, "Select Workspace", System.Windows.MessageBoxButton.OKCancel);
            
            // For now, return 0 (first workspace) if OK, -1 if cancelled
            return input == System.Windows.MessageBoxResult.OK ? 0 : -1;
        }

        /// <summary>
        /// Saves the current project state.
        /// </summary>
        private void SaveProject()
        {
            try
            {
                if (string.IsNullOrEmpty(CurrentAnythingLLMWorkspaceSlug))
                {
                    SetTransientStatus("⚠️ No AnythingLLM workspace selected", 3);
                    return;
                }
                
                // Use existing SaveWorkspace functionality
                SaveWorkspace();
                
                SetTransientStatus($"💾 Project saved", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Project saved for workspace '{CurrentAnythingLLMWorkspaceSlug}'");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[PROJECT] Failed to save project: {ex.Message}");
                SetTransientStatus("❌ Failed to save project", 3);
            }
        }

        /// <summary>
        /// Closes the current project.
        /// </summary>
        private void CloseProject()
        {
            try
            {
                // Clear requirements and reset state
                Requirements.Clear();
                CurrentAnythingLLMWorkspaceSlug = null;
                WorkspacePath = null;
                
                // Reset to first step
                SelectedStep = TestCaseGeneratorSteps.FirstOrDefault();
                
                SetTransientStatus("📄 Project closed", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[PROJECT] Project closed");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[PROJECT] Failed to close project: {ex.Message}");
                SetTransientStatus("❌ Failed to close project", 3);
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
            public string? ShowSaveFile(string title, string suggestedFileName, string filter, string defaultExt, string? initialDirectory = null) => null;
        }

        // Dispose/unsubscribe
        public void Dispose()
        {
            try { if (Requirements != null) Requirements.CollectionChanged -= RequirementsOnCollectionChanged; } catch { }
            try { UnhookOldRequirement(); } catch { }
            try { if (_statusTimer != null) { _statusTimer.Stop(); _statusTimer = null; } } catch { }
            try { if (_autoSaveService != null) { _autoSaveService.Stop(); _autoSaveService = null; } } catch { }
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