using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models;
using TestFlowStep = TestCaseEditorApp.MVVM.Events.TestFlowStep;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.MVVM.Mediators
{
    /// <summary>
    /// Interface for the Test Flow domain mediator.
    /// Handles the entire "Test Flow Generator" menu section including:
    /// - Flow template selection and configuration
    /// - Flow design and step definition
    /// - Flow validation and execution
    /// - Results analysis and reporting
    /// </summary>
    public interface ITestFlowMediator
    {
        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        /// <summary>
        /// Subscribe to TestFlow domain events
        /// </summary>
        void Subscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Unsubscribe from TestFlow domain events
        /// </summary>
        void Unsubscribe<T>(Action<T> handler) where T : class;
        
        /// <summary>
        /// Publish events within TestFlow domain
        /// </summary>
        void PublishEvent<T>(T eventData) where T : class;
        
        /// <summary>
        /// Current step in the TestFlow workflow
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
        /// Mark this mediator as registered and ready for use (called by DI container)
        /// </summary>
        void MarkAsRegistered();

        // ===== NAVIGATION & WORKFLOW =====
        
        /// <summary>
        /// Navigate to the initial step of TestFlow workflow
        /// </summary>
        void NavigateToInitialStep();
        
        /// <summary>
        /// Navigate to the final step of TestFlow workflow
        /// </summary>
        void NavigateToFinalStep();
        
        /// <summary>
        /// Navigate to template selection step
        /// </summary>
        void NavigateToTemplateSelection();
        
        /// <summary>
        /// Navigate to flow configuration step
        /// </summary>
        void NavigateToConfiguration();
        
        /// <summary>
        /// Navigate to flow design step
        /// </summary>
        void NavigateToDesign();
        
        /// <summary>
        /// Navigate to flow validation step
        /// </summary>
        void NavigateToValidation();
        
        /// <summary>
        /// Navigate to flow execution step
        /// </summary>
        void NavigateToExecution();
        
        /// <summary>
        /// Navigate to results analysis step
        /// </summary>
        void NavigateToResults();
        
        /// <summary>
        /// Try to navigate back to previous step
        /// </summary>
        bool TryNavigateBack();
        
        /// <summary>
        /// Check if navigation back is possible
        /// </summary>
        bool CanNavigateBack();
        
        /// <summary>
        /// Check if navigation forward is possible
        /// </summary>
        bool CanNavigateForward();

        // ===== FLOW TEMPLATE MANAGEMENT =====
        
        /// <summary>
        /// Get available flow templates
        /// </summary>
        Task<IReadOnlyList<FlowTemplate>> GetAvailableTemplatesAsync();
        
        /// <summary>
        /// Select a flow template for the current workflow
        /// </summary>
        Task<bool> SelectTemplateAsync(string templateName);

        // ===== FLOW CONFIGURATION =====
        
        /// <summary>
        /// Configure flow with name and basic settings
        /// </summary>
        Task<bool> ConfigureFlowAsync(string flowName, Dictionary<string, object> configuration);
        
        /// <summary>
        /// Set target requirements for the flow
        /// </summary>
        Task<bool> SetTargetRequirementsAsync(IReadOnlyList<Requirement> requirements);

        // ===== FLOW DESIGN =====
        
        /// <summary>
        /// Create a new test flow step
        /// </summary>
        Task<TestFlowStep> CreateFlowStepAsync(string stepName, string stepType);
        
        /// <summary>
        /// Update an existing flow step
        /// </summary>
        Task<bool> UpdateFlowStepAsync(TestFlowStep step);
        
        /// <summary>
        /// Remove a flow step
        /// </summary>
        Task<bool> RemoveFlowStepAsync(string stepId);
        
        /// <summary>
        /// Connect two flow steps
        /// </summary>
        Task<bool> ConnectStepsAsync(string fromStepId, string toStepId);

        // ===== FLOW VALIDATION =====
        
        /// <summary>
        /// Validate the current flow design
        /// </summary>
        Task<FlowValidationResult> ValidateFlowAsync(string flowId);

        // ===== FLOW EXECUTION =====
        
        /// <summary>
        /// Execute the current flow
        /// </summary>
        Task<FlowExecutionResult> ExecuteFlowAsync(string flowId);
        
        /// <summary>
        /// Get execution status for a running flow
        /// </summary>
        Task<FlowExecutionStatus> GetExecutionStatusAsync(string executionId);

        // ===== RESULTS ANALYSIS =====
        
        /// <summary>
        /// Analyze flow execution results
        /// </summary>
        Task<FlowAnalysisResult> AnalyzeResultsAsync(string executionId);
        
        /// <summary>
        /// Export flow results to various formats
        /// </summary>
        Task<bool> ExportResultsAsync(string executionId, string format, string filePath);

        // ===== CROSS-DOMAIN COMMUNICATION =====
        
        /// <summary>
        /// Request action from another domain
        /// </summary>
        void RequestCrossDomainAction<T>(T request) where T : class;
        
        /// <summary>
        /// Broadcast event to all domains
        /// </summary>
        void BroadcastToAllDomains<T>(T notification) where T : class;
    }
    
    // Supporting types for the interface
    public class FlowTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> RequiredInputs { get; set; } = new();
        public List<TestFlowStep> DefaultSteps { get; set; } = new();
    }
    
    public class FlowValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
    
    public class FlowExecutionResult
    {
        public string ExecutionId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Dictionary<string, object> Results { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
    
    public class FlowExecutionStatus
    {
        public string ExecutionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Running", "Completed", "Failed"
        public string CurrentStepId { get; set; } = string.Empty;
        public double ProgressPercentage { get; set; }
        public TimeSpan ElapsedTime { get; set; }
    }
    
    public class FlowAnalysisResult
    {
        public Dictionary<string, object> Analysis { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public double OverallScore { get; set; }
        public Dictionary<string, double> StepScores { get; set; } = new();
    }
}