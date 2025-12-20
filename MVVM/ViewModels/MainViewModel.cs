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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.ViewModels;
using TestCaseEditorApp.MVVM.Domains.ChatGptExportAnalysis.ViewModels;
using TestCaseEditorApp.MVVM.Domains.RequirementAnalysisWorkflow.ViewModels;
using TestCaseEditorApp.Helpers;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
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
        // --- Service Layer ---
        private readonly IApplicationServices _applicationServices;
        private readonly IViewModelFactory _viewModelFactory;
        
        // --- NEW: Unified Navigation System ---
        private readonly IViewAreaCoordinator _viewAreaCoordinator;
        
        // --- Services / collaborators (for backwards compatibility) ---
        private readonly IRequirementService _requirementService;
        private readonly IPersistenceService? _persistence;
        private readonly IFileDialogService _fileDialog;
        private readonly IServiceProvider _services;

        // Optional/managed runtime services
        private LlmProbeService? _llmProbeService;
        private readonly ToastNotificationService _toastService;
        private readonly NotificationService _notificationService;
        private readonly ChatGptExportService _chatGptExportService;
        private readonly AnythingLLMService _anythingLLMService;

        // --- Logging ---
        private readonly ILogger<MainViewModel>? _logger;

        // --- Domain ViewModels ---
        private RequirementAnalysisViewModel? _requirementAnalysis;
        private RequirementGenerationViewModel? _requirementGeneration;
        private NavigationHeaderManagementViewModel? _navigationHeaderManagement;
        private ProjectManagementViewModel? _projectManagement;

        // --- Header / navigation / view state ---
        // Strongly-typed header instances
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private WorkspaceHeaderViewModel? _workspaceHeaderViewModel;

        // Shared ViewModels for menu access (created once, reused)
        private TestCaseGenerator_QuestionsVM? _questionsViewModel;
        private TestCaseGenerator_AssumptionsVM? _assumptionsViewModel;

        // Public accessors for domain ViewModels
        public RequirementAnalysisViewModel? RequirementAnalysis => _requirementAnalysis;
        public RequirementGenerationViewModel? RequirementGeneration => _requirementGeneration;
        public TestCaseGenerator_QuestionsVM? QuestionsViewModel => _questionsViewModel;
        public TestCaseGenerator_AssumptionsVM? AssumptionsViewModel => _assumptionsViewModel;

        // === UNIFIED NAVIGATION SYSTEM ===
        // Replace competing navigation properties with ViewAreaCoordinator
        public IViewAreaCoordinator ViewAreaCoordinator => _viewAreaCoordinator;
        public INavigationMediator NavigationMediator => _viewAreaCoordinator.NavigationMediator;
        
        // LEGACY NAVIGATION SUPPORT (for gradual migration)
        // These delegate to unified system but maintain compatibility
        
        // Active header slot: the UI binds to ActiveHeader (ContentControl Content="{Binding ActiveHeader}")
        public object? ActiveHeader
        {
            get => _viewAreaCoordinator.NavigationMediator.CurrentHeader;
            private set => _viewAreaCoordinator.NavigationMediator.SetActiveHeader(value);
        }

        // CurrentStepViewModel - main content area (UNIFIED via mediator)
        public object? CurrentStepViewModel
        {
            get => _viewAreaCoordinator.NavigationMediator.CurrentContent;
            set => _viewAreaCoordinator.NavigationMediator.SetMainContent(value);
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

        // Import workflow VM
        public ImportRequirementsWorkflowViewModel ImportWorkflow { get; private set; }
        public NewProjectWorkflowViewModel NewProjectWorkflow { get; set; }
        
        // --- Core observable collections / properties ---
        private ObservableCollection<Requirement> _requirements = new ObservableCollection<Requirement>();
        public ObservableCollection<Requirement> Requirements => _requirements;

        // Toast notifications collection for UI binding
        public ObservableCollection<ToastNotification> ToastNotifications => _toastService.Toasts;

        // --- Modal overlay system ---
        private object? _modalViewModel;
        public object? ModalViewModel
        {
            get => _modalViewModel;
            private set => SetProperty(ref _modalViewModel, value);
        }

        private ImportMethodSelectionViewModel? _importMethodViewModel;

        private string _modalTitle = "Modal Dialog";
        public string ModalTitle
        {
            get => _modalTitle;
            private set => SetProperty(ref _modalTitle, value);
        }

        public ICommand CloseModalCommand { get; }

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

        // Static initialization guard to prevent multiple instances from initializing AnythingLLM
        private static bool _anythingLLMInitializing = false;
        private static readonly object _initializationLock = new object();

        // AnythingLLM Status Properties - Track LlmConnectionManager with proper notifications
        [ObservableProperty]
        private bool isAnythingLLMAvailable;
        
        [ObservableProperty]
        private bool isAnythingLLMStarting;
        
        [ObservableProperty]
        private string anythingLLMStatusMessage = "Initializing AnythingLLM...";

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
        public ICommand AnalyzeRequirementsCommand { get; private set; }
        public ICommand BatchAnalyzeCommand { get; private set; }
        
        // ChatGPT Analysis Import Commands
        public ICommand ImportStructuredAnalysisCommand { get; private set; }
        public ICommand PasteChatGptAnalysisCommand { get; private set; }
        public ICommand GenerateLearningPromptCommand { get; private set; }
        public ICommand SetupLlmWorkspaceCommand { get; private set; }
        public ICommand GenerateAnalysisCommandCommand { get; private set; }
        public ICommand GenerateTestCaseCommandCommand { get; private set; }

        // Selected menu section - UNIFIED: delegates to NavigationMediator
        public string? SelectedMenuSection
        {
            get => _viewAreaCoordinator.NavigationMediator.CurrentSection;
            set
            {
                if (_viewAreaCoordinator.NavigationMediator.CurrentSection != value)
                {
                    _viewAreaCoordinator.NavigationMediator.NavigateToSection(value ?? "");
                    OnPropertyChanged(); // Maintain legacy binding compatibility
                    // TODO: Remove OnSelectedMenuSectionChanged after migration complete
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
            // Update workspace header to show dirty state (asterisk)
            if (_workspaceHeaderViewModel != null)
            {
                var baseName = string.IsNullOrEmpty(_workspaceHeaderViewModel.WorkspaceName)
                    ? "Test Case Editor"
                    : _workspaceHeaderViewModel.WorkspaceName;
                _workspaceHeaderViewModel.Title = IsDirty ? $"{baseName} *" : baseName;
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
                    
                    // Notify analysis commands that depend on current requirement
                    ((RelayCommand?)AnalyzeRequirementsCommand)?.NotifyCanExecuteChanged();
                    
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
        // Design-time constructor
        public MainViewModel()
            : this(
                  applicationServices: new ApplicationServices(
                      requirementService: new NoOpRequirementService(),
                      persistenceService: new NoOpPersistenceService(),
                      fileDialogService: new NoOpFileDialogService(),
                      toastService: new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher),
                      notificationService: new NotificationService(new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher)),
                      anythingLLMService: new AnythingLLMService(),
                      chatGptExportService: new ChatGptExportService()
                  ),
                  viewModelFactory: new ViewModelFactory(new ApplicationServices(
                      requirementService: new NoOpRequirementService(),
                      persistenceService: new NoOpPersistenceService(),
                      fileDialogService: new NoOpFileDialogService(),
                      toastService: new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher),
                      notificationService: new NotificationService(new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher)),
                      anythingLLMService: new AnythingLLMService(),
                      chatGptExportService: new ChatGptExportService()
                  )),
                  services: new SimpleServiceProviderStub())
        {
            // parameterless delegates to full constructor (no-op services)
        }

        // DI-friendly constructor. logger parameters are optional.
        public MainViewModel(
            IApplicationServices applicationServices,
            IViewModelFactory viewModelFactory,
            ProjectManagementViewModel? projectManagement = null,
            IServiceProvider? services = null)
        {
            // Store core services
            _applicationServices = applicationServices ?? throw new ArgumentNullException(nameof(applicationServices));
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            _services = services ?? new SimpleServiceProviderStub();
            
            // Initialize unified navigation system
            _viewAreaCoordinator = _viewModelFactory.CreateViewAreaCoordinator();
            
            // Subscribe to navigation mediator events to update legacy properties
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.HeaderChanged>(e => OnPropertyChanged(nameof(ActiveHeader)));
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.ContentChanged>(e => OnPropertyChanged(nameof(CurrentStepViewModel)));
            _viewAreaCoordinator.NavigationMediator.Subscribe<NavigationEvents.SectionChanged>(e => OnPropertyChanged(nameof(SelectedMenuSection)));
            
            // Subscribe to ViewAreaCoordinator changes to forward property notifications for UI binding
            
            // Extract commonly used services for convenience
            _requirementService = _applicationServices.RequirementService;
            _persistence = _applicationServices.PersistenceService;
            _fileDialog = _applicationServices.FileDialogService;
            _toastService = _applicationServices.ToastService;
            _notificationService = _applicationServices.NotificationService;
            _anythingLLMService = _applicationServices.AnythingLLMService;
            _chatGptExportService = _applicationServices.ChatGptExportService;
            _logger = _applicationServices.LoggerFactory?.CreateLogger<MainViewModel>();

            // Initialize domain ViewModels
            _projectManagement = projectManagement;
            _projectManagement?.Initialize(this);

            // LEGACY: Create ViewModels through factory for backward compatibility
            _workspaceHeaderViewModel = _viewAreaCoordinator.HeaderArea.ActiveHeader as WorkspaceHeaderViewModel ?? 
                                       _viewModelFactory.CreateWorkspaceHeaderViewModel();
            Navigation = _viewModelFactory.CreateNavigationViewModel();
            
            // Initialize workflows with proper event handling
            ImportWorkflow = _viewModelFactory.CreateImportWorkflowViewModel();
            ImportWorkflow.WorkflowCompleted += OnImportRequirementsWorkflowCompleted;
            ImportWorkflow.WorkflowCancelled += OnImportRequirementsWorkflowCancelled;
            
            NewProjectWorkflow = _viewModelFactory.CreateNewProjectWorkflowViewModel();
            NewProjectWorkflow.ProjectCreated += OnNewProjectCreated;
            NewProjectWorkflow.ProjectCancelled += OnNewProjectCancelled;

            // Initialize header instances through factory
            _testCaseGeneratorHeader = _viewModelFactory.CreateTestCaseGeneratorHeaderViewModel(this);

            // Complete initialization as in original constructor
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

            // Subscribe to AnythingLLM status updates via mediator
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusFromMediator;

            // Initialize auto-save timer
            InitializeAutoSave();

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

            // Subscribe to AnythingLLM status updates via mediator
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusFromMediator;

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
            // Initialize placeholder commands for Import/Export domain (TODO: Wire to actual domain ViewModels)
            OpenChatGptExportCommand = new RelayCommand(() => { /* TODO: Wire to RequirementImportExportViewModel */ }, () => !string.IsNullOrEmpty(LastChatGptExportFilePath) && System.IO.File.Exists(LastChatGptExportFilePath));
            
            // Initialize project management commands
            NewProjectCommand = new RelayCommand(() => CreateNewProject());
            OpenProjectCommand = new RelayCommand(() => OpenProject());
            SaveProjectCommand = new RelayCommand(() => SaveProject());
            CloseProjectCommand = new RelayCommand(() => CloseProject());
            
            // Initialize modal commands
            CloseModalCommand = new RelayCommand(() => CloseModal());
            
            // Initialize analysis commands via domain ViewModel
            AnalyzeUnanalyzedCommand = _requirementAnalysis?.AnalyzeUnanalyzedCommand ?? new RelayCommand(() => { });
            ReAnalyzeModifiedCommand = _requirementAnalysis?.ReAnalyzeModifiedCommand ?? new RelayCommand(() => { });
            ImportAdditionalCommand = new RelayCommand(() => { /* TODO: Wire to RequirementImportExportViewModel.ImportAdditional */ });
            AnalyzeRequirementsCommand = _requirementAnalysis?.AnalyzeCurrentRequirementCommand ?? new RelayCommand(() => { });
            BatchAnalyzeCommand = _requirementAnalysis?.BatchAnalyzeAllRequirementsCommand ?? new RelayCommand(() => { });
            
            // Initialize ChatGPT analysis import commands  
            ImportStructuredAnalysisCommand = new RelayCommand(() => { /* TODO: Wire to RequirementImportExportViewModel.ImportStructuredAnalysis */ });
            PasteChatGptAnalysisCommand = new RelayCommand(() => PasteChatGptAnalysis());
            GenerateLearningPromptCommand = _requirementGeneration?.GenerateLearningPromptCommand ?? new RelayCommand(() => { });
            SetupLlmWorkspaceCommand = new RelayCommand(() => SetupLlmWorkspace());
            GenerateAnalysisCommandCommand = _requirementGeneration?.GenerateAnalysisCommandCommand ?? new RelayCommand(() => { });
            GenerateTestCaseCommandCommand = _requirementGeneration?.GenerateTestCaseCommandCommand ?? new RelayCommand(() => { });

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
            
            // TODO: Initialize domain ViewModels via IViewModelFactory when DI container is fully setup
            // For now, keep manual creation but mark for refactoring
            // _requirementAnalysis = _viewModelFactory.CreateRequirementAnalysisWorkflowViewModel();
            // _chatGptExportAnalysis = _viewModelFactory.CreateChatGptExportAnalysisViewModel();
            // _workspaceManagement = _viewModelFactory.CreateWorkspaceManagementViewModel();
            
            // Temporary manual creation until DI container supports domain ViewModels
            _requirementAnalysis = new RequirementAnalysisViewModel(
                () => Requirements,
                () => CurrentRequirement,
                (message, duration) => SetTransientStatus(message, duration),
                _applicationServices.LoggerFactory?.CreateLogger<RequirementAnalysisViewModel>());
                
            _requirementGeneration = new RequirementGenerationViewModel(
                () => Requirements,
                () => CurrentRequirement,
                (message, duration) => SetTransientStatus(message, duration));
                
            // Initialize NavigationHeaderManagementViewModel
            _navigationHeaderManagement = new NavigationHeaderManagementViewModel(
                getRequirements: () => Requirements,
                getCurrentRequirement: () => CurrentRequirement,
                setCurrentRequirement: (req) => CurrentRequirement = req,
                setTransientStatus: (msg, dur) => SetTransientStatus(msg, dur),
                commitPendingEdits: CommitPendingEdits,
                getActiveHeader: () => ActiveHeader,
                setActiveHeader: (header) => ActiveHeader = header,
                getCurrentWorkspace: () => CurrentWorkspace,
                getWorkspacePath: () => WorkspacePath,
                getWrapOnNextWithoutTestCase: () => WrapOnNextWithoutTestCase,
                getIsLlmBusy: () => IsLlmBusy,
                getTestCaseGeneratorHeader: () => _testCaseGeneratorHeader,
                getTestCaseGeneratorInstance: GetTestCaseGeneratorInstance
            );
                
            // Note: This violates DI principles and should be replaced with:
            // _requirementAnalysis = _viewModelFactory.CreateRequirementAnalysisWorkflowViewModel();
            // _requirementGeneration = _viewModelFactory.CreateRequirementGenerationViewModel();

            // Initialize/ensure Import command exists before wiring header (so both menu and header share the same command)
            ImportWordCommand = ImportWordCommand ?? new AsyncRelayCommand(async () => { /* TODO: Wire to RequirementImportExportViewModel.ImportWordAsync */ await Task.CompletedTask; });
            QuickImportCommand = new AsyncRelayCommand(async () => { /* TODO: Wire to RequirementImportExportViewModel.QuickImportAsync */ await Task.CompletedTask; });
            LoadWorkspaceCommand = new RelayCommand(() => LoadWorkspace());
            SaveWorkspaceCommand = new RelayCommand(() => SaveWorkspace());
            ReloadCommand = new AsyncRelayCommand(ReloadAsync);
            ExportAllToJamaCommand = new RelayCommand(() => TryInvokeExportAllToJama());
            HelpCommand = new RelayCommand(() => TryInvokeHelp());
            ExportForChatGptCommand = new RelayCommand(() => { /* TODO: Wire to ChatGptExportAnalysisViewModel.ExportCurrentRequirementForChatGpt */ }, () => CurrentRequirement != null);
            ExportSelectedForChatGptCommand = new RelayCommand(() => { /* TODO: Wire to ChatGptExportAnalysisViewModel.ExportSelectedRequirementsForChatGpt */ });

            // Create navigator and pass child logger if available
            var requirementsIndexLogger = _applicationServices.LoggerFactory?.CreateLogger<RequirementsIndexViewModel>();
            _requirementsNavigator = new RequirementsIndexViewModel(
                Requirements,
                () => CurrentRequirement,
                r => CurrentRequirement = r,
                () => CommitPendingEdits(),
                logger: requirementsIndexLogger);

            // Wire navigation commands to NavigationHeaderManagementViewModel
            NextRequirementCommand = _navigationHeaderManagement.NextRequirementCommand;
            PreviousRequirementCommand = _navigationHeaderManagement.PreviousRequirementCommand;
            NextWithoutTestCaseCommand = _navigationHeaderManagement.NextWithoutTestCaseCommand;
            
            // Wire Re-Analyze command to workspace header
            _workspaceHeaderViewModel.ReAnalyzeCommand = new AsyncRelayCommand(ReAnalyzeRequirementAsync, CanReAnalyze);

            // Populate UI steps (factories create step VMs)
            InitializeSteps();

            // Start with initial state view instead of no content
            CurrentStepViewModel = InitialStateViewModel;

            // Ensure header wiring is consistent
            TryWireDynamicTestCaseGenerator();
            WireHeaderSubscriptions();

            // Check AnythingLLM status on startup and integrate with LlmConnectionManager
            // Only do this for runtime instances, not design-time
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(new System.Windows.DependencyObject()))
            {
                _ = Task.Run(async () => await InitializeAnythingLLMAsync());
            }

            _logger?.LogDebug("MainViewModel constructed");
        }

        /// <summary>
        /// Initializes RAG workspace for the current project workspace.
        /// Creates or finds AnythingLLM workspace and loads documents for RAG functionality.
        /// </summary>
        private async Task InitializeRagForWorkspaceAsync()
        {
            if (CurrentWorkspace == null || string.IsNullOrEmpty(WorkspacePath))
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[RAG] No workspace loaded, skipping RAG initialization");
                return;
            }

            try
            {
                // Generate workspace name based on the current workspace
                var workspaceName = Path.GetFileNameWithoutExtension(WorkspacePath) ?? "Requirements Workspace";
                
                // Update header with RAG status
                if (_workspaceHeaderViewModel != null)
                {
                    _workspaceHeaderViewModel.IsRagInitializing = true;
                    _workspaceHeaderViewModel.RagStatusMessage = "Initializing RAG workspace...";
                    _workspaceHeaderViewModel.RagWorkspaceName = workspaceName;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Initializing RAG for workspace: {workspaceName}");

                // Check if AnythingLLM service is available
                if (!await _anythingLLMService.IsServiceAvailableAsync())
                {
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.RagStatusMessage = "AnythingLLM service not available";
                        _workspaceHeaderViewModel.IsRagInitializing = false;
                    }
                    TestCaseEditorApp.Services.Logging.Log.Warn("[RAG] AnythingLLM service not available for RAG initialization");
                    return;
                }

                // Check if workspace already exists
                var existingWorkspaces = await _anythingLLMService.GetWorkspacesAsync();
                var existingWorkspace = existingWorkspaces.FirstOrDefault(w => 
                    string.Equals(w.Name, workspaceName, StringComparison.OrdinalIgnoreCase));

                if (existingWorkspace != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Found existing workspace: {existingWorkspace.Name}");
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.RagStatusMessage = "RAG workspace ready";
                        _workspaceHeaderViewModel.IsRagInitializing = false;
                    }
                }
                else
                {
                    // Create new workspace
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.RagStatusMessage = "Creating RAG workspace...";
                    }

                    var newWorkspace = await _anythingLLMService.CreateWorkspaceAsync(workspaceName);
                    if (newWorkspace != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[RAG] Created new workspace: {newWorkspace.Name}");
                        if (_workspaceHeaderViewModel != null)
                        {
                            _workspaceHeaderViewModel.RagStatusMessage = "RAG workspace created successfully";
                        }
                        
                        // Show success message in status
                        SetTransientStatus($"RAG workspace '{workspaceName}' initialized for enhanced AI analysis", 5);
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[RAG] Failed to create workspace: {workspaceName}");
                        if (_workspaceHeaderViewModel != null)
                        {
                            _workspaceHeaderViewModel.RagStatusMessage = "Failed to create RAG workspace";
                        }
                    }
                    
                    if (_workspaceHeaderViewModel != null)
                    {
                        _workspaceHeaderViewModel.IsRagInitializing = false;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[RAG] Error during RAG workspace initialization");
                if (_workspaceHeaderViewModel != null)
                {
                    _workspaceHeaderViewModel.RagStatusMessage = "RAG initialization failed";
                    _workspaceHeaderViewModel.IsRagInitializing = false;
                }
            }
        }

        /// <summary>
        /// Initializes AnythingLLM connection and updates the LlmConnectionManager with the status.
        /// This integrates with the existing LLM connection system.
        /// </summary>
        private async Task InitializeAnythingLLMAsync()
        {
            // Prevent multiple simultaneous initialization attempts across all instances
            lock (_initializationLock)
            {
                if (_anythingLLMInitializing)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[STARTUP] AnythingLLM initialization already in progress, skipping duplicate call");
                    return;
                }
                _anythingLLMInitializing = true;
            }
            
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info("[STARTUP] Checking AnythingLLM availability...");
                
                // Set initial checking status via mediator
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var status = new AnythingLLMStatus
                    {
                        IsAvailable = false,
                        IsStarting = true,
                        StatusMessage = "Checking AnythingLLM service..."
                    };
                    AnythingLLMMediator.NotifyStatusUpdated(status);
                });
                
                // Check if AnythingLLM is available
                bool isAvailable = await _anythingLLMService.IsServiceAvailableAsync();
                
                if (isAvailable)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[STARTUP] AnythingLLM is available and connected");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var status = new AnythingLLMStatus
                        {
                            IsAvailable = true,
                            IsStarting = false,
                            StatusMessage = "AnythingLLM is ready"
                        };
                        AnythingLLMMediator.NotifyStatusUpdated(status);
                    });
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[STARTUP] AnythingLLM not available, trying to start...");
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var status = new AnythingLLMStatus
                        {
                            IsAvailable = false,
                            IsStarting = true,
                            StatusMessage = "Starting AnythingLLM..."
                        };
                        AnythingLLMMediator.NotifyStatusUpdated(status);
                    });
                    
                    // Subscribe to status updates from the service
                    _anythingLLMService.StatusUpdated += OnAnythingLLMStatusUpdated;
                    
                    try
                    {
                        // Try to start AnythingLLM
                        var (success, message) = await _anythingLLMService.EnsureServiceRunningAsync();
                        
                        if (success)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info("[STARTUP] AnythingLLM started successfully");
                            
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var status = new AnythingLLMStatus
                                {
                                    IsAvailable = true,
                                    IsStarting = false,
                                    StatusMessage = "AnythingLLM is ready"
                                };
                                AnythingLLMMediator.NotifyStatusUpdated(status);
                            });
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[STARTUP] Failed to start AnythingLLM: {message}");
                            
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                var status = new AnythingLLMStatus
                                {
                                    IsAvailable = false,
                                    IsStarting = false,
                                    StatusMessage = $"Failed to start AnythingLLM: {message}"
                                };
                                AnythingLLMMediator.NotifyStatusUpdated(status);
                            });
                        }
                    }
                    finally
                    {
                        // Unsubscribe from status updates
                        _anythingLLMService.StatusUpdated -= OnAnythingLLMStatusUpdated;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[STARTUP] Error initializing AnythingLLM");
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var status = new AnythingLLMStatus
                    {
                        IsAvailable = false,
                        IsStarting = false,
                        StatusMessage = $"Error: {ex.Message}"
                    };
                    AnythingLLMMediator.NotifyStatusUpdated(status);
                });
            }
            finally
            {
                lock (_initializationLock)
                {
                    _anythingLLMInitializing = false;
                }
            }
        }
        
        /// <summary>
        /// Handles AnythingLLM status updates from mediator to keep MainViewModel properties in sync
        /// </summary>
        private void OnAnythingLLMStatusFromMediator(AnythingLLMStatus status)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                IsAnythingLLMAvailable = status.IsAvailable;
                IsAnythingLLMStarting = status.IsStarting;
                AnythingLLMStatusMessage = status.StatusMessage;
            });
        }
        
        /// <summary>
        /// Handles real-time status updates from AnythingLLM service during startup
        /// </summary>
        private void OnAnythingLLMStatusUpdated(string statusMessage)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var status = new AnythingLLMStatus
                {
                    IsAvailable = false, // Still starting if we're getting status updates
                    IsStarting = !string.IsNullOrEmpty(statusMessage) && 
                               statusMessage != "AnythingLLM — connected" && 
                               statusMessage != "AnythingLLM — disconnected",
                    StatusMessage = statusMessage
                };
                AnythingLLMMediator.NotifyStatusUpdated(status);
            });
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
                    return new ProjectViewModel();
                }
            });

            TestCaseGeneratorSteps.Add(new StepDescriptor
            {
                Id = "requirements",
                DisplayName = "Requirement",
                Badge = string.Empty,
                HasFileMenu = true,
                CreateViewModel = svc =>
                {
                    return new RequirementsViewModel(_persistence!, this, _testCaseGenerator ?? new TestCaseGenerator_CoreVM());
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
                    return new LLMLearningViewModel();
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

            // Start with no selected step to show initial state
            SelectedStep = null;
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
                    CurrentStepViewModel = InitialStateViewModel;
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
                    CurrentStepViewModel = InitialStateViewModel;
                }
            }
        }

        // Initial state view model for when no content is loaded
        private InitialStateViewModel? _initialStateViewModel;
        private InitialStateViewModel InitialStateViewModel
        {
            get
            {
                if (_initialStateViewModel == null)
                {
                    _initialStateViewModel = new InitialStateViewModel();
                }
                return _initialStateViewModel;
            }
        }

        // -----------------------------
        // Header wiring and helpers
        // -----------------------------

        // Insert this method into MainViewModel.cs (near other header helpers)
        private void OnSelectedMenuSectionChanged(string? value)
        {
            try
            {
                // When all sections are collapsed, show initial state
                if (string.IsNullOrEmpty(value))
                {
                    CurrentStepViewModel = InitialStateViewModel;
                    SelectedStep = null;
                    ActiveHeader = _workspaceHeaderViewModel;
                    return;
                }

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
                    _navigationHeaderManagement?.CreateAndAssignWorkspaceHeader();
                    return;
                }

                // Import Requirements workflow
                if (string.Equals(value, "Import", StringComparison.OrdinalIgnoreCase))
                {
                    // Don't call CreateAndAssignWorkspaceHeader() as it might trigger SelectedStep changes
                    // Instead, create a simple header for Import
                    if (_workspaceHeaderViewModel == null)
                    {
                        _workspaceHeaderViewModel = new WorkspaceHeaderViewModel();
                    }
                    _workspaceHeaderViewModel.WorkspaceName = "Import Requirements";
                    ActiveHeader = _workspaceHeaderViewModel;
                    
                    // Initialize workflow with current settings
                    ImportWorkflow.Initialize(AutoAnalyzeOnImport, AutoExportForChatGpt);
                    CurrentStepViewModel = ImportWorkflow;
                    return;
                }

                // New Project workflow - show header with title and main content with workflow
                if (string.Equals(value, "NewProject", StringComparison.OrdinalIgnoreCase))
                {
                    _navigationHeaderManagement?.CreateAndAssignNewProjectHeader();
                    
                    // Show the full workflow in the main content area
                    if (NewProjectWorkflow == null)
                    {
                        NewProjectWorkflow = new NewProjectWorkflowViewModel(_anythingLLMService, _toastService);
                        NewProjectWorkflow.ProjectCreated += OnNewProjectCreated;
                        NewProjectWorkflow.ProjectCancelled += OnNewProjectCancelled;
                    }
                    
                    NewProjectWorkflow.Initialize();
                    CurrentStepViewModel = NewProjectWorkflow;
                    return;
                }

                // Default to workspace header
                _navigationHeaderManagement?.CreateAndAssignWorkspaceHeader();
            }
            catch
            {
                // Best-effort fallback
                _navigationHeaderManagement?.CreateAndAssignWorkspaceHeader();
            }
        }

        // TODO: Extract to NavigationHeaderManagementViewModel - method moved for Round 7

        // TODO: Extract to NavigationHeaderManagementViewModel - method moved for Round 7

        private void CreateAndAssignTestCaseGeneratorHeader()
        {
            if (_testCaseGeneratorHeader == null)
                _testCaseGeneratorHeader = new TestCaseGenerator_HeaderVM(this);

            // Create shared ViewModels for menu access
            if (_questionsViewModel == null)
            {
                var llm = TestCaseEditorApp.Services.LlmFactory.Create();
                _questionsViewModel = new TestCaseGenerator_QuestionsVM(_persistence!, llm, _testCaseGeneratorHeader, this);
            }

            if (_assumptionsViewModel == null)
            {
                _assumptionsViewModel = new TestCaseGenerator_AssumptionsVM(_testCaseGeneratorHeader, this);
            }

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

        // ----------------- helpers that avoid compile-time coupling -----------------
        public object? GetTestCaseGeneratorInstance()
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

        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

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

        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6
        
        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

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
                _notificationService.ShowError($"Failed to save workspace: {ex.Message}", 8);
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

            LoadWorkspaceFromPath(ofd.FileName);
        }

        public void LoadWorkspaceFromPath(string filePath)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Starting to load workspace from: {filePath}");
            
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Invalid file path or file doesn't exist: {filePath}");
                SetTransientStatus("Invalid workspace file path.", blockingError: true);
                return;
            }

            WorkspacePath = filePath;
            TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Set WorkspacePath to: {WorkspacePath}");
            
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Attempting to load workspace file...");
                var ws = TestCaseEditorApp.Services.WorkspaceFileManager.Load(WorkspacePath!);
                if (ws == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[LoadWorkspace] Workspace file loaded but returned null");
                    SetTransientStatus("Failed to load workspace (file empty or invalid).", blockingError: true);
                    return;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Successfully loaded workspace. Requirements count: {ws.Requirements?.Count ?? 0}");
                CurrentWorkspace = ws;
                
                // Track in recent files
                try { _recentFilesService?.AddRecentFile(WorkspacePath!); } catch { }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Clearing existing requirements and loading new ones...");
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
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[LoadWorkspace] Workspace loading completed successfully. Current requirement: {CurrentRequirement?.GlobalId}");
                SetTransientStatus($"Opened workspace: {Path.GetFileName(WorkspacePath)} - {Requirements.Count} requirements", 4);
                HasUnsavedChanges = false;
                IsDirty = false;
                
                // Initialize RAG workspace for enhanced AI analysis
                _ = Task.Run(async () => await InitializeRagForWorkspaceAsync());
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[LoadWorkspace] Exception occurred while loading workspace: {ex.Message}");
                MessageBox.Show($"Failed to load workspace: {ex.Message}", "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Command to initialize RAG workspace manually
        /// </summary>
        public ICommand InitializeRagCommand => new RelayCommand(async () => await InitializeRagForWorkspaceAsync());

        // Navigation methods moved to NavigationHeaderManagementViewModel
        
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
        
        // TODO: Extract to NavigationHeaderManagementViewModel - method moved for Round 7

        // TODO: Extract to NavigationHeaderManagementViewModel - method moved for Round 7

        // TODO: Extract to NavigationHeaderManagementViewModel - method moved for Round 7

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
                // If WorkspacePath is already set (e.g., from new project workflow), skip the dialog
                if (string.IsNullOrWhiteSpace(WorkspacePath))
                {
                    var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestCaseEditorApp", "Workspaces");
                    Directory.CreateDirectory(defaultFolder);

                    var suggested = FileNameHelper.GenerateUniqueFileName(Path.GetFileNameWithoutExtension(path), ".tcex.json");

                    // Inform user about the next step
                    var result = MessageBox.Show(
                        $"Great! Your document '{Path.GetFileName(path)}' is ready to import.\n\n" +
                        "Next, choose where to save your new project workspace. This will create a project file (.tcex.json) that contains your imported requirements and any test cases you generate.\n\n" +
                        "Would you like to proceed?",
                        "Create New Project Workspace",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result != MessageBoxResult.Yes)
                    {
                        SetTransientStatus("Import canceled by user.", 2);
                        _logger?.LogInformation("Import canceled by user at workspace creation step.");
                        return;
                    }

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
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[Import] Using existing WorkspacePath: '{WorkspacePath}' (from new project workflow)");
                }

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
                    _ = Task.Run(() => { /* TODO: Wire to ChatGptExportAnalysisViewModel.BatchExportRequirementsForChatGpt(reqs) */ });
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
            => _fileDialog.ShowSaveFile(title: "Save New Project Workspace", suggestedFileName: suggestedFileName, filter: "Test Case Editor Session|*.tcex.json|JSON|*.json|All Files|*.*", defaultExt: ".tcex.json", initialDirectory: initialDirectory) ?? string.Empty;

        public void SetTransientStatus(string message, int seconds = 3, bool blockingError = false)
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
                            var currentAvg = avgAnalysisTime ?? TimeSpan.Zero;
                            avgAnalysisTime = TimeSpan.FromTicks((currentAvg.Ticks + analysisDuration.Ticks) / 2);
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
        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

        /// <summary>
        /// Creates a new project with comprehensive workflow in main GUI.
        /// </summary>
        private void CreateNewProject()
        {
            _projectManagement?.CreateNewProject();
        }

        /// <summary>
        /// Opens an existing project by selecting from available AnythingLLM workspaces.
        /// </summary>
        private void OpenProject()
        {
            _projectManagement?.OpenProject();
        }

        // TODO: Extract to RequirementAnalysisViewModel - method moved for Round 3 testing

        // TODO: Extract to RequirementAnalysisViewModel - method moved for domain completion

        // TODO: Extract to RequirementAnalysisViewModel - method moved for domain completion

        // TODO: Extract to RequirementAnalysisViewModel - method moved for domain completion

        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

        // TODO: Extract to RequirementImportExportViewModel - method moved for Round 6

        // TODO: Extract to RequirementGenerationViewModel - method moved for Round 5

        // TODO: Extract to RequirementGenerationViewModel - method moved for Round 5

        // TODO: Extract to RequirementGenerationViewModel - method moved for Round 5

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
        /// Saves the current project state.
        /// </summary>
        private void SaveProject()
        {
            _projectManagement?.SaveProject();
        }

        /// <summary>
        /// Closes the current project.
        /// </summary>
        private void CloseProject()
        {
            _projectManagement?.CloseProject();
        }
        
        public void RefreshCommandStates()
        {
            try
            {
                // Refresh command states that depend on project state
                ((RelayCommand?)ExportForChatGptCommand)?.NotifyCanExecuteChanged();
                ((RelayCommand?)GenerateAnalysisCommandCommand)?.NotifyCanExecuteChanged();
                ((RelayCommand?)GenerateTestCaseCommandCommand)?.NotifyCanExecuteChanged();
                ((RelayCommand?)AnalyzeRequirementsCommand)?.NotifyCanExecuteChanged();
                ((RelayCommand?)BatchAnalyzeCommand)?.NotifyCanExecuteChanged();
                ((RelayCommand?)SaveWorkspaceCommand)?.NotifyCanExecuteChanged();
                ((RelayCommand?)CloseProjectCommand)?.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[PROJECT] Error refreshing command states during close");
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
            public string? ShowOpenFile(string title, string filter, string? initialDirectory = null) => null;
            public string? ShowFolderDialog(string title, string? initialDirectory = null) => null;
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
                AnythingLLMMediator.StatusUpdated -= OnAnythingLLMStatusFromMediator;
            }
            catch { }

            try
            {
                _llmProbeService?.Stop();
                _llmProbeService?.Dispose();
            }
            catch { }
            _llmProbeService = null;
        }

        // -------------------------
        // Modal overlay system
        // -------------------------
        
        /// <summary>
        /// Show a modal dialog with the specified content and title
        /// </summary>
        public void ShowModal(object viewModel, string title = "Modal Dialog")
        {
            ModalTitle = title;
            ModalViewModel = viewModel;
        }

        /// <summary>
        /// Close the current modal dialog
        /// </summary>
        public void CloseModal()
        {
            ModalViewModel = null;
            ModalTitle = "Modal Dialog";
        }

        /// <summary>
        /// Check if a modal dialog is currently shown
        /// </summary>
        public bool IsModalOpen => ModalViewModel != null;

        /// <summary>
        /// Show the API key configuration modal
        /// </summary>
        public void ShowApiKeyConfigModal()
        {
            var viewModel = new ApiKeyConfigViewModel(_notificationService);
            viewModel.ApiKeyConfigured += OnApiKeyConfigured;
            viewModel.Cancelled += OnApiKeyConfigCancelled;
            ShowModal(viewModel, "Configure AnythingLLM API Key");
        }

        /// <summary>
        /// Show the workspace selection modal for creating a new project
        /// </summary>
        public void ShowWorkspaceSelectionModal()
        {
            var viewModel = new WorkspaceSelectionViewModel(_anythingLLMService, _notificationService, WorkspaceSelectionViewModel.SelectionMode.CreateNew);
            viewModel.WorkspaceSelected += OnWorkspaceSelected;
            viewModel.Cancelled += OnWorkspaceSelectionCancelled;
            ShowModal(viewModel, "Create New Project");
        }

        /// <summary>
        /// Show the workspace selection modal for opening an existing project
        /// </summary>
        public void ShowWorkspaceSelectionModalForOpen()
        {
            var viewModel = new WorkspaceSelectionViewModel(_anythingLLMService, _notificationService, WorkspaceSelectionViewModel.SelectionMode.SelectExisting);
            viewModel.WorkspaceSelected += OnWorkspaceSelected;
            viewModel.Cancelled += OnWorkspaceSelectionCancelled;
            ShowModal(viewModel, "Open Existing Project");
        }

        /// <summary>
        /// Show the import workflow modal for step-by-step import configuration
        /// </summary>
        public void ShowImportWorkflow()
        {
            var viewModel = new ImportWorkflowViewModel();
            viewModel.ImportWorkflowCompleted += OnImportWorkflowCompleted;
            viewModel.ImportWorkflowCancelled += OnImportWorkflowCancelled;
            viewModel.Show();
            ShowModal(viewModel, "Import Requirements Document");
        }

        private void OnImportWorkflowCompleted(object? sender, ImportWorkflowCompletedEventArgs e)
        {
            CloseModal();
            
            // Configure settings based on user choices
            AutoAnalyzeOnImport = e.AutoAnalyzeRequirements;
            AutoExportForChatGpt = e.ExportForChatGpt;
            
            // TODO: Implement the actual import with the configured settings
            // This should use the document path and workspace save path from the args
            SetTransientStatus($"Import workflow completed. Document: {e.DocumentPath}, Workspace: {e.WorkspaceName}", 3);
        }

        private void OnImportWorkflowCancelled(object? sender, EventArgs e)
        {
            CloseModal();
            SetTransientStatus("Import workflow cancelled.", 2);
        }

        private async void OnImportRequirementsWorkflowCompleted(object? sender, RequirementsImportCompletedEventArgs e)
        {
            try
            {
                // Configure settings based on user choices
                AutoAnalyzeOnImport = e.AutoAnalyzeEnabled;
                AutoExportForChatGpt = e.AutoExportEnabled;

                // Set workspace path for saving later
                WorkspacePath = e.WorkspaceSavePath;

                // Import the requirements from the document
                if (e.DocumentPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    await ImportFromPathAsync(e.DocumentPath, replace: true);
                }
                else
                {
                    SetTransientStatus("Unsupported file format. Please select a .docx file.", blockingError: true);
                    return;
                }

                // Navigate back to Requirements view after import
                SelectedMenuSection = "TestCase";
                
                SetTransientStatus($"Successfully imported requirements from {Path.GetFileName(e.DocumentPath)}. Workspace saved as {Path.GetFileName(e.WorkspaceSavePath)}.", 5);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to complete import workflow");
                SetTransientStatus($"Failed to import requirements: {ex.Message}", blockingError: true);
            }
        }

        private void OnImportRequirementsWorkflowCancelled(object? sender, EventArgs e)
        {
            // Navigate back to previous section (typically TestCase)
            SelectedMenuSection = "TestCase";
            SetTransientStatus("Import workflow cancelled.", 2);
        }
        public void ShowRequirementDescriptionEditorModal(Requirement requirement)
        {
            if (requirement == null) return;
            
            var viewModel = new RequirementDescriptionEditorViewModel(requirement, _notificationService);
            viewModel.RequirementEdited += OnRequirementEdited;
            viewModel.AnalysisRequested += OnRequirementAnalysisRequested;
            viewModel.Cancelled += OnRequirementEditCancelled;
            ShowModal(viewModel, "Edit Requirement Description");
        }

        /// <summary>
        /// Implementation of ITestCaseGenerator_Navigator.ShowRequirementEditor
        /// </summary>
        public void ShowRequirementEditor(Requirement requirement)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[MainViewModel] ShowRequirementEditor called for {requirement?.Item}");
                
                if (requirement == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn("[MainViewModel] ShowRequirementEditor called with null requirement");
                    return;
                }
                
                ShowRequirementDescriptionEditorModal(requirement);
                TestCaseEditorApp.Services.Logging.Log.Debug("[MainViewModel] ShowRequirementDescriptionEditorModal completed");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[MainViewModel] Error in ShowRequirementEditor");
                _notificationService?.ShowError($"Failed to open requirement editor: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the split text editor modal
        /// </summary>
        public void ShowSplitTextEditorModal(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            var viewModel = new SplitTextEditorViewModel(text, _notificationService);
            viewModel.SplitCompleted += OnTextSplitCompleted;
            viewModel.Cancelled += OnTextSplitCancelled;
            ShowModal(viewModel, "Split Text");
        }

        /// <summary>
        /// Show the import method selection modal
        /// </summary>
        public Task<ImportMethod> ShowImportMethodSelectionModalAsync()
        {
            var tcs = new TaskCompletionSource<ImportMethod>();
            
            _importMethodViewModel = new ImportMethodSelectionViewModel();
            _importMethodViewModel.ImportMethodSelected += (sender, e) =>
            {
                CloseModal();
                
                // Handle ImportWorkflow by navigating to Import section
                if (e.Method == ImportMethod.ImportWorkflow)
                {
                    SelectedMenuSection = "Import";
                    tcs.SetResult(ImportMethod.ImportWorkflow);
                }
                else
                {
                    tcs.SetResult(e.Method);
                }
            };
            
            ShowModal(_importMethodViewModel, "Import Method");
            return tcs.Task;
        }

        private void OnApiKeyConfigured(object? sender, ApiKeyConfiguredEventArgs e)
        {
            CloseModal();
            _notificationService.ShowSuccess($"API key configured successfully");
        }

        private void OnApiKeyConfigCancelled(object? sender, EventArgs e)
        {
            CloseModal();
        }

        private async void OnWorkspaceSelected(object? sender, WorkspaceSelectedEventArgs e)
        {
            CloseModal();
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[OnWorkspaceSelected] Workspace selected: {e.WorkspaceName}, WasCreated: {e.WasCreated}");
            
            try
            {
                // Find the actual workspace object from the AnythingLLM service
                TestCaseEditorApp.Services.Logging.Log.Info($"[OnWorkspaceSelected] Getting workspaces from AnythingLLM service...");
                var workspaces = await _anythingLLMService.GetWorkspacesAsync().ConfigureAwait(false);
                var workspace = workspaces?.FirstOrDefault(w => w.Name.Equals(e.WorkspaceName, StringComparison.OrdinalIgnoreCase));
                
                if (workspace == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[OnWorkspaceSelected] Could not find workspace: {e.WorkspaceName}");
                    // Use Dispatcher to show error on UI thread
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        _notificationService.ShowError($"Could not find workspace: {e.WorkspaceName}");
                    });
                    return;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[OnWorkspaceSelected] Found workspace: {workspace.Name} (Slug: {workspace.Slug}), HasLocalFile: {workspace.HasLocalFile}, LocalFilePath: {workspace.LocalFilePath}");
                
                // Use Dispatcher for UI operations
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Set workspace slug but don't clear requirements yet - LoadWorkspaceFromPath will handle that
                    CurrentAnythingLLMWorkspaceSlug = workspace.Slug;
                    
                    // Don't change the selected step - keep current selection (e.g., Project menu)
                });
                
                if (e.WasCreated)
                {
                    // New workspace was created - show import options using custom modal
                    var importMethod = await ShowImportMethodSelectionModalAsync();
                        
                    // Execute commands on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        switch (importMethod)
                        {
                            case ImportMethod.Word: // Word with analysis
                                AutoAnalyzeOnImport = true;
                                ImportWordCommand?.Execute(null);
                                break;
                            case ImportMethod.WordNoAnalysis: // Word without analysis
                                AutoAnalyzeOnImport = false;
                                ImportWordCommand?.Execute(null);
                                break;
                            case ImportMethod.ImportWorkflow: // Full workflow
                                CloseModal(); // Close the import method selection first
                                ShowImportWorkflow();
                                break;
                            case ImportMethod.Quick: // Quick
                                QuickImportCommand?.Execute(null);
                                break;
                            case ImportMethod.Skip: // Skip or cancelled
                                break;
                        }
                        
                        SetTransientStatus($"🆕 Created and opened project: {workspace.Name}", 4);
                    });
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Created new project with workspace slug '{workspace.Slug}'");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[OnWorkspaceSelected] Existing workspace selected");
                    // Existing workspace was selected - run UI operations on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SetTransientStatus($"📂 Opened existing project: {workspace.Name}", 4);
                    });
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Opened existing project with workspace slug '{workspace.Slug}'");
                    
                    // Automatically load local workspace file if it exists
                    if (workspace.HasLocalFile && !string.IsNullOrEmpty(workspace.LocalFilePath))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[OnWorkspaceSelected] Loading local workspace file: {workspace.LocalFilePath}");
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            LoadWorkspaceFromPath(workspace.LocalFilePath);
                        });
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[OnWorkspaceSelected] No local file found for workspace. HasLocalFile: {workspace.HasLocalFile}, LocalFilePath: '{workspace.LocalFilePath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[PROJECT] Error handling workspace selection");
                
                // Show error on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _notificationService.ShowError($"Error opening project: {ex.Message}");
                    SetTransientStatus("❌ Failed to open project", 3);
                });
            }
        }

        private void OnWorkspaceSelectionCancelled(object? sender, EventArgs e)
        {
            CloseModal();
        }

        private void OnRequirementEdited(object? sender, RequirementEditedEventArgs e)
        {
            CloseModal();
            _notificationService.ShowSuccess("Requirement updated successfully");
            
            // Refresh any dependent views if needed
            // The requirement is already updated since it's passed by reference
        }

        private void OnRequirementAnalysisRequested(object? sender, RequirementAnalysisRequestedEventArgs e)
        {
            // Close the editor modal first
            CloseModal();
            
            // Request analysis from the analysis service
            // This would typically be handled by the TestCaseGenerator_AnalysisVM
            _notificationService.ShowInfo("Analysis requested - please use the main analysis panel");
        }

        private void OnRequirementEditCancelled(object? sender, EventArgs e)
        {
            CloseModal();
        }

        private void OnTextSplitCompleted(object? sender, TextSplitCompletedEventArgs e)
        {
            CloseModal();
            
            if (e.SplitResults?.Count > 0)
            {
                _notificationService.ShowSuccess($"Text split into {e.SplitResults.Count} parts");
                
                // The split results can be handled by the calling code
                // For now, just show success - integration with specific features
                // would be done by the components that show the modal
            }
        }

        private void OnTextSplitCancelled(object? sender, EventArgs e)
        {
            CloseModal();
        }

        private async void OnNewProjectCreated(object? sender, NewProjectCompletedEventArgs e)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Starting project creation. WorkspaceName: {e.WorkspaceName}, DocumentPath: {e.DocumentPath}");
                
                // Configure settings based on user choices
                AutoExportForChatGpt = e.AutoExportEnabled;
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Settings configured - AutoExport: {AutoExportForChatGpt}");

                // Set workspace path BEFORE importing to avoid dialog
                WorkspacePath = e.ProjectSavePath;
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Workspace path set to: {WorkspacePath}");

                // Import requirements from the selected document
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Importing requirements from: {e.DocumentPath}");
                await ImportFromPathAsync(e.DocumentPath, replace: true);

                // Save project using the existing path (no dialog)
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Saving project to: {WorkspacePath}");
                SaveWorkspace(); // Use SaveWorkspace() instead of SaveWorkspaceAsync() to avoid dialog

                // Switch to main workspace view
                SelectedMenuSection = "Requirements";
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[PROJECT] Project creation completed successfully");
                _notificationService.ShowSuccess($"Project '{e.ProjectName}' created successfully with {Requirements?.Count ?? 0} requirements!");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[PROJECT] Error completing new project creation");
                _notificationService.ShowError($"Error creating project: {ex.Message}");
            }
        }

        private void OnNewProjectCancelled(object? sender, EventArgs e)
        {
            _projectManagement?.OnNewProjectCancelled(sender, e);
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