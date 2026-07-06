using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Net.Http;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Views.Dialogs;
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
        private readonly IRequirementsMediator _requirementsMediator;
        private readonly TestCaseAnythingLLMService _testCaseAnythingLLMService;
        private readonly JamaConnectService _jamaConnectService;
        private readonly IRequirementService _requirementService;
        private readonly IJamaTestCaseConversionService _jamaTestCaseConversionService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly ISettingsDialogService _settingsDialogService;
        private readonly ILogger<SideMenuViewModel> _logger;
        
        // AnythingLLM status tracking - use ObservableProperty for automatic command updates  
        [ObservableProperty]
        private bool _isAnythingLLMReady = false;
        
        // Requirements state tracking - use ObservableProperty for automatic command updates
        [ObservableProperty] 
        private bool hasRequirements = false;
        
        // Workspace dirty state - indicates unsaved changes
        [ObservableProperty]
        private bool hasUnsavedChanges = false;

        // AnythingLLM status text for Test Case Generator section
        [ObservableProperty]
        private string anythingLLMStatusText = "AnythingLLM not detected";
        
        // Analysis tab state tracking for context-sensitive menu visibility
        [ObservableProperty]
        private bool isAnalysisTabActive = false;

        // Project state tracking
        [ObservableProperty]
        private bool isProjectLoaded = false;

        [ObservableProperty]
        private string? selectedSection;

        [ObservableProperty]
        private ObservableCollection<MenuItemViewModel> menuItems = new();
        
        // Domain-driven side menu section (not tied to TestCaseGenerator)
        [ObservableProperty]
        private MenuSection? sideMenuSection = new MenuSection 
        { 
            Id = "loading", 
            Text = "Loading...", 
            Icon = "", 
            Items = new ObservableCollection<MenuContentItem>() 
        };
        
        // === DATA-DRIVEN MENU SYSTEM ===
        // New data-driven approach that replaces hardcoded XAML

        // Project Management Commands
        public ICommand TestClickCommand { get; private set; } = null!;
        public ICommand StartupNavigationCommand { get; private set; } = null!;

        // Availability properties
        [ObservableProperty]
        private bool isAnythingLLMAvailable = true;
        
        // === MISSING PROPERTIES FOR UI BINDING ===
        // These properties are bound to in XAML but were missing from the ViewModel
        
        [ObservableProperty]
        private string? sapStatus = "Systems ATE APP v2.0";
        
        [ObservableProperty]
        private string? selectedMenuSection;
        
        [ObservableProperty]
        private ObservableCollection<object> testFlowSteps = new();
        
        [ObservableProperty]
        private object? selectedStep;
        
        [ObservableProperty]
        private bool autoExportForChatGpt = false;

        public SideMenuViewModel(INewProjectMediator newProjectMediator, IOpenProjectMediator openProjectMediator, INavigationMediator navigationMediator, ITestCaseGenerationMediator testCaseGenerationMediator, IRequirementsMediator requirementsMediator, TestCaseAnythingLLMService testCaseAnythingLLMService, JamaConnectService jamaConnectService, IRequirementService requirementService, IJamaTestCaseConversionService jamaTestCaseConversionService, IUserSettingsService userSettingsService, ISettingsDialogService settingsDialogService, ILogger<SideMenuViewModel> logger)
        {
            //// ("*** SideMenuViewModel constructor called! ***");
            //// ("*** SideMenuViewModel constructor called! ***");
            
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));
            _testCaseAnythingLLMService = testCaseAnythingLLMService ?? throw new ArgumentNullException(nameof(testCaseAnythingLLMService));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _jamaTestCaseConversionService = jamaTestCaseConversionService ?? throw new ArgumentNullException(nameof(jamaTestCaseConversionService));
            _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
            _settingsDialogService = settingsDialogService ?? throw new ArgumentNullException(nameof(settingsDialogService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            //// ("*** SideMenuViewModel constructor: Dependencies resolved successfully ***");
            
            try
            {
                // Subscribe to AnythingLLM status updates
                AnythingLLMMediator.StatusUpdated += OnAnythingLLMStatusUpdated;
                
                // Subscribe to requirements state changes for command availability
                SetupRequirementsEventSubscriptions();
                
                //// ("*** SideMenuViewModel constructor: About to initialize commands ***");
                InitializeCommands();
                
                //// ("*** SideMenuViewModel constructor: About to initialize menu items ***");
                InitializeMenuItems();
                
                //// ("*** SideMenuViewModel constructor: About to initialize side menu ***");
                InitializeSideMenu();
                
                //// ("*** SideMenuViewModel constructor: Initialization completed ***");
            }
            catch (Exception)
            {
                //// ("*** SideMenuViewModel constructor: ERROR during initialization ***");
                throw;
            }
            // Removed TestCaseGenerator menu initialization - using Requirements domain directly
            
            // Request current status in case it was already set before we subscribed
            AnythingLLMMediator.RequestCurrentStatus();
        }

        private void InitializeCommands()
        {
            TestClickCommand = new RelayCommand(() => System.Windows.MessageBox.Show("Test button clicked!", "Data-Driven Test"));
            StartupNavigationCommand = new RelayCommand(NavigateToStartup);
        }

        private void NavigateToStartup()
        {
// ("*** SideMenuViewModel.NavigateToStartup called! ***");
// ("*** SideMenuViewModel.NavigateToStartup called! ***");
            
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
        
        private void InitializeMenuItems()
        {
            MenuItems.Add(new MenuItemViewModel { Id = "Project", Title = "Workshop", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "requirements", Title = "Requirements", Badge = "", HasFileMenu = true });
            MenuItems.Add(new MenuItemViewModel { Id = "TestCases", Title = "Test Cases", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "TestFlow", Title = "Test Flow", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "Import", Title = "Import", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "NewProject", Title = "Create a Requirements Workshop", Badge = "" });
        }
        
        private void InitializeSideMenu()
        {
            // Create domain-independent side menu using existing data-driven menu infrastructure.
            // The dashboard now owns the action surface, so the side menu only keeps section navigation.
            SideMenuSection = new MenuSection
            {
                Id = "application-menu",
                Text = "Application Menu",
                Icon = "",
                IsExpanded = true, // Make sure the section itself is expanded
                Items = new ObservableCollection<MenuContentItem>
                {
                        new MenuAction { Id = "startup", Text = "Dashboard", Icon = "🏠", Command = StartupNavigationCommand }
                }
            };
        }
        
        /// <summary>
        /// Setup event subscriptions for requirements state changes
        /// </summary>
        private void SetupRequirementsEventSubscriptions()
        {
            // Subscribe to TestCaseGeneration domain events (legacy compatibility)
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnTestCaseGenerationRequirementsImported);
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.AdditionalRequirementsImported>(OnAdditionalRequirementsImported);
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnTestCaseGenerationRequirementsCollectionChanged);
            _testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.SupportViewChanged>(OnSupportViewChanged);
            
            // Subscribe to Requirements domain events (new independent domain)
            _requirementsMediator.Subscribe<RequirementsEvents.RequirementsImported>(OnRequirementsImported);
            _requirementsMediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            
            // Subscribe to OpenProject domain events (for project opening)
            _openProjectMediator.Subscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
            
            // Subscribe to NewProject domain events (for workspace state)
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectCreated>(OnProjectCreated);
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectClosed>(OnProjectClosed);
            _newProjectMediator.Subscribe<NewProjectEvents.WorkspaceModified>(OnWorkspaceModified);
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectSaved>(OnProjectSaved);
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
        /// Handle project created from NewProject domain
        /// </summary>
        private void OnProjectCreated(NewProjectEvents.ProjectCreated evt)
        {
            IsProjectLoaded = true; // Track that a project is now loaded
            HasRequirements = evt.Workspace?.Requirements?.Count > 0;
            HasUnsavedChanges = false; // Newly created project, no unsaved changes yet
            _logger.LogInformation("[SideMenuVM] Project created, IsProjectLoaded=true, HasRequirements set to {HasReq} ({Count} requirements)", 
                HasRequirements, evt.Workspace?.Requirements?.Count ?? 0);
        }
        
        /// <summary>
        /// Handle project opened from OpenProject domain
        /// </summary>
        private void OnProjectOpened(OpenProjectEvents.ProjectOpened evt)
        {
            IsProjectLoaded = true; // Track that a project is now loaded
            HasRequirements = evt.Workspace?.Requirements?.Count > 0;
            HasUnsavedChanges = false; // Fresh project load, no unsaved changes
            _logger.LogInformation("[SideMenuVM] Project opened, IsProjectLoaded=true, HasRequirements set to {HasReq} ({Count} requirements)", 
                HasRequirements, evt.Workspace?.Requirements?.Count ?? 0);
        }

        /// <summary>
        /// Handle project closed state
        /// </summary>
        private void OnProjectClosed(NewProjectEvents.ProjectClosed evt)
        {
            IsProjectLoaded = false; // Track that no project is loaded
            HasRequirements = false; // No requirements when project is closed
            HasUnsavedChanges = false; // No unsaved changes when project is closed
            _logger.LogInformation("[SideMenuVM] Project closed, IsProjectLoaded=false");
        }
        
        /// <summary>
        /// Handle workspace modifications (data changed, needs save)
        /// </summary>
        private void OnWorkspaceModified(NewProjectEvents.WorkspaceModified evt)
        {
            HasUnsavedChanges = true;
            _logger.LogInformation("[SideMenuVM] Workspace modified: {Reason}", evt.Reason);
        }
        
        /// <summary>
        /// Handle project saved (clear dirty flag)
        /// </summary>
        private void OnProjectSaved(NewProjectEvents.ProjectSaved evt)
        {
            HasUnsavedChanges = false;
            _logger.LogInformation("[SideMenuVM] Project saved, unsaved changes cleared");
        }
        
        /// <summary>
        /// Handle requirements imported events from TestCaseGeneration domain
        /// </summary>
        private void OnTestCaseGenerationRequirementsImported(TestCaseGenerationEvents.RequirementsImported evt)
        {
            HasRequirements = evt.Requirements?.Count > 0; // ObservableProperty automatically triggers command updates
        }
        
        /// <summary>
        /// Handle requirements imported events from Requirements domain
        /// </summary>
        private void OnRequirementsImported(RequirementsEvents.RequirementsImported evt)
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
        /// Handle requirements collection changes from TestCaseGeneration domain (add/remove/clear)
        /// </summary>
        private void OnTestCaseGenerationRequirementsCollectionChanged(TestCaseGenerationEvents.RequirementsCollectionChanged evt)
        {
            HasRequirements = evt.NewCount > 0; // ObservableProperty automatically triggers command updates
        }
        
        /// <summary>
        /// Handle requirements collection changes from Requirements domain (add/remove/clear)
        /// </summary>
        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged evt)
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
            // No command notifications required after pruning the legacy action surface.
        }

        /// <summary>
        /// Called when IsProjectLoaded changes - notify commands that depend on it
        /// </summary>
        partial void OnIsProjectLoadedChanged(bool value)
        {
            // No command notifications required after pruning the legacy action surface.
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
// ($"Publishing global state change: {description}");
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
        /// Export FIRST test case only to Jama Connect (for debugging)
        /// </summary>
        private async Task ExportAllToJamaAsync()
        {
            try
            {
                _logger?.LogInformation("Starting SINGLE test case import to Jama Connect for debugging...");

                var requirements = _requirementsMediator.Requirements?.ToList() ?? new List<Requirement>();

                var requirementsWithSavedTests = requirements
                    .Where(r => r.GeneratedTestCases?.Any() == true)
                    .ToList();

                Requirement? firstRequirement = null;
                string testCaseSource = string.Empty;

                if (requirementsWithSavedTests.Any())
                {
                    firstRequirement = requirementsWithSavedTests.First();
                    testCaseSource = $"saved test case from requirement '{firstRequirement.Item}' ({firstRequirement.GeneratedTestCases?.Count} saved test cases)";
                }

                // Check if we have any test cases to export
                if (firstRequirement == null)
                {
                    MessageBox.Show(
                        "No test cases found to export.\n\n" +
                        "Only explicit saved/generated test cases are exportable.\n\n" +
                        "Please create or generate test cases in the Test Case Creation section, then save them before export.",
                        "No Test Cases Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _logger?.LogInformation("Found first test case: {Source}", testCaseSource);

                // Use the existing configured Jama service (credentials from environment variables)
                var jamaService = _jamaConnectService;
                
                // Test connection with enhanced logging
                _logger?.LogInformation("Testing Jama connection...");
                var (connectionSuccess, connectionMessage) = await jamaService.TestConnectionAsync();
                if (!connectionSuccess)
                {
                    _logger?.LogError("Jama connection failed: {Message}", connectionMessage);
                    MessageBox.Show(
                        $"❌ Failed to connect to Jama:\n\n{connectionMessage}\n\n" +
                        "Please check your Jama configuration:\n" +
                        "• JAMA_BASE_URL environment variable\n" +
                        "• JAMA_CLIENT_ID and JAMA_CLIENT_SECRET\n" +
                        "• Network connectivity to Jama instance",
                        "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _logger?.LogInformation("Jama connection successful");

                // Get projects with enhanced logging
                _logger?.LogInformation("Fetching Jama projects...");
                var projects = await jamaService.GetProjectsAsync();
                if (!projects?.Any() == true)
                {
                    _logger?.LogWarning("No Jama projects found or accessible");
                    MessageBox.Show(
                        "No Jama projects found.\n\n" +
                        "Please ensure you have access to at least one project.",
                        "No Projects Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _logger?.LogInformation("Found {ProjectCount} Jama projects", projects.Count);

                // Show project selection dialog
                var projectDialog = new JamaProjectSelectionDialog(projects);
                if (projectDialog.ShowDialog() != true || projectDialog.SelectedProject == null)
                    return;

                var selectedProject = projectDialog.SelectedProject;
                _logger?.LogInformation("Selected Jama project: {ProjectName} (ID: {ProjectId})", 
                    selectedProject.ProjectKey, selectedProject.Id);

                // Convert ONLY the first test case to Jama format
                var jamaCases = _jamaTestCaseConversionService.ConvertSingleTestCaseToJamaFormat(firstRequirement);
                
                if (!jamaCases.Any())
                {
                    _logger?.LogWarning("Failed to convert test case to Jama format for requirement: {RequirementId}", 
                        firstRequirement.GlobalId ?? firstRequirement.Item ?? "Unknown");
                    MessageBox.Show(
                        "Failed to convert test case to Jama format.\n\n" +
                        $"Source: {testCaseSource}\n\n" +
                        "Please check the application logs for more details.",
                        "Conversion Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                _logger?.LogInformation("Converted single test case for import. Source: {Source}", testCaseSource);

                // Import SINGLE test case with component-aware placement
                _logger?.LogInformation("Starting component-aware Jama import for single test case...");
                var (importSuccess, importMessage, createdId) = await jamaService.ImportTestCaseFromRequirementAsync(
                    selectedProject.Id, jamaCases.First(), firstRequirement);

                if (importSuccess && createdId.HasValue)
                {
                    var jamaBaseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL") ?? "your Jama instance";
                    var successMessage = $"✅ SINGLE Test Case Imported Successfully! (Component-Aware Mode)\n\n" +
                        $"🎯 Project: {selectedProject.ProjectKey}\n" +
                        $"📋 Source: {testCaseSource}\n" +
                        $"📊 Import Summary:\n{importMessage}\n\n" +
                        $"✨ Created Test Case ID: {createdId}\n\n" +
                        $"🌐 View your test case in Jama Connect:\n{jamaBaseUrl}";

                    _logger?.LogInformation("Successfully imported SINGLE test case to Jama project {Project}: {Id}. Source: {Source}", 
                        selectedProject.ProjectKey, createdId, testCaseSource);

                    MessageBox.Show(successMessage, "Component-Aware Test Case Import Successful", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var errorMessage = $"❌ SINGLE Test Case Import Failed\n\n" +
                        $"Project: {selectedProject.ProjectKey}\n" +
                        $"Source: {testCaseSource}\n\n" +
                        $"Error Details:\n{importMessage}";
                    
                    _logger?.LogError("Failed to import SINGLE test case to Jama: {Error}. Source: {Source}", importMessage, testCaseSource);
                    
                    MessageBox.Show(errorMessage, "Single Test Case Import Failed", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error during SINGLE test case Jama import");
                MessageBox.Show(
                    $"❌ Unexpected Error (Single Test Case Import)\n\n" +
                    $"An error occurred while importing to Jama:\n{ex.Message}\n\n" +
                    "Please check the application logs for more details.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Convert SINGLE requirement's first test case to Jama format (for debugging)
        /// </summary>
        // Note: Test case conversion methods have been moved to IJamaTestCaseConversionService
        // to follow architectural guidelines (separation of concerns, single responsibility)
        // Methods moved:
        // - ConvertSingleTestCaseToJamaFormat()
        // - ConvertAllTestCasesToJamaFormat()
        // - ConvertAIGeneratedTestCase()
        // - ConvertSavedTestCase()  
        // - ParseTestStepsFromOutput()
        // - ExtractDescriptionFromOutput()
        
        /// <summary>
        /// Test importing requirements from Jama
        /// </summary>
        private async Task TestImportRequirementsAsync(int projectId)
        {
            try
            {
                _logger?.LogInformation("Testing requirement import from Jama project {ProjectId}", projectId);
                
                var jamaItems = await _jamaConnectService.GetRequirementsAsync(projectId);
                var requirements = await _jamaConnectService.ConvertToRequirementsAsync(jamaItems);
                
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

        #endregion
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
