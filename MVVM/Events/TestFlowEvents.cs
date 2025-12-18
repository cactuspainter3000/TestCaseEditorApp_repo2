using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Test Flow domain events for type-safe communication within the test flow domain
    /// </summary>
    public class TestFlowEvents
    {
        /// <summary>
        /// Fired when navigation changes within test flow workflow
        /// </summary>
        public class StepChanged
        {
            public string Step { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a test flow template is selected
        /// </summary>
        public class FlowTemplateSelected
        {
            public string TemplateName { get; set; } = string.Empty;
            public string TemplateDescription { get; set; } = string.Empty;
            public List<string> RequiredInputs { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow configuration is updated
        /// </summary>
        public class FlowConfigured
        {
            public string FlowName { get; set; } = string.Empty;
            public Dictionary<string, object> Configuration { get; set; } = new();
            public List<Requirement> TargetRequirements { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow design is updated
        /// </summary>
        public class FlowDesignUpdated
        {
            public string FlowId { get; set; } = string.Empty;
            public List<TestFlowStep> Steps { get; set; } = new();
            public Dictionary<string, string> StepConnections { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow validation starts
        /// </summary>
        public class FlowValidationStarted
        {
            public string FlowId { get; set; } = string.Empty;
            public List<TestFlowStep> Steps { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow validation completes
        /// </summary>
        public class FlowValidated
        {
            public string FlowId { get; set; } = string.Empty;
            public bool IsValid { get; set; }
            public List<string> ValidationErrors { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow execution starts
        /// </summary>
        public class FlowExecutionStarted
        {
            public string FlowId { get; set; } = string.Empty;
            public string ExecutionId { get; set; } = string.Empty;
            public List<TestFlowStep> Steps { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a test flow step completes
        /// </summary>
        public class FlowStepCompleted
        {
            public string FlowId { get; set; } = string.Empty;
            public string ExecutionId { get; set; } = string.Empty;
            public string StepId { get; set; } = string.Empty;
            public bool Success { get; set; }
            public string? Result { get; set; }
            public TimeSpan ExecutionTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow execution completes
        /// </summary>
        public class FlowExecutionCompleted
        {
            public string FlowId { get; set; } = string.Empty;
            public string ExecutionId { get; set; } = string.Empty;
            public bool Success { get; set; }
            public Dictionary<string, object> Results { get; set; } = new();
            public TimeSpan TotalExecutionTime { get; set; }
            public List<string> Errors { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test flow results are analyzed
        /// </summary>
        public class FlowResultsAnalyzed
        {
            public string FlowId { get; set; } = string.Empty;
            public string ExecutionId { get; set; } = string.Empty;
            public Dictionary<string, object> Analysis { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
    
    /// <summary>
    /// Represents a step in a test flow
    /// </summary>
    public class TestFlowStep
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "TestCase", "Validation", "Analysis", etc.
        public Dictionary<string, object> Parameters { get; set; } = new();
        public List<string> Dependencies { get; set; } = new(); // IDs of prerequisite steps
        public bool IsOptional { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
    }
}