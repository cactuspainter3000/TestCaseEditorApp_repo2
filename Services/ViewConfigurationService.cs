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
                "newproject" => CreateNewProjectConfiguration(context),
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
            return new ViewConfiguration(
                sectionName: "Startup",
                titleViewModel: new TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartupTitleVM(),
                headerViewModel: new TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartupHeaderVM(),
                contentViewModel: new TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartupMainVM(),
                navigationViewModel: new TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartupNavigationVM(),
                notificationViewModel: new TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartupNotificationVM(),
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

            if (_testCaseGeneratorContent == null)
            {
                // Use direct service access instead of factory
                _testCaseGeneratorContent = App.ServiceProvider?.GetService<object>() // TODO: Replace with actual generator ViewModel type
                    ?? new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("Test Case Generator");
            }

            return new ViewConfiguration(
                sectionName: "TestCase",                titleViewModel: EnsureTestCaseGeneratorTitle(),                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _testCaseGeneratorContent,
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

        private ViewConfiguration CreateNewProjectConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "NewProject",
                headerViewModel: _workspaceHeader,
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.PlaceholderViewModel("New Project"),
                notificationViewModel: new TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel(App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<TestCaseEditorApp.MVVM.ViewModels.DefaultNotificationViewModel>>()),
                context: context
            );
        }

        private ViewConfiguration CreateDefaultConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "Default",
                headerViewModel: _workspaceHeader,
                contentViewModel: new TestCaseEditorApp.MVVM.ViewModels.InitialStateViewModel(),
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
