using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators
{
    /// <summary>
    /// Mediator interface for Test Case Creation domain
    /// </summary>
    public interface ITestCaseCreationMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to TestCaseCreation domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from TestCaseCreation domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish events within TestCaseCreation domain
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;
        
        /// <summary>
        /// Current step in the TestCaseCreation workflow
        /// </summary>
        string? CurrentStep { get; }
        
        /// <summary>
        /// Current ViewModel being displayed in this domain
        /// </summary>
        object? CurrentViewModel { get; }

        // === TEST CASE OPERATIONS ===
        
        /// <summary>
        /// Create a new test case
        /// </summary>
        Task<EditableTestCase> CreateTestCaseAsync(string? templateTitle = null);
        
        /// <summary>
        /// Delete a test case
        /// </summary>
        Task DeleteTestCaseAsync(EditableTestCase testCase);
        
        /// <summary>
        /// Save test cases to requirement
        /// </summary>
        Task SaveTestCasesToRequirementAsync(Requirement requirement, IEnumerable<EditableTestCase> testCases);
        
        /// <summary>
        /// Load test cases from requirement
        /// </summary>
        Task<IReadOnlyList<EditableTestCase>> LoadTestCasesFromRequirementAsync(Requirement requirement);

        // === EXTERNAL COMMANDS ===
        
        /// <summary>
        /// Generate test case command for external tool (Jama, etc.)
        /// </summary>
        Task<string> GenerateTestCaseCommandAsync(EditableTestCase testCase, string targetFormat = "jama");
        
        // === WORKSPACE INTEGRATION ===
        
        /// <summary>
        /// Set current requirement context
        /// </summary>
        Task SetRequirementContextAsync(Requirement? requirement);
        
        /// <summary>
        /// Get current requirement context
        /// </summary>
        Requirement? GetCurrentRequirement();
        
        /// <summary>
        /// Check if there are unsaved changes
        /// </summary>
        bool HasUnsavedChanges();
        
        /// <summary>
        /// Mark workspace as dirty/clean
        /// </summary>
        void SetWorkspaceDirty(bool isDirty);
    }
}