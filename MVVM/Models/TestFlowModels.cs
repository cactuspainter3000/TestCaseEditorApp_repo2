using System;
using System.Collections.Generic;
using TestFlowStep = TestCaseEditorApp.MVVM.Events.TestFlowStep;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Represents a template for creating test flows with predefined structure and steps.
    /// </summary>
    public class FlowTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> RequiredInputs { get; set; } = new List<string>();
        public List<TestFlowStep> DefaultSteps { get; set; } = new List<TestFlowStep>();
        public string Author { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(1, 0);
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Result of flow validation containing errors, warnings, and overall validity status.
    /// </summary>
    public class FlowValidationResult
    {
        public bool IsValid { get; set; } = false;
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public Dictionary<string, List<string>> StepSpecificErrors { get; set; } = new Dictionary<string, List<string>>();
        public DateTime ValidatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Result of flow execution containing success status, results, and execution metrics.
    /// </summary>
    public class FlowExecutionResult
    {
        public string ExecutionId { get; set; } = string.Empty;
        public bool Success { get; set; } = false;
        public Dictionary<string, object> Results { get; set; } = new Dictionary<string, object>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public TimeSpan ExecutionTime { get; set; } = TimeSpan.Zero;
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>
    /// Current status of a flow execution for monitoring and progress tracking.
    /// </summary>
    public class FlowExecutionStatus
    {
        public string ExecutionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Running", "Completed", "Failed", "Cancelled"
        public string CurrentStepId { get; set; } = string.Empty;
        public double ProgressPercentage { get; set; } = 0.0;
        public TimeSpan ElapsedTime { get; set; } = TimeSpan.Zero;
        public string? CurrentMessage { get; set; }
        public Dictionary<string, object> StatusData { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Result of analyzing flow execution results with insights and recommendations.
    /// </summary>
    public class FlowAnalysisResult
    {
        public Dictionary<string, object> Analysis { get; set; } = new Dictionary<string, object>();
        public List<string> Recommendations { get; set; } = new List<string>();
        public double OverallScore { get; set; } = 0.0;
        public Dictionary<string, double> StepScores { get; set; } = new Dictionary<string, double>();
        public List<string> KeyInsights { get; set; } = new List<string>();
        public DateTime AnalyzedAt { get; set; } = DateTime.Now;
    }
}