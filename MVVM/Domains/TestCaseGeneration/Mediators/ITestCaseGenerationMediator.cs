using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators
{
    /// <summary>
    /// Interface for the Test Case Generation domain mediator.
    /// Handles the entire "Test Case Generator" menu section including:
    /// - Requirements import, analysis, and management
    /// - Assumptions workflow
    /// - Questions workflow  
    /// - Test case creation and generation
    /// - Export functionality
    /// </summary>
    public interface ITestCaseGenerationMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to TestCaseGeneration domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from TestCaseGeneration domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish events within TestCaseGeneration domain
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;
        
        /// <summary>
        /// Current step in the TestCaseGeneration workflow
        /// </summary>
        string? CurrentStep { get; }
        
        /// <summary>
        /// Current ViewModel being displayed in this domain
        /// </summary>
        object? CurrentViewModel { get; }
        
        /// <summary>
        /// Whether this mediator is properly registered and ready
        /// </summary>
        bool IsRegistered { get; }
        
        /// <summary>
        /// Requirements collection for UI binding across the domain
        /// </summary>
        ObservableCollection<Requirement> Requirements { get; }
        
        /// <summary>
        /// Mark this mediator as registered and ready for use (called by DI container)
        /// </summary>
        void MarkAsRegistered();
        /// Navigate to the initial step of TestCaseGeneration workflow
        /// </summary>
        void NavigateToInitialStep();
        
        /// <summary>
        /// Navigate to requirements management
        /// </summary>
        void NavigateToRequirements();
        
        /// <summary>
        /// Navigate to assumptions selection for a requirement
        /// </summary>
        void NavigateToAssumptions(Requirement requirement);
        
        /// <summary>
        /// Navigate to questions workflow for a requirement
        /// </summary>
        void NavigateToQuestions(Requirement requirement);
        
        /// <summary>
        /// Navigate to test case creation for a requirement
        /// </summary>
        void NavigateToTestCaseCreation(Requirement requirement);
        
        /// <summary>
        /// Navigate to export functionality
        /// </summary>
        void NavigateToExport(IReadOnlyList<Requirement> requirements);
        
        // ===== REQUIREMENTS MANAGEMENT =====
        
        /// <summary>
        /// Start requirements import workflow
        /// </summary>
        Task<bool> ImportRequirementsAsync(string filePath, string importType = "Auto");
        
        /// <summary>
        /// Start requirements analysis for a single requirement
        /// </summary>
        Task<bool> AnalyzeRequirementAsync(Requirement requirement);
        
        /// <summary>
        /// Start batch requirements analysis
        /// </summary>
        Task<bool> AnalyzeBatchRequirementsAsync(IReadOnlyList<Requirement> requirements);
        
        /// <summary>
        /// Select a requirement for further processing
        /// </summary>
        void SelectRequirement(Requirement requirement);
        
        /// <summary>
        /// Update a requirement and notify subscribers
        /// </summary>
        void UpdateRequirement(Requirement requirement, IReadOnlyList<string> modifiedFields);
        
        // ===== ASSUMPTIONS WORKFLOW =====
        
        /// <summary>
        /// Update assumptions for a requirement
        /// </summary>
        void UpdateAssumptions(Requirement requirement, IReadOnlyList<string> assumptions);
        
        /// <summary>
        /// Get current assumptions for a requirement
        /// </summary>
        IReadOnlyList<string> GetAssumptions(Requirement requirement);
        
        // ===== QUESTIONS WORKFLOW =====
        
        /// <summary>
        /// Generate clarifying questions for a requirement
        /// </summary>
        Task<IReadOnlyList<ClarifyingQuestionData>> GenerateQuestionsAsync(Requirement requirement, int questionBudget = 5);
        
        /// <summary>
        /// Update answers for clarifying questions
        /// </summary>
        void UpdateQuestionAnswers(Requirement requirement, IReadOnlyList<ClarifyingQuestionData> answeredQuestions);
        
        // ===== TEST CASE GENERATION =====
        
        /// <summary>
        /// Generate test cases for a requirement
        /// </summary>
        Task<IReadOnlyList<TestCase>> GenerateTestCasesAsync(Requirement requirement, VerificationMethod method);
        
        /// <summary>
        /// Validate generated test cases
        /// </summary>
        Task<bool> ValidateTestCasesAsync(IReadOnlyList<TestCase> testCases);
        
        // ===== EXPORT FUNCTIONALITY =====
        
        /// <summary>
        /// Export requirements to various formats
        /// </summary>
        Task<bool> ExportRequirementsAsync(IReadOnlyList<Requirement> requirements, string exportType, string outputPath);
        
        /// <summary>
        /// Export test cases to various formats
        /// </summary>
        Task<bool> ExportTestCasesAsync(IReadOnlyList<TestCase> testCases, string exportType, string outputPath);
        
        // ===== CROSS-DOMAIN COMMUNICATION =====
        
        /// <summary>
        /// Request action from another domain
        /// </summary>
        void RequestCrossDomainAction<T>(T request) where T : class;
        
        /// <summary>
        /// Broadcast notification to all domains
        /// </summary>
        void BroadcastToAllDomains<T>(T notification) where T : class;
        
        // ===== DOMAIN STATE MANAGEMENT =====
        
        /// <summary>
        /// Current requirement selected in the domain workflow
        /// </summary>
        Requirement? CurrentRequirement { get; set; }
        
        /// <summary>
        /// Indicates if the domain has unsaved changes
        /// </summary>
        bool IsDirty { get; set; }
        
        /// <summary>
        /// Indicates if any requirement analysis is in progress (individual or batch)
        /// </summary>
        bool IsAnalyzing { get; set; }
        
        /// <summary>
        /// Current step in the workflow
        /// </summary>
        object? SelectedStep { get; set; }
        
        /// <summary>
        /// Current step's view model
        /// </summary>
        object? CurrentStepViewModel { get; set; }
        
        /// <summary>
        /// HeaderVM instance created and managed by this mediator
        /// </summary>
        TestCaseGenerator_HeaderVM? HeaderViewModel { get; }
        
        /// <summary>
        /// TitleVM instance created and managed by this mediator
        /// </summary>
        TestCaseGenerator_TitleVM? TitleViewModel { get; }
        
        // ===== HEADER INTEGRATION =====
        
        // REMOVED: SetHeaderViewModel - HeaderVM is now created directly by mediator via DI
        // Legacy factory pattern conflicts with proper domain architecture
        
        /// <summary>
        /// Handle broadcast notifications from other domains
        /// </summary>
        void HandleBroadcastNotification<T>(T notification) where T : class;
    }
}