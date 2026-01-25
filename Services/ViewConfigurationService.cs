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
        private readonly ITestCaseCreationMediator _testCaseCreationMediator;
        
        // Cached view models - created once and reused
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel? _notificationWorkspace;
        private object? _testCaseCreationContent;
        
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
            ITestCaseGenerationMediator testCaseGenerationMediator,
            ITestCaseCreationMediator testCaseCreationMediator)
        {
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _testCaseCreationMediator = testCaseCreationMediator ?? throw new ArgumentNullException(nameof(testCaseCreationMediator));
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
                
                // TestCaseCreation domain - fixed case mismatch
                "testcasecreation" or "test case creation" or "TestCaseCreation" => CreateTestCaseCreationConfiguration(context),
                
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
                var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
                var sharedNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Verify ViewModels were created
                if (projectMainVM == null) throw new InvalidOperationException("Project_MainViewModel not resolved");
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not resolved");
                if (sharedHeaderVM == null) throw new InvalidOperationException("TestCaseGenerator_HeaderVM not resolved");
                if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not resolved");
                if (sharedNotificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not resolved");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Project ViewModels created successfully");

                // 🔍 DEBUG: Check if TestCaseGenerationMediator has requirements when in Project mode
                Console.WriteLine($"🔍 ViewConfigurationService (Project mode): TestCaseGenerationMediator.Requirements.Count = {_testCaseGenerationMediator?.Requirements?.Count ?? 0}");

                // Return ViewModels directly (same pattern as Friday working version - shared title/header)
                return new ViewConfiguration(
                    sectionName: "Project",
                    titleViewModel: sharedTitleVM,               // Use shared TestCaseGeneration title
                    headerViewModel: sharedHeaderVM,             // Use shared TestCaseGeneration header
                    contentViewModel: projectMainVM,             // Return ViewModel directly
                    navigationViewModel: sharedNavigationVM,     // Use shared navigation for consistent UX across working domains
                    notificationViewModel: sharedNotificationVM, // Use shared notification with LLM status
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
            
            // Resolve all ViewModels from DI container (use Requirements-specific header for requirement details)
            var requirementsHeaderVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>();
            
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
            
            var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
            var sharedNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (requirementsHeaderVM == null) throw new InvalidOperationException("Requirements_HeaderViewModel not registered in DI container");
            if (mainVM == null) 
            {
                var viewType = isJamaImport ? "JamaRequirementsMainViewModel" : "Requirements_MainViewModel";
                throw new InvalidOperationException($"{viewType} not registered in DI container");
            }
            if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
            if (sharedNotificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            // Return ViewModels directly - DataTemplates automatically render corresponding Views
            // Note: Update title to show project name when switching to Requirements
            var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
            if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered (used for Requirements title)");
            
            // Update title to show current project name when in Requirements mode
            UpdateTitleForRequirementsMode(titleVM);

            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: titleVM,         // Shared TestCaseGeneration title
                headerViewModel: requirementsHeaderVM,       // Use Requirements-specific header to show requirement details
                contentViewModel: mainVM,        // ViewModel → DataTemplate renders appropriate Requirements view
                navigationViewModel: sharedNavigationVM, // Shared navigation ViewModel for consistent UX across working domains
                notificationViewModel: sharedNotificationVM, // Use shared TestCaseGenerator notification (same as OpenProject_Mode)
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

                // ✅ Use same title view as TestCaseGenerator for consistency
                var titleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                
                // ✅ Use same notification view as TestCaseGenerator for consistency
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // ✅ Use shared navigation ViewModel for consistency across working domains
                var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();

                var config = new ViewConfiguration(
                    sectionName: "TestCaseCreation",
                    titleViewModel: titleVM,
                    headerViewModel: _workspaceHeader,
                    contentViewModel: _testCaseCreationContent,
                    navigationViewModel: navigationVM,
                    notificationViewModel: notificationVM,
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
            var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_HeaderVM>();
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels.TestCaseGeneratorMode_MainVM>();
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
            if (headerVM == null) throw new InvalidOperationException("TestCaseGenerator_HeaderVM not registered in DI container");
            if (mainVM == null) throw new InvalidOperationException("TestCaseGeneratorMode_MainVM not registered in DI container");
            if (navigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All TestCaseGenerator ViewModels resolved successfully");

            // Return ViewModels directly - DataTemplates automatically render corresponding Views
            return new ViewConfiguration(
                sectionName: "TestCaseGenerator",
                titleViewModel: titleVM,         // ViewModel → DataTemplate renders TestCaseGenerator_TitleView
                headerViewModel: headerVM,       // ViewModel → DataTemplate renders TestCaseGenerator_HeaderView
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
                
                // ✅ PHASE 2: Convert to AI Guide standard - ViewModels + DataTemplates pattern
                // Resolve all ViewModels from DI container (use shared title same as Project_Mode)
                var sharedTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectHeaderViewModel>();
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
                if (headerVM == null) throw new InvalidOperationException("NewProjectHeaderViewModel not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("NewProjectWorkflowViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All NewProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "NewProject",
                    titleViewModel: sharedTitleVM,         // Use shared TestCaseGeneration title (same as Project_Mode)
                    headerViewModel: headerVM,       // ViewModel → DataTemplate renders NewProjectHeaderView  
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders NewProject_MainView
                    navigationViewModel: null,       // No navigation (blank, same as Project_Mode)
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
                // Resolve all ViewModels from DI container (use Requirements_HeaderViewModel to show requirement details)
                var sharedTitleVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_TitleVM>();
                var requirementsHeaderVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>();
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>();
                var sharedNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.NavigationViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (sharedTitleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleVM not registered in DI container");
                if (requirementsHeaderVM == null) throw new InvalidOperationException("Requirements_HeaderViewModel not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("OpenProjectWorkflowViewModel not registered in DI container");
                if (sharedNavigationVM == null) throw new InvalidOperationException("NavigationViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All OpenProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "OpenProject",
                    titleViewModel: sharedTitleVM,         // Use shared TestCaseGeneration title (same as Project_Mode and NewProject_Mode)
                    headerViewModel: requirementsHeaderVM,       // Use Requirements header to show requirement details when available
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders OpenProject_MainView
                    navigationViewModel: sharedNavigationVM, // Use shared Requirements navigation (same as Requirements_Mode)
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
