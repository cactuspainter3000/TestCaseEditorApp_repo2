using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestFlow.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Coordinates communication between domain mediators while maintaining domain isolation.
    /// Acts as a message broker for TestCaseGeneration ↔ TestFlow coordination.
    /// </summary>
    public class DomainCoordinator : IDomainCoordinator
    {
        private readonly ILogger<DomainCoordinator> _logger;
        private readonly Dictionary<string, object> _registeredMediators = new();
        private readonly object _lockObject = new object();

        public event EventHandler<CrossDomainCommunicationEventArgs>? CrossDomainCommunicationOccurred;

        public DomainCoordinator(ILogger<DomainCoordinator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug("DomainCoordinator created");
        }

        // ===== REGISTRATION MANAGEMENT =====

        public void RegisterDomainMediator(string domainName, object mediator)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                throw new ArgumentException("Domain name cannot be null or empty", nameof(domainName));
            if (mediator == null)
                throw new ArgumentNullException(nameof(mediator));

            lock (_lockObject)
            {
                _registeredMediators[domainName] = mediator;
                _logger.LogInformation("Registered domain mediator: {DomainName} ({MediatorType})", 
                    domainName, mediator.GetType().Name);
            }
        }

        public void UnregisterDomainMediator(string domainName)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                return;

            lock (_lockObject)
            {
                if (_registeredMediators.Remove(domainName))
                {
                    _logger.LogInformation("Unregistered domain mediator: {DomainName}", domainName);
                }
            }
        }

        public bool IsDomainAvailable(string domainName)
        {
            if (string.IsNullOrWhiteSpace(domainName))
                return false;

            lock (_lockObject)
            {
                return _registeredMediators.ContainsKey(domainName);
            }
        }

        public string[] GetRegisteredDomains()
        {
            lock (_lockObject)
            {
                return _registeredMediators.Keys.ToArray();
            }
        }

        // ===== CROSS-DOMAIN REQUEST HANDLING =====

        public async Task<T?> HandleCrossDomainRequestAsync<T>(object request, string requestingDomain) where T : class
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(requestingDomain))
                throw new ArgumentException("Requesting domain cannot be null or empty", nameof(requestingDomain));

            var startTime = DateTime.Now;
            var requestType = request.GetType().Name;
            
            _logger.LogDebug("Handling cross-domain request: {RequestType} from {RequestingDomain}", 
                requestType, requestingDomain);

            try
            {
                var response = request switch
                {
                    // ===== TESTCASE GENERATION → TESTFLOW REQUESTS =====
                    CrossDomainMessages.CreateFlowFromTestCases createFlowRequest =>
                        await HandleCreateFlowFromTestCasesAsync(createFlowRequest) as T,

                    CrossDomainMessages.ValidateTestCasesAgainstFlow validateRequest =>
                        await HandleValidateTestCasesAgainstFlowAsync(validateRequest) as T,

                    CrossDomainMessages.RequestFlowTemplates templatesRequest =>
                        await HandleRequestFlowTemplatesAsync(templatesRequest) as T,

                    // ===== TESTFLOW → TESTCASE GENERATION REQUESTS =====
                    CrossDomainMessages.AnalyzeRequirementsForFlow analyzeRequest =>
                        await HandleAnalyzeRequirementsForFlowAsync(analyzeRequest) as T,

                    CrossDomainMessages.GenerateTestCasesForFlowSteps generateRequest =>
                        await HandleGenerateTestCasesForFlowStepsAsync(generateRequest) as T,

                    CrossDomainMessages.RequestRequirementQualityAnalysis qualityRequest =>
                        await HandleRequestRequirementQualityAnalysisAsync(qualityRequest) as T,

                    // ===== WORKSPACE MANAGEMENT REQUESTS =====
                    ShowWorkspaceSelectionModalRequest workspaceRequest =>
                        await HandleShowWorkspaceSelectionModal(workspaceRequest) as T,

                    NavigateToSectionRequest navigationRequest =>
                        HandleNavigateToSection(navigationRequest) as T,

                    _ => throw new NotSupportedException($"Request type {requestType} is not supported")
                };

                var processingTime = DateTime.Now - startTime;
                
                FireCommunicationEvent(new CrossDomainCommunicationEventArgs
                {
                    RequestingDomain = requestingDomain,
                    RespondingDomain = GetRespondingDomain(requestType),
                    RequestType = requestType,
                    ResponseType = typeof(T).Name,
                    Success = response != null,
                    ProcessingTime = processingTime
                });

                _logger.LogDebug("Cross-domain request completed: {RequestType} in {ProcessingTime}ms", 
                    requestType, processingTime.TotalMilliseconds);

                return response;
            }
            catch (Exception ex)
            {
                var processingTime = DateTime.Now - startTime;
                
                FireCommunicationEvent(new CrossDomainCommunicationEventArgs
                {
                    RequestingDomain = requestingDomain,
                    RespondingDomain = GetRespondingDomain(requestType),
                    RequestType = requestType,
                    ResponseType = typeof(T).Name,
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTime = processingTime
                });

                _logger.LogError(ex, "Cross-domain request failed: {RequestType} from {RequestingDomain}", 
                    requestType, requestingDomain);

                return null;
            }
        }

        // ===== BROADCAST HANDLING =====

        public async Task BroadcastNotificationAsync<T>(T notification, string originatingDomain) where T : class
        {
            if (notification == null) throw new ArgumentNullException(nameof(notification));
            if (string.IsNullOrWhiteSpace(originatingDomain))
                throw new ArgumentException("Originating domain cannot be null or empty", nameof(originatingDomain));

            var notificationType = typeof(T).Name;
            _logger.LogDebug("Broadcasting notification: {NotificationType} from {OriginatingDomain}", 
                notificationType, originatingDomain);

            var mediators = GetAllMediators().Where(kvp => kvp.Key != originatingDomain).ToList();

            var tasks = mediators.Select(async kvp =>
            {
                try
                {
                    await BroadcastToMediatorAsync(kvp.Key, kvp.Value, notification);
                    _logger.LogDebug("Broadcast delivered to {DomainName}: {NotificationType}", 
                        kvp.Key, notificationType);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deliver broadcast to {DomainName}: {NotificationType}", 
                        kvp.Key, notificationType);
                }
            });

            await Task.WhenAll(tasks);
        }

        // ===== REQUEST HANDLERS =====

        private async Task<CrossDomainMessages.FlowCreationResponse> HandleCreateFlowFromTestCasesAsync(
            CrossDomainMessages.CreateFlowFromTestCases request)
        {
            var testFlowMediator = GetMediator<ITestFlowMediator>("TestFlow");
            if (testFlowMediator == null)
            {
                return new CrossDomainMessages.FlowCreationResponse
                {
                    Success = false,
                    Message = "TestFlow domain is not available"
                };
            }

            try
            {
                // Configure flow from test cases
                var success = await testFlowMediator.ConfigureFlowAsync(
                    request.FlowName, 
                    request.FlowConfiguration);

                if (success && request.TestCases.Any())
                {
                    // Note: GeneratedTestCase doesn't have SourceRequirement property
                    // This is a placeholder implementation - would need to track requirement relationships
                    // differently in a real implementation
                    var requirements = new List<Requirement>();
                    await testFlowMediator.SetTargetRequirementsAsync(requirements);
                }

                var flowId = success ? Guid.NewGuid().ToString() : string.Empty;

                return new CrossDomainMessages.FlowCreationResponse
                {
                    Success = success,
                    FlowId = flowId,
                    Message = success ? "Flow created successfully" : "Flow creation failed"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create flow from test cases");
                return new CrossDomainMessages.FlowCreationResponse
                {
                    Success = false,
                    Message = $"Flow creation failed: {ex.Message}"
                };
            }
        }

        private async Task<CrossDomainMessages.ValidationResponse> HandleValidateTestCasesAgainstFlowAsync(
            CrossDomainMessages.ValidateTestCasesAgainstFlow request)
        {
            var testFlowMediator = GetMediator<ITestFlowMediator>("TestFlow");
            if (testFlowMediator == null)
            {
                return new CrossDomainMessages.ValidationResponse
                {
                    Success = false,
                    Message = "TestFlow domain is not available",
                    RespondingDomain = "TestFlow"
                };
            }

            try
            {
                // Get available templates
                var templates = await testFlowMediator.GetAvailableTemplatesAsync();
                var targetTemplate = templates.FirstOrDefault(t => t.Name == request.FlowTemplateName);

                if (targetTemplate == null)
                {
                    return new CrossDomainMessages.ValidationResponse
                    {
                        Success = false,
                        IsValid = false,
                        ValidationErrors = { $"Flow template '{request.FlowTemplateName}' not found" },
                        Message = "Validation failed",
                        RespondingDomain = "TestFlow"
                    };
                }

                // Validate test cases against template requirements
                var validationErrors = new List<string>();
                var validationWarnings = new List<string>();
                var recommendations = new List<string>();

                foreach (var testCase in request.TestCases)
                {
                    if (string.IsNullOrWhiteSpace(testCase.Steps))
                    {
                        validationErrors.Add($"Test case '{testCase.Title}' has no steps");
                    }

                    if (string.IsNullOrWhiteSpace(testCase.ExpectedResults))
                    {
                        validationWarnings.Add($"Test case '{testCase.Title}' has no expected results");
                    }
                }

                if (request.IncludeRecommendations)
                {
                    recommendations.Add("Consider adding setup and teardown steps");
                    recommendations.Add("Ensure test cases cover all flow branches");
                }

                return new CrossDomainMessages.ValidationResponse
                {
                    Success = true,
                    IsValid = validationErrors.Count == 0,
                    ValidationErrors = validationErrors,
                    ValidationWarnings = validationWarnings,
                    Recommendations = recommendations,
                    Message = "Validation completed",
                    RespondingDomain = "TestFlow"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate test cases against flow");
                return new CrossDomainMessages.ValidationResponse
                {
                    Success = false,
                    Message = $"Validation failed: {ex.Message}",
                    RespondingDomain = "TestFlow"
                };
            }
        }

        private async Task<CrossDomainMessages.FlowTemplateResponse> HandleRequestFlowTemplatesAsync(
            CrossDomainMessages.RequestFlowTemplates request)
        {
            var testFlowMediator = GetMediator<ITestFlowMediator>("TestFlow");
            if (testFlowMediator == null)
            {
                return new CrossDomainMessages.FlowTemplateResponse
                {
                    Success = false,
                    Message = "TestFlow domain is not available"
                };
            }

            try
            {
                var templates = await testFlowMediator.GetAvailableTemplatesAsync();
                
                // Convert to Models.FlowTemplate for response (simplified conversion)
                var responseTemplates = templates.Select(t => new TestCaseEditorApp.MVVM.Models.FlowTemplate
                {
                    Name = t.Name,
                    Description = t.Description,
                    RequiredInputs = t.RequiredInputs,
                    DefaultSteps = t.DefaultSteps,
                    Author = "Unknown", // Default value - not available in ITestFlowMediator.FlowTemplate
                    Version = new Version(1, 0), // Default value
                    CreatedAt = DateTime.Now, // Default value
                    Metadata = new Dictionary<string, object>() // Default value
                }).ToList();
                
                var recommendations = new List<string>();
                if (request.TargetRequirements.Any())
                {
                    // Note: Requirement doesn't have TestCases collection
                    // This is simplified logic for template recommendations
                    var requirementCount = request.TargetRequirements.Count;
                    if (requirementCount > 5)
                    {
                        recommendations.Add("Consider using 'Comprehensive' template for many requirements");
                    }
                    else
                    {
                        recommendations.Add("'Basic' template should be sufficient for these requirements");
                    }
                }

                return new CrossDomainMessages.FlowTemplateResponse
                {
                    Success = true,
                    AvailableTemplates = responseTemplates,
                    Recommendations = recommendations,
                    Message = $"Retrieved {templates.Count} flow templates"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve flow templates");
                return new CrossDomainMessages.FlowTemplateResponse
                {
                    Success = false,
                    Message = $"Failed to retrieve templates: {ex.Message}"
                };
            }
        }

        private async Task<CrossDomainMessages.RequirementAnalysisResponse> HandleAnalyzeRequirementsForFlowAsync(
            CrossDomainMessages.AnalyzeRequirementsForFlow request)
        {
            await Task.CompletedTask;
            var testCaseGenMediator = GetMediator<ITestCaseGenerationMediator>("TestCaseGeneration");
            if (testCaseGenMediator == null)
            {
                return new CrossDomainMessages.RequirementAnalysisResponse
                {
                    Success = false,
                    Message = "TestCaseGeneration domain is not available"
                };
            }

            try
            {
                var analysisResults = new Dictionary<string, RequirementAnalysis>();
                var recommendations = new List<string>();

                foreach (var requirement in request.Requirements)
                {
                    // Create simplified analysis result since AnalyzeRequirementAsync only returns bool
                    var analysis = new RequirementAnalysis
                    {
                        OriginalQualityScore = 4, // Mock score (1-10 scale)
                        IsAnalyzed = true,
                        Timestamp = DateTime.Now
                    };
                    
                    // Use requirement text as key since there's no Id property
                    var key = requirement.Description?.Length > 50 ? requirement.Description.Substring(0, 50) + "..." : requirement.Description ?? "Unknown";
                    analysisResults[key] = analysis;
                }

                if (analysisResults.Any())
                {
                    var analyzedCount = analysisResults.Values.Count(a => a.IsAnalyzed);
                    recommendations.Add($"{analyzedCount} of {analysisResults.Count} requirements have been analyzed");
                    
                    if (analyzedCount < analysisResults.Count)
                    {
                        recommendations.Add("Consider analyzing remaining requirements before flow creation");
                    }
                }

                return new CrossDomainMessages.RequirementAnalysisResponse
                {
                    Success = true,
                    AnalysisResults = analysisResults,
                    Recommendations = recommendations,
                    Message = $"Analyzed {analysisResults.Count} requirements"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze requirements for flow");
                return new CrossDomainMessages.RequirementAnalysisResponse
                {
                    Success = false,
                    Message = $"Analysis failed: {ex.Message}"
                };
            }
        }

        private async Task<CrossDomainMessages.TestCaseGenerationResponse> HandleGenerateTestCasesForFlowStepsAsync(
            CrossDomainMessages.GenerateTestCasesForFlowSteps request)
        {
            var testCaseGenMediator = GetMediator<ITestCaseGenerationMediator>("TestCaseGeneration");
            if (testCaseGenMediator == null)
            {
                return new CrossDomainMessages.TestCaseGenerationResponse
                {
                    Success = false,
                    Message = "TestCaseGeneration domain is not available"
                };
            }

            try
            {
                var generatedTestCases = new List<GeneratedTestCase>();

                // Simplified implementation for architectural proof-of-concept
                // In a real implementation, this would generate test cases based on flow steps
                foreach (var step in request.FlowSteps.Take(3)) // Limit for demo
                {
                    var generatedTc = new GeneratedTestCase();
                    generatedTc.SetPropertiesForLoad(
                        $"Test case for {step.Name}", 
                        "Setup test environment", 
                        $"Execute: {step.Description}", 
                        "Verify expected behavior");
                    generatedTestCases.Add(generatedTc);
                }

                var metadata = new Dictionary<string, object>
                {
                    { "flow_steps_count", request.FlowSteps.Count },
                    { "source_requirements_count", request.SourceRequirements.Count },
                    { "generated_test_cases_count", generatedTestCases.Count }
                };

                await Task.CompletedTask;
                return new CrossDomainMessages.TestCaseGenerationResponse
                {
                    Success = true,
                    GeneratedTestCases = generatedTestCases,
                    Message = $"Generated {generatedTestCases.Count} test cases for flow steps",
                    GenerationMetadata = metadata
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate test cases for flow steps");
                return new CrossDomainMessages.TestCaseGenerationResponse
                {
                    Success = false,
                    Message = $"Generation failed: {ex.Message}"
                };
            }
        }

        private async Task<CrossDomainMessages.RequirementAnalysisResponse> HandleRequestRequirementQualityAnalysisAsync(
            CrossDomainMessages.RequestRequirementQualityAnalysis request)
        {
            var testCaseGenMediator = GetMediator<ITestCaseGenerationMediator>("TestCaseGeneration");
            if (testCaseGenMediator == null)
            {
                return new CrossDomainMessages.RequirementAnalysisResponse
                {
                    Success = false,
                    Message = "TestCaseGeneration domain is not available"
                };
            }

            try
            {
                var analysisResults = new Dictionary<string, RequirementAnalysis>();
                var recommendations = new List<string>();

                // Simplified implementation for architectural proof-of-concept
                for (int i = 0; i < Math.Min(request.Requirements.Count, 5); i++)
                {
                    var requirement = request.Requirements[i];
                    var key = $"Requirement_{i + 1}";
                    
                    // Create simplified analysis result
                    var analysis = new RequirementAnalysis
                    {
                        OriginalQualityScore = 3 + (i % 3), // Mock scores between 3-5
                        IsAnalyzed = true,
                        Timestamp = DateTime.Now
                    };
                    
                    analysisResults[key] = analysis;
                }

                // Generate flow-specific recommendations
                var qualityScore = analysisResults.Values.Average(a => a.OriginalQualityScore);
                recommendations.Add($"Average requirement quality score: {qualityScore:F2}/5.0");

                if (qualityScore < 3.0)
                {
                    recommendations.Add("Requirements quality is below average - consider refinement before flow creation");
                }
                else if (qualityScore >= 4.0)
                {
                    recommendations.Add("Requirements are high quality - well suited for flow automation");
                }

                await Task.CompletedTask;
                return new CrossDomainMessages.RequirementAnalysisResponse
                {
                    Success = true,
                    AnalysisResults = analysisResults,
                    Recommendations = recommendations,
                    Message = $"Quality analysis completed for {analysisResults.Count} requirements"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform requirement quality analysis");
                return new CrossDomainMessages.RequirementAnalysisResponse
                {
                    Success = false,
                    Message = $"Quality analysis failed: {ex.Message}"
                };
            }
        }

        // ===== HELPER METHODS =====

        private T? GetMediator<T>(string domainName) where T : class
        {
            lock (_lockObject)
            {
                if (_registeredMediators.TryGetValue(domainName, out var mediator))
                {
                    return mediator as T;
                }
                return null;
            }
        }

        private KeyValuePair<string, object>[] GetAllMediators()
        {
            lock (_lockObject)
            {
                return _registeredMediators.ToArray();
            }
        }

        private async Task BroadcastToMediatorAsync<T>(string domainName, object mediator, T notification) where T : class
        {
            try
            {
                // Use reflection to call HandleBroadcastNotification method on the mediator
                var method = mediator.GetType().GetMethod("HandleBroadcastNotification");
                if (method != null)
                {
                    var genericMethod = method.MakeGenericMethod(typeof(T));
                    genericMethod.Invoke(mediator, new object[] { notification });
                    
                    _logger.LogDebug("Delivered broadcast to {DomainName}: {NotificationType}", 
                        domainName, typeof(T).Name);
                }
                else
                {
                    _logger.LogDebug("Mediator {DomainName} does not handle broadcast notifications", domainName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver broadcast to {DomainName}: {NotificationType}", 
                    domainName, typeof(T).Name);
            }
            
            await Task.CompletedTask;
        }

        private string GetRespondingDomain(string requestType)
        {
            return requestType switch
            {
                "CreateFlowFromTestCases" => "TestFlow",
                "ValidateTestCasesAgainstFlow" => "TestFlow", 
                "RequestFlowTemplates" => "TestFlow",
                "AnalyzeRequirementsForFlow" => "TestCaseGeneration",
                "GenerateTestCasesForFlowSteps" => "TestCaseGeneration",
                "RequestRequirementQualityAnalysis" => "TestCaseGeneration",
                "ShowWorkspaceSelectionModalRequest" => "Workspace Management",
                "NavigateToSectionRequest" => "Navigation",
                _ => "Unknown"
            };
        }

        private void FireCommunicationEvent(CrossDomainCommunicationEventArgs args)
        {
            try
            {
                CrossDomainCommunicationOccurred?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error firing cross-domain communication event");
            }
        }

        /// <summary>
        /// Handle navigation request by using the navigation mediator
        /// </summary>
        private object HandleNavigateToSection(NavigateToSectionRequest request)
        {
            _logger.LogInformation("Navigation requested: Section={Section}, Context={Context}", 
                request.SectionName, request.Context);

            // Get the navigation mediator and navigate
            if (_registeredMediators.TryGetValue("Navigation", out var mediatorObj) 
                && mediatorObj is INavigationMediator navigationMediator)
            {
                navigationMediator.NavigateToSection(request.SectionName);
                return new { Success = true, Message = $"Navigated to {request.SectionName}" };
            }
            else
            {
                _logger.LogWarning("NavigationMediator not found in registered mediators");
                return new { Success = false, Message = "NavigationMediator not available" };
            }
        }

        /// <summary>
        /// Handle workspace selection modal request - simulate workspace selection for now
        /// </summary>
        private async Task<object> HandleShowWorkspaceSelectionModal(ShowWorkspaceSelectionModalRequest request)
        {
            _logger.LogInformation("Workspace selection modal requested: ForOpenExisting={ForOpenExisting}, DomainContext={DomainContext}", 
                request.ForOpenExisting, request.DomainContext);

            // Simulate immediate workspace selection with default values
            if (_registeredMediators.TryGetValue("NewProject", out var mediatorObj) 
                && mediatorObj is INewProjectMediator newProjectMediator)
            {
                _logger.LogInformation("Simulating workspace selection for new project");
                
                // Simulate user selecting a default workspace
                await newProjectMediator.OnWorkspaceSelectedAsync(
                    "default-workspace", 
                    "Default Workspace", 
                    !request.ForOpenExisting);
                
                return new { Success = true, Message = "Workspace selected automatically" };
            }
            else
            {
                _logger.LogWarning("NewProjectMediator not found in registered mediators");
                return new { Success = false, Message = "NewProjectMediator not available" };
            }
        }
    }
}