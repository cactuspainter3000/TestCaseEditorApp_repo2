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
    /// Creates complete view configurations for each section.
    /// This is the single source of truth for what views should be active in each section.
    /// </summary>
    public class ViewConfigurationFactory
    {
        private readonly IViewModelFactory _viewModelFactory;
        private readonly IWorkspaceManagementMediator _workspaceManagementMediator;
        private readonly ITestCaseGenerationMediator _testCaseGenerationMediator;
        
        // Cached view models for reuse
        private WorkspaceHeaderViewModel? _workspaceHeader;
        private TestCaseGenerator_HeaderVM? _testCaseGeneratorHeader;
        private object? _projectContent;
        private object? _requirementsContent;

        public ViewConfigurationFactory(
            IViewModelFactory viewModelFactory,
            IWorkspaceManagementMediator workspaceManagementMediator,
            ITestCaseGenerationMediator testCaseGenerationMediator)
        {
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));
            _testCaseGenerationMediator = testCaseGenerationMediator ?? throw new ArgumentNullException(nameof(testCaseGenerationMediator));
        }

        /// <summary>
        /// Get the complete view configuration for a specific section
        /// </summary>
        public ViewConfiguration GetConfigurationForSection(string sectionName, object? context = null)
        {
            return sectionName?.ToLowerInvariant() switch
            {
                "project" => CreateProjectConfiguration(context),
                "requirements" => CreateRequirementsConfiguration(context),
                "testcase" or "test case creator" => CreateTestCaseGeneratorConfiguration(context),
                "testflow" => CreateTestFlowConfiguration(context),
                "import" => CreateImportConfiguration(context),
                "newproject" => CreateNewProjectConfiguration(context),
                _ => CreateDefaultConfiguration(context)
            };
        }

        private ViewConfiguration CreateProjectConfiguration(object? context)
        {
            EnsureWorkspaceHeader();
            
            if (_projectContent == null)
            {
                _projectContent = _viewModelFactory.CreateProjectViewModel();
            }

            return new ViewConfiguration(
                sectionName: "Project",
                headerViewModel: _workspaceHeader,
                contentViewModel: _projectContent,
                notificationViewModel: _viewModelFactory.CreateDefaultNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateRequirementsConfiguration(object? context)
        {
            EnsureTestCaseGeneratorHeader();
            
            // IMPORTANT: Synchronize requirement context before creating configuration
            SynchronizeCurrentRequirementContext();
            
            if (_requirementsContent == null)
            {
                _requirementsContent = _viewModelFactory.CreateRequirementsWorkspaceViewModel();
            }

            return new ViewConfiguration(
                sectionName: "Requirements",
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _requirementsContent,
                notificationViewModel: _viewModelFactory.CreateDefaultNotificationViewModel(),
                context: context
            );
        }

        private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
        {
            EnsureTestCaseGeneratorHeader();

            return new ViewConfiguration(
                sectionName: "TestCase",
                headerViewModel: _testCaseGeneratorHeader,
                contentViewModel: _viewModelFactory.CreateTestCaseGeneratorSplashScreenViewModel(),
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

        private void EnsureWorkspaceHeader()
        {
            if (_workspaceHeader == null)
            {
                _workspaceHeader = _viewModelFactory.CreateWorkspaceHeaderViewModel();
            }
            
            _workspaceHeader.UpdateSaveStatus(_workspaceManagementMediator);
        }

        private void EnsureTestCaseGeneratorHeader()
        {
            if (_testCaseGeneratorHeader == null)
            {
                _testCaseGeneratorHeader = _viewModelFactory.CreateTestCaseGeneratorHeaderViewModel(_testCaseGenerationMediator);
            }
        }

        /// <summary>
        /// Synchronizes current requirement context from workspace to TestCaseGeneration domain
        /// </summary>
        private void SynchronizeCurrentRequirementContext()
        {
            try
            {
                var currentReqTitle = _workspaceHeader?.CurrentRequirementTitle;
                var currentReqSummary = _workspaceHeader?.CurrentRequirementSummary;
                
                if (!string.IsNullOrEmpty(currentReqTitle) && _testCaseGenerationMediator?.Requirements?.Any() == true)
                {
                    var titleParts = currentReqTitle.Split(new[] { " - " }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (titleParts.Length >= 1)
                    {
                        var itemToMatch = titleParts[0].Trim();
                        
                        var matchingRequirement = _testCaseGenerationMediator.Requirements
                            .FirstOrDefault(r => string.Equals(r.Item?.Trim(), itemToMatch, StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals($"{r.Item} - {r.Name}".Trim(), currentReqTitle.Trim(), StringComparison.OrdinalIgnoreCase));
                        
                        if (matchingRequirement != null)
                        {
                            _testCaseGenerationMediator.CurrentRequirement = matchingRequirement;
                            _testCaseGenerationMediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected
                            {
                                Requirement = matchingRequirement,
                                SelectedBy = "ViewConfigurationSync"
                            });
                        }
                    }
                }
                else if (_testCaseGenerationMediator?.Requirements?.Any() == true && _testCaseGenerationMediator.CurrentRequirement == null)
                {
                    var firstRequirement = _testCaseGenerationMediator.Requirements.First();
                    _testCaseGenerationMediator.CurrentRequirement = firstRequirement;
                    _testCaseGenerationMediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected
                    {
                        Requirement = firstRequirement,
                        SelectedBy = "ViewConfigurationFallback"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ViewConfigurationFactory] Error synchronizing requirement context: {ex.Message}");
            }
        }
    }
}