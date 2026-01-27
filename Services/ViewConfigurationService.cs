using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of view configuration service.
    /// Defines complete view configurations for each section and broadcasts them.
    /// 
    /// DOMAIN TERMINOLOGY:
    /// - Menu Item Domains (_Mode suffix): Handle what displays when specific menu items are clicked
    ///   Examples: TestCaseGenerator_Mode, Project_Mode, Requirements_Mode
    /// - Codebase Domains (no suffix): Broader implementation functionality 
    ///   Examples: TestCaseGeneration, WorkspaceManagement
    /// 
    /// CRITICAL DISTINCTION: TestCaseGenerator_Mode (menu item) vs TestCaseGeneration (codebase)
    /// </summary>
    public class ViewConfigurationService : IViewConfigurationService
    {
        private readonly INewProjectMediator _workspaceManagementMediator;
        private readonly IOpenProjectMediator _openProjectMediator;
        private readonly IRequirementsMediator _requirementsMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        
        // Cached view models - created once and reused
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel? _notificationWorkspace;
        
        public ViewConfiguration? CurrentConfiguration { get; private set; }

        /// <summary>
        /// Clears the current configuration. Called when workspace is unloaded to ensure
        /// fresh view routing on next project load.
        /// </summary>
        public void ClearCurrentConfiguration()
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Clearing current configuration on workspace unload");
            CurrentConfiguration = null;
        }

        public ViewConfigurationService(
            INewProjectMediator workspaceManagementMediator,
            IOpenProjectMediator openProjectMediator,
            IRequirementsMediator requirementsMediator,
            ITestCaseGenerationMediator testCaseGenerationMediator)
        {
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
        }

        public ViewConfiguration GetConfigurationForSection(string sectionName, object? context = null)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] GetConfigurationForSection called with: '{sectionName}'");
            
            return sectionName?.ToLowerInvariant() switch
            {
                // Startup domain - works correctly
                "startup" => CreateStartupConfiguration(context),
                
                // Project domain - fixed case mismatch
                "project" or "Project" => CreateProjectConfiguration(context),
                
                // Requirements domain - works correctly  
                "requirements" => CreateRequirementsConfiguration(context),
                
                // TestCaseGenerator domain - fixed case mismatch  
                "testcasegenerator" or "test case generator" or "TestCaseGenerator" => DebugAndCallTestCaseGenerator(context),
                
                // TestFlow domain
                "testflow" => CreateTestFlowConfiguration(context),
                
                // LLM Learning domain
                "llm learning" => CreateLLMLearningConfiguration(context),
                
                // Import domain
                "import" => CreateImportConfiguration(context),
                
                // NewProject domain - fixed case mismatch
                "newproject" or "new project" or "NewProject" => CreateNewProjectConfiguration(context),
                
                // OpenProject domain - fixed case mismatch  
                "openproject" or "open project" or "OpenProject" => CreateOpenProjectConfiguration(context),
                
                // Dummy domain - fixed case mismatch
                "dummy" or "Dummy" => CreateDummyConfiguration(context),
                
                _ => CreateDefaultConfiguration(context)
            };
        }

        public void ApplyConfiguration(string sectionName, object? context = null)
        {
            var configuration = GetConfigurationForSection(sectionName, context);
            CurrentConfiguration = configuration;
            
            // Configuration created but not published - ViewAreaCoordinator will handle publishing
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] Configuration created for: {sectionName}");
        }

        #region Configuration Creators

        private ViewConfiguration CreateStartupConfiguration(object? context)
        {
            // Use DI container to resolve ViewModels following AI Guide patterns
            var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_TitleViewModel>();
            var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_HeaderViewModel>();
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_MainViewModel>();
            var navVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NavigationViewModel>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NotificationViewModel>();
            
            if (titleVM == null) throw new InvalidOperationException("StartUp_TitleViewModel not resolved from DI container");
            if (headerVM == null) throw new InvalidOperationException("StartUp_HeaderViewModel not resolved from DI container");
            if (mainVM == null) throw new InvalidOperationException("StartUp_MainViewModel not resolved from DI container");
            if (navVM == null) throw new InvalidOperationException("StartUp_NavigationViewModel not resolved from DI container");
            if (notificationVM == null) throw new InvalidOperationException("StartUp_NotificationViewModel not resolved from DI container");
            
            return new ViewConfiguration(
                sectionName: "Startup",
                titleViewModel: titleVM,
                headerViewModel: headerVM,
                contentViewModel: mainVM,
                navigationViewModel: navVM,
                notificationViewModel: notificationVM,
                context: context
            );
        }

        private ViewConfiguration CreateProjectConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating Project configuration (workspace management)");
            
            try
            {
                if (App.ServiceProvider == null)
                {
                    throw new InvalidOperationException("App.ServiceProvider is null - DI container not initialized yet");
                }
                
                // Project mode: Static view (no ViewModel) with shared title/navigation
                var projectStaticView = new TestCaseEditorApp.MVVM.Views.ProjectStaticView();
                var sharedTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
                var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not resolved");
                if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not resolved");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not resolved");
                
                return new ViewConfiguration(
                    sectionName: "Project",
                    titleViewModel: sharedTitleVM,
                    headerViewModel: blankHeaderVM,
                    contentViewModel: projectStaticView,
                    navigationViewModel: sharedNavigationVM,
                    notificationViewModel: notificationVM,
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create Project configuration");
                
                return new ViewConfiguration(
                    sectionName: "Project (Error)",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Project Error: {ex.Message}"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel(""),
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Project Error: {ex.Message}"),
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateLLMLearningConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating LLM Learning configuration using proper ViewModels");
            
            try
            {
                // ✅ Use same title view as TestCaseGenerator for consistency
                var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var headerVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Learning Configuration");
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.ViewModels.LLMLearningViewModel>() 
                            ?? new TestCaseEditorApp.MVVM.ViewModels.LLMLearningViewModel();
                
                // ✅ Use shared navigation and notification like TestCaseGenerator
                var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                return new ViewConfiguration(
                    sectionName: "LLM Learning",
                    titleViewModel: titleVM,
                    headerViewModel: headerVM,
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders LLMLearningView
                    navigationViewModel: navigationVM, // Shared navigation ViewModel for consistent UX across working domains
                    notificationViewModel: notificationVM, // Shared notification with LLM status
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create LLMLearning configuration");
                
                return new ViewConfiguration(
                    sectionName: "LLM Learning (Error)",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"LLM Learning Error: {ex.Message}"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"LLM Learning Header Error: {ex.Message}"),
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"LLM Learning Main Error: {ex.Message}"),
                    navigationViewModel: null,
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateRequirementsConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating Requirements configuration");
            
            // Determine if this is a Jama import or document import
            var isJamaImport = _requirementsMediator?.IsJamaDataSource() == true;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] Using {(isJamaImport ? "Jama" : "document")} view configuration");
            
            // Select appropriate main view based on import source
            object? mainVM;
            if (isJamaImport)
            {
                // Use Jama-optimized view for structured content
                mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.JamaRequirementsMainViewModel>();
            }
            else
            {
                // Use traditional document-focused view  
                mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel>();
            }
            
            var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
            var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
            var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
            var sharedNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (mainVM == null) 
            {
                var viewType = isJamaImport ? "JamaRequirementsMainViewModel" : "Requirements_MainViewModel";
                throw new InvalidOperationException($"{viewType} not registered in DI container");
            }
            if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered (used for Requirements title)");
            if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
            if (sharedNotificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            // Update title to show current project name when in Requirements mode
            UpdateTitleForRequirementsMode(titleVM);

            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: titleVM,         // Shared TestCaseGeneration title
                headerViewModel: blankHeaderVM,  // Blank header (will be updated later)
                contentViewModel: mainVM,        // ViewModel → DataTemplate renders appropriate Requirements view
                navigationViewModel: sharedNavigationVM, // Shared navigation ViewModel for consistent UX across working domains
                notificationViewModel: sharedNotificationVM, // Shared notification
                context: context
            );
        }



        private ViewConfiguration CreateTestFlowConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            var sharedNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            if (sharedNotificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            return new ViewConfiguration(
                sectionName: "TestFlow",
                headerViewModel: GetWorkspaceHeader(),
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Test Flow Designer"),
                notificationViewModel: sharedNotificationVM, // SHARED: Same notification area for all domains
                context: context
            );
        }

        private ViewConfiguration CreateImportConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            var sharedNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            if (sharedNotificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            return new ViewConfiguration(
                sectionName: "Import",
                headerViewModel: _workspaceHeader,
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Import Requirements"),
                notificationViewModel: sharedNotificationVM, // SHARED: Same notification area for all domains
                context: context
            );
        }

        private ViewConfiguration DebugAndCallTestCaseGenerator(object? context)
        {
            Console.WriteLine($"[DEBUG] TestCaseGenerator called with context: {context}");
            Console.WriteLine($"[DEBUG] Creating TestCaseGenerator configuration...");
            
            File.AppendAllText(@"c:\temp\navigation-debug.log", 
                $"[{DateTime.Now:HH:mm:ss}] DebugAndCallTestCaseGenerator: Creating TestCaseGenerator configuration with context: {context}\n");
                
            var config = CreateTestCaseGeneratorConfiguration(context);
            Console.WriteLine($"[DEBUG] TestCaseGenerator config created.");
            Console.WriteLine($"[DEBUG] Config type: {config?.GetType().Name}");
            
            File.AppendAllText(@"c:\temp\navigation-debug.log", 
                $"[{DateTime.Now:HH:mm:ss}] DebugAndCallTestCaseGenerator: TestCaseGenerator config created - type: {config?.GetType().Name}\n");
                
            return config;
        }

        private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating TestCaseGenerator configuration using AI Guide standard pattern");
            
            // ✅ PHASE 2: Convert to AI Guide standard - ViewModels + DataTemplates pattern
            // Resolve all ViewModels from DI container (no manual UserControl creation)
            var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
            var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels.TestCaseGeneratorMode_MainVM>();
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
            if (mainVM == null) throw new InvalidOperationException("TestCaseGeneratorMode_MainVM not registered in DI container");
            if (navigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All TestCaseGenerator ViewModels resolved successfully");

            // Return ViewModels directly - DataTemplates automatically render corresponding Views
            return new ViewConfiguration(
                sectionName: "TestCaseGenerator",
                titleViewModel: titleVM,         // Shared title
                headerViewModel: blankHeaderVM,  // Blank header (will be updated later)
                contentViewModel: mainVM,        // ViewModel → DataTemplate renders TestCaseGeneratorMainView
                navigationViewModel: navigationVM, // Shared navigation ViewModel for consistent UX across working domains
                notificationViewModel: notificationVM, // SHARED: Same notification area for all domains
                context: context
            );
        }

        private ViewConfiguration CreateDummyConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating Dummy domain configuration");
            
            try
            {
                // Diagnostic: Check if ServiceProvider is available
                if (App.ServiceProvider == null)
                {
                    throw new InvalidOperationException("App.ServiceProvider is null - DI container not initialized yet");
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] ServiceProvider available, testing mediator resolution");
                
                // Test mediator resolution first
                var dummyMediator = App.ServiceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.Mediators.IDummyMediator>();
                if (dummyMediator == null)
                {
                    throw new InvalidOperationException("IDummyMediator not resolved - check DI registration");
                }
                
                // Check if mediator is marked as registered
                var isRegisteredProp = dummyMediator.GetType().GetProperty("IsRegistered");
                if (isRegisteredProp != null)
                {
                    var isRegistered = (bool)(isRegisteredProp.GetValue(dummyMediator) ?? false);
                    if (!isRegistered)
                    {
                        throw new InvalidOperationException("DummyMediator not marked as registered - timing issue with MarkAsRegistered call");
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] DummyMediator resolved and registered, testing ViewModel resolution");
                
                // Get Dummy domain ViewModels from DI container - updated naming convention
                var dummyMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_MainViewModel>();
                var dummyHeaderVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_HeaderViewModel>();
                var dummyNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_NavigationViewModel>();
                var dummyTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_TitleViewModel>();
                var dummyNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.Dummy_NotificationViewModel>();
                
                // Verify all ViewModels were created
                if (dummyMainVM == null) throw new InvalidOperationException("Dummy_MainViewModel not resolved");
                if (dummyHeaderVM == null) throw new InvalidOperationException("Dummy_HeaderViewModel not resolved");
                if (dummyNavigationVM == null) throw new InvalidOperationException("Dummy_NavigationViewModel not resolved");
                if (dummyTitleVM == null) throw new InvalidOperationException("Dummy_TitleViewModel not resolved");
                if (dummyNotificationVM == null) throw new InvalidOperationException("Dummy_NotificationViewModel not resolved");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All 5 Dummy ViewModels created successfully");

                // Return ViewModels directly (same pattern as StartUp domain) - no manual UserControl creation
                return new ViewConfiguration(
                    sectionName: "Dummy",
                    titleViewModel: dummyTitleVM, // ViewModel, not UserControl
                    headerViewModel: dummyHeaderVM, // ViewModel, not UserControl
                    contentViewModel: dummyMainVM, // ViewModel, not UserControl
                    navigationViewModel: dummyNavigationVM, // ViewModel, not UserControl
                    notificationViewModel: dummyNotificationVM, // Domain-specific notification (test/reference implementation)
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create Dummy domain configuration");
                
                // Fallback to placeholder
                return new ViewConfiguration(
                    sectionName: "Dummy Domain (Error)",
                    headerViewModel: null,
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Dummy Domain Error: {ex.Message}"),
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateNewProjectConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating NewProject configuration using AI Guide standard pattern");
            
            try
            {
                if (App.ServiceProvider == null)
                {
                    throw new InvalidOperationException("App.ServiceProvider is null - DI container not initialized yet");
                }
                
                // ✅ Use shared title/navigation (same as Requirements) with NewProject-specific header
                // Shared title for consistency across working domains
                var sharedTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectHeaderViewModel>();
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>();
                // Shared navigation ViewModel for consistent UX across working domains (same as Requirements)
                var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
                if (headerVM == null) throw new InvalidOperationException("NewProjectHeaderViewModel not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("NewProjectWorkflowViewModel not registered in DI container");
                if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All NewProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "NewProject",
                    titleViewModel: sharedTitleVM,         // Shared title (same as Requirements for consistency)
                    headerViewModel: headerVM,       // ViewModel → DataTemplate renders NewProjectHeaderView
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders NewProject_MainView
                    navigationViewModel: sharedNavigationVM,       // Shared navigation (same as Requirements for consistency)
                    notificationViewModel: notificationVM, // Shared notification workspace
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create New Project configuration");
                
                return new ViewConfiguration(
                    sectionName: "NewProject (Error)",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Error: {ex.Message}"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Header Error: {ex.Message}"),
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Main Error: {ex.Message}"),
                    navigationViewModel: null,
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateOpenProjectConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating OpenProject configuration using AI Guide standard pattern");
            
            try
            {
                // ✅ Use shared title/navigation (same as Requirements) with blank header
                // Shared title for consistency across working domains
                var sharedTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>();
                // Shared navigation ViewModel for consistent UX across working domains (same as Requirements)
                var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("OpenProjectWorkflowViewModel not registered in DI container");
                if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All OpenProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "OpenProject",
                    titleViewModel: sharedTitleVM,         // Shared title (same as Requirements for consistency)
                    headerViewModel: blankHeaderVM,       // Blank header (will be updated later)
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders OpenProject_MainView
                    navigationViewModel: sharedNavigationVM, // Shared navigation (same as Requirements for consistency)
                    notificationViewModel: notificationVM, // Shared notification workspace
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create OpenProject configuration");
                
                return new ViewConfiguration(
                    sectionName: "OpenProject (Error)",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"OpenProject Error: {ex.Message}"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"OpenProject Header Error: {ex.Message}"),
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"OpenProject Main Error: {ex.Message}"),
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateDefaultConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] CreateDefaultConfiguration called - falling back to startup configuration");
            // Use startup configuration as default for initial app state
            return CreateStartupConfiguration(context);
        }

        #endregion

        #region Helper Methods

        private void EnsureWorkspaceHeader()
        {
            if (_workspaceHeader == null)
            {
                _workspaceHeader = App.ServiceProvider?.GetService<WorkspaceHeaderViewModel>()
                    ?? throw new InvalidOperationException("WorkspaceHeaderViewModel not registered in DI container");
            }
            
            // Always update save status
            _workspaceHeader.UpdateSaveStatus(_workspaceManagementMediator);
        }

        private WorkspaceHeaderViewModel GetWorkspaceHeader()
        {
            EnsureWorkspaceHeader();
            return _workspaceHeader!;
        }

        private void EnsureTestCaseGeneratorHeader()
        {
            if (_testCaseGeneratorHeader == null)
            {
                // HeaderVM is now created directly by the mediator - no factory needed
                _testCaseGeneratorHeader = _testCaseGenerationMediator.HeaderViewModel 
                    ?? throw new InvalidOperationException("HeaderViewModel not initialized in TestCaseGenerationMediator");
            }
        }
        
        private TestCaseGenerator_TitleVM? EnsureTestCaseGeneratorTitle()
        {
            // TitleVM is created directly by the mediator
            return _testCaseGenerationMediator.TitleViewModel
                ?? throw new InvalidOperationException("TitleViewModel not initialized in TestCaseGenerationMediator");
        }

        private TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel EnsureNotificationWorkspace()
        {
            if (_notificationWorkspace == null)
            {
                // Get the notification ViewModel from the service provider
                _notificationWorkspace = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>()
                    ?? throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            }
            return _notificationWorkspace;
        }

        /// <summary>
        /// Update shared title ViewModel when switching to Requirements mode
        /// </summary>
        private void UpdateTitleForRequirementsMode(TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM titleVM)
        {
            try
            {
                // Get current workspace name from workspace management
                var workspaceInfo = _workspaceManagementMediator?.GetCurrentWorkspaceInfo();
                var projectName = workspaceInfo?.Name;
                
                if (!string.IsNullOrEmpty(projectName))
                {
                    titleVM.Title = $"Test Case Generator - {projectName}";
                }
                else
                {
                    titleVM.Title = "Test Case Generator - Requirements";
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] Updated title for Requirements mode: {titleVM.Title}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error($"[ViewConfigurationService] Error updating title for Requirements mode: {ex.Message}");
                // Fallback
                titleVM.Title = "Test Case Generator - Requirements";
            }
        }

        #endregion
    }
}
