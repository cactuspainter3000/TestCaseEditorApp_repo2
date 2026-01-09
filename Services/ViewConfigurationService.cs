using System;
using System.Linq;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Implementation of view configuration service.
    /// Defines complete view configurations for each section and broadcasts them.
    /// </summary>
    public class ViewConfigurationService : IViewConfigurationService
    {
        private readonly IViewModelFactory _viewModelFactory;
        private readonly IWorkspaceManagementMediator _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        
        // Cached view models - created once and reused
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private object? _projectContent;
        private object? _requirementsContent;
        private object? _testCaseGeneratorContent;
        
        public ViewConfiguration? CurrentConfiguration { get; private set; }

        public ViewConfigurationService(
            IViewModelFactory viewModelFactory,
            IWorkspaceManagementMediator workspaceManagementMediator,
            ITestCaseGenerationMediator testCaseGenerationMediator)
        {
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
        }

        public ViewConfiguration GetConfigurationForSection(string sectionName, object? context = null)
        {
            return sectionName?.ToLowerInvariant() switch
            {
                "startup" => CreateStartupConfiguration(context),
                "project" => CreateProjectConfiguration(context),
                "requirements" => CreateRequirementsConfiguration(context),
                "testcase" or "test case creator" => CreateTestCaseGeneratorConfiguration(context),
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
                _projectContent = _viewModelFactory.CreateProjectViewModel();
            }

            return new ViewConfiguration(
                sectionName: "Project",
                titleViewModel: EnsureTestCaseGeneratorTitle(),
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _projectContent,
                notificationViewModel: _viewModelFactory.CreateTestCaseGeneratorNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateRequirementsConfiguration(object? context)
        {
            EnsureTestCaseGeneratorHeader();
            
            // ViewConfigurationService should only handle view creation, not domain business logic
            // Requirement selection logic belongs in the TestCaseGeneration domain

            if (_requirementsContent == null)
            {
                _requirementsContent = _viewModelFactory.CreateRequirementsWorkspaceViewModel();
            }

            return new ViewConfiguration(
                sectionName: "Requirements",
                titleViewModel: EnsureTestCaseGeneratorTitle(),
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _requirementsContent,
                notificationViewModel: _viewModelFactory.CreateTestCaseGeneratorNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
        {
            EnsureTestCaseGeneratorHeader();

            if (_testCaseGeneratorContent == null)
            {
                _testCaseGeneratorContent = _viewModelFactory.CreateTestCaseGeneratorSplashScreenViewModel();
            }

            return new ViewConfiguration(
                sectionName: "TestCase",                titleViewModel: EnsureTestCaseGeneratorTitle(),                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _testCaseGeneratorContent,
                notificationViewModel: _viewModelFactory.CreateTestCaseGeneratorNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateTestFlowConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "TestFlow",
                headerViewModel: _workspaceHeader,
                contentViewModel: _viewModelFactory.CreatePlaceholderViewModel(),
                notificationViewModel: _viewModelFactory.CreateDefaultNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateImportConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "Import",
                headerViewModel: _workspaceHeader,
                contentViewModel: _viewModelFactory.CreateImportWorkflowViewModel(),
                notificationViewModel: _viewModelFactory.CreateDefaultNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateNewProjectConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "NewProject",
                headerViewModel: _workspaceHeader,
                contentViewModel: _viewModelFactory.CreateNewProjectWorkflowViewModel(),
                notificationViewModel: _viewModelFactory.CreateDefaultNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateDefaultConfiguration(object? context)
        {
            EnsureWorkspaceHeader();

            return new ViewConfiguration(
                sectionName: "Default",
                headerViewModel: _workspaceHeader,
                contentViewModel: _viewModelFactory.CreateInitialStateViewModel(),
                notificationViewModel: _viewModelFactory.CreateDefaultNotificationViewModel(),
                context: context
            );
        }

        #endregion

        #region Helper Methods

        private void EnsureWorkspaceHeader()
        {
            if (_workspaceHeader == null)
            {
                _workspaceHeader = _viewModelFactory.CreateWorkspaceHeaderViewModel();
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

        #endregion
    }
}