using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;

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
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        private readonly ITestCaseCreationMediator _testCaseCreationMediator;
        
        // Cached view models - created once and reused
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel? _testCaseGeneratorNotification;
        private object? _projectContent;
        private object? _testCaseCreationContent;
        
        // Cached navigation views - critical for idempotency
        private object? _cachedRequirementsNavigationView;
        
        public ViewConfiguration? CurrentConfiguration { get; private set; }

        public ViewConfigurationService(
            INewProjectMediator workspaceManagementMediator,
            IOpenProjectMediator openProjectMediator,
            ITestCaseGenerationMediator testCaseGenerationMediator,
            ITestCaseCreationMediator testCaseCreationMediator)
        {
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _testCaseCreationMediator = testCaseCreationMediator ?? throw new ArgumentNullException(nameof(testCaseCreationMediator));
        }

        public ViewConfiguration GetConfigurationForSection(string sectionName, object? context = null)
        {
            Console.WriteLine($"*** ViewConfigurationService: GetConfigurationForSection called with '{sectionName}' ***");
            Console.WriteLine($"*** ViewConfigurationService: sectionName.ToLowerInvariant() = '{sectionName?.ToLowerInvariant()}' ***");
            
            // Write to log file for easier debugging
            File.AppendAllText(@"c:\temp\navigation-debug.log", 
                $"[{DateTime.Now:HH:mm:ss}] ViewConfigurationService: GetConfigurationForSection('{sectionName}') - lowercase: '{sectionName?.ToLowerInvariant()}'\n");
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] GetConfigurationForSection called with: '{sectionName}' (lowercase: '{sectionName?.ToLowerInvariant()}')");
            
            // Debug: Add console output to see what's happening
            System.Diagnostics.Debug.WriteLine($"*** ViewConfigurationService: GetConfigurationForSection('{sectionName}') ***");
            Console.WriteLine($"*** ViewConfigurationService: GetConfigurationForSection('{sectionName}') ***");
            Console.WriteLine($"*** Lowercase: '{sectionName?.ToLowerInvariant()}' ***");
            
            return sectionName?.ToLowerInvariant() switch
            {
                // Startup domain - works correctly
                "startup" => CreateStartupConfiguration(context),
                
                // Project domain - fix case mismatch
                "project" => CreateProjectConfiguration(context),
                
                // Requirements domain - works correctly  
                "requirements" => CreateRequirementsConfiguration(context),
                
                // TestCaseGenerator domain - fix case mismatch  
                "testcasegenerator" or "test case generator" => DebugAndCallTestCaseGenerator(context),
                
                // TestCaseCreation domain - fix case mismatch
                "testcasecreation" or "test case creation" => CreateTestCaseCreationConfiguration(context),
                
                // TestFlow domain
                "testflow" => CreateTestFlowConfiguration(context),
                
                // LLM Learning domain
                "llm learning" => CreateLLMLearningConfiguration(context),
                
                // Import domain
                "import" => CreateImportConfiguration(context),
                
                // NewProject domain - fix case mismatch
                "newproject" or "new project" => CreateNewProjectConfiguration(context),
                
                // OpenProject domain - fix case mismatch  
                "openproject" or "open project" => CreateOpenProjectConfiguration(context),
                
                // Dummy domain - fix case mismatch
                "dummy" => CreateDummyConfiguration(context),
                
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
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating Project configuration");
            
            try
            {
                if (App.ServiceProvider == null)
                {
                    throw new InvalidOperationException("App.ServiceProvider is null - DI container not initialized yet");
                }
                
                // Get Project domain ViewModels from DI container - use shared ViewModels from TestCaseGeneration
                var projectMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Project.ViewModels.Project_MainViewModel>();
                var sharedTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var sharedHeaderVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_HeaderVM>();
                
                // Verify ViewModels were created
                if (projectMainVM == null) throw new InvalidOperationException("Project_MainViewModel not resolved");
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not resolved");
                if (sharedHeaderVM == null) throw new InvalidOperationException("TestCaseGenerator_HeaderVM not resolved");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Project ViewModels created successfully");

                // Return ViewModels directly (same pattern as Friday working version - shared title/header)
                return new ViewConfiguration(
                    sectionName: "Project",
                    titleViewModel: sharedTitleVM,               // Use shared TestCaseGeneration title
                    headerViewModel: sharedHeaderVM,             // Use shared TestCaseGeneration header
                    contentViewModel: projectMainVM,             // Return ViewModel directly
                    navigationViewModel: null,                   // No specific navigation for Project
                    notificationViewModel: null,
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create Project configuration");
                
                return new ViewConfiguration(
                    sectionName: "Project (Error)",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Project Title Error: {ex.Message}"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Project Header Error: {ex.Message}"),
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Project Main Error: {ex.Message}"),
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateLLMLearningConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating LLM Learning configuration");
            
            // TODO: Create proper LLMLearning domain ViewModels when this feature is implemented
            // For now, return a simple placeholder configuration until the domain is built
            return new ViewConfiguration(
                sectionName: "LLM Learning",
                titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("LLM Learning"),
                headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("LLM Learning Header"),
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("LLM Learning functionality coming soon..."),
                navigationViewModel: null,
                notificationViewModel: null,
                context: context
            );
        }

        private ViewConfiguration CreateRequirementsConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating Requirements configuration using AI Guide standard pattern");
            
            // ✅ PHASE 2: Convert to AI Guide standard - ViewModels + DataTemplates pattern
            // Resolve all ViewModels from DI container (Requirements domain already properly implemented)
            var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>();
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel>();
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_NavigationViewModel>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_NotificationViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (headerVM == null) throw new InvalidOperationException("Requirements_HeaderViewModel not registered in DI container");
            if (mainVM == null) throw new InvalidOperationException("Requirements_MainViewModel not registered in DI container");
            if (navigationVM == null) throw new InvalidOperationException("Requirements_NavigationViewModel not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("Requirements_NotificationViewModel not registered in DI container");
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All Requirements ViewModels resolved successfully");

            // Return ViewModels directly - DataTemplates automatically render corresponding Views
            // Note: Requirements domain shares TestCaseGenerator title for consistency
            var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
            if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered (used for Requirements title)");

            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: titleVM,         // Shared TestCaseGeneration title
                headerViewModel: headerVM,       // ViewModel → DataTemplate renders Requirements_HeaderView
                contentViewModel: mainVM,        // ViewModel → DataTemplate renders Requirements_MainView
                navigationViewModel: navigationVM, // ViewModel → DataTemplate renders Requirements_NavigationView
                notificationViewModel: notificationVM, // ViewModel → DataTemplate renders Requirements_NotificationView
                context: context
            );
        }



        private ViewConfiguration CreateTestCaseCreationConfiguration(object? context)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating TestCaseCreation configuration");
                EnsureWorkspaceHeader();

                if (_testCaseCreationContent == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Getting TestCaseCreationMainVM from DI");
                    try
                    {
                        _testCaseCreationContent = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels.TestCaseCreationMainVM>();
                        if (_testCaseCreationContent == null)
                        {
                            throw new InvalidOperationException("TestCaseCreationMainVM not registered in DI container or ServiceProvider is null");
                        }
                        TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] TestCaseCreationMainVM created successfully");
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create TestCaseCreationMainVM");
                        throw;
                    }
                }

                var config = new ViewConfiguration(
                    sectionName: "TestCaseCreation",
                    headerViewModel: _workspaceHeader,
                    contentViewModel: _testCaseCreationContent,
                    notificationViewModel: new TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel(App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel>>()),
                    context: context
                );
                TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] TestCaseCreation ViewConfiguration created with content: {config.ContentViewModel?.GetType().Name}");
                return config;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Error creating TestCaseCreation configuration");
                throw; // Re-throw to see the error
            }
        }

        private ViewConfiguration CreateTestFlowConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "TestFlow",
                headerViewModel: _workspaceHeader,
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Test Flow"),
                notificationViewModel: new TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel(App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel>>()),
                context: context
            );
        }

        private ViewConfiguration CreateImportConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "Import",
                headerViewModel: _workspaceHeader,
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Import Requirements"),
                notificationViewModel: EnsureTestCaseGeneratorNotification(), // FIX: Use Test Case Generator notification for Requirements Import
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
            var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_HeaderVM>();
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels.TestCaseGeneratorMode_MainVM>();
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_NavigationVM>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
            if (headerVM == null) throw new InvalidOperationException("TestCaseGenerator_HeaderVM not registered in DI container");
            if (mainVM == null) throw new InvalidOperationException("TestCaseGeneratorMode_MainVM not registered in DI container");
            if (navigationVM == null) throw new InvalidOperationException("TestCaseGenerator_NavigationVM not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("TestCaseGeneratorNotificationViewModel not registered in DI container");
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All TestCaseGenerator ViewModels resolved successfully");

            // Return ViewModels directly - DataTemplates automatically render corresponding Views
            return new ViewConfiguration(
                sectionName: "TestCaseGenerator",
                titleViewModel: titleVM,         // ViewModel → DataTemplate renders TestCaseGenerator_TitleView
                headerViewModel: headerVM,       // ViewModel → DataTemplate renders TestCaseGenerator_HeaderView
                contentViewModel: mainVM,        // ViewModel → DataTemplate renders TestCaseGeneratorMainView
                navigationViewModel: navigationVM, // ViewModel → DataTemplate renders TestCaseGenerator_NavigationControl
                notificationViewModel: notificationVM, // ViewModel → DataTemplate renders TestCaseGeneratorNotificationView
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
                    sectionName: "Dummy Domain",
                    titleViewModel: dummyTitleVM,         // ViewModel, not UserControl
                    headerViewModel: dummyHeaderVM,       // ViewModel, not UserControl
                    contentViewModel: dummyMainVM,        // ViewModel, not UserControl
                    navigationViewModel: dummyNavigationVM, // ViewModel, not UserControl
                    notificationViewModel: dummyNotificationVM, // ViewModel, not UserControl
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
                
                // ✅ PHASE 2: Convert to AI Guide standard - ViewModels + DataTemplates pattern
                // Resolve all ViewModels from DI container (no PlaceholderViewModels)
                var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.DummyNewProjectTitleViewModel>();
                var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectHeaderViewModel>();
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>();
                var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.DummyNewProjectNavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (titleVM == null) throw new InvalidOperationException("DummyNewProjectTitleViewModel not registered in DI container");
                if (headerVM == null) throw new InvalidOperationException("NewProjectHeaderViewModel not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("NewProjectWorkflowViewModel not registered in DI container");
                if (navigationVM == null) throw new InvalidOperationException("DummyNewProjectNavigationViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("TestCaseGeneratorNotificationViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All NewProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "NewProject",
                    titleViewModel: titleVM,         // ViewModel → DataTemplate renders DummyNewProjectTitleView
                    headerViewModel: headerVM,       // ViewModel → DataTemplate renders NewProjectHeaderView  
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders NewProject_MainView
                    navigationViewModel: navigationVM, // ViewModel → DataTemplate renders DummyNewProjectNavigationView
                    notificationViewModel: notificationVM, // Shared TestCaseGenerator notification
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
                // ✅ PHASE 2: Convert to AI Guide standard - ViewModels + DataTemplates pattern
                // Resolve all ViewModels from DI container (no PlaceholderViewModels)
                var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProject_TitleViewModel>();
                var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProject_HeaderViewModel>();
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>();
                var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProject_NavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (titleVM == null) throw new InvalidOperationException("OpenProject_TitleViewModel not registered in DI container");
                if (headerVM == null) throw new InvalidOperationException("OpenProject_HeaderViewModel not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("OpenProjectWorkflowViewModel not registered in DI container");
                if (navigationVM == null) throw new InvalidOperationException("OpenProject_NavigationViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("TestCaseGeneratorNotificationViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All OpenProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "OpenProject",
                    titleViewModel: titleVM,         // ViewModel → DataTemplate renders OpenProject_TitleView
                    headerViewModel: headerVM,       // ViewModel → DataTemplate renders OpenProject_HeaderView  
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders OpenProject_MainView
                    navigationViewModel: navigationVM, // ViewModel → DataTemplate renders OpenProject_NavigationView
                    notificationViewModel: notificationVM, // Shared TestCaseGenerator notification
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

        private TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel EnsureTestCaseGeneratorNotification()
        {
            if (_testCaseGeneratorNotification == null)
            {
                // Get the notification ViewModel from the service provider
                _testCaseGeneratorNotification = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel>()
                    ?? throw new InvalidOperationException("TestCaseGeneratorNotificationViewModel not registered in DI container");
            }
            return _testCaseGeneratorNotification;
        }

        /// <summary>
        /// Ensure Requirements navigation view is cached and reused for idempotency
        /// </summary>
        private object EnsureRequirementsNavigationView()
        {
            if (_cachedRequirementsNavigationView == null)
            {
                // Create new navigation view and ViewModel
                var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_NavigationViewModel>();
                if (navigationVM == null)
                {
                    throw new InvalidOperationException("Requirements_NavigationViewModel not registered in DI container");
                }

                var navigationControl = new TestCaseEditorApp.MVVM.Domains.Requirements.Views.RequirementsNavigationView();
                navigationControl.DataContext = navigationVM;
                _cachedRequirementsNavigationView = navigationControl;
                
                System.Diagnostics.Debug.WriteLine($"[ViewConfigurationService] Created cached Requirements navigation view: {_cachedRequirementsNavigationView.GetHashCode()}, DataContext: {navigationControl.DataContext?.GetType().Name}");
                Console.WriteLine($"*** [ViewConfigurationService] Cached navigation view created with DataContext: {navigationControl.DataContext?.GetType().Name} ***");
            }
            else
            {
                // CRITICAL FIX: When reusing cached view, get a fresh ViewModel and ensure it's properly initialized
                var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_NavigationViewModel>();
                if (navigationVM == null)
                {
                    throw new InvalidOperationException("Requirements_NavigationViewModel not registered in DI container");
                }

                if (_cachedRequirementsNavigationView is System.Windows.FrameworkElement cachedElement)
                {
                    // Set the fresh ViewModel as DataContext
                    cachedElement.DataContext = navigationVM;
                    
                    // CRITICAL: Force the navigation ViewModel to refresh its data from the mediator
                    try
                    {
                        // Call the known RefreshRequirementsFromMediator method
                        var refreshMethod = navigationVM.GetType().GetMethod("RefreshRequirementsFromMediator", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (refreshMethod != null)
                        {
                            refreshMethod.Invoke(navigationVM, null);
                            System.Diagnostics.Debug.WriteLine($"[ViewConfigurationService] Successfully called RefreshRequirementsFromMediator on NavigationVM");
                            Console.WriteLine($"*** [ViewConfigurationService] RefreshRequirementsFromMediator called successfully ***");
                        }
                        else
                        {
                            // Fallback - check mediator state
                            var mediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.IRequirementsMediator>();
                            if (mediator != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ViewConfigurationService] Mediator available - Requirements count: {mediator.Requirements.Count}, Current: {mediator.CurrentRequirement?.Item ?? "null"}");
                                Console.WriteLine($"*** [ViewConfigurationService] Mediator has {mediator.Requirements.Count} requirements, Current: {mediator.CurrentRequirement?.Item ?? "null"} ***");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ViewConfigurationService] Error refreshing NavigationVM: {ex.Message}");
                        Console.WriteLine($"*** [ViewConfigurationService] Error refreshing NavigationVM: {ex.Message} ***");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[ViewConfigurationService] Re-set DataContext on cached view: {cachedElement.DataContext?.GetType().Name}");
                    Console.WriteLine($"*** [ViewConfigurationService] Fixed cached view DataContext: {cachedElement.DataContext?.GetType().Name} ***");
                }
            }
            return _cachedRequirementsNavigationView;
        }

        #endregion
    }
}
