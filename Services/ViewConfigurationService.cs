using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
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
        
        // Cached view models - created once and reused
        private WorkspaceHeaderViewModel? _workspaceHeader;
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
            IRequirementsMediator requirementsMediator)
        {
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));
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
                
                // TestCaseCreation domain (manual test case editing)
                "testcasecreation" or "test case creation" or "TestCaseCreation" => CreateTestCaseCreationConfiguration(context),
                
                // TestFlow domain
                "testflow" => CreateTestFlowConfiguration(context),
                
                // LLM Learning domain
                "llm learning" => CreateLLMLearningConfiguration(context),
                
                // LLM Test Case Generator domain
                "llmtestcasegenerator" or "llm test case generator" or "LLMTestCaseGenerator" => CreateLLMTestCaseGeneratorConfiguration(context),
                
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
                
                // OpenProject mode: Project workflow with null shared ViewModels (handled internally)
                var projectStaticView = new TestCaseEditorApp.MVVM.Views.ProjectStaticView();
                var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not resolved");
                
                return new ViewConfiguration(
                    sectionName: "Project",
                    titleViewModel: null, // Project mode handles title internally
                    headerViewModel: blankHeaderVM,
                    contentViewModel: projectStaticView,
                    navigationViewModel: null, // Project mode handles navigation internally
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
                // LLM Learning domain uses null ViewModels (handles internally like Requirements)
                var headerVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Learning Configuration");
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.ViewModels.LLMLearningViewModel>() 
                            ?? new TestCaseEditorApp.MVVM.ViewModels.LLMLearningViewModel();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                return new ViewConfiguration(
                    sectionName: "LLM Learning",
                    titleViewModel: null, // LLM Learning domain handles title internally
                    headerViewModel: headerVM,
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders LLMLearningView
                    navigationViewModel: null, // LLM Learning domain handles navigation internally
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
            System.Diagnostics.Debug.WriteLine("[ViewConfigurationService] *** CreateRequirementsConfiguration called! ***");
            try {
                System.IO.File.AppendAllText("debug_requirements.log", 
                    $"{DateTime.Now}: ViewConfigurationService: CreateRequirementsConfiguration called\n");
            } catch { /* ignore */ }
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating Requirements configuration with independent Requirements domain architecture");
            
            // Requirements domain is self-contained and uses its own ViewModels
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: About to resolve UnifiedRequirementsMainViewModel\n");
            } catch { /* ignore */ }
            var unifiedMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.UnifiedRequirementsMainViewModel>();
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: UnifiedRequirementsMainViewModel resolved: {unifiedMainVM != null}\n");
            } catch { /* ignore */ }
            
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: About to resolve Requirements_HeaderViewModel\n");
            } catch { /* ignore */ }
            var requirementsHeaderVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>();
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: Requirements_HeaderViewModel resolved: {requirementsHeaderVM != null}\n");
            } catch { /* ignore */ }
            
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: About to resolve Requirements_NavigationViewModel\n");
            } catch { /* ignore */ }
            var requirementsNavigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_NavigationViewModel>();
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: Requirements_NavigationViewModel resolved: {requirementsNavigationVM != null}\n");
            } catch { /* ignore */ }
            
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: About to resolve NotificationWorkspaceViewModel\n");
            } catch { /* ignore */ }
            var sharedNotificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            try {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: NotificationWorkspaceViewModel resolved: {sharedNotificationVM != null}\n");
            } catch { /* ignore */ }
            
            System.Diagnostics.Debug.WriteLine($"[ViewConfigurationService] *** Retrieved ViewModels: Main={unifiedMainVM != null}, Header={requirementsHeaderVM != null}, Navigation={requirementsNavigationVM != null}, Notification={sharedNotificationVM != null} ***");
            try {
                System.IO.File.AppendAllText("debug_requirements.log", 
                    $"{DateTime.Now}: ViewConfigurationService: Retrieved ViewModels - Main={unifiedMainVM != null}, Header={requirementsHeaderVM != null}, Navigation={requirementsNavigationVM != null}, Notification={sharedNotificationVM != null}\\n");
            } catch { /* ignore */ }
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] Retrieved UnifiedRequirementsMainViewModel instance {unifiedMainVM?.GetHashCode()} for workspace");
            
            // Fail-fast validation (AI Guide requirement)
            try {
                if (unifiedMainVM == null) 
                {
                    System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: ERROR: UnifiedRequirementsMainViewModel is NULL!\\n");
                    throw new InvalidOperationException("UnifiedRequirementsMainViewModel not registered in DI container");
                }
                if (requirementsHeaderVM == null) {
                    System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: ERROR: Requirements_HeaderViewModel is NULL!\\n");
                    throw new InvalidOperationException("Requirements_HeaderViewModel not registered in DI container");
                }
                if (requirementsNavigationVM == null) {
                    System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: ERROR: Requirements_NavigationViewModel is NULL!\\n");
                    throw new InvalidOperationException("Requirements_NavigationViewModel not registered in DI container");
                }
                if (sharedNotificationVM == null) {
                    System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: ERROR: NotificationWorkspaceViewModel is NULL!\\n");
                    throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                }
            } catch (Exception ex) {
                System.IO.File.AppendAllText("debug_requirements.log", $"{DateTime.Now}: EXCEPTION in validation: {ex.Message}\\n");
                throw;
            }

            System.Diagnostics.Debug.WriteLine("[ViewConfigurationService] *** Requirements ViewConfiguration created and returned! ***");
            try {
                System.IO.File.AppendAllText("debug_requirements.log", 
                    $"{DateTime.Now}: ViewConfigurationService: Requirements ViewConfiguration created\\n");
            } catch { /* ignore */ }
            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: null, // Requirements domain handles its own title internally
                headerViewModel: requirementsHeaderVM, // Requirements-specific header  
                contentViewModel: unifiedMainVM, // Unified ViewModel → DataTemplate renders appropriate Requirements view
                navigationViewModel: requirementsNavigationVM, // Requirements navigation for left panel
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

        private ViewConfiguration CreateTestCaseCreationConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating TestCaseCreation configuration");
            
            // Resolve ViewModels from DI container
            var titleVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Test Case Creation");
            var headerVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels.TestCaseCreationMainVM>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation
            if (mainVM == null) throw new InvalidOperationException("TestCaseCreationMainVM not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            // Use null for navigationViewModel - TestCaseCreation handles its own navigation internally
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] TestCaseCreation ViewModels resolved successfully");

            return new ViewConfiguration(
                sectionName: "TestCaseCreation",
                titleViewModel: titleVM,
                headerViewModel: headerVM,
                contentViewModel: mainVM,
                navigationViewModel: null,
                notificationViewModel: notificationVM,
                context: context
            );
        }

        private ViewConfiguration CreateLLMTestCaseGeneratorConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating LLMTestCaseGenerator configuration");
            
            // Resolve ViewModels from DI container
            var titleVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("LLM Test Case Generator");
            var headerVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels.LLMTestCaseGeneratorViewModel>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation
            if (mainVM == null) throw new InvalidOperationException("LLMTestCaseGeneratorViewModel not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            // Resolve navigation ViewModel from DI container
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels.TestCaseCreation_NavigationViewModel>();
            if (navigationVM == null) throw new InvalidOperationException("TestCaseCreation_NavigationViewModel not registered in DI container");
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] LLMTestCaseGenerator ViewModels resolved successfully");

            return new ViewConfiguration(
                sectionName: "LLMTestCaseGenerator",
                titleViewModel: titleVM,
                headerViewModel: headerVM,
                contentViewModel: mainVM,
                navigationViewModel: navigationVM,
                notificationViewModel: notificationVM,
                context: context
            );
        }

        private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating TestCaseGenerator configuration using AI Guide standard pattern");
            
            // TestCaseGenerator domain using legitimate ViewModels only (no deprecated TestCaseGeneration ViewModels)
            var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels.TestCaseGeneratorMode_MainVM>();
            var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
            
            // Fail-fast validation (AI Guide requirement)
            if (mainVM == null) throw new InvalidOperationException("TestCaseGeneratorMode_MainVM not registered in DI container");
            if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
            
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All TestCaseGenerator ViewModels resolved successfully");

            // Return ViewModels directly - DataTemplates automatically render corresponding Views
            return new ViewConfiguration(
                sectionName: "TestCaseGenerator",
                titleViewModel: null,         // TestCaseGenerator domain handles title internally
                headerViewModel: blankHeaderVM,  // Blank header (will be updated later)
                contentViewModel: mainVM,        // ViewModel → DataTemplate renders TestCaseGeneratorMainView
                navigationViewModel: null, // TestCaseGenerator domain handles navigation internally
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
                
                // NewProject domain using own ViewModels (no shared deprecated ViewModels)
                var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectHeaderViewModel>();
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (headerVM == null) throw new InvalidOperationException("NewProjectHeaderViewModel not registered in DI container");
                if (mainVM == null) throw new InvalidOperationException("NewProjectWorkflowViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All NewProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "NewProject",
                    titleViewModel: null,         // NewProject domain handles title internally
                    headerViewModel: headerVM,       // ViewModel → DataTemplate renders NewProjectHeaderView
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders NewProject_MainView
                    navigationViewModel: null,       // NewProject domain handles navigation internally
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
                // OpenProject domain using own ViewModels (no shared deprecated ViewModels)
                var blankHeaderVM = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("");
                var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>();
                var notificationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Notification.ViewModels.NotificationWorkspaceViewModel>();
                
                // Fail-fast validation (AI Guide requirement)
                if (mainVM == null) throw new InvalidOperationException("OpenProjectWorkflowViewModel not registered in DI container");
                if (notificationVM == null) throw new InvalidOperationException("NotificationWorkspaceViewModel not registered in DI container");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] All OpenProject ViewModels resolved successfully");

                // Return ViewModels directly - DataTemplates automatically render corresponding Views
                return new ViewConfiguration(
                    sectionName: "OpenProject",
                    titleViewModel: null,         // OpenProject domain handles title internally
                    headerViewModel: blankHeaderVM,       // Blank header (will be updated later)
                    contentViewModel: mainVM,        // ViewModel → DataTemplate renders OpenProject_MainView
                    navigationViewModel: null, // OpenProject domain handles navigation internally
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

        #endregion
    }
}
