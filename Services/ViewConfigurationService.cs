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

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of view configuration service.
    /// Defines complete view configurations for each section and broadcasts them.
    /// </summary>
    public class ViewConfigurationService : IViewConfigurationService
    {
        private readonly INewProjectMediator _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        private readonly ITestCaseCreationMediator _testCaseCreationMediator;
        
        // Cached view models - created once and reused
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGeneratorNotificationViewModel? _testCaseGeneratorNotification;
        private object? _projectContent;
        private object? _requirementsContent;
        private object? _testCaseGeneratorContent;
        private object? _testCaseCreationContent;
        
        public ViewConfiguration? CurrentConfiguration { get; private set; }

        public ViewConfigurationService(
            INewProjectMediator workspaceManagementMediator,
            ITestCaseGenerationMediator testCaseGenerationMediator,
            ITestCaseCreationMediator testCaseCreationMediator)
        {
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
            _testCaseCreationMediator = testCaseCreationMediator ?? throw new ArgumentNullException(nameof(testCaseCreationMediator));
        }

        public ViewConfiguration GetConfigurationForSection(string sectionName, object? context = null)
        {
            return sectionName?.ToLowerInvariant() switch
            {
                "startup" => CreateStartupConfiguration(context),
                "project" => CreateProjectConfiguration(context),
                "requirements" => CreateRequirementsConfiguration(context),
                "testcase" or "test case creator" => CreateTestCaseGeneratorConfiguration(context),
                "testcasecreation" or "test case creation" => CreateTestCaseCreationConfiguration(context),
                "testflow" => CreateTestFlowConfiguration(context),
                "import" => CreateImportConfiguration(context),
                "newproject" => CreateNewProjectConfiguration(context),                "dummy" => CreateDummyConfiguration(context),                _ => CreateDefaultConfiguration(context)
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
            EnsureTestCaseGeneratorHeader();

            if (_projectContent == null)
            {
                // Use direct service access instead of factory
                _projectContent = App.ServiceProvider?.GetService<object>() // TODO: Replace with actual project ViewModel type
                    ?? new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Project Content");
            }

            return new ViewConfiguration(
                sectionName: "Project",
                titleViewModel: EnsureTestCaseGeneratorTitle(),
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _projectContent,
                notificationViewModel: EnsureTestCaseGeneratorNotification(), // FIX: Use Test Case Generator notification for project operations
                context: context
            );
        }

        private ViewConfiguration CreateRequirementsConfiguration(object? context)
        {
            EnsureTestCaseGeneratorHeader();
            
            // Use direct DI injection - modern approach
            if (_requirementsContent == null)
            {
                _requirementsContent = App.ServiceProvider?.GetRequiredService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.RequirementsWorkspaceViewModel>()
                    ?? throw new InvalidOperationException("RequirementsWorkspaceViewModel not registered in DI container");
            }

            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: EnsureTestCaseGeneratorTitle(),
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _requirementsContent,
                notificationViewModel: EnsureTestCaseGeneratorNotification(), // FIX: Use Test Case Generator notification for Requirements
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

                // Create UserControls and bind ViewModels (following TestCaseGenerator pattern)
                var titleControl = new TestCaseEditorApp.MVVM.Domains.Dummy.Views.Dummy_TitleView();
                titleControl.DataContext = dummyTitleVM;
                
                var headerControl = new TestCaseEditorApp.MVVM.Domains.Dummy.Views.Dummy_HeaderView();
                headerControl.DataContext = dummyHeaderVM;
                
                var mainControl = new TestCaseEditorApp.MVVM.Domains.Dummy.Views.Dummy_MainView();
                mainControl.DataContext = dummyMainVM;
                
                var navigationControl = new TestCaseEditorApp.MVVM.Domains.Dummy.Views.Dummy_NavigationView();
                navigationControl.DataContext = dummyNavigationVM;
                
                var notificationControl = new TestCaseEditorApp.MVVM.Domains.Dummy.Views.Dummy_NotificationView();
                notificationControl.DataContext = dummyNotificationVM;
                
                return new ViewConfiguration(
                    sectionName: "Dummy Domain",
                    titleViewModel: titleControl,           // 🩷 Pink border UserControl
                    headerViewModel: headerControl,         // 🟠 Orange border UserControl
                    contentViewModel: mainControl,          // 🟢 Green border UserControl
                    navigationViewModel: navigationControl, // 🔵 Blue border UserControl
                    notificationViewModel: notificationControl, // 🟡 Gold border UserControl
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
                // Use actual NewProject domain ViewModels when they're ready
                // For now, create a basic configuration that shows this is the New Project section
                EnsureWorkspaceHeader();
                
                return new ViewConfiguration(
                    sectionName: "New Project",
                    headerViewModel: _workspaceHeader,
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("New Project functionality coming soon..."),
                    navigationViewModel: null,
                    notificationViewModel: null,
                    context: context
                );
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[ViewConfigurationService] Failed to create New Project configuration");
                
                return new ViewConfiguration(
                    sectionName: "New Project (Error)",
                    headerViewModel: null,
                    contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel($"New Project Error: {ex.Message}"),
                    notificationViewModel: null,
                    context: context
                );
            }
        }

        private ViewConfiguration CreateDefaultConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "Default",
                headerViewModel: _workspaceHeader,
                contentViewModel: App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_MainViewModel>(),
                notificationViewModel: new TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel(App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel>>()),
                context: context
            );
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

        #endregion
    }
}
