using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators
{
    /// <summary>
    /// Test Case Creation domain mediator - AI Guidelines compliant
    /// </summary>
    public class TestCaseCreationMediator : BaseDomainMediator<TestCaseCreationEvents>, ITestCaseCreationMediator
    {
        private Requirement? _currentRequirement;
        private readonly List<EditableTestCase> _currentTestCases = new();
        private bool _hasUnsavedChanges;
        
        public TestCaseCreationMediator(
            ILogger<TestCaseCreationMediator> logger,
            IDomainUICoordinator uiCoordinator,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "TestCaseCreation", performanceMonitor, eventReplay)
        {
            _logger.LogDebug("TestCaseCreationMediator initialized");
        }

        // ===== CORE MEDIATOR FUNCTIONALITY =====

        /// <summary>
        /// Subscribe to TestCaseCreation domain events
        /// </summary>
        public new void Subscribe<T>(Action<T> handler) where T : class
        {
            base.Subscribe(handler);
        }

        /// <summary>
        /// Unsubscribe from TestCaseCreation domain events
        /// </summary>
        public new void Unsubscribe<T>(Action<T> handler) where T : class
        {
            base.Unsubscribe(handler);
        }

        /// <summary>
        /// Publish events within TestCaseCreation domain
        /// </summary>
        public new void PublishEvent<T>(T eventData) where T : class
        {
            base.PublishEvent(eventData);
        }

        // Abstract method implementations required by BaseDomainMediator
        public override void NavigateToInitialStep()
        {
            // Navigate to main test case creation view
            _logger.LogInformation("Navigating to initial Test Case Creation step");
        }

        public override void NavigateToFinalStep()
        {
            // Navigate to final step (save/export)
            _logger.LogInformation("Navigating to final Test Case Creation step");
        }

        public override bool CanNavigateBack()
        {
            // TestCaseCreation is a single-step domain
            return false;
        }

        public override bool CanNavigateForward()
        {
            // TestCaseCreation is a single-step domain
            return false;
        }

        // === TEST CASE OPERATIONS ===

        /// <summary>
        /// Create a new test case
        /// </summary>
        public async Task<EditableTestCase> CreateTestCaseAsync(string? templateTitle = null)
        {
            try
            {
                var sequenceNumber = _currentTestCases.Count + 1;
                var newTestCase = EditableTestCase.CreateNew(sequenceNumber);
                
                if (!string.IsNullOrWhiteSpace(templateTitle))
                {
                    newTestCase.Title = templateTitle;
                }
                
                _currentTestCases.Add(newTestCase);
                SetWorkspaceDirty(true);
                
                PublishEvent(new TestCaseCreationEvents.TestCaseCreated 
                { 
                    TestCase = newTestCase,
                    CreatedBy = "User",
                    Timestamp = DateTime.Now,
                    CorrelationId = Guid.NewGuid().ToString()
                });
                
                _logger.LogInformation("Created new test case: {Title}", newTestCase.Title);
                return newTestCase;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create test case");
                throw;
            }
        }

        /// <summary>
        /// Delete a test case
        /// </summary>
        public async Task DeleteTestCaseAsync(EditableTestCase testCase)
        {
            try
            {
                if (_currentTestCases.Remove(testCase))
                {
                    SetWorkspaceDirty(true);
                    
                    PublishEvent(new TestCaseCreationEvents.TestCaseDeleted 
                    { 
                        TestCaseId = testCase.Id,
                        TestCaseTitle = testCase.Title,
                        DeletedBy = "User",
                        Timestamp = DateTime.Now,
                        CorrelationId = Guid.NewGuid().ToString()
                    });
                    
                    _logger.LogInformation("Deleted test case: {Title}", testCase.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete test case: {Title}", testCase.Title);
                throw;
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Save test cases to requirement
        /// </summary>
        public async Task SaveTestCasesToRequirementAsync(Requirement requirement, IEnumerable<EditableTestCase> testCases)
        {
            try
            {
                var testCaseList = testCases.ToList();
                
                // Convert EditableTestCase to TestCase domain models
                var domainTestCases = testCaseList.Select(tc => tc.ToTestCase()).ToList();
                
                // Update requirement
                requirement.GeneratedTestCases?.Clear();
                if (requirement.GeneratedTestCases == null)
                {
                    requirement.GeneratedTestCases = new ObservableCollection<TestCase>();
                }
                
                foreach (var testCase in domainTestCases)
                {
                    requirement.GeneratedTestCases.Add(testCase);
                }
                
                // Mark test cases as clean
                foreach (var testCase in testCaseList)
                {
                    testCase.MarkAsClean();
                }
                
                SetWorkspaceDirty(false);
                
                PublishEvent(new TestCaseCreationEvents.TestCasesSaved 
                { 
                    Requirement = requirement,
                    TestCases = testCaseList,
                    SavedBy = "User",
                    Timestamp = DateTime.Now,
                    CorrelationId = Guid.NewGuid().ToString()
                });
                
                _logger.LogInformation("Saved {Count} test cases to requirement {RequirementId}", 
                    testCaseList.Count, requirement.GlobalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save test cases to requirement {RequirementId}", requirement.GlobalId);
                throw;
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Load test cases from requirement
        /// </summary>
        public async Task<IReadOnlyList<EditableTestCase>> LoadTestCasesFromRequirementAsync(Requirement requirement)
        {
            try
            {
                _currentTestCases.Clear();
                
                if (requirement.GeneratedTestCases != null)
                {
                    foreach (var testCase in requirement.GeneratedTestCases)
                    {
                        var editableTestCase = EditableTestCase.FromTestCase(testCase);
                        editableTestCase.MarkAsClean();
                        _currentTestCases.Add(editableTestCase);
                    }
                }
                
                _logger.LogInformation("Loaded {Count} test cases from requirement {RequirementId}", 
                    _currentTestCases.Count, requirement.GlobalId);
                
                return _currentTestCases.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load test cases from requirement {RequirementId}", requirement.GlobalId);
                throw;
            }
        }

        // === EXTERNAL COMMANDS ===

        /// <summary>
        /// Generate test case command for external tool (Jama, etc.)
        /// </summary>
        public async Task<string> GenerateTestCaseCommandAsync(EditableTestCase testCase, string targetFormat = "jama")
        {
            try
            {
                PublishEvent(new TestCaseCreationEvents.GenerateTestCaseCommandRequested 
                { 
                    TestCase = testCase,
                    TargetFormat = targetFormat,
                    RequestedBy = "User",
                    Timestamp = DateTime.Now,
                    CorrelationId = Guid.NewGuid().ToString()
                });
                
                // TODO: Implement actual command generation based on target format
                var command = targetFormat.ToLowerInvariant() switch
                {
                    "jama" => GenerateJamaCommand(testCase),
                    "excel" => GenerateExcelCommand(testCase),
                    "markdown" => GenerateMarkdownCommand(testCase),
                    _ => GenerateGenericCommand(testCase)
                };
                
                _logger.LogInformation("Generated {Format} command for test case: {Title}", targetFormat, testCase.Title);
                return command;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate {Format} command for test case: {Title}", targetFormat, testCase.Title);
                throw;
            }
        }

        // === WORKSPACE INTEGRATION ===

        /// <summary>
        /// Set current requirement context
        /// </summary>
        public async Task SetRequirementContextAsync(Requirement? requirement)
        {
            try
            {
                var previousRequirement = _currentRequirement;
                _currentRequirement = requirement;
                
                PublishEvent(new TestCaseCreationEvents.RequirementContextChanged 
                { 
                    PreviousRequirement = previousRequirement,
                    CurrentRequirement = requirement,
                    ChangedBy = "User",
                    Timestamp = DateTime.Now,
                    CorrelationId = Guid.NewGuid().ToString()
                });
                
                _logger.LogInformation("Requirement context changed from {PreviousId} to {CurrentId}", 
                    previousRequirement?.GlobalId ?? "<none>", requirement?.GlobalId ?? "<none>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set requirement context");
                throw;
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Get current requirement context
        /// </summary>
        public Requirement? GetCurrentRequirement() => _currentRequirement;

        /// <summary>
        /// Check if there are unsaved changes
        /// </summary>
        public bool HasUnsavedChanges() => _hasUnsavedChanges || _currentTestCases.Any(tc => tc.IsDirty);

        /// <summary>
        /// Mark workspace as dirty/clean
        /// </summary>
        public void SetWorkspaceDirty(bool isDirty)
        {
            _hasUnsavedChanges = isDirty;
            // TODO: Integrate with workspace dirty tracking when available
        }

        // === PRIVATE COMMAND GENERATORS ===

        private string GenerateJamaCommand(EditableTestCase testCase)
        {
            return $"CREATE_TEST_CASE --project='{_currentRequirement?.GlobalId}' " +
                   $"--name='{testCase.Title}' " +
                   $"--preconditions='{testCase.Preconditions}' " +
                   $"--steps='{testCase.Steps.Replace("\n", " | ")}' " +
                   $"--expected='{testCase.ExpectedResults}'";
        }

        private string GenerateExcelCommand(EditableTestCase testCase)
        {
            return $"{testCase.Title}\t{testCase.Preconditions}\t{testCase.Steps}\t{testCase.ExpectedResults}";
        }

        private string GenerateMarkdownCommand(EditableTestCase testCase)
        {
            return $"## {testCase.Title}\n\n" +
                   $"**Preconditions:** {testCase.Preconditions}\n\n" +
                   $"**Steps:**\n{testCase.Steps}\n\n" +
                   $"**Expected Results:** {testCase.ExpectedResults}";
        }

        private string GenerateGenericCommand(EditableTestCase testCase)
        {
            return $"Test Case: {testCase.Title}\n" +
                   $"Preconditions: {testCase.Preconditions}\n" +
                   $"Steps: {testCase.Steps}\n" +
                   $"Expected: {testCase.ExpectedResults}";
        }
    }
}