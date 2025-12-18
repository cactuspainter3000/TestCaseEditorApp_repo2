using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Dedicated ViewModel for test case generation and AI/LLM integration.
    /// Handles all test case generation workflows and AnythingLLM integration.
    /// </summary>
    public partial class TestCaseGeneratorViewModel : ObservableObject
    {
        // Service dependencies
        private readonly AnythingLLMService _anythingLLMService;
        private readonly ChatGptExportService _chatGptExportService;
        private readonly NotificationService _notificationService;
        private readonly INavigationMediator _navigationMediator;
        private readonly ILogger<TestCaseGeneratorViewModel>? _logger;
        
        // Core test case generation components
        private TestCaseGenerator_CoreVM? _testCaseGenerator;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private TestCaseGenerator_QuestionsVM? _questionsViewModel;
        private TestCaseGenerator_AssumptionsVM? _assumptionsViewModel;

        // Shared requirements collection
        public ObservableCollection<Requirement> Requirements { get; }

        // AnythingLLM Status Properties
        [ObservableProperty]
        private bool _isAnythingLLMAvailable;
        
        [ObservableProperty]
        private bool _isAnythingLLMStarting;
        
        [ObservableProperty]
        private string _anythingLLMStatusMessage = "Initializing AnythingLLM...";
        
        [ObservableProperty]
        private string? _currentAnythingLLMWorkspaceSlug;

        // LLM Busy Status
        [ObservableProperty]
        private bool _isLlmBusy;

        // UI Properties
        [ObservableProperty]
        private string title = "Test Case Generation";
        
        [ObservableProperty]
        private string description = "Generate test cases using AI-powered analysis and validation.";
        
        [ObservableProperty]
        private Requirement? _currentRequirement;

        // ViewModels for test case generation workflow
        internal TestCaseGenerator_CoreVM? TestCaseGenerator => _testCaseGenerator;
        public TestCaseGenerator_HeaderVM? TestCaseGeneratorHeader => _testCaseGeneratorHeader;
        public TestCaseGenerator_QuestionsVM? QuestionsViewModel => _questionsViewModel;
        public TestCaseGenerator_AssumptionsVM? AssumptionsViewModel => _assumptionsViewModel;

        // Commands
        public ICommand GenerateTestCaseCommand { get; }
        public ICommand GenerateAnalysisCommand { get; }
        public ICommand GenerateLearningPromptCommand { get; }
        public ICommand SetupLlmWorkspaceCommand { get; }
        public ICommand ConnectAnythingLLMCommand { get; }
        public ICommand CreateAnythingLLMWorkspaceCommand { get; }

        /// <summary>
        /// Main constructor with dependency injection
        /// </summary>
        public TestCaseGeneratorViewModel(
            AnythingLLMService anythingLLMService,
            ChatGptExportService chatGptExportService,
            NotificationService notificationService,
            INavigationMediator navigationMediator,
            ObservableCollection<Requirement> requirements,
            ILogger<TestCaseGeneratorViewModel>? logger = null)
        {
            // Store dependencies
            _anythingLLMService = anythingLLMService ?? throw new ArgumentNullException(nameof(anythingLLMService));
            _chatGptExportService = chatGptExportService ?? throw new ArgumentNullException(nameof(chatGptExportService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            _logger = logger;

            TestCaseEditorApp.Services.Logging.Log.Info("[TestCaseGeneratorViewModel] Constructor called with full dependencies");

            // Initialize test case generation components
            InitializeTestCaseGenerationComponents();

            // Initialize commands
            GenerateTestCaseCommand = new RelayCommand(ExecuteGenerateTestCaseCommand, () => CurrentRequirement != null && !IsLlmBusy);
            GenerateAnalysisCommand = new RelayCommand(ExecuteGenerateAnalysisCommand, () => CurrentRequirement != null);
            GenerateLearningPromptCommand = new RelayCommand(ExecuteGenerateLearningPrompt, () => CurrentRequirement != null);
            SetupLlmWorkspaceCommand = new AsyncRelayCommand(SetupLlmWorkspaceAsync);
            ConnectAnythingLLMCommand = new AsyncRelayCommand(ConnectAnythingLLMAsync);
            CreateAnythingLLMWorkspaceCommand = new AsyncRelayCommand(CreateAnythingLLMWorkspaceAsync);

            // Monitor property changes
            PropertyChanged += OnPropertyChanged;
            
            // Subscribe to AnythingLLM status updates
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Request current status
            Task.Run(() => AnythingLLMMediator.RequestCurrentStatus());
        }

        /// <summary>
        /// Legacy constructor for compatibility (minimal functionality)
        /// </summary>
        public TestCaseGeneratorViewModel() : this(
            new StubAnythingLLMService(),
            new ChatGptExportService(), // Use real service since it doesn't need dependencies
            new StubNotificationService(),
            new StubNavigationMediator(),
            new ObservableCollection<Requirement>(),
            null)
        {
            TestCaseEditorApp.Services.Logging.Log.Info("[TestCaseGeneratorViewModel] Legacy constructor called - limited functionality");
        }

        private void InitializeTestCaseGenerationComponents()
        {
            try
            {
                // Initialize core test case generator
                _testCaseGenerator = new TestCaseGenerator_CoreVM();
                
                // Initialize header view model (will be created via factory in real usage)
                // _testCaseGeneratorHeader = will be set via injection
                
                // Initialize workflow view models - provide minimal dependencies for stub constructor compatibility
                // These will be properly initialized when used in the main application
                // For now, skip initialization to avoid dependency issues in legacy constructor
                
                TestCaseEditorApp.Services.Logging.Log.Info("[TestCaseGeneratorViewModel] Test case generation components initialized");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseGeneratorViewModel] Failed to initialize test case generation components");
                _notificationService.ShowError($"Failed to initialize test case generator: {ex.Message}", 8);
            }
        }

        private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CurrentRequirement):
                case nameof(IsLlmBusy):
                    ((RelayCommand)GenerateTestCaseCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)GenerateAnalysisCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)GenerateLearningPromptCommand).NotifyCanExecuteChanged();
                    break;
            }
        }

        private void OnAnythingLLMStatusUpdated(AnythingLLMStatus status)
        {
            try
            {
                IsAnythingLLMAvailable = status.IsAvailable;
                IsAnythingLLMStarting = status.IsStarting;
                AnythingLLMStatusMessage = status.StatusMessage;
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGeneratorViewModel] AnythingLLM status updated - Available: {status.IsAvailable}, Starting: {status.IsStarting}, Message: {status.StatusMessage}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseGeneratorViewModel] Error handling AnythingLLM status update");
            }
        }

        /// <summary>
        /// Generate test case command for the current requirement
        /// </summary>
        private void ExecuteGenerateTestCaseCommand()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    _notificationService.ShowWarning("⚠️ No requirement selected", 3);
                    return;
                }

                var command = $"GENERATE TEST CASES: {CurrentRequirement.Item} - {CurrentRequirement.Name}\n\n{CurrentRequirement.Description}";
                System.Windows.Clipboard.SetText(command);
                
                _notificationService.ShowSuccess($"📋 Test case command copied for {CurrentRequirement.Item}", 4);
                TestCaseEditorApp.Services.Logging.Log.Info($"[TestCaseGenerator] Generated test case command for {CurrentRequirement.Item}");
                
                // Publish event for other ViewModels
                _navigationMediator.Publish(new TestCaseGeneratorEvents.TestCaseCommandGenerated(CurrentRequirement, command));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[TestCaseGenerator] Failed to generate test case command: {ex.Message}");
                _notificationService.ShowError("❌ Failed to generate test case command", 4);
            }
        }

        /// <summary>
        /// Generate analysis command for the current requirement
        /// </summary>
        private void ExecuteGenerateAnalysisCommand()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    _notificationService.ShowWarning("⚠️ No requirement selected", 3);
                    return;
                }

                var command = $"ANALYZE REQUIREMENT: {CurrentRequirement.Item} - {CurrentRequirement.Name}\n\nProvide detailed analysis including:\n- Test scenarios\n- Edge cases\n- Dependencies\n- Risk areas\n\nRequirement:\n{CurrentRequirement.Description}";
                System.Windows.Clipboard.SetText(command);
                
                _notificationService.ShowSuccess($"📋 Analysis command copied for {CurrentRequirement.Item}", 4);
                TestCaseEditorApp.Services.Logging.Log.Info($"[TestCaseGenerator] Generated analysis command for {CurrentRequirement.Item}");
                
                // Publish event for other ViewModels
                _navigationMediator.Publish(new TestCaseGeneratorEvents.AnalysisCommandGenerated(CurrentRequirement, command));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[TestCaseGenerator] Failed to generate analysis command: {ex.Message}");
                _notificationService.ShowError("❌ Failed to generate analysis command", 4);
            }
        }

        /// <summary>
        /// Generate learning prompt for the current requirement
        /// </summary>
        private void ExecuteGenerateLearningPrompt()
        {
            try
            {
                if (CurrentRequirement == null)
                {
                    _notificationService.ShowWarning("⚠️ No requirement selected", 3);
                    return;
                }

                var learningPrompt = $"LEARNING PROMPT: {CurrentRequirement.Item}\n\nHelp me understand:\n1. Key concepts in this requirement\n2. Common testing approaches\n3. Typical challenges and pitfalls\n4. Best practices for validation\n\nRequirement Details:\n{CurrentRequirement.Name}\n{CurrentRequirement.Description}";
                System.Windows.Clipboard.SetText(learningPrompt);
                
                _notificationService.ShowSuccess($"📋 Learning prompt copied for {CurrentRequirement.Item}", 4);
                TestCaseEditorApp.Services.Logging.Log.Info($"[TestCaseGenerator] Generated learning prompt for {CurrentRequirement.Item}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[TestCaseGenerator] Failed to generate learning prompt: {ex.Message}");
                _notificationService.ShowError("❌ Failed to generate learning prompt", 4);
            }
        }

        /// <summary>
        /// Set up LLM workspace with standardized templates
        /// </summary>
        private async Task SetupLlmWorkspaceAsync()
        {
            try
            {
                _notificationService.ShowInfo("Setting up LLM workspace...");
                IsLlmBusy = true;

                // Generate comprehensive workspace setup instructions
                var setupInstructions = GenerateWorkspaceSetupInstructions();
                System.Windows.Clipboard.SetText(setupInstructions);

                _notificationService.ShowSuccess("📋 LLM workspace setup instructions copied to clipboard!", 6);
                TestCaseEditorApp.Services.Logging.Log.Info("[TestCaseGenerator] LLM workspace setup instructions generated");
                
                // Publish event
                _navigationMediator.Publish(new TestCaseGeneratorEvents.WorkspaceSetupGenerated(setupInstructions));
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseGenerator] Failed to setup LLM workspace");
                _notificationService.ShowError("❌ Failed to setup LLM workspace", 5);
            }
            finally
            {
                IsLlmBusy = false;
            }
        }

        /// <summary>
        /// Connect to AnythingLLM service
        /// </summary>
        private async Task ConnectAnythingLLMAsync()
        {
            try
            {
                _notificationService.ShowInfo("Connecting to AnythingLLM...");
                IsLlmBusy = true;

                var (success, message) = await _anythingLLMService.TestConnectivityAsync();
                
                if (success)
                {
                    _notificationService.ShowSuccess($"✅ Connected to AnythingLLM: {message}", 5);
                }
                else
                {
                    _notificationService.ShowError($"❌ AnythingLLM connection failed: {message}", 8);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseGenerator] Failed to connect to AnythingLLM");
                _notificationService.ShowError("❌ Failed to connect to AnythingLLM", 5);
            }
            finally
            {
                IsLlmBusy = false;
            }
        }

        /// <summary>
        /// Create new AnythingLLM workspace
        /// </summary>
        private async Task CreateAnythingLLMWorkspaceAsync()
        {
            try
            {
                _notificationService.ShowInfo("Creating AnythingLLM workspace...");
                IsLlmBusy = true;

                // Generate workspace name based on current project or timestamp
                var workspaceName = $"TestCaseEditor_{DateTime.Now:yyyyMMdd_HHmmss}";
                
                var workspace = await _anythingLLMService.CreateWorkspaceAsync(workspaceName);
                
                if (workspace != null)
                {
                    CurrentAnythingLLMWorkspaceSlug = workspace.Slug;
                    _notificationService.ShowSuccess($"✅ Created AnythingLLM workspace: {workspace.Name}", 6);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[TestCaseGenerator] Created AnythingLLM workspace: {workspace.Name} ({workspace.Slug})");
                }
                else
                {
                    _notificationService.ShowError("❌ Failed to create AnythingLLM workspace", 5);
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[TestCaseGenerator] Failed to create AnythingLLM workspace");
                _notificationService.ShowError("❌ Failed to create AnythingLLM workspace", 5);
            }
            finally
            {
                IsLlmBusy = false;
            }
        }

        private string GenerateWorkspaceSetupInstructions()
        {
            var setupBuilder = new System.Text.StringBuilder();
            setupBuilder.AppendLine("LLM WORKSPACE SETUP - REQUIREMENT ANALYSIS & TEST CASE GENERATION");
            setupBuilder.AppendLine("=".PadRight(80, '='));
            setupBuilder.AppendLine();
            setupBuilder.AppendLine("This document sets up standardized communication between the Test Case Editor App and your LLM workspace.");
            setupBuilder.AppendLine("Please save this as your workspace context for consistent, automated responses.");
            setupBuilder.AppendLine();
            setupBuilder.AppendLine("ROLE & RESPONSIBILITIES:");
            setupBuilder.AppendLine("You are a Test Case Generation Assistant specialized in:");
            setupBuilder.AppendLine("• Analyzing software requirements for testability");
            setupBuilder.AppendLine("• Generating comprehensive test cases and scenarios");
            setupBuilder.AppendLine("• Identifying edge cases and boundary conditions");
            setupBuilder.AppendLine("• Providing structured, actionable testing guidance");
            setupBuilder.AppendLine();
            setupBuilder.AppendLine("RESPONSE FORMATS:");
            setupBuilder.AppendLine("When analyzing requirements, always include:");
            setupBuilder.AppendLine("1. SUMMARY: Brief overview of the requirement");
            setupBuilder.AppendLine("2. TEST SCENARIOS: Core functionality tests");
            setupBuilder.AppendLine("3. EDGE CASES: Boundary and exceptional conditions");
            setupBuilder.AppendLine("4. DEPENDENCIES: Prerequisites and integration points");
            setupBuilder.AppendLine("5. RISK ASSESSMENT: Potential failure modes");
            setupBuilder.AppendLine();
            setupBuilder.AppendLine("COMMAND PREFIXES:");
            setupBuilder.AppendLine("• ANALYZE REQUIREMENT: Provide detailed analysis");
            setupBuilder.AppendLine("• GENERATE TEST CASES: Create specific test cases");
            setupBuilder.AppendLine("• LEARNING PROMPT: Explain concepts and best practices");
            setupBuilder.AppendLine();
            setupBuilder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            
            return setupBuilder.ToString();
        }

        /// <summary>
        /// Set the test case generator header (called from factory)
        /// </summary>
        public void SetTestCaseGeneratorHeader(TestCaseGenerator_HeaderVM header)
        {
            _testCaseGeneratorHeader = header;
            OnPropertyChanged(nameof(TestCaseGeneratorHeader));
        }

        #region Stub Services (for legacy constructor compatibility)

        private class StubAnythingLLMService : AnythingLLMService
        {
            public StubAnythingLLMService() : base("", "") { }
            
            // Use new method hiding instead of override for non-virtual methods
            public new async Task<(bool Success, string Message)> TestConnectivityAsync() => (false, "Stub service");
            public new async Task<AnythingLLMService.Workspace?> CreateWorkspaceAsync(string name, System.Threading.CancellationToken cancellationToken = default) => null;
        }

        private class StubNotificationService : NotificationService
        {
            private static ToastNotificationService CreateStubToastService()
            {
                try
                {
                    return new ToastNotificationService(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                }
                catch
                {
                    return null!;
                }
            }

            public StubNotificationService() : base(CreateStubToastService()!)
            {
            }

            public new void ShowSuccess(string message, int durationSeconds = 4) { }
            public new void ShowError(string message, int durationSeconds = 8) { }
            public new void ShowWarning(string message, int durationSeconds = 6) { }
            public new void ShowInfo(string message, int durationSeconds = 4) { }
        }

        private class StubNavigationMediator : INavigationMediator
        {
            public string? CurrentSection => null;
            public object? CurrentHeader => null;
            public object? CurrentContent => null;
            public void NavigateToSection(string sectionName, object? context = null) { }
            public void NavigateToStep(string stepId, object? context = null) { }
            public void SetActiveHeader(object? headerViewModel) { }
            public void SetMainContent(object? contentViewModel) { }
            public void Subscribe<T>(Action<T> handler) where T : class { }
            public void Unsubscribe<T>(Action<T> handler) where T : class { }
            public void Publish<T>(T navigationEvent) where T : class { }
        }

        #endregion
    }

    /// <summary>
    /// Test case generation related events for the navigation mediator
    /// </summary>
    public static class TestCaseGeneratorEvents
    {
        public class TestCaseCommandGenerated
        {
            public Requirement Requirement { get; }
            public string Command { get; }

            public TestCaseCommandGenerated(Requirement requirement, string command)
            {
                Requirement = requirement;
                Command = command;
            }
        }

        public class AnalysisCommandGenerated
        {
            public Requirement Requirement { get; }
            public string Command { get; }

            public AnalysisCommandGenerated(Requirement requirement, string command)
            {
                Requirement = requirement;
                Command = command;
            }
        }

        public class WorkspaceSetupGenerated
        {
            public string SetupInstructions { get; }

            public WorkspaceSetupGenerated(string setupInstructions)
            {
                SetupInstructions = setupInstructions;
            }
        }
    }
}