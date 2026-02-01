using System;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Events
{
    /// <summary>
    /// Cross-domain communication contracts for TestCaseGeneration ↔ TestFlow coordination.
    /// These represent requests and responses that cross domain boundaries.
    /// </summary>
    public static class CrossDomainMessages
    {
        // ===== TESTCASE GENERATION → TESTFLOW REQUESTS =====

        /// <summary>
        /// Request to create a test flow from generated test cases
        /// </summary>
        public class CreateFlowFromTestCases
        {
            public List<GeneratedTestCase> TestCases { get; set; } = new();
            public string FlowName { get; set; } = string.Empty;
            public string RequestingDomain { get; set; } = "TestCaseGeneration";
            public string? TargetTemplate { get; set; }
            public Dictionary<string, object> FlowConfiguration { get; set; } = new();
        }

        /// <summary>
        /// Request to validate test cases against flow templates
        /// </summary>
        public class ValidateTestCasesAgainstFlow
        {
            public List<GeneratedTestCase> TestCases { get; set; } = new();
            public string FlowTemplateName { get; set; } = string.Empty;
            public string RequestingDomain { get; set; } = "TestCaseGeneration";
            public bool IncludeRecommendations { get; set; } = true;
        }

        /// <summary>
        /// Request available flow templates for test case generation context
        /// </summary>
        public class RequestFlowTemplates
        {
            public List<Requirement> TargetRequirements { get; set; } = new();
            public string TestCaseGenerationContext { get; set; } = string.Empty;
            public string RequestingDomain { get; set; } = "TestCaseGeneration";
        }

        // ===== PROJECT LIFECYCLE MESSAGES =====

        /// <summary>
        /// Notification that a new project has been created and is ready for domain-specific processing
        /// </summary>
        public class ProjectCreatedNotification
        {
            public string ProjectName { get; set; } = string.Empty;
            public string WorkspaceName { get; set; } = string.Empty;
            public string ProjectPath { get; set; } = string.Empty;
            public bool IsJamaImport { get; set; }
            public int? JamaProjectId { get; set; }
            public string? JamaProjectName { get; set; }
            public DateTime CreatedTime { get; set; } = DateTime.Now;
            public string SourceDomain { get; set; } = "NewProject";
        }

        // ===== TESTFLOW → TESTCASE GENERATION REQUESTS =====

        /// <summary>
        /// Request to analyze requirements for flow design
        /// </summary>
        public class AnalyzeRequirementsForFlow
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string FlowContext { get; set; } = string.Empty;
            public string RequestingDomain { get; set; } = "TestFlow";
            public string? FlowTemplateName { get; set; }
        }

        /// <summary>
        /// Request to generate test cases for specific flow steps
        /// </summary>
        public class GenerateTestCasesForFlowSteps
        {
            public List<TestCaseEditorApp.MVVM.Events.TestFlowStep> FlowSteps { get; set; } = new();
            public List<Requirement> SourceRequirements { get; set; } = new();
            public string RequestingDomain { get; set; } = "TestFlow";
            public Dictionary<string, object> GenerationContext { get; set; } = new();
        }

        /// <summary>
        /// Request requirement quality analysis for flow validation
        /// </summary>
        public class RequestRequirementQualityAnalysis
        {
            public List<Requirement> Requirements { get; set; } = new();
            public string FlowValidationContext { get; set; } = string.Empty;
            public string RequestingDomain { get; set; } = "TestFlow";
        }

        // ===== BIDIRECTIONAL RESPONSES =====

        /// <summary>
        /// Response to flow creation request
        /// </summary>
        public class FlowCreationResponse
        {
            public bool Success { get; set; }
            public string FlowId { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string RespondingDomain { get; set; } = "TestFlow";
            public List<string> ValidationWarnings { get; set; } = new();
        }

        /// <summary>
        /// Response to test case generation request
        /// </summary>
        public class TestCaseGenerationResponse
        {
            public bool Success { get; set; }
            public List<GeneratedTestCase> GeneratedTestCases { get; set; } = new();
            public string Message { get; set; } = string.Empty;
            public string RespondingDomain { get; set; } = "TestCaseGeneration";
            public Dictionary<string, object> GenerationMetadata { get; set; } = new();
        }

        /// <summary>
        /// Response to requirement analysis request
        /// </summary>
        public class RequirementAnalysisResponse
        {
            public bool Success { get; set; }
            public Dictionary<string, RequirementAnalysis> AnalysisResults { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
            public string Message { get; set; } = string.Empty;
            public string RespondingDomain { get; set; } = "TestCaseGeneration";
        }

        /// <summary>
        /// Response to flow template request
        /// </summary>
        public class FlowTemplateResponse
        {
            public bool Success { get; set; }
            public List<TestCaseEditorApp.MVVM.Models.FlowTemplate> AvailableTemplates { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
            public string Message { get; set; } = string.Empty;
            public string RespondingDomain { get; set; } = "TestFlow";
        }

        /// <summary>
        /// Response to validation request
        /// </summary>
        public class ValidationResponse
        {
            public bool Success { get; set; }
            public bool IsValid { get; set; }
            public List<string> ValidationErrors { get; set; } = new();
            public List<string> ValidationWarnings { get; set; } = new();
            public List<string> Recommendations { get; set; } = new();
            public string Message { get; set; } = string.Empty;
            public string RespondingDomain { get; set; } = string.Empty;
            public Dictionary<string, object> ValidationMetadata { get; set; } = new();
        }

        // ===== BROADCAST NOTIFICATIONS =====

        /// <summary>
        /// Notification that workspace context has changed
        /// </summary>
        public class WorkspaceContextChanged
        {
            public string WorkspaceName { get; set; } = string.Empty;
            public List<Requirement> Requirements { get; set; } = new();
            public List<GeneratedTestCase> TestCases { get; set; } = new();
            public string ChangeType { get; set; } = string.Empty; // "RequirementsUpdated", "TestCasesGenerated", etc.
            public string OriginatingDomain { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Notification that LLM processing has completed
        /// </summary>
        public class LLMProcessingCompleted
        {
            public string ProcessingType { get; set; } = string.Empty; // "RequirementAnalysis", "TestCaseGeneration", etc.
            public bool Success { get; set; }
            public string ProcessingId { get; set; } = string.Empty;
            public string OriginatingDomain { get; set; } = string.Empty;
            public Dictionary<string, object> Results { get; set; } = new();
            public TimeSpan ProcessingTime { get; set; }
            public DateTime CompletedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// Notification about domain navigation changes
        /// </summary>
        public class DomainNavigationChanged
        {
            public string Domain { get; set; } = string.Empty;
            public string PreviousStep { get; set; } = string.Empty;
            public string CurrentStep { get; set; } = string.Empty;
            public object? ViewModel { get; set; }
            public Dictionary<string, object> NavigationContext { get; set; } = new();
            public DateTime Timestamp { get; set; } = DateTime.Now;
        }

        // ===== WORKSPACE MANAGEMENT → TESTCASE GENERATION REQUESTS =====

        /// <summary>
        /// Request to import requirements from a document
        /// </summary>
        public class ImportRequirementsRequest
        {
            public string DocumentPath { get; set; } = string.Empty;
            public string RequestingDomain { get; set; } = "WorkspaceManagement";
            public bool PreferJamaParser { get; set; } = false;
            public string? ProjectName { get; set; }
            public DateTime RequestTimestamp { get; set; } = DateTime.Now;
        }
    }
}