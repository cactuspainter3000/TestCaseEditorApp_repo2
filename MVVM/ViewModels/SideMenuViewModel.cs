using System;
using System.Linq;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the side navigation menu.
    /// Manages which menu section is currently selected and available menu items.
    /// </summary>
    public partial class SideMenuViewModel : ObservableObject
    {
        private readonly INewProjectMediator _newProjectMediator;
        private readonly IOpenProjectMediator _openProjectMediator;
        private readonly INavigationMediator _navigationMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        private readonly TestCaseAnythingLLMService _testCaseAnythingLLMService;
        private readonly JamaConnectService _jamaConnectService;
        private readonly ILogger<SideMenuViewModel> _logger;
        
        // AnythingLLM status tracking - use ObservableProperty for automatic command updates  
        [ObservableProperty]
        private bool _isAnythingLLMReady = false;
        
        // Requirements state tracking - use ObservableProperty for automatic command updates
        [ObservableProperty] 
        private bool hasRequirements = false;

        // AnythingLLM status text for Test Case Generator section
        [ObservableProperty]
        private string anythingLLMStatusText = "AnythingLLM not detected";
        
        // Analysis tab state tracking for context-sensitive menu visibility
        [ObservableProperty]
        private bool isAnalysisTabActive = false;

        [ObservableProperty]
        private string? selectedSection;

        [ObservableProperty]
        private ObservableCollection<MenuItemViewModel> menuItems = new();
        
        [ObservableProperty]
        private ObservableCollection<StepDescriptor> testCaseGeneratorSteps = new();
        
        // === DATA-DRIVEN MENU SYSTEM ===
        // New data-driven approach that replaces hardcoded XAML
        [ObservableProperty]
        private MenuSection? testCaseGeneratorMenuSection;

        // Project Management Commands
        public ICommand NewProjectCommand { get; private set; } = null!;
        public ICommand TestClickCommand { get; private set; } = null!;
        public ICommand OpenProjectCommand { get; private set; } = null!;
        public ICommand SaveProjectCommand { get; private set; } = null!;

        public ICommand ProjectNavigationCommand { get; private set; } = null!;
        public ICommand TestCaseGeneratorNavigationCommand { get; private set; } = null!;
        public ICommand RequirementsNavigationCommand { get; private set; } = null!;
        public ICommand NewProjectNavigationCommand { get; private set; } = null!;
        public ICommand DummyNavigationCommand { get; private set; } = null!;
        public ICommand StartupNavigationCommand { get; private set; } = null!;

        // Requirements Management Commands
        public ICommand ImportAdditionalCommand { get; private set; } = null!;
        
        // === MISSING COMMANDS FOR UI BINDING ===
        // These commands are bound to in XAML but were missing from the ViewModel
        
        public ICommand UnloadProjectCommand { get; private set; } = null!;
        public ICommand BatchAnalyzeCommand { get; private set; } = null!;
        public ICommand AnalyzeUnanalyzedCommand { get; private set; } = null!;
        public ICommand ReAnalyzeModifiedCommand { get; private set; } = null!;
        public ICommand GenerateAnalysisCommandCommand { get; private set; } = null!;
        public ICommand GenerateTestCaseCommandCommand { get; private set; } = null!;
        public ICommand ToggleAutoExportCommand { get; private set; } = null!;
        public ICommand OpenChatGptExportCommand { get; private set; } = null!;
        public ICommand ExportAllToJamaCommand { get; private set; } = null!;
        public ICommand ExportForChatGptCommand { get; private set; } = null!;
        public ICommand DemoStateManagementCommand { get; private set; } = null!;

        // Availability properties
        [ObservableProperty]
        private bool isAnythingLLMAvailable = true;
        
        // === MISSING PROPERTIES FOR UI BINDING ===
        // These properties are bound to in XAML but were missing from the ViewModel
        
        [ObservableProperty]
        private string? sapStatus;
        
        [ObservableProperty]
        private string? selectedMenuSection;
        
        [ObservableProperty]
        private ObservableCollection<object> testFlowSteps = new();
        
        [ObservableProperty]
        private object? selectedStep;
        
        [ObservableProperty]
        private bool autoExportForChatGpt = false;

        public SideMenuViewModel(INewProjectMediator newProjectMediator, IOpenProjectMediator openProjectMediator, INavigationMediator navigationMediator, ITestCaseGenerationMediator testCaseGenerationMediator, TestCaseAnythingLLMService testCaseAnythingLLMService, JamaConnectService jamaConnectService, ILogger<SideMenuViewModel> logger)
        {
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _testCaseAnythingLLMService = testCaseAnythingLLMService ?? throw new ArgumentNullException(nameof(testCaseAnythingLLMService));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Subscribe to AnythingLLM status updates
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Subscribe to requirements state changes for command availability
            SetupRequirementsEventSubscriptions();
            
            InitializeCommands();
            InitializeMenuItems();
            InitializeTestCaseGeneratorSteps();
            InitializeDataDrivenTestCaseGenerator(); // NEW: Data-driven menu
            
            // Request current status in case it was already set before we subscribed
            AnythingLLMMediator.RequestCurrentStatus();
        }

        private void InitializeCommands()
        {
            NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync, CanExecuteProjectCommands);
            TestClickCommand = new RelayCommand(() => System.Windows.MessageBox.Show("Test button clicked!", "Data-Driven Test"));
            OpenProjectCommand = new RelayCommand(NavigateToOpenProject, CanExecuteProjectCommands);
            SaveProjectCommand = new RelayCommand(() => { /* TODO: Implement save */ }, CanExecuteProjectActions);
            ProjectNavigationCommand = new RelayCommand(NavigateToProject);
            TestCaseGeneratorNavigationCommand = new RelayCommand(NavigateToTestCaseGenerator);
            TestCaseGeneratorNavigationCommand = new RelayCommand(NavigateToTestCaseGenerator);
            RequirementsNavigationCommand = new RelayCommand(NavigateToRequirements);
            NewProjectNavigationCommand = new RelayCommand(NavigateToNewProject, CanExecuteProjectCommands);
            DummyNavigationCommand = new RelayCommand(NavigateToDummy);
            StartupNavigationCommand = new RelayCommand(NavigateToStartup);
            
            // Requirements commands
            ImportAdditionalCommand = new AsyncRelayCommand(ImportAdditionalAsync, CanImportAdditionalRequirements);
            
            // Initialize missing commands
            UnloadProjectCommand = new AsyncRelayCommand(UnloadProjectAsync, CanExecuteProjectActions);
            BatchAnalyzeCommand = new RelayCommand(() => { /* TODO: Implement batch analyze */ }, CanAnalyzeRequirements);
            AnalyzeUnanalyzedCommand = new RelayCommand(() => { /* TODO: Implement analyze unanalyzed */ });
            ReAnalyzeModifiedCommand = new RelayCommand(() => { /* TODO: Implement re-analyze modified */ });
            GenerateAnalysisCommandCommand = new RelayCommand(() => { /* TODO: Implement generate analysis command */ });
            GenerateTestCaseCommandCommand = new RelayCommand(NavigateToTestCaseCreation);
            ToggleAutoExportCommand = new RelayCommand(() => AutoExportForChatGpt = !AutoExportForChatGpt);
            ExportAllToJamaCommand = new AsyncRelayCommand(ExportAllToJamaAsync);
            
            // Demo command for testing state management
            DemoStateManagementCommand = new RelayCommand(DemoStateManagement);
        }

        private async Task CreateNewProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.CreateNewProject called! ***");
            await _newProjectMediator.CreateNewProjectAsync();
        }
        
        /// <summary>
        /// Unload the current project
        /// </summary>
        private async Task UnloadProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.UnloadProject called! ***");
            await _newProjectMediator.CloseProjectAsync();
        }
        private void NavigateToProject()
        {
            System.Diagnostics.Debug.WriteLine("*** SideMenuViewModel.NavigateToProject called! ***");
            Console.WriteLine("*** SideMenuViewModel.NavigateToProject called! ***");
            
            SelectedSection = "Project"; // Update selected section to trigger SectionChanged event
            
            _navigationMediator.NavigateToSection("Project");
        }
        
        private void NavigateToTestCaseCreation()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("*** SideMenuViewModel.NavigateToTestCaseCreation called! ***");
                Console.WriteLine("*** SideMenuViewModel.NavigateToTestCaseCreation called! ***");
                
                Console.WriteLine($"*** Navigating to TestCaseCreation section, current selected: {SelectedSection} ***");
                SelectedSection = "TestCaseCreation"; // Update selected section to trigger SectionChanged event
                
                Console.WriteLine("*** About to call NavigateToSection('TestCaseCreation') ***");
                _navigationMediator.NavigateToSection("TestCaseCreation");
                Console.WriteLine("*** NavigateToSection call completed ***");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** ERROR in NavigateToTestCaseCreation: {ex.Message} ***");
                System.Diagnostics.Debug.WriteLine($"*** ERROR in NavigateToTestCaseCreation: {ex.Message} ***");
                System.Diagnostics.Debug.WriteLine($"*** Stack trace: {ex.StackTrace} ***");
            }
        }
        
        private async void NavigateToTestCaseGenerator()
        {
            System.Diagnostics.Debug.WriteLine("*** SideMenuViewModel.NavigateToTestCaseGenerator called! ***");
            // Request fresh AnythingLLM status when navigating to Test Case Generator
            AnythingLLMMediator.RequestCurrentStatus();
            
            SelectedSection = "TestCase"; // Update selected section to trigger SectionChanged event
            
            // Navigate to splash screen first
            _navigationMediator.NavigateToSection("TestCase");
            
            // Then launch AnythingLLM in background
            await _testCaseAnythingLLMService.ConnectAsync();
        }
        
        private void NavigateToRequirements()
        {
            System.Diagnostics.Debug.WriteLine("*** SideMenuViewModel.NavigateToRequirements called! ***");
            Console.WriteLine("*** SideMenuViewModel.NavigateToRequirements called! ***");
            
            SelectedSection = "Requirements"; // Update selected section to trigger SectionChanged event
            
            _navigationMediator.NavigateToSection("Requirements");
        }
        
        /// <summary>
        /// Import additional requirements to existing project (append mode)
        /// </summary>
        private async Task ImportAdditionalAsync()
        {
            try
            {
                await _newProjectMediator.ImportAdditionalRequirementsAsync();
            }
            catch (Exception ex)
            {
                // Error handling is done in the mediator, but log here for completeness
                System.Diagnostics.Debug.WriteLine($"Error in ImportAdditionalAsync: {ex.Message}");
            }
        }
        
        private void NavigateToNewProject()
        {
            Console.WriteLine("*** SideMenuViewModel.NavigateToNewProject called! ***");
            
            SelectedSection = "NewProject"; // Update selected section to trigger SectionChanged event
            
            _navigationMediator.NavigateToSection("NewProject");
        }
        
        private void NavigateToDummy()
        {
            System.Diagnostics.Debug.WriteLine("*** SideMenuViewModel.NavigateToDummy called! ***");
            Console.WriteLine("*** SideMenuViewModel.NavigateToDummy called! ***");
            
            SelectedSection = "Dummy"; // Update selected section to trigger SectionChanged event
            
            _navigationMediator.NavigateToSection("Dummy");
        }
        
        private void NavigateToStartup()
        {
            System.Diagnostics.Debug.WriteLine("*** SideMenuViewModel.NavigateToStartup called! ***");
            Console.WriteLine("*** SideMenuViewModel.NavigateToStartup called! ***");
            
            SelectedSection = "startup"; // Update selected section to trigger SectionChanged event
            
            _navigationMediator.NavigateToSection("startup");
        }
        
        #region AnythingLLM Status Handling
        
        private void OnAnythingLLMStatusUpdated(AnythingLLMStatus status)
        {
            IsAnythingLLMReady = status.IsAvailable && !status.IsStarting;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[MENU] AnythingLLM status updated - Ready: {IsAnythingLLMReady}, Available: {status.IsAvailable}, Starting: {status.IsStarting}");
            // Note: Status display is handled by NewProjectHeaderView, not the side menu
        }
        
        private bool CanExecuteProjectCommands()
        {
            return true; // Project creation/opening should always be available
        }

        private bool CanExecuteProjectActions()
        {
            return HasRequirements; // Save/Unload only available when there's an active project
        }

        private bool CanImportAdditionalRequirements()
        {
            return HasRequirements; // Only requires existing requirements to append to
        }

        private bool CanAnalyzeRequirements()
        {
            return IsAnythingLLMReady && HasRequirements; // Analysis requires LLM
        }
        
        #endregion
        
        
        private void NavigateToOpenProject()
        {
            Console.WriteLine("*** SideMenuViewModel.NavigateToOpenProject called! ***");
            
            SelectedSection = "OpenProject"; // Update selected section to trigger SectionChanged event
            
            _navigationMediator.NavigateToSection("OpenProject");
        }

        // ===============================================
        // REUSABLE HELPER METHODS - Copy these anywhere!
        // ===============================================
        
        /// <summary>
        /// Creates a simple menu button - 1 line instead of 6
        /// </summary>
        private MenuAction CreateButton(string id, string icon, string text, ICommand? command, string tooltip, int level = 2)
        {
            var button = MenuAction.Create(id, icon, text, command, tooltip);
            button.Level = level;
            return button;
        }
        
        /// <summary>
        /// Creates a dropdown menu with children - handles all the setup automatically
        /// </summary>
        private MenuAction CreateDropdown(string id, string icon, string text, string tooltip, params MenuAction[] children)
        {
            return CreateDropdownWithLevel(id, icon, text, tooltip, 1, children);
        }
        
        /// <summary>
        /// Creates a dropdown menu with children and specific level - handles all the setup automatically
        /// </summary>
        private MenuAction CreateDropdownWithLevel(string id, string icon, string text, string tooltip, int level, params MenuAction[] children)
        {
            var dropdown = MenuAction.Create(id, icon, text, null, tooltip);
            dropdown.IsDropdown = true;
            dropdown.IsExpanded = false;
            dropdown.Level = level;
            
            foreach (var child in children)
            {
                dropdown.AddChild(child);
                child.Level = level + 1;
            }
            
            return dropdown;
        }

        private void InitializeMenuItems()
        {
            MenuItems.Add(new MenuItemViewModel { Id = "Project", Title = "Project", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "requirements", Title = "Requirements", Badge = "", HasFileMenu = true });
            MenuItems.Add(new MenuItemViewModel { Id = "TestCases", Title = "Test Cases", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "TestFlow", Title = "Test Flow", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "Import", Title = "Import", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "NewProject", Title = "New Project", Badge = "" });
        }
        
        private void InitializeTestCaseGeneratorSteps()
        {
            TestCaseGeneratorSteps.Add(new StepDescriptor 
            { 
                Id = "project",
                DisplayName = "Project", 
                Badge = "",
                HasFileMenu = true
            });
            TestCaseGeneratorSteps.Add(new StepDescriptor 
            { 
                Id = "requirements",
                DisplayName = "Requirements", 
                Badge = "",
                HasFileMenu = true
            });
            TestCaseGeneratorSteps.Add(new StepDescriptor 
            { 
                Id = "llm-learning",
                DisplayName = "LLM Learning", 
                Badge = "",
                HasFileMenu = true
            });
            TestCaseGeneratorSteps.Add(new StepDescriptor 
            { 
                Id = "testcase-creation",
                DisplayName = "Test Case Generator", 
                Badge = "",
                HasFileMenu = true
            });
        }
        
        /// <summary>
        private void SetupRequirementsEventSubscriptions()
        {
            // Subscribe to requirements imported events
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnRequirementsImported);
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.AdditionalRequirementsImported>(OnAdditionalRequirementsImported);
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.SupportViewChanged>(OnSupportViewChanged);
        }
        
        /// <summary>
        /// Handle project loaded state
        /// </summary>
        private void OnProjectLoaded(/*ProjectLoadedEvent evt*/)
        {
            // TODO: Update menu item states when project loads
            // UpdateMenuItemState calls removed - need to implement with MenuAction system
        }
        
        /// <summary>
        /// Handle requirements imported events
        /// </summary>
        private void OnRequirementsImported(TestCaseGenerationEvents.RequirementsImported evt)
        {
            HasRequirements = evt.Requirements?.Count > 0; // ObservableProperty automatically triggers command updates
        }

        /// <summary>
        /// Handle additional requirements imported events
        /// </summary>
        private void OnAdditionalRequirementsImported(TestCaseGenerationEvents.AdditionalRequirementsImported evt)
        {
            HasRequirements = evt.Requirements?.Count > 0; // ObservableProperty automatically triggers command updates
        }

        /// <summary>
        /// Handle requirements collection changes (add/remove/clear)
        /// </summary>
        private void OnRequirementsCollectionChanged(TestCaseGenerationEvents.RequirementsCollectionChanged evt)
        {
            HasRequirements = evt.NewCount > 0; // ObservableProperty automatically triggers command updates
        }
        
        private void OnSupportViewChanged(TestCaseGenerationEvents.SupportViewChanged eventData)
        {
            IsAnalysisTabActive = eventData.IsAnalysisView;
            
            // Note: Analysis commands enable/disable is now handled by MenuAction system
            // Context-sensitive menu visibility is managed through the existing dropdown structure
            
            // TODO: Update enabled state of Analysis section items (visible but contextually enabled)
            // UpdateAnalysisItemsEnabledState call removed - need to implement with MenuAction system
            
            // TODO: Clarifying Questions section remains context-sensitive for visibility  
            // _clarifyingQuestionsSection references removed - need to implement with MenuAction system
                
            _logger?.LogDebug("Menu state updated: Analysis tab {EnabledState}", 
                IsAnalysisTabActive ? "active" : "inactive");
        }
        
        /// <summary>
        /// Called when HasRequirements changes - notify commands that depend on it
        /// </summary>
        partial void OnHasRequirementsChanged(bool value)
        {
            // Notify commands that depend on HasRequirements to re-evaluate their CanExecute
            (ImportAdditionalCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (BatchAnalyzeCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (UnloadProjectCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }
        
        
        public void PublishGlobalStateChange(string description, object? data = null)
        {
            // Example: When user performs an action, notify other parts of the app
            var stateEvent = new MenuStateEvents.GlobalStateChanged
            {
                IsProjectLoaded = true,
                HasRequirements = true,
                UnanalyzedCount = 3,
                IsAnalyzing = false
            };
            
            // TODO: Replace with actual mediator publication
            // _mediator.Publish(stateEvent);
            
            // For now, just demonstrate the pattern
            System.Diagnostics.Debug.WriteLine($"Publishing global state change: {description}");
        }
        
        /// <summary>
        /// Demo: Batch update multiple menu items (useful for major state transitions)
        /// </summary>
        public void DemoStateBatchUpdate()
        {
            // Example scenario: Project just loaded, enable relevant actions
            var updates = new (string id, bool? enabled, bool? visible, string? badge, string? icon)[]
            {
                ("project.save", true, null, null, null),       // Enable save
                ("project.unload", true, null, null, null),     // Enable unload (fixed ID)
                ("requirement.import", true, null, null, null), // Enable requirement import
                ("analysis.batch", false, null, null, "⚠️"),    // Disable but show warning icon
                ("analysis.unanalyzed", false, null, "3", null) // Disable but show count badge
            };
            
            // TODO: Implement batch menu updates with MenuAction system
            // foreach (var (id, enabled, visible, badge, icon) in updates)
            // {
            //     UpdateMenuItemState(id, enabled, visible, badge, icon);
            // }
            
            PublishGlobalStateChange("Project loaded", new { ProjectPath = "example.proj" });
        }
        
        /// <summary>
        /// Demo command implementation
        /// </summary>
        private void DemoStateManagement()
        {
            DemoStateBatchUpdate();
        }
        
        /// <summary>
        /// <summary>
        /// Updates badge for a specific menu item
        /// </summary>
        public void UpdateMenuItemBadge(string menuId, string badge)
        {
            var item = MenuItems.FirstOrDefault(m => m.Id == menuId);
            if (item != null)
            {
                item.Badge = badge;
            }
        }
        
        /// <summary>
        /// Initialize data-driven Test Case Generator with proper hierarchical structure
        /// </summary>
        private void InitializeDataDrivenTestCaseGenerator()
        {
            TestCaseGeneratorMenuSection = MenuSection.Create(
                id: "test-case-generator-data-driven",
                headerIcon: "🧪",
                headerText: "Test Case Generator"
            );

            // Add a "Home" button at the top level to navigate to startup domain
            var homeButton = CreateButton("home", "🏠", "Home", StartupNavigationCommand, "Return to application home screen", 0);
            TestCaseGeneratorMenuSection.AddItem(homeButton);

            // Create the main Test Case Generator dropdown with all sections as children
            var mainTestCaseGeneratorDropdown = CreateDropdownWithLevel("test-case-generator-main", "🧪", "Test Case Generator", "Test case generation workflow", 0,
                
                // === PROJECT DROPDOWN (as sub-item) ===
                CreateDropdown("project", "📁", "Project", "Project management options",
                    CreateButton("new-project", "🆕", "New Project", NewProjectNavigationCommand, "Create a new test case project"),
                    CreateButton("dummy-domain", "🎯", "Dummy Domain", DummyNavigationCommand, "Navigate to Dummy domain - AI Guide reference implementation"),
                    CreateButton("open-project", "📁", "Open Project", OpenProjectCommand, "Load an existing project"),
                    CreateButton("save-project", "💾", "Save Project", SaveProjectCommand, "Save current project"),
                    CreateButton("unload-project", "📤", "Unload Project", UnloadProjectCommand, "Unload current project")
                ),

                // === REQUIREMENTS DROPDOWN (as sub-item) ===
                CreateDropdown("requirements", "📋", "Requirements", "Requirements management options",
                    CreateButton("import-additional", "📥", "Import Additional Requirements", ImportAdditionalCommand, "Import additional requirements"),
                    CreateDropdown("analysis", "📊", "Analysis", "LLM analysis operations", 
                        CreateButton("batch-analyze", "⚡", "Analyze All Requirements", BatchAnalyzeCommand, "Analyze all requirements"),
                        CreateButton("analyze-unanalyzed", "🔍", "Analyze Unanalyzed", AnalyzeUnanalyzedCommand, "Analyze unanalyzed requirements"),
                        CreateButton("reanalyze-modified", "🔄", "Re-analyze Modified", ReAnalyzeModifiedCommand, "Re-analyze modified requirements"),
                        CreateButton("generate-analysis-command", "🔍", "Generate Analysis Command", GenerateAnalysisCommandCommand, "Generate analysis command")
                    )
                ),

                // === LLM LEARNING DROPDOWN (as sub-item) ===
                CreateDropdown("llm-learning", "🧠", "LLM Learning", "LLM learning and training options",
                    CreateButton("toggle-auto-export", "📤", "Toggle Auto Export", ToggleAutoExportCommand, "Toggle auto-export for ChatGPT analysis")
                ),

                // === TEST CASE CREATION DROPDOWN (as sub-item) ===
                CreateDropdown("testcase-creation", "⚙️", "Test Case Creation", "Test case generation options",
                    CreateButton("testcase-creation-main", "🏠", "Test Case Creation", new RelayCommand(NavigateToTestCaseCreation), "Navigate to Test Case Creation workspace"),
                    CreateButton("generate-testcase-command", "⚙️", "Generate Test Case Command", GenerateTestCaseCommandCommand, "Generate test case command for current requirement"),
                    CreateButton("export-to-jama", "📋", "Export to Jama…", ExportAllToJamaCommand, "Export all test cases to Jama")
                )
            );

            TestCaseGeneratorMenuSection.AddItem(mainTestCaseGeneratorDropdown);

            // Set commands after creation
            mainTestCaseGeneratorDropdown.Command = TestCaseGeneratorNavigationCommand; // Navigate when clicked
            var projectDropdown = mainTestCaseGeneratorDropdown.Children.FirstOrDefault(x => x.Id == "project") as MenuAction;
            if (projectDropdown != null)
            {
                projectDropdown.Command = ProjectNavigationCommand;
            }
            var requirementsDropdown = mainTestCaseGeneratorDropdown.Children.FirstOrDefault(x => x.Id == "requirements") as MenuAction;
            if (requirementsDropdown != null)
            {
                requirementsDropdown.Command = RequirementsNavigationCommand;
            }

            System.Diagnostics.Debug.WriteLine($"[SideMenuViewModel] Data-driven TestCaseGenerator initialized with hierarchical structure: 1 main dropdown containing {mainTestCaseGeneratorDropdown.Children.Count} sub-sections");
        }

        /// <summary>
        /// Creates a test dropdown item to demonstrate expand/collapse functionality
        /// </summary>
        private MenuAction CreateTestDropdownItem()
        {
            var dropdownItem = MenuAction.Create("test.dropdown", "📁", "Test Dropdown", null, "Click chevron to expand/collapse");
            dropdownItem.IsDropdown = true;
            dropdownItem.IsExpanded = false;
            return dropdownItem;
        }

        partial void OnSelectedSectionChanged(string? value)
        {
            // Notify other view models when section changes
            SectionChanged?.Invoke(value);
        }

        public event System.Action<string?>? SectionChanged;

        /// <summary>
        /// Export all generated test cases to Jama Connect
        /// </summary>
        private async Task ExportAllToJamaAsync()
        {
            try
            {
                _logger?.LogInformation("Starting Jama Connect export...");
                
                // JamaConnectService is now guaranteed to be non-null via DI, check if configured
                if (!_jamaConnectService.IsConfigured)
                {
                    // Add debugging info to see what's happening with environment variables
                    var debugInfo = $"Debug Info:\n" +
                        $"JAMA_BASE_URL = '{Environment.GetEnvironmentVariable("JAMA_BASE_URL")}'\n" +
                        $"JAMA_CLIENT_ID = '{Environment.GetEnvironmentVariable("JAMA_CLIENT_ID")}'\n" +
                        $"JAMA_CLIENT_SECRET = '{(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET")) ? "Not Set" : "Set")}'\n\n";
                    
                    MessageBox.Show(
                        debugInfo +
                        "Jama Connect is not configured.\n\n" +
                        "To enable Jama integration, configure these environment variables:\n\n" +
                        "Option 1 - API Token (if available):\n" +
                        "• JAMA_BASE_URL (e.g., https://jama02.rockwellcollins.com/contour)\n" +
                        "• JAMA_API_TOKEN\n\n" +
                        "Option 2 - OAuth Client Credentials (recommended):\n" +
                        "• JAMA_BASE_URL (e.g., https://jama02.rockwellcollins.com/contour)\n" +
                        "• JAMA_CLIENT_ID\n" +
                        "• JAMA_CLIENT_SECRET\n\n" +
                        "Option 3 - Username/Password:\n" +
                        "• JAMA_BASE_URL\n" +
                        "• JAMA_USERNAME\n" +
                        "• JAMA_PASSWORD",
                        "Jama Connect Not Configured", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // Test connection first
                var (success, message) = await _jamaConnectService.TestConnectionAsync();
                if (!success)
                {
                    MessageBox.Show(
                        $"Cannot connect to Jama Connect:\n{message}\n\n" +
                        "Please check your configuration and network connection.",
                        "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                _logger?.LogInformation("Jama connection successful: {Message}", message);
                
                // Get projects to let user choose
                var projects = await _jamaConnectService.GetProjectsAsync();
                if (projects == null || projects.Count == 0)
                {
                    MessageBox.Show(
                        "No projects found in Jama Connect.\n\n" +
                        "Please ensure your user has access to at least one project.",
                        "No Projects Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // For now, show available projects and let user know this is a test
                var projectList = string.Join("\n", projects.Select(p => $"• {p.Name} (ID: {p.Id})"));
                
                var result = MessageBox.Show(
                    $"Jama Connect integration test successful!\n\n" +
                    $"Found {projects.Count} project(s):\n{projectList}\n\n" +
                    $"Would you like to run a test import of requirements from the first project?",
                    "Jama Connect Test", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                if (result == MessageBoxResult.Yes && projects.Count > 0)
                {
                    await TestImportRequirementsAsync(projects[0].Id);
                }
                
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export to Jama Connect");
                MessageBox.Show(
                    $"Jama Connect export failed:\n{ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Test importing requirements from Jama
        /// </summary>
        private async Task TestImportRequirementsAsync(int projectId)
        {
            try
            {
                _logger?.LogInformation("Testing requirement import from Jama project {ProjectId}", projectId);
                
                var jamaItems = await _jamaConnectService.GetRequirementsAsync(projectId);
                var requirements = _jamaConnectService.ConvertToRequirements(jamaItems);
                
                MessageBox.Show(
                    $"Successfully imported {requirements.Count} requirements from Jama!\n\n" +
                    $"Sample requirements:\n" +
                    string.Join("\n", requirements.Take(3).Select(r => $"• {r.Item}: {r.Name}")),
                    "Import Test Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                _logger?.LogInformation("Successfully imported {Count} requirements from Jama", requirements.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to test import from Jama project {ProjectId}", projectId);
                MessageBox.Show(
                    $"Test import failed:\n{ex.Message}",
                    "Import Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public partial class MenuItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string badge = string.Empty;
        
        [ObservableProperty]
        private bool hasFileMenu = false;
    }
}
