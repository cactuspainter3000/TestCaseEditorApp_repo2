using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestFlowStep = TestCaseEditorApp.MVVM.Events.TestFlowStep;

namespace TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators
{
    /// <summary>
    /// TestFlow domain mediator that handles the entire "Test Flow Generator" menu section.
    /// Coordinates flow templates, design, validation, execution, and results analysis
    /// with full UI coordination and cross-domain communication support.
    /// </summary>
    public class TestFlowMediator : BaseDomainMediator<TestFlowEvents>, ITestFlowMediator
    {
        private readonly ITextGenerationService _llmService;
        
        // Workflow state
        private readonly Dictionary<string, FlowTemplate> _availableTemplates = new();
        private string? _currentFlowId;
        private FlowTemplate? _selectedTemplate;
        private Dictionary<string, object> _currentConfiguration = new();
        private List<Requirement> _targetRequirements = new();
        private List<TestFlowStep> _currentSteps = new();
        private Dictionary<string, string> _stepConnections = new();

        public TestFlowMediator(
            ILogger<TestFlowMediator> logger,
            IDomainUICoordinator uiCoordinator,
            ITextGenerationService llmService,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Test Flow Generator", performanceMonitor, eventReplay)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));

            _logger.LogDebug("TestFlowMediator created with domain '{DomainName}'", _domainName);
            InitializeTemplates();
        }

        // ===== CORE MEDIATOR FUNCTIONALITY =====
        
        public override void Subscribe<T>(Action<T> handler) where T : class
        {
            base.Subscribe(handler);
        }
        
        public override void Unsubscribe<T>(Action<T> handler) where T : class
        {
            base.Unsubscribe(handler);
        }
        
        public new void PublishEvent<T>(T eventData) where T : class
        {
            base.PublishEvent(eventData);
        }

        // ===== NAVIGATION & WORKFLOW =====

        public override void NavigateToInitialStep()
        {
            NavigateToStep("Template Selection", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Template Selection", 
                ViewModel = null 
            });
            
            _logger.LogInformation("Navigated to initial step: Template Selection");
        }

        public override void NavigateToFinalStep()
        {
            NavigateToStep("Results Analysis", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Results Analysis", 
                ViewModel = null 
            });
            
            _logger.LogInformation("Navigated to final step: Results Analysis");
        }

        public override bool CanNavigateBack()
        {
            return _navigationHistory.Count > 0;
        }

        public override bool CanNavigateForward()
        {
            // TestFlow workflow is generally linear but can branch based on validation results
            return false; // Simplified for now
        }

        public void NavigateToTemplateSelection()
        {
            NavigateToStep("Template Selection", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Template Selection", 
                ViewModel = null 
            });
        }

        public void NavigateToConfiguration()
        {
            NavigateToStep("Configuration", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Configuration", 
                ViewModel = null 
            });
        }

        public void NavigateToDesign()
        {
            NavigateToStep("Design", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Design", 
                ViewModel = null 
            });
        }

        public void NavigateToValidation()
        {
            NavigateToStep("Validation", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Validation", 
                ViewModel = null 
            });
        }

        public void NavigateToExecution()
        {
            NavigateToStep("Execution", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Execution", 
                ViewModel = null 
            });
        }

        public void NavigateToResults()
        {
            NavigateToStep("Results Analysis", null);
            
            PublishEvent(new TestFlowEvents.StepChanged 
            { 
                Step = "Results Analysis", 
                ViewModel = null 
            });
        }

        // ===== FLOW TEMPLATE MANAGEMENT =====

        public async Task<IReadOnlyList<FlowTemplate>> GetAvailableTemplatesAsync()
        {
            ShowProgress("Loading flow templates...", 0);
            
            try
            {
                // Simulate loading templates (in real implementation, might load from files/database)
                await Task.Delay(500);
                
                var templates = _availableTemplates.Values.ToList();
                
                HideProgress();
                ShowNotification($"Loaded {templates.Count} flow templates", DomainNotificationType.Info);
                
                return templates.AsReadOnly();
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Failed to load templates: {ex.Message}", DomainNotificationType.Error);
                
                _logger.LogError(ex, "Failed to load flow templates");
                return Array.Empty<FlowTemplate>();
            }
        }

        public async Task<bool> SelectTemplateAsync(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                throw new ArgumentException("Template name cannot be null or empty", nameof(templateName));

            ShowProgress($"Selecting template '{templateName}'...", 0);

            try
            {
                if (!_availableTemplates.TryGetValue(templateName, out var template))
                {
                    HideProgress();
                    ShowNotification($"Template '{templateName}' not found", DomainNotificationType.Error);
                    return false;
                }

                _selectedTemplate = template;
                _currentSteps.Clear();
                _currentSteps.AddRange(template.DefaultSteps);

                PublishEvent(new TestFlowEvents.FlowTemplateSelected
                {
                    TemplateName = template.Name,
                    TemplateDescription = template.Description,
                    RequiredInputs = template.RequiredInputs.ToList()
                });

                HideProgress();
                ShowNotification($"Selected template: {templateName}", DomainNotificationType.Success);

                _logger.LogInformation("Selected flow template: {TemplateName}", templateName);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Template selection failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Template selection failed for {TemplateName}", templateName);
                return false;
            }
        }

        // ===== FLOW CONFIGURATION =====

        public async Task<bool> ConfigureFlowAsync(string flowName, Dictionary<string, object> configuration)
        {
            if (string.IsNullOrWhiteSpace(flowName))
                throw new ArgumentException("Flow name cannot be null or empty", nameof(flowName));

            ShowProgress("Configuring flow...", 0);

            try
            {
                _currentFlowId = Guid.NewGuid().ToString();
                _currentConfiguration = new Dictionary<string, object>(configuration ?? new Dictionary<string, object>());

                PublishEvent(new TestFlowEvents.FlowConfigured
                {
                    FlowName = flowName,
                    Configuration = _currentConfiguration,
                    TargetRequirements = _targetRequirements.ToList()
                });

                HideProgress();
                ShowNotification($"Configured flow: {flowName}", DomainNotificationType.Success);

                _logger.LogInformation("Configured flow: {FlowName} with ID: {FlowId}", flowName, _currentFlowId);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Flow configuration failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow configuration failed for {FlowName}", flowName);
                return false;
            }
        }

        public async Task<bool> SetTargetRequirementsAsync(IReadOnlyList<Requirement> requirements)
        {
            ShowProgress("Setting target requirements...", 0);

            try
            {
                _targetRequirements = requirements?.ToList() ?? new List<Requirement>();

                HideProgress();
                ShowNotification($"Set {_targetRequirements.Count} target requirements", DomainNotificationType.Success);

                _logger.LogInformation("Set {Count} target requirements for flow", _targetRequirements.Count);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Failed to set requirements: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Failed to set target requirements");
                return false;
            }
        }

        // ===== FLOW DESIGN =====

        public async Task<TestFlowStep> CreateFlowStepAsync(string stepName, string stepType)
        {
            if (string.IsNullOrWhiteSpace(stepName))
                throw new ArgumentException("Step name cannot be null or empty", nameof(stepName));

            ShowProgress($"Creating flow step '{stepName}'...", 0);

            try
            {
                var step = new TestFlowStep
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = stepName,
                    Type = stepType,
                    Description = $"Generated step for {stepName}",
                    Parameters = new Dictionary<string, object>(),
                    Dependencies = new List<string>(),
                    EstimatedDuration = TimeSpan.FromMinutes(5)
                };

                _currentSteps.Add(step);

                PublishEvent(new TestFlowEvents.FlowDesignUpdated
                {
                    FlowId = _currentFlowId ?? "unknown",
                    Steps = _currentSteps.ToList(),
                    StepConnections = _stepConnections
                });

                HideProgress();
                ShowNotification($"Created step: {stepName}", DomainNotificationType.Success);

                _logger.LogInformation("Created flow step: {StepName} ({StepId})", stepName, step.Id);
                return step;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Step creation failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow step creation failed for {StepName}", stepName);
                throw;
            }
        }

        public async Task<bool> UpdateFlowStepAsync(TestFlowStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));

            ShowProgress($"Updating step '{step.Name}'...", 0);

            try
            {
                var existingStep = _currentSteps.FirstOrDefault(s => s.Id == step.Id);
                if (existingStep == null)
                {
                    HideProgress();
                    ShowNotification($"Step '{step.Name}' not found", DomainNotificationType.Error);
                    return false;
                }

                var index = _currentSteps.IndexOf(existingStep);
                _currentSteps[index] = step;

                PublishEvent(new TestFlowEvents.FlowDesignUpdated
                {
                    FlowId = _currentFlowId ?? "unknown",
                    Steps = _currentSteps.ToList(),
                    StepConnections = _stepConnections
                });

                HideProgress();
                ShowNotification($"Updated step: {step.Name}", DomainNotificationType.Success);

                _logger.LogInformation("Updated flow step: {StepName} ({StepId})", step.Name, step.Id);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Step update failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow step update failed for {StepId}", step.Id);
                return false;
            }
        }

        public async Task<bool> RemoveFlowStepAsync(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                throw new ArgumentException("Step ID cannot be null or empty", nameof(stepId));

            ShowProgress("Removing flow step...", 0);

            try
            {
                var step = _currentSteps.FirstOrDefault(s => s.Id == stepId);
                if (step == null)
                {
                    HideProgress();
                    ShowNotification("Step not found", DomainNotificationType.Error);
                    return false;
                }

                _currentSteps.RemoveAll(s => s.Id == stepId);

                // Remove any connections involving this step
                var connectionsToRemove = _stepConnections.Where(kvp => 
                    kvp.Key == stepId || kvp.Value == stepId).ToList();
                
                foreach (var connection in connectionsToRemove)
                {
                    _stepConnections.Remove(connection.Key);
                }

                PublishEvent(new TestFlowEvents.FlowDesignUpdated
                {
                    FlowId = _currentFlowId ?? "unknown",
                    Steps = _currentSteps.ToList(),
                    StepConnections = _stepConnections
                });

                HideProgress();
                ShowNotification($"Removed step: {step.Name}", DomainNotificationType.Success);

                _logger.LogInformation("Removed flow step: {StepName} ({StepId})", step.Name, stepId);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Step removal failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow step removal failed for {StepId}", stepId);
                return false;
            }
        }

        public async Task<bool> ConnectStepsAsync(string fromStepId, string toStepId)
        {
            if (string.IsNullOrWhiteSpace(fromStepId))
                throw new ArgumentException("From step ID cannot be null or empty", nameof(fromStepId));
            if (string.IsNullOrWhiteSpace(toStepId))
                throw new ArgumentException("To step ID cannot be null or empty", nameof(toStepId));

            ShowProgress("Connecting flow steps...", 0);

            try
            {
                // Validate both steps exist
                var fromStep = _currentSteps.FirstOrDefault(s => s.Id == fromStepId);
                var toStep = _currentSteps.FirstOrDefault(s => s.Id == toStepId);

                if (fromStep == null || toStep == null)
                {
                    HideProgress();
                    ShowNotification("One or both steps not found", DomainNotificationType.Error);
                    return false;
                }

                _stepConnections[fromStepId] = toStepId;

                // Update dependencies
                if (!toStep.Dependencies.Contains(fromStepId))
                {
                    toStep.Dependencies.Add(fromStepId);
                }

                PublishEvent(new TestFlowEvents.FlowDesignUpdated
                {
                    FlowId = _currentFlowId ?? "unknown",
                    Steps = _currentSteps.ToList(),
                    StepConnections = _stepConnections
                });

                HideProgress();
                ShowNotification($"Connected {fromStep.Name} → {toStep.Name}", DomainNotificationType.Success);

                _logger.LogInformation("Connected flow steps: {FromStep} → {ToStep}", fromStep.Name, toStep.Name);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Step connection failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow step connection failed: {FromStepId} → {ToStepId}", fromStepId, toStepId);
                return false;
            }
        }

        // ===== FLOW VALIDATION =====

        public async Task<FlowValidationResult> ValidateFlowAsync(string flowId)
        {
            if (string.IsNullOrWhiteSpace(flowId))
                throw new ArgumentException("Flow ID cannot be null or empty", nameof(flowId));

            ShowProgress("Validating flow...", 0);

            var result = new FlowValidationResult();

            try
            {
                PublishEvent(new TestFlowEvents.FlowValidationStarted
                {
                    FlowId = flowId,
                    Steps = _currentSteps.ToList()
                });

                UpdateProgress("Checking flow structure...", 25);

                // Basic validation checks
                if (_currentSteps.Count == 0)
                {
                    result.Errors.Add("Flow must have at least one step");
                }

                // Check for circular dependencies
                if (HasCircularDependencies())
                {
                    result.Errors.Add("Flow has circular dependencies");
                }

                // Check for orphaned steps
                var orphanedSteps = FindOrphanedSteps();
                if (orphanedSteps.Any())
                {
                    result.Warnings.Add($"Found {orphanedSteps.Count} orphaned steps: {string.Join(", ", orphanedSteps.Select(s => s.Name))}");
                }

                UpdateProgress("Validating step configurations...", 75);

                // Validate individual steps
                foreach (var step in _currentSteps)
                {
                    var stepValidation = ValidateStep(step);
                    result.Errors.AddRange(stepValidation.errors);
                    result.Warnings.AddRange(stepValidation.warnings);
                }

                result.IsValid = result.Errors.Count == 0;

                PublishEvent(new TestFlowEvents.FlowValidated
                {
                    FlowId = flowId,
                    IsValid = result.IsValid,
                    ValidationErrors = result.Errors.ToList(),
                    Warnings = result.Warnings.ToList()
                });

                HideProgress();
                
                if (result.IsValid)
                {
                    ShowNotification("Flow validation successful", DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification($"Flow validation failed: {result.Errors.Count} errors", DomainNotificationType.Error);
                }

                _logger.LogInformation("Flow validation completed for {FlowId}: Valid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}", 
                    flowId, result.IsValid, result.Errors.Count, result.Warnings.Count);

                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Validation failed: {ex.Message}");

                HideProgress();
                ShowNotification($"Flow validation error: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow validation failed for {FlowId}", flowId);
                return result;
            }
        }

        // ===== FLOW EXECUTION =====

        public async Task<FlowExecutionResult> ExecuteFlowAsync(string flowId)
        {
            if (string.IsNullOrWhiteSpace(flowId))
                throw new ArgumentException("Flow ID cannot be null or empty", nameof(flowId));

            var executionId = Guid.NewGuid().ToString();
            var result = new FlowExecutionResult { ExecutionId = executionId };

            ShowProgress("Starting flow execution...", 0);

            try
            {
                var startTime = DateTime.Now;

                PublishEvent(new TestFlowEvents.FlowExecutionStarted
                {
                    FlowId = flowId,
                    ExecutionId = executionId,
                    Steps = _currentSteps.ToList()
                });

                // Execute steps in dependency order
                var executedSteps = new HashSet<string>();
                var totalSteps = _currentSteps.Count;
                var completedSteps = 0;

                while (executedSteps.Count < totalSteps && completedSteps < totalSteps * 2) // Prevent infinite loops
                {
                    var readySteps = _currentSteps.Where(step => 
                        !executedSteps.Contains(step.Id) &&
                        step.Dependencies.All(dep => executedSteps.Contains(dep))).ToList();

                    if (!readySteps.Any())
                    {
                        result.Errors.Add("Cannot execute flow: unresolvable dependencies or circular references");
                        break;
                    }

                    foreach (var step in readySteps)
                    {
                        var stepResult = await ExecuteStepAsync(executionId, step);
                        executedSteps.Add(step.Id);
                        completedSteps++;

                        var progress = (double)completedSteps / totalSteps * 100;
                        UpdateProgress($"Executing step: {step.Name}", progress);

                        if (!stepResult.Success && !step.IsOptional)
                        {
                            result.Errors.Add($"Required step failed: {step.Name}");
                            break;
                        }

                        result.Results[step.Id] = stepResult;
                    }

                    if (result.Errors.Any())
                        break;
                }

                result.Success = !result.Errors.Any() && executedSteps.Count == totalSteps;
                result.ExecutionTime = DateTime.Now - startTime;

                PublishEvent(new TestFlowEvents.FlowExecutionCompleted
                {
                    FlowId = flowId,
                    ExecutionId = executionId,
                    Success = result.Success,
                    Results = result.Results,
                    TotalExecutionTime = result.ExecutionTime,
                    Errors = result.Errors.ToList()
                });

                HideProgress();
                
                if (result.Success)
                {
                    ShowNotification($"Flow execution completed successfully in {result.ExecutionTime.TotalSeconds:F1}s", DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification($"Flow execution failed: {result.Errors.Count} errors", DomainNotificationType.Error);
                }

                _logger.LogInformation("Flow execution completed for {FlowId}: Success={Success}, Duration={Duration}ms, Steps={StepsExecuted}", 
                    flowId, result.Success, result.ExecutionTime.TotalMilliseconds, executedSteps.Count);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Execution failed: {ex.Message}");

                HideProgress();
                ShowNotification($"Flow execution error: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow execution failed for {FlowId}", flowId);
                return result;
            }
        }

        public async Task<FlowExecutionStatus> GetExecutionStatusAsync(string executionId)
        {
            // Placeholder implementation - in real system would track active executions
            return new FlowExecutionStatus
            {
                ExecutionId = executionId,
                Status = "Completed",
                CurrentStepId = "",
                ProgressPercentage = 100.0,
                ElapsedTime = TimeSpan.Zero
            };
        }

        // ===== RESULTS ANALYSIS =====

        public async Task<FlowAnalysisResult> AnalyzeResultsAsync(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                throw new ArgumentException("Execution ID cannot be null or empty", nameof(executionId));

            ShowProgress("Analyzing flow results...", 0);

            try
            {
                var result = new FlowAnalysisResult();

                // Placeholder analysis - in real implementation would use LLM for deep analysis
                UpdateProgress("Generating analysis with LLM...", 50);

                var prompt = $"Analyze the test flow execution results for execution {executionId}. Provide insights, recommendations, and overall assessment.";
                var analysis = await _llmService.GenerateAsync(prompt);

                result.Analysis["llm_analysis"] = analysis;
                result.Analysis["execution_id"] = executionId;
                result.Analysis["analyzed_at"] = DateTime.Now;

                // Generate basic recommendations
                result.Recommendations.Add("Review failed steps for improvement opportunities");
                result.Recommendations.Add("Consider adding additional validation steps");
                result.Recommendations.Add("Document lessons learned from this execution");

                result.OverallScore = 0.85; // Placeholder score

                PublishEvent(new TestFlowEvents.FlowResultsAnalyzed
                {
                    FlowId = _currentFlowId ?? "unknown",
                    ExecutionId = executionId,
                    Analysis = result.Analysis,
                    Recommendations = result.Recommendations.ToList()
                });

                HideProgress();
                ShowNotification("Results analysis completed", DomainNotificationType.Success);

                _logger.LogInformation("Flow results analyzed for execution {ExecutionId}: Score={OverallScore:F2}", 
                    executionId, result.OverallScore);

                return result;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Results analysis failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow results analysis failed for execution {ExecutionId}", executionId);
                throw;
            }
        }

        public async Task<bool> ExportResultsAsync(string executionId, string format, string filePath)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                throw new ArgumentException("Execution ID cannot be null or empty", nameof(executionId));
            if (string.IsNullOrWhiteSpace(format))
                throw new ArgumentException("Format cannot be null or empty", nameof(format));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            ShowProgress($"Exporting results to {format}...", 0);

            try
            {
                // Placeholder export implementation
                await Task.Delay(1000); // Simulate export time

                HideProgress();
                ShowNotification($"Results exported to {filePath}", DomainNotificationType.Success);

                _logger.LogInformation("Flow results exported: ExecutionId={ExecutionId}, Format={Format}, Path={FilePath}", 
                    executionId, format, filePath);

                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Export failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Flow results export failed for execution {ExecutionId}", executionId);
                return false;
            }
        }

        // ===== CROSS-DOMAIN COMMUNICATION =====

        public override void RequestCrossDomainAction<T>(T request) where T : class
        {
            base.RequestCrossDomainAction(request);
        }

        public override void BroadcastToAllDomains<T>(T notification) where T : class
        {
            base.BroadcastToAllDomains(notification);
        }

        // ===== HELPER METHODS =====

        private void InitializeTemplates()
        {
            // Initialize with some default templates
            _availableTemplates["basic"] = new FlowTemplate
            {
                Name = "Basic Test Flow",
                Description = "A simple linear test flow for basic scenarios",
                RequiredInputs = new List<string> { "target_system", "test_data" },
                DefaultSteps = new List<TestFlowStep>
                {
                    new TestFlowStep { Id = "setup", Name = "Setup", Type = "Setup", Description = "Initialize test environment" },
                    new TestFlowStep { Id = "execute", Name = "Execute", Type = "TestCase", Description = "Run test cases" },
                    new TestFlowStep { Id = "validate", Name = "Validate", Type = "Validation", Description = "Validate results" },
                    new TestFlowStep { Id = "cleanup", Name = "Cleanup", Type = "Cleanup", Description = "Clean up resources" }
                }
            };

            _availableTemplates["comprehensive"] = new FlowTemplate
            {
                Name = "Comprehensive Test Flow",
                Description = "Full test flow with analysis and reporting",
                RequiredInputs = new List<string> { "requirements", "test_data", "environment_config" },
                DefaultSteps = new List<TestFlowStep>
                {
                    new TestFlowStep { Id = "analyze", Name = "Analyze Requirements", Type = "Analysis", Description = "Analyze requirements for testability" },
                    new TestFlowStep { Id = "design", Name = "Design Tests", Type = "Design", Description = "Design test cases" },
                    new TestFlowStep { Id = "setup", Name = "Setup Environment", Type = "Setup", Description = "Prepare test environment" },
                    new TestFlowStep { Id = "execute", Name = "Execute Tests", Type = "TestCase", Description = "Run all test cases" },
                    new TestFlowStep { Id = "validate", Name = "Validate Results", Type = "Validation", Description = "Validate test outcomes" },
                    new TestFlowStep { Id = "analyze_results", Name = "Analyze Results", Type = "Analysis", Description = "Analyze test results" },
                    new TestFlowStep { Id = "report", Name = "Generate Report", Type = "Reporting", Description = "Generate comprehensive report" },
                    new TestFlowStep { Id = "cleanup", Name = "Cleanup", Type = "Cleanup", Description = "Clean up all resources" }
                }
            };
        }

        private bool HasCircularDependencies()
        {
            // Simplified circular dependency detection
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var step in _currentSteps)
            {
                if (HasCircularDependencyHelper(step.Id, visited, recursionStack))
                    return true;
            }

            return false;
        }

        private bool HasCircularDependencyHelper(string stepId, HashSet<string> visited, HashSet<string> recursionStack)
        {
            visited.Add(stepId);
            recursionStack.Add(stepId);

            var step = _currentSteps.FirstOrDefault(s => s.Id == stepId);
            if (step != null)
            {
                foreach (var dependency in step.Dependencies)
                {
                    if (!visited.Contains(dependency))
                    {
                        if (HasCircularDependencyHelper(dependency, visited, recursionStack))
                            return true;
                    }
                    else if (recursionStack.Contains(dependency))
                    {
                        return true;
                    }
                }
            }

            recursionStack.Remove(stepId);
            return false;
        }

        private List<TestFlowStep> FindOrphanedSteps()
        {
            // Find steps with no incoming dependencies and no connections
            var stepsWithIncomingConnections = new HashSet<string>(_stepConnections.Values);
            var stepsWithDependents = new HashSet<string>();

            foreach (var step in _currentSteps)
            {
                foreach (var dependency in step.Dependencies)
                {
                    stepsWithDependents.Add(dependency);
                }
            }

            return _currentSteps.Where(step => 
                !stepsWithIncomingConnections.Contains(step.Id) && 
                !stepsWithDependents.Contains(step.Id) && 
                step.Dependencies.Count == 0).ToList();
        }

        private (List<string> errors, List<string> warnings) ValidateStep(TestFlowStep step)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"Step {step.Id} must have a name");

            if (string.IsNullOrWhiteSpace(step.Type))
                errors.Add($"Step {step.Name} must have a type");

            if (step.EstimatedDuration == TimeSpan.Zero)
                warnings.Add($"Step {step.Name} has no estimated duration");

            return (errors, warnings);
        }

        private async Task<StepExecutionResult> ExecuteStepAsync(string executionId, TestFlowStep step)
        {
            var stepStart = DateTime.Now;

            try
            {
                PublishEvent(new TestFlowEvents.FlowStepCompleted
                {
                    FlowId = _currentFlowId ?? "unknown",
                    ExecutionId = executionId,
                    StepId = step.Id,
                    Success = true,
                    Result = $"Step {step.Name} executed successfully",
                    ExecutionTime = DateTime.Now - stepStart
                });

                // Simulate step execution
                await Task.Delay(100);

                return new StepExecutionResult
                {
                    Success = true,
                    Result = $"Step {step.Name} completed successfully",
                    ExecutionTime = DateTime.Now - stepStart
                };
            }
            catch (Exception ex)
            {
                PublishEvent(new TestFlowEvents.FlowStepCompleted
                {
                    FlowId = _currentFlowId ?? "unknown",
                    ExecutionId = executionId,
                    StepId = step.Id,
                    Success = false,
                    Result = ex.Message,
                    ExecutionTime = DateTime.Now - stepStart
                });

                return new StepExecutionResult
                {
                    Success = false,
                    Result = ex.Message,
                    ExecutionTime = DateTime.Now - stepStart
                };
            }
        }

        private class StepExecutionResult
        {
            public bool Success { get; set; }
            public string Result { get; set; } = string.Empty;
            public TimeSpan ExecutionTime { get; set; }
        }
    }
}