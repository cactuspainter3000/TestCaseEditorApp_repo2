using System;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the side navigation menu.
    /// Manages which menu section is currently selected and available menu items.
    /// </summary>
    public partial class SideMenuViewModel : ObservableObject
    {
        private readonly IWorkspaceManagementMediator? _workspaceManagementMediator;
        private readonly INavigationMediator? _navigationMediator;
        private readonly ITestCaseGenerationMediator? _testCaseGenerationMediator;
        private readonly TestCaseAnythingLLMService? _testCaseAnythingLLMService;
        
        // AnythingLLM status tracking - use ObservableProperty for automatic command updates  
        [ObservableProperty]
        private bool _isAnythingLLMReady = false;
        
        // Requirements state tracking - use ObservableProperty for automatic command updates
        [ObservableProperty] 
        private bool hasRequirements = false;

        // AnythingLLM status text for Test Case Generator section
        [ObservableProperty]
        private string anythingLLMStatusText = "AnythingLLM not detected";

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
        
        // === NEW DATA-DRIVEN APPROACH ===
        // Single collection that defines the entire menu hierarchy
        [ObservableProperty]
        private ObservableCollection<MenuHierarchyItem> hierarchicalMenuItems = new();

        // Project Management Commands
        public ICommand NewProjectCommand { get; private set; } = null!;
        public ICommand TestClickCommand { get; private set; } = null!;
        public ICommand OpenProjectCommand { get; private set; } = null!;
        public ICommand SaveProjectCommand { get; private set; } = null!;

        public ICommand ProjectNavigationCommand { get; private set; } = null!;
        public ICommand TestCaseGeneratorNavigationCommand { get; private set; } = null!;
        public ICommand RequirementsNavigationCommand { get; private set; } = null!;
        public ICommand NewProjectNavigationCommand { get; private set; } = null!;

        // Requirements Management Commands
        public ICommand ImportAdditionalCommand { get; private set; } = null!;
        
        // === MISSING COMMANDS FOR UI BINDING ===
        // These commands are bound to in XAML but were missing from the ViewModel
        
        public ICommand UnloadProjectCommand { get; private set; } = null!;
        public ICommand BatchAnalyzeCommand { get; private set; } = null!;
        public ICommand AnalyzeUnanalyzedCommand { get; private set; } = null!;
        public ICommand ReAnalyzeModifiedCommand { get; private set; } = null!;
        public ICommand GenerateLearningPromptCommand { get; private set; } = null!;
        public ICommand PasteChatGptAnalysisCommand { get; private set; } = null!;
        public ICommand SetupLlmWorkspaceCommand { get; private set; } = null!;
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

        public SideMenuViewModel(IWorkspaceManagementMediator? workspaceManagementMediator = null, INavigationMediator? navigationMediator = null, ITestCaseGenerationMediator? testCaseGenerationMediator = null, TestCaseAnythingLLMService? testCaseAnythingLLMService = null)
        {
            _workspaceManagementMediator = workspaceManagementMediator;
            _navigationMediator = navigationMediator;
            _testCaseGenerationMediator = testCaseGenerationMediator;
            _testCaseAnythingLLMService = testCaseAnythingLLMService;
            
            // Subscribe to AnythingLLM status updates
            AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
            
            // Subscribe to requirements state changes for command availability
            SetupRequirementsEventSubscriptions();
            
            InitializeCommands();
            InitializeMenuItems();
            InitializeTestCaseGeneratorSteps();
            InitializeDataDrivenTestCaseGenerator(); // NEW: Data-driven menu
            InitializeHierarchicalMenu(); // NEW: Data-driven menu
            
            // Request current status in case it was already set before we subscribed
            AnythingLLMMediator.RequestCurrentStatus();
        }

        private void InitializeCommands()
        {
            NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync, CanExecuteProjectCommands);
            TestClickCommand = new RelayCommand(() => System.Windows.MessageBox.Show("Test button clicked!", "Data-Driven Test"));
            OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync, CanExecuteProjectCommands);
            SaveProjectCommand = new RelayCommand(() => { /* TODO: Implement save */ }, CanExecuteProjectActions);
            ProjectNavigationCommand = new RelayCommand(NavigateToProject);
            TestCaseGeneratorNavigationCommand = new RelayCommand(NavigateToTestCaseGenerator);
            TestCaseGeneratorNavigationCommand = new RelayCommand(NavigateToTestCaseGenerator);
            RequirementsNavigationCommand = new RelayCommand(NavigateToRequirements);
            NewProjectNavigationCommand = new RelayCommand(NavigateToNewProject, CanExecuteProjectCommands);
            
            // Requirements commands
            ImportAdditionalCommand = new AsyncRelayCommand(ImportAdditionalAsync, CanImportAdditionalRequirements);
            
            // Initialize missing commands
            UnloadProjectCommand = new AsyncRelayCommand(UnloadProjectAsync, CanExecuteProjectActions);
            BatchAnalyzeCommand = new RelayCommand(() => { /* TODO: Implement batch analyze */ }, CanAnalyzeRequirements);
            AnalyzeUnanalyzedCommand = new RelayCommand(() => { /* TODO: Implement analyze unanalyzed */ });
            ReAnalyzeModifiedCommand = new RelayCommand(() => { /* TODO: Implement re-analyze modified */ });
            GenerateLearningPromptCommand = new RelayCommand(() => { /* TODO: Implement generate learning prompt */ });
            PasteChatGptAnalysisCommand = new RelayCommand(() => { /* TODO: Implement paste ChatGPT analysis */ });
            SetupLlmWorkspaceCommand = new RelayCommand(() => { /* TODO: Implement setup LLM workspace */ });
            GenerateAnalysisCommandCommand = new RelayCommand(() => { /* TODO: Implement generate analysis command */ });
            GenerateTestCaseCommandCommand = new RelayCommand(() => { /* TODO: Implement generate test case command */ });
            ToggleAutoExportCommand = new RelayCommand(() => AutoExportForChatGpt = !AutoExportForChatGpt);
            OpenChatGptExportCommand = new RelayCommand(() => { /* TODO: Implement open ChatGPT export */ });
            ExportAllToJamaCommand = new RelayCommand(() => { /* TODO: Implement export all to Jama */ });
            ExportForChatGptCommand = new AsyncRelayCommand(ExportForChatGptAsync);
            
            // Demo command for testing state management
            DemoStateManagementCommand = new RelayCommand(DemoStateManagement);
        }

        private async Task CreateNewProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.CreateNewProject called! ***");
            if (_workspaceManagementMediator != null)
            {
                await _workspaceManagementMediator.CreateNewProjectAsync();
            }
            else
            {
                Console.WriteLine("*** WorkspaceManagementMediator is null in SideMenuViewModel! ***");
            }
        }
        
        /// <summary>
        /// Unload the current project
        /// </summary>
        private async Task UnloadProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.UnloadProject called! ***");
            if (_workspaceManagementMediator != null)
            {
                await _workspaceManagementMediator.CloseProjectAsync();
            }
            else
            {
                Console.WriteLine("*** WorkspaceManagementMediator is null in SideMenuViewModel! ***");
            }
        }
        private void NavigateToProject()
        {
            Console.WriteLine("*** SideMenuViewModel.NavigateToProject called! ***");
            if (_navigationMediator != null)
            {
                _navigationMediator.NavigateToSection("Project");
            }
        }
        
        private async void NavigateToTestCaseGenerator()
        {
            // Request fresh AnythingLLM status when navigating to Test Case Generator
            AnythingLLMMediator.RequestCurrentStatus();
            
            // Navigate to splash screen first
            if (_navigationMediator != null)
            {
                _navigationMediator.NavigateToSection("TestCase");
            }
            
            // Then launch AnythingLLM in background
            if (_testCaseAnythingLLMService != null)
            {
                await _testCaseAnythingLLMService.ConnectAsync();
            }
        }
        
        private void NavigateToRequirements()
        {
            Console.WriteLine("*** SideMenuViewModel.NavigateToRequirements called! ***");
            
            if (_navigationMediator != null)
            {
                _navigationMediator.NavigateToSection("Requirements");
            }
            else
            {
                Console.WriteLine("*** NavigationMediator is null in SideMenuViewModel! ***");
            }
        }
        
        /// <summary>
        /// Import additional requirements to existing project (append mode)
        /// </summary>
        private async Task ImportAdditionalAsync()
        {
            try
            {
                if (_workspaceManagementMediator == null)
                {
                    return;
                }
                
                await _workspaceManagementMediator.ImportAdditionalRequirementsAsync();
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
            
            if (_navigationMediator != null)
            {
                _navigationMediator.NavigateToSection("NewProject");
            }
            else
            {
                Console.WriteLine("*** NavigationMediator is null in SideMenuViewModel! ***");
            }
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
            return IsAnythingLLMReady && HasRequirements; // Analysis requires LLM
        }

        private bool CanAnalyzeRequirements()
        {
            return IsAnythingLLMReady && HasRequirements; // Analysis requires LLM
        }
        
        #endregion
        
        private async Task OpenProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.OpenProject called! ***");
            if (_workspaceManagementMediator != null)
            {
                await _workspaceManagementMediator.OpenProjectAsync();
            }
            else
            {
                Console.WriteLine("*** WorkspaceManagementMediator is null in SideMenuViewModel! ***");
            }
        }

        // ===============================================
        // REUSABLE HELPER METHODS - Copy these anywhere!
        // ===============================================
        
        /// <summary>
        /// Creates a simple menu button - 1 line instead of 6
        /// </summary>
        private static MenuAction CreateButton(string id, string icon, string text, ICommand? command, string tooltip, int level = 2)
        {
            var button = MenuAction.Create(id, icon, text, command, tooltip);
            button.Level = level;
            return button;
        }
        
        /// <summary>
        /// Creates a dropdown menu with children - handles all the setup automatically
        /// </summary>
        private static MenuAction CreateDropdown(string id, string icon, string text, string tooltip, params MenuAction[] children)
        {
            return CreateDropdownWithLevel(id, icon, text, tooltip, 1, children);
        }
        
        /// <summary>
        /// Creates a dropdown menu with children and specific level - handles all the setup automatically
        /// </summary>
        private static MenuAction CreateDropdownWithLevel(string id, string icon, string text, string tooltip, int level, params MenuAction[] children)
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
        /// NEW: Initialize the data-driven hierarchical menu
        /// This shows how easy it becomes to define complex menu structures
        /// </summary>
        private void InitializeHierarchicalMenu()
        {
            Console.WriteLine("*** InitializeHierarchicalMenu() called! ***");
            HierarchicalMenuItems.Clear();
            
            // Level 1: Test Case Generator (Primary header)
            var testCaseGenerator = MenuHierarchyItem.CreateSection("Test Case Generator", 1, true,
                // Level 2: Project (Secondary header)
                CreateProjectSectionWithCommand(),
                
                // Level 2: Requirement (Secondary header)
                MenuHierarchyItem.CreateSection("Requirement", 2, true,
                    CreateActionWithId("requirement.import", "📥 Import Additional Requirements", "📥", ImportAdditionalCommand, false), // Disabled until project loaded
                    
                    // Level 3: Analysis (Tertiary header)
                    MenuHierarchyItem.CreateSection("Analysis", 3, true,
                        CreateActionWithId("analysis.batch", "⚡ Analyze All Requirements", "⚡", BatchAnalyzeCommand, false),
                        CreateActionWithId("analysis.unanalyzed", "🔍 Analyze Unanalyzed", "🔍", AnalyzeUnanalyzedCommand, false),
                        CreateActionWithId("analysis.export", "📝 Export for ChatGPT", "📝", ExportForChatGptCommand, false)
                    ),
                    
                    // Level 3: Clarifying Questions (Tertiary header) 
                    MenuHierarchyItem.CreateSection("Clarifying Questions", 3, true,
                        CreateActionWithId("questions.ask", "❓ Ask Questions", "❓", null, false),
                        CreateActionWithId("questions.paste", "📋 Paste from Clipboard", "📋", null, false),
                        CreateActionWithId("questions.regenerate", "🔄 Regenerate Questions", "🔄", null, false)
                    ),
                    
                    // Level 3: Verification Method Assumptions (Tertiary header)
                    MenuHierarchyItem.CreateSection("Verification Method Assumptions", 3, true,
                        CreateActionWithId("verification.reset", "🔄 Reset All Assumptions", "🔄", null, false),
                        CreateActionWithId("verification.clear", "🧹 Clear Preset Filter", "🧹", null, false)
                    )
                ),
                
                // Level 2: Direct items (Secondary headers but not expandable)
                CreateNonExpandableWithId("llm.learning", "LLM Learning", 2),
                // Removed duplicate "Test Case Generator" - the top-level one now handles navigation
                CreateActionWithId("demo.state", "🧪 Demo State Management", "🧪", DemoStateManagementCommand)
            );
            
            // CRITICAL: Assign navigation command to top-level Test Case Generator section
            // This is what makes clicking "Test Case Generator" work, just like Project does!
            testCaseGenerator.Command = TestCaseGeneratorNavigationCommand;
            Console.WriteLine("*** Assigned TestCaseGeneratorNavigationCommand to top-level Test Case Generator section! ***");
            
            // Level 1: Test Flow Generator (Primary header)
            var testFlowGenerator = MenuHierarchyItem.CreateSection("Test Flow Generator", 1, true,
                // Add test flow items here when needed
                MenuHierarchyItem.CreateSection("Flow Design", 2, true,
                    CreateActionWithId("flow.create", "🆕 Create New Flow", "🆕", null, false),
                    CreateActionWithId("flow.validate", "✅ Validate Flow", "✅", null, false),
                    CreateActionWithId("flow.export", "📤 Export Flow", "📤", null, false)
                ),
                MenuHierarchyItem.CreateSection("Testing", 2, true,
                    CreateActionWithId("testing.option1", "🧪 Test Option 1", "🧪", null, false),
                    CreateActionWithId("testing.option2", "🔬 Test Option 2", "🔬", null, false)
                )
            );
            
            HierarchicalMenuItems.Add(testCaseGenerator);
            HierarchicalMenuItems.Add(testFlowGenerator);
            
            // Level 1: TEST - Data-Driven Test Flow Pattern (Primary header)
            var testFlowPattern = MenuHierarchyItem.CreateSection("🧪 Data-Driven Test Flow", 1, true,
                // Level 2: Flow Steps (simulating ListBox items)
                CreateFlowStepSection("Flow Step 1", "Ready", true,
                    CreateActionWithId("flow.create", "🆕 Create New Flow", "🆕", null, true),
                    CreateActionWithId("flow.validate", "✅ Validate Flow", "✅", null, true),
                    CreateActionWithId("flow.export", "📤 Export Flow", "📤", null, true)
                ),
                
                CreateFlowStepSection("Flow Step 2", "2", false),
                
                CreateFlowStepSection("Testing Options", "", true,
                    CreateActionWithId("test.option1", "🧪 Test Option 1", "🧪", null, true),
                    CreateActionWithId("test.option2", "🔬 Test Option 2", "🔬", null, true)
                )
            );
            
            HierarchicalMenuItems.Add(testFlowPattern);
            
            // Subscribe to state change events
            SubscribeToStateEvents();
        }
        
        private MenuHierarchyItem CreateFlowStepSection(string title, string badge, bool isExpandable, params MenuHierarchyItem[] children)
        {
            var section = MenuHierarchyItem.CreateSection(title, 2, isExpandable, children);
            section.Badge = badge;
            section.StatusIcon = "";
            return section;
        }
        
        /// <summary>
        /// Helper to create action items with IDs for state management
        /// </summary>
        private MenuHierarchyItem CreateActionWithId(string id, string title, string icon, ICommand? command, bool isEnabled = true)
        {
            var item = MenuHierarchyItem.CreateAction(title, icon, command, isEnabled);
            item.Tag = id; // Store ID for mediator lookups
            return item;
        }
        
        /// <summary>
        /// Helper to create non-expandable items with IDs
        /// </summary>
        private MenuHierarchyItem CreateNonExpandableWithId(string id, string title, int level)
        {
            var item = new MenuHierarchyItem 
            { 
                Title = title, 
                Level = level, 
                IsExpandable = false,
                Tag = id
            };
            
            // Assign commands based on ID
            if (id == "testcase.generator")
            {
                item.Command = TestCaseGeneratorNavigationCommand;
                Console.WriteLine($"*** Assigned TestCaseGeneratorNavigationCommand to item: {title} (id: {id}) ***");
            }
            
            Console.WriteLine($"*** Created non-expandable item: {title} (id: {id}) with command: {item.Command?.GetType().Name ?? "null"} ***");
            
            return item;
        }
        
        /// <summary>
        /// Creates the Project section with RelayCommand attached to notify mediators
        /// </summary>
        private MenuHierarchyItem CreateProjectSectionWithCommand()
        {
            var projectSection = MenuHierarchyItem.CreateSection("Project", 2, true,
                CreateActionWithId("project.new", "🗂️ New Project", "🗂️", NewProjectCommand),
                CreateActionWithId("project.open", "📂 Open Project", "📂", OpenProjectCommand),
                CreateActionWithId("project.save", "💾 Save Project", "💾", SaveProjectCommand, true),
                CreateActionWithId("project.unload", "📤 Unload Project", "📤", UnloadProjectCommand, true)
            );
            
            projectSection.Command = ProjectNavigationCommand;
            return projectSection;
        }
        
        /// <summary>
        /// Subscribe to mediator events for state management
        /// </summary>
        private void SubscribeToStateEvents()
        {
            // Subscribe to global state changes that affect multiple menu items
            // Example: When project loads, enable project-related menu items
            
            // Cross-domain event subscriptions for UI coordination
            // Requirements state changes affect command availability
        }

        /// <summary>
        /// Set up event subscriptions for requirements state changes
        /// </summary>
        private void SetupRequirementsEventSubscriptions()
        {
            if (_testCaseGenerationMediator != null)
            {
                // Subscribe to requirements imported events
                _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnRequirementsImported);
                _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.AdditionalRequirementsImported>(OnAdditionalRequirementsImported);
                _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            }
        }
        
        /// <summary>
        /// Handle project loaded state
        /// </summary>
        private void OnProjectLoaded(/*ProjectLoadedEvent evt*/)
        {
            UpdateMenuItemState("project.save", isEnabled: true);
            UpdateMenuItemState("project.unload", isEnabled: true); // Fixed ID mismatch
            UpdateMenuItemState("requirement.import", isEnabled: true);
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
        
        /// <summary>
        /// Update state of a specific menu item by ID
        /// </summary>
        private void UpdateMenuItemState(string id, bool? isEnabled = null, bool? isVisible = null, string? badge = null, string? statusIcon = null)
        {
            var item = FindMenuItemById(id);
            item?.UpdateState(isEnabled, isVisible, badge, statusIcon);
        }
        
        /// <summary>
        /// Find menu item by ID (recursive search)
        /// </summary>
        private MenuHierarchyItem? FindMenuItemById(string id)
        {
            foreach (var rootItem in HierarchicalMenuItems)
            {
                var found = FindMenuItemByIdRecursive(rootItem, id);
                if (found != null) return found;
            }
            return null;
        }
        
        private MenuHierarchyItem? FindMenuItemByIdRecursive(MenuHierarchyItem item, string id)
        {
            if (item.Tag as string == id) return item;
            
            foreach (var child in item.Children)
            {
                var found = FindMenuItemByIdRecursive(child, id);
                if (found != null) return found;
            }
            return null;
        }
        
        /// <summary>
        /// Demo: Publish state updates through mediator (for other components to respond to)
        /// </summary>
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
            
            foreach (var (id, enabled, visible, badge, icon) in updates)
            {
                UpdateMenuItemState(id, enabled, visible, badge, icon);
            }
            
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
        /// Enhanced export command with state feedback
        /// </summary>
        private async Task ExportForChatGptAsync()
        {
            UpdateMenuItemState("analysis.export", badge: "⏳");
            
            try
            {
                await Task.Delay(2000); // Simulate work
                UpdateMenuItemState("analysis.export", badge: "✅");
                PublishGlobalStateChange("Export completed");
                
                // Clear status after 3 seconds
                await Task.Delay(3000);
                UpdateMenuItemState("analysis.export", badge: null);
            }
            catch
            {
                UpdateMenuItemState("analysis.export", badge: "❌");
                await Task.Delay(3000);
                UpdateMenuItemState("analysis.export", badge: null);
            }
        }

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

            // Create the main Test Case Generator dropdown with all sections as children
            var mainTestCaseGeneratorDropdown = CreateDropdownWithLevel("test-case-generator-main", "🧪", "Test Case Generator", "Test case generation workflow", 0,
                
                // === PROJECT DROPDOWN (as sub-item) ===
                CreateDropdown("project", "📁", "Project", "Project management options",
                    CreateButton("new-project", "🆕", "New Project", NewProjectNavigationCommand, "Create a new test case project"),
                    CreateButton("open-project", "📁", "Open Project", OpenProjectCommand, "Load an existing project"),
                    CreateButton("save-project", "💾", "Save Project", SaveProjectCommand, "Save current project"),
                    CreateButton("unload-project", "📤", "Unload Project", UnloadProjectCommand, "Unload current project")
                ),

                // === REQUIREMENTS DROPDOWN (as sub-item) ===
                CreateDropdown("requirements", "📋", "Requirements", "Requirements management options",
                    CreateButton("import-additional", "📥", "Import Additional Requirements", ImportAdditionalCommand, "Import additional requirements"),
                    CreateButton("batch-analyze", "⚡", "Analyze All Requirements", BatchAnalyzeCommand, "Analyze all requirements")
                ),

                // === LLM LEARNING DROPDOWN (as sub-item) ===
                CreateDropdown("llm-learning", "🧠", "LLM Learning", "LLM learning and training options",
                    CreateButton("analyze-unanalyzed", "🔍", "Analyze Unanalyzed", AnalyzeUnanalyzedCommand, "Analyze unanalyzed requirements"),
                    CreateButton("reanalyze-modified", "🔄", "Re-analyze Modified", ReAnalyzeModifiedCommand, "Re-analyze modified requirements"),
                    CreateButton("generate-learning-prompt", "📋", "Generate Learning Prompt", GenerateLearningPromptCommand, "Generate learning prompt and copy to clipboard"),
                    CreateButton("paste-chatgpt-analysis", "📥", "Paste ChatGPT Analysis", PasteChatGptAnalysisCommand, "Paste and import ChatGPT analysis results"),
                    CreateButton("setup-llm-workspace", "🔧", "Setup LLM Workspace", SetupLlmWorkspaceCommand, "Setup integrated LLM workspace"),
                    CreateButton("generate-analysis-command", "🔍", "Generate Analysis Command", GenerateAnalysisCommandCommand, "Generate analysis command for current requirement"),
                    CreateButton("generate-testcase-command", "⚙️", "Generate Test Case Command", GenerateTestCaseCommandCommand, "Generate test case command for current requirement"),
                    CreateButton("toggle-auto-export", "📤", "Export for ChatGPT", ToggleAutoExportCommand, "Toggle auto-export for ChatGPT analysis"),
                    CreateButton("open-chatgpt-export", "📝", "Open Export", OpenChatGptExportCommand, "Open the most recent ChatGPT export file")
                ),

                // === TEST CASE CREATION DROPDOWN (as sub-item) ===
                CreateDropdown("testcase-creation", "⚙️", "Test Case Creation", "Test case generation options",
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
