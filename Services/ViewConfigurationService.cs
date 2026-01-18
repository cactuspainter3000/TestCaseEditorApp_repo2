using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of view configuration service.
    /// Defines complete view configurations for each section and broadcasts them.
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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[ViewConfigurationService] GetConfigurationForSection called with: '{sectionName}' (lowercase: '{sectionName?.ToLowerInvariant()}')");
            
            return sectionName?.ToLowerInvariant() switch
            {
                "startup" => CreateStartupConfiguration(context),
                "project" => CreateProjectConfiguration(context),
                "requirements" => CreateRequirementsConfiguration(context),
                "testcasegenerator" or "test case generator" => CreateTestCaseGeneratorConfiguration(context),
                "testcasecreation" or "test case creation" => CreateTestCaseCreationConfiguration(context),
                "testflow" => CreateTestFlowConfiguration(context),
                "llm learning" => CreateLLMLearningConfiguration(context),
                "import" => CreateImportConfiguration(context),
                "newproject" or "new project" => CreateNewProjectConfiguration(context),
                "openproject" or "open project" => CreateOpenProjectConfiguration(context),
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
                
                // Get Project domain ViewModels from DI container - return ViewModels directly
                var projectMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Project.ViewModels.Project_MainViewModel>();
                
                // Verify ViewModels were created
                if (projectMainVM == null) throw new InvalidOperationException("Project_MainViewModel not resolved");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Project ViewModels created successfully");

                // Return ViewModels directly (same pattern as New Project and Dummy domains)
                return new ViewConfiguration(
                    sectionName: "Project",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Project"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Project Header"),
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
            
            // For now, return a simple placeholder configuration
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
            // PHASE 3.1: Complete Requirements domain switch - use ALL Requirements ViewModels
            System.Diagnostics.Debug.WriteLine("*** CreateRequirementsConfiguration: Complete Requirements domain switch ***");
            Console.WriteLine("*** CreateRequirementsConfiguration: Complete Requirements domain switch ***");
            
            // Get Requirements domain ViewModels from DI container
            var headerVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_HeaderViewModel>();
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_MainViewModel>();
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels.Requirements_NavigationViewModel>();
            var notificationVM = EnsureTestCaseGeneratorNotification(); // Keep using TestCaseGeneration notification for now
            
            if (headerVM == null) throw new InvalidOperationException("Requirements_HeaderViewModel not registered in DI container");
            if (mainVM == null) throw new InvalidOperationException("Requirements_MainViewModel not registered in DI container");
            if (navigationVM == null) throw new InvalidOperationException("Requirements_NavigationViewModel not registered in DI container");
            
            System.Diagnostics.Debug.WriteLine($"*** All Requirements ViewModels resolved successfully ***");
            Console.WriteLine($"*** Requirements Domain: Header={headerVM.GetType().Name}, Main={mainVM.GetType().Name}, Navigation={navigationVM.GetType().Name} ***");
            
            // CRITICAL FIX: Ensure navigation ViewModel is properly initialized
            // Force refresh from mediator to ensure it has current data
            try
            {
                var refreshMethod = navigationVM.GetType().GetMethod("RefreshRequirementsFromMediator", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (refreshMethod != null)
                {
                    refreshMethod.Invoke(navigationVM, null);
                    System.Diagnostics.Debug.WriteLine($"*** Successfully refreshed navigation ViewModel data ***");
                    Console.WriteLine($"*** Successfully refreshed navigation ViewModel data ***");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"*** Error refreshing navigation ViewModel: {ex.Message} ***");
            }
            
            // Return complete Requirements domain configuration with ACTUAL ViewModels
            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: EnsureTestCaseGeneratorTitle(), // Keep using TestCaseGeneration title for now
                headerViewModel: headerVM,        // Requirements header
                contentViewModel: mainVM,         // Requirements main (with correct DataTemplate)
                navigationViewModel: navigationVM, // FIXED: Use actual ViewModel, not UserControl
                notificationViewModel: notificationVM,
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

        private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
        {
            EnsureTestCaseGeneratorHeader();

            // Get TestCaseGeneratorMainVM from DI and create the main view UserControl
            var mainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorMainVM>();
            object? mainContent = null;
            if (mainVM != null)
            {
                var mainControl = new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Views.TestCaseGeneratorMainView();
                mainControl.DataContext = mainVM;
                mainContent = mainControl;
            }
            else
            {
                mainContent = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Test Case Generator Main - ViewModel not found");
            }

            // Get TestCaseGenerator_NavigationVM from DI container
            var navigationVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_NavigationVM>();
            if (navigationVM == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] TestCaseGenerator_NavigationVM not resolved from DI");
            }

            // Create the actual UserControl and bind the ViewModel
            object? navigationContent = null;
            if (navigationVM != null)
            {
                var navigationControl = new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Views.TestCaseGenerator_NavigationControl();
                navigationControl.DataContext = navigationVM;
                navigationContent = navigationControl;
            }

            return new ViewConfiguration(
                sectionName: "TestCase",
                titleViewModel: EnsureTestCaseGeneratorTitle(),
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: mainContent, // Use the proper TestCaseGenerator main view
                navigationViewModel: navigationContent, // Use the actual UserControl, not just ViewModel
                notificationViewModel: EnsureTestCaseGeneratorNotification(),
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
            TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Creating New Project configuration");
            
            try
            {
                // Diagnostic: Check if ServiceProvider is available
                if (App.ServiceProvider == null)
                {
                    throw new InvalidOperationException("App.ServiceProvider is null - DI container not initialized yet");
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] ServiceProvider available, testing mediator resolution");
                
                // Test mediator resolution first
                var newProjectMediator = App.ServiceProvider.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.Mediators.INewProjectMediator>();
                if (newProjectMediator == null)
                {
                    throw new InvalidOperationException("INewProjectMediator not resolved - check DI registration");
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] NewProjectMediator resolved successfully, testing ViewModel resolution");
                
                // Get NewProject domain ViewModels from DI container - return ViewModels directly like Dummy domain
                var newProjectMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectWorkflowViewModel>();
                var newProjectHeaderVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.NewProject.ViewModels.NewProjectHeaderViewModel>();
                
                // Verify ViewModels were created
                if (newProjectMainVM == null) throw new InvalidOperationException("NewProjectWorkflowViewModel not resolved");
                if (newProjectHeaderVM == null) throw new InvalidOperationException("NewProjectHeaderViewModel not resolved");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] NewProject ViewModels created successfully");

                // Return ViewModels directly (same pattern as Dummy domain) - let WPF handle View creation
                return new ViewConfiguration(
                    sectionName: "New Project",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("New Project"), // Simple title
                    headerViewModel: newProjectHeaderVM,          // Return ViewModel directly
                    contentViewModel: newProjectMainVM,           // Return ViewModel directly  
                    navigationViewModel: null,                    // No specific navigation for NewProject
                    notificationViewModel: EnsureTestCaseGeneratorNotification(), // Reuse notification
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create New Project configuration");
                
                return new ViewConfiguration(
                    sectionName: "New Project (Error)",
                    titleViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Title Error: {ex.Message}"),
                    headerViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Header Error: {ex.Message}"),
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Main Error: {ex.Message}"),
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateOpenProjectConfiguration(object? context)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] CreateOpenProjectConfiguration called");
                
                // Only the main content differs between NewProject and OpenProject
                // Other workspaces remain the same - project title, header, notification
                
                // Get OpenProjectWorkflowViewModel from DI container
                TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] Attempting to resolve OpenProjectWorkflowViewModel from DI...");
                var openProjectMainVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.OpenProject.ViewModels.OpenProjectWorkflowViewModel>();
                object? mainContent = null;
                
                if (openProjectMainVM != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] OpenProjectWorkflowViewModel resolved successfully, creating view...");
                    // Create the OpenProject_MainView UserControl
                    var openProjectMainView = new TestCaseEditorApp.MVVM.Domains.OpenProject.Views.OpenProject_MainView();
                    openProjectMainView.DataContext = openProjectMainVM;
                    mainContent = openProjectMainView;
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] OpenProject_MainView created and DataContext set");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ViewConfigurationService] OpenProjectWorkflowViewModel is NULL - using placeholder");
                    mainContent = new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("OpenProjectWorkflowViewModel not found in DI container");
                }
                
                // Navigation should be null for OpenProject - no requirements navigation needed during project selection
                
                return new ViewConfiguration(
                    sectionName: "Open Project",
                    titleViewModel: EnsureTestCaseGeneratorTitle(),
                    headerViewModel: null,
                    contentViewModel: mainContent,
                    navigationViewModel: null, // No navigation needed for project selection
                    notificationViewModel: EnsureTestCaseGeneratorNotification(),
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create Open Project configuration");
                
                return new ViewConfiguration(
                    sectionName: "Open Project (Error)",
                    titleViewModel: EnsureTestCaseGeneratorTitle(),
                    headerViewModel: null,
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"Open Project Error: {ex.Message}"),
                    navigationViewModel: null, // No navigation in error case
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
