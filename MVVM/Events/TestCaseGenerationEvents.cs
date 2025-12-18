using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Test Case Generation domain events for type-safe communication within the domain
    /// </summary>
    public class TestCaseGenerationEvents
    {
        /// <summary>
        /// Fired when navigation changes within test case generation workflow
        /// </summary>
        public class StepChanged
        {
            public string Step { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when a requirement is selected for test case generation
        /// </summary>
        public class RequirementSelected
        {
            public Requirement Requirement { get; set; } = default!;
            public string SelectedBy { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when assumptions are updated for current requirement
        /// </summary>
        public class AssumptionsUpdated
        {
            public List<string> Assumptions { get; set; } = new();
            public Requirement Requirement { get; set; } = default!;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when clarifying questions are answered
        /// </summary>
        public class QuestionsAnswered
        {
            public List<ClarifyingQuestionData> Questions { get; set; } = new();
            public Requirement Requirement { get; set; } = default!;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test case generation starts
        /// </summary>
        public class GenerationStarted
        {
            public Requirement Requirement { get; set; } = default!;
            public VerificationMethod Method { get; set; }
            public List<string> Assumptions { get; set; } = new();
            public List<ClarifyingQuestionData> Questions { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test cases are successfully generated
        /// </summary>
        public class TestCasesGenerated
        {
            public List<TestCase> TestCases { get; set; } = new();
            public Requirement SourceRequirement { get; set; } = default!;
            public VerificationMethod Method { get; set; }
            public string LlmResponse { get; set; } = string.Empty;
            public TimeSpan GenerationTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test case generation fails
        /// </summary>
        public class GenerationFailed
        {
            public Requirement Requirement { get; set; } = default!;
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test cases are validated
        /// </summary>
        public class TestCasesValidated
        {
            public List<TestCase> TestCases { get; set; } = new();
            public bool IsValid { get; set; }
            public List<string> ValidationErrors { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
        
        /// <summary>
        /// Fired when test case generation workflow is completed
        /// </summary>
        public class GenerationCompleted
        {
            public Requirement Requirement { get; set; } = default!;
            public List<TestCase> FinalTestCases { get; set; } = new();
            public bool Success { get; set; }
            public TimeSpan TotalTime { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }
    }
}