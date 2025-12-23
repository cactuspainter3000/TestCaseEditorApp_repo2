using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the side navigation menu.
    /// Manages which menu section is currently selected and available menu items.
    /// </summary>
    public partial class SideMenuViewModel : ObservableObject
    {
        private readonly IWorkspaceManagementMediator? _workspaceManagementMediator;

        [ObservableProperty]
        private string? selectedSection;

        [ObservableProperty]
        private ObservableCollection<MenuItemViewModel> menuItems = new();
        
        [ObservableProperty]
        private ObservableCollection<StepDescriptor> testCaseGeneratorSteps = new();
        
        // === NEW DATA-DRIVEN APPROACH ===
        // Single collection that defines the entire menu hierarchy
        [ObservableProperty]
        private ObservableCollection<MenuHierarchyItem> hierarchicalMenuItems = new();

        // Project Management Commands
        public ICommand NewProjectCommand { get; private set; } = null!;
        public ICommand OpenProjectCommand { get; private set; } = null!;
        public ICommand SaveProjectCommand { get; private set; } = null!;
        public ICommand QuickImportCommand { get; private set; } = null!;

        // Requirements Management Commands
        public ICommand ImportAdditionalCommand { get; private set; } = null!;
        
        // === MISSING COMMANDS FOR UI BINDING ===
        // These commands are bound to in XAML but were missing from the ViewModel
        
        public ICommand CloseProjectCommand { get; private set; } = null!;
        public ICommand BatchAnalyzeCommand { get; private set; } = null!;
        public ICommand AnalyzeUnanalyzedCommand { get; private set; } = null!;
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

        public SideMenuViewModel(IWorkspaceManagementMediator? workspaceManagementMediator = null)
        {
            _workspaceManagementMediator = workspaceManagementMediator;
            InitializeCommands();
            InitializeMenuItems();
            InitializeTestCaseGeneratorSteps();
            InitializeHierarchicalMenu(); // NEW: Data-driven menu
        }

        private void InitializeCommands()
        {
            NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync);
            OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
            SaveProjectCommand = new RelayCommand(() => { /* TODO: Implement save */ });
            QuickImportCommand = new RelayCommand(() => { /* TODO: Implement quick import */ });
            
            // Requirements commands
            ImportAdditionalCommand = new RelayCommand(() => { /* TODO: Implement import additional */ });
            
            // Initialize missing commands
            CloseProjectCommand = new RelayCommand(() => { /* TODO: Implement close project */ });
            BatchAnalyzeCommand = new RelayCommand(() => { /* TODO: Implement batch analyze */ });
            AnalyzeUnanalyzedCommand = new RelayCommand(() => { /* TODO: Implement analyze unanalyzed */ });
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
            HierarchicalMenuItems.Clear();
            
            // Level 1: Test Case Generator (Primary header)
            var testCaseGenerator = MenuHierarchyItem.CreateSection("Test Case Generator", 1, true,
                // Level 2: Project (Secondary header)
                MenuHierarchyItem.CreateSection("Project", 2, true,
                    CreateActionWithId("project.new", "🗂️ New Project", "🗂️", NewProjectCommand),
                    CreateActionWithId("project.quickimport", "⚡ Quick Import (Legacy)", "⚡", QuickImportCommand),
                    CreateActionWithId("project.open", "📂 Open Project", "📂", OpenProjectCommand),
                    CreateActionWithId("project.save", "💾 Save Project", "💾", SaveProjectCommand, false), // Disabled by default
                    CreateActionWithId("project.close", "❌ Close Project", "❌", CloseProjectCommand, false) // Disabled by default
                ),
                
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
                CreateNonExpandableWithId("testcase.generator", "Test Case Generator", 2),
                CreateActionWithId("demo.state", "🧪 Demo State Management", "🧪", DemoStateManagementCommand)
            );
            
            // Level 1: Test Flow Generator (Primary header)
            var testFlowGenerator = MenuHierarchyItem.CreateSection("Test Flow Generator", 1, true,
                // Add test flow items here when needed
                MenuHierarchyItem.CreateSection("Testing", 2, true,
                    CreateActionWithId("testing.option1", "🧪 Test Option 1", "🧪", null, false),
                    CreateActionWithId("testing.option2", "🔬 Test Option 2", "🔬", null, false)
                )
            );
            
            HierarchicalMenuItems.Add(testCaseGenerator);
            HierarchicalMenuItems.Add(testFlowGenerator);
            
            // Subscribe to state change events
            SubscribeToStateEvents();
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
            return item;
        }
        
        /// <summary>
        /// Subscribe to mediator events for state management
        /// </summary>
        private void SubscribeToStateEvents()
        {
            // Subscribe to global state changes that affect multiple menu items
            // Example: When project loads, enable project-related menu items
            
            // TODO: Connect to your domain mediators
            // _workspaceManagementMediator?.Subscribe<WorkspaceEvents.ProjectLoaded>(OnProjectLoaded);
            // _testCaseGenerationMediator?.Subscribe<TestCaseEvents.RequirementsLoaded>(OnRequirementsLoaded);
        }
        
        /// <summary>
        /// Example: Handle project loaded state
        /// </summary>
        private void OnProjectLoaded(/*ProjectLoadedEvent evt*/)
        {
            UpdateMenuItemState("project.save", isEnabled: true);
            UpdateMenuItemState("project.close", isEnabled: true);
            UpdateMenuItemState("requirement.import", isEnabled: true);
        }
        
        /// <summary>
        /// Example: Handle requirements analysis state
        /// </summary>
        private void OnRequirementsLoaded(/*RequirementsLoadedEvent evt*/)
        {
            UpdateMenuItemState("analysis.batch", isEnabled: true);
            UpdateMenuItemState("analysis.unanalyzed", isEnabled: true, badge: "5"); // Show count of unanalyzed
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
                ("project.close", true, null, null, null),      // Enable close
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