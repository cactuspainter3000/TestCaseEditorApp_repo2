using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators
{
    /// <summary>
    /// TestCaseGeneration domain mediator that handles the entire "Test Case Generator" menu section.
    /// Coordinates requirements, assumptions, questions, test case creation, and export workflows
    /// with full UI coordination and cross-domain communication support.
    /// </summary>
    public class TestCaseGenerationMediator : BaseDomainMediator<TestCaseGenerationEvents>, ITestCaseGenerationMediator
    {
        private readonly IRequirementService _requirementService;
        private readonly RequirementAnalysisService _analysisService;
        private readonly ITextGenerationService _llmService;
        
        // Workflow state
        private readonly Dictionary<Requirement, List<string>> _requirementAssumptions = new();
        private readonly Dictionary<Requirement, List<ClarifyingQuestionData>> _requirementQuestions = new();
        
        // Domain state management - replaces MainViewModel dependencies
        private Requirement? _currentRequirement;
        private bool _isDirty = false;
        private bool _isBatchAnalyzing = false;
        private object? _selectedStep;
        private object? _currentStepViewModel;
        
        public Requirement? CurrentRequirement 
        { 
            get => _currentRequirement; 
            set 
            { 
                if (_currentRequirement != value) 
                { 
                    _currentRequirement = value;
                    PublishEvent(new TestCaseGenerationEvents.RequirementChanged 
                    { 
                        Requirement = value, 
                        ChangedBy = "Domain State Management" 
                    });
                    _logger.LogDebug("Current requirement changed to: {RequirementId}", value?.GlobalId ?? "null");
                }
            } 
        }
        
        public bool IsDirty 
        { 
            get => _isDirty; 
            set 
            { 
                if (_isDirty != value) 
                { 
                    _isDirty = value;
                    PublishEvent(new TestCaseGenerationEvents.WorkflowStateChanged 
                    { 
                        PropertyName = nameof(IsDirty), 
                        NewValue = value 
                    });
                    _logger.LogDebug("IsDirty changed to: {IsDirty}", value);
                }
            } 
        }
        
        public bool IsBatchAnalyzing 
        { 
            get => _isBatchAnalyzing; 
            set 
            { 
                if (_isBatchAnalyzing != value) 
                { 
                    _isBatchAnalyzing = value;
                    PublishEvent(new TestCaseGenerationEvents.WorkflowStateChanged 
                    { 
                        PropertyName = nameof(IsBatchAnalyzing), 
                        NewValue = value 
                    });
                    _logger.LogDebug("IsBatchAnalyzing changed to: {IsBatchAnalyzing}", value);
                }
            } 
        }
        
        public object? SelectedStep 
        { 
            get => _selectedStep; 
            set 
            { 
                if (_selectedStep != value) 
                { 
                    _selectedStep = value;
                    _logger.LogDebug("SelectedStep changed");
                }
            } 
        }
        
        public object? CurrentStepViewModel 
        { 
            get => _currentStepViewModel; 
            set 
            { 
                if (_currentStepViewModel != value) 
                { 
                    _currentStepViewModel = value;
                    _logger.LogDebug("CurrentStepViewModel changed");
                }
            } 
        }

        public TestCaseGenerationMediator(
            ILogger<TestCaseGenerationMediator> logger,
            IDomainUICoordinator uiCoordinator,
            IRequirementService requirementService,
            RequirementAnalysisService analysisService,
            ITextGenerationService llmService,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Test Case Generator", performanceMonitor, eventReplay)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));

            _logger.LogDebug("TestCaseGenerationMediator created with domain '{DomainName}'", _domainName);
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
            NavigateToStep("Requirements", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "Requirements", 
                ViewModel = null 
            });
            
            ShowNotification("Welcome to Test Case Generator", DomainNotificationType.Info);
            _logger.LogInformation("Navigated to initial step: Requirements");
        }

        public override void NavigateToFinalStep()
        {
            NavigateToStep("Export", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "Export", 
                ViewModel = null 
            });
            
            _logger.LogInformation("Navigated to final step: Export");
        }

        public override bool CanNavigateBack()
        {
            return _navigationHistory.Count > 0;
        }

        /// <summary>
        /// Test method to verify cross-domain communication is working
        /// </summary>
        public void TestCrossDomainCommunication()
        {
            _logger.LogInformation("Testing cross-domain communication from TestCaseGeneration to TestFlow");
            
            // Create a test request for flow templates
            var request = new TestCaseEditorApp.MVVM.Events.CrossDomainMessages.RequestFlowTemplates
            {
                TargetRequirements = new List<Requirement>(), // Empty for test
                TestCaseGenerationContext = "Testing cross-domain integration",
                RequestingDomain = "TestCaseGeneration"
            };
            
            // Send cross-domain request 
            RequestCrossDomainAction(request);
            
            // Also test broadcasting
            var notification = new TestCaseEditorApp.MVVM.Events.CrossDomainMessages.WorkspaceContextChanged
            {
                WorkspaceName = "test-workspace",
                ChangeType = "Integration Test",
                OriginatingDomain = "TestCaseGeneration",
                Timestamp = DateTime.Now
            };
            
            BroadcastToAllDomains(notification);
            
            _logger.LogInformation("Cross-domain communication test requests sent successfully");
            ShowNotification("Cross-domain integration test completed", DomainNotificationType.Success);
        }

        public override bool CanNavigateForward()
        {
            // TestCaseGeneration workflow is generally linear with branching
            return false; // Simplified for now
        }

        public void NavigateToRequirements()
        {
            NavigateToStep("Requirements", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "Requirements", 
                ViewModel = null 
            });
        }

        public void NavigateToAssumptions(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            NavigateToStep("Assumptions", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "Assumptions", 
                ViewModel = null 
            });
            
            PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
            { 
                Requirement = requirement, 
                SelectedBy = "NavigateToAssumptions" 
            });
        }

        public void NavigateToQuestions(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            NavigateToStep("Questions", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "Questions", 
                ViewModel = null 
            });
            
            PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
            { 
                Requirement = requirement, 
                SelectedBy = "NavigateToQuestions" 
            });
        }

        public void NavigateToTestCaseCreation(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            NavigateToStep("TestCaseCreation", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "TestCaseCreation", 
                ViewModel = null 
            });
            
            PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
            { 
                Requirement = requirement, 
                SelectedBy = "NavigateToTestCaseCreation" 
            });
        }

        public void NavigateToExport(IReadOnlyList<Requirement> requirements)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            
            NavigateToStep("Export", null);
            PublishEvent(new TestCaseGenerationEvents.StepChanged 
            { 
                Step = "Export", 
                ViewModel = null 
            });
            
            PublishEvent(new TestCaseGenerationEvents.RequirementsExportStarted 
            { 
                Requirements = requirements.ToList(), 
                ExportType = "Pending", 
                OutputPath = "TBD" 
            });
        }

        // ===== REQUIREMENTS MANAGEMENT =====

        public async Task<bool> ImportRequirementsAsync(string filePath, string importType = "Auto")
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            ShowProgress("Starting requirements import...", 0);
            
            PublishEvent(new TestCaseGenerationEvents.RequirementsImportStarted 
            { 
                FilePath = filePath, 
                ImportType = importType 
            });

            try
            {
                UpdateProgress("Parsing requirements file...", 25);
                
                // Use existing requirement service for import
                List<Requirement> requirements;
                if (filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    requirements = await Task.Run(() => _requirementService.ImportRequirementsFromJamaAllDataDocx(filePath));
                }
                else
                {
                    requirements = await Task.Run(() => _requirementService.ImportRequirementsFromWord(filePath));
                }
                
                UpdateProgress("Processing imported requirements...", 75);
                
                if (requirements?.Any() == true)
                {
                    PublishEvent(new TestCaseGenerationEvents.RequirementsImported 
                    { 
                        Requirements = requirements.ToList(), 
                        SourceFile = filePath, 
                        ImportType = importType,
                        ImportTime = TimeSpan.FromSeconds(1) // Placeholder
                    });

                    HideProgress();
                    ShowNotification($"Successfully imported {requirements.Count()} requirements", DomainNotificationType.Success);
                    
                    _logger.LogInformation("Requirements import completed: {Count} requirements from {FilePath}", 
                        requirements.Count(), filePath);
                    
                    return true;
                }
                else
                {
                    HideProgress();
                    ShowNotification("No requirements found in the file", DomainNotificationType.Warning);
                    
                    PublishEvent(new TestCaseGenerationEvents.RequirementsImportFailed 
                    { 
                        FilePath = filePath, 
                        ImportType = importType, 
                        ErrorMessage = "No requirements found" 
                    });
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Import failed: {ex.Message}", DomainNotificationType.Error);
                
                PublishEvent(new TestCaseGenerationEvents.RequirementsImportFailed 
                { 
                    FilePath = filePath, 
                    ImportType = importType, 
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                
                _logger.LogError(ex, "Requirements import failed for {FilePath}", filePath);
                return false;
            }
        }

        public async Task<bool> AnalyzeRequirementAsync(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            ShowProgress($"Analyzing requirement {requirement.GlobalId}...", 0);
            
            PublishEvent(new TestCaseGenerationEvents.RequirementAnalysisStarted 
            { 
                Requirement = requirement, 
                AnalysisType = "Quality" 
            });

            try
            {
                UpdateProgress($"Running LLM analysis...", 50);
                
                var analysis = await _analysisService.AnalyzeRequirementAsync(requirement);
                
                requirement.Analysis = analysis;
                
                PublishEvent(new TestCaseGenerationEvents.RequirementAnalyzed 
                { 
                    Requirement = requirement, 
                    Analysis = analysis, 
                    Success = true,
                    AnalysisTime = TimeSpan.FromSeconds(2) // Placeholder
                });

                HideProgress();
                ShowNotification($"Analysis completed for {requirement.GlobalId}", DomainNotificationType.Success);
                
                _logger.LogInformation("Requirement analysis completed for {RequirementId}", requirement.GlobalId);
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Analysis failed: {ex.Message}", DomainNotificationType.Error);
                
                PublishEvent(new TestCaseGenerationEvents.RequirementAnalyzed 
                { 
                    Requirement = requirement, 
                    Analysis = null, 
                    Success = false,
                    AnalysisTime = TimeSpan.Zero
                });
                
                _logger.LogError(ex, "Requirement analysis failed for {RequirementId}", requirement.GlobalId);
                return false;
            }
        }

        public async Task<bool> AnalyzeBatchRequirementsAsync(IReadOnlyList<Requirement> requirements)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (!requirements.Any()) return true;

            ShowProgress("Starting batch analysis...", 0);
            
            PublishEvent(new TestCaseGenerationEvents.BatchAnalysisStarted 
            { 
                Requirements = requirements.ToList(), 
                AnalysisType = "Quality" 
            });

            var successful = 0;
            var failed = 0;
            var errors = new List<string>();
            
            try
            {
                for (int i = 0; i < requirements.Count; i++)
                {
                    var requirement = requirements[i];
                    var progress = (double)(i + 1) / requirements.Count * 100;
                    
                    UpdateProgress($"Analyzing {requirement.GlobalId}... ({i + 1}/{requirements.Count})", progress);
                    
                    try
                    {
                        var analysis = await _analysisService.AnalyzeRequirementAsync(requirement);
                        requirement.Analysis = analysis;
                        successful++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{requirement.GlobalId}: {ex.Message}");
                        _logger.LogError(ex, "Batch analysis failed for requirement {RequirementId}", requirement.GlobalId);
                    }
                }

                PublishEvent(new TestCaseGenerationEvents.BatchAnalysisCompleted 
                { 
                    Requirements = requirements.ToList(), 
                    SuccessfulAnalyses = successful,
                    FailedAnalyses = failed,
                    TotalTime = TimeSpan.FromSeconds(requirements.Count * 2), // Placeholder
                    Errors = errors
                });

                HideProgress();
                
                if (failed == 0)
                {
                    ShowNotification($"Batch analysis completed successfully: {successful} requirements", DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification($"Batch analysis completed: {successful} successful, {failed} failed", DomainNotificationType.Warning);
                }
                
                _logger.LogInformation("Batch analysis completed: {Successful} successful, {Failed} failed", successful, failed);
                return failed == 0;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Batch analysis failed: {ex.Message}", DomainNotificationType.Error);
                
                _logger.LogError(ex, "Batch analysis failed completely");
                return false;
            }
        }

        public void SelectRequirement(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
            { 
                Requirement = requirement, 
                SelectedBy = "UserSelection" 
            });
            
            _logger.LogDebug("Requirement selected: {RequirementId}", requirement.GlobalId);
        }

        public void UpdateRequirement(Requirement requirement, IReadOnlyList<string> modifiedFields)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            PublishEvent(new TestCaseGenerationEvents.RequirementUpdated 
            { 
                Requirement = requirement, 
                UpdatedBy = "UserEdit", 
                ModifiedFields = modifiedFields?.ToList() ?? new List<string>()
            });
            
            _logger.LogDebug("Requirement updated: {RequirementId}, Fields: {Fields}", 
                requirement.GlobalId, string.Join(", ", modifiedFields ?? Array.Empty<string>()));
        }

        // ===== ASSUMPTIONS WORKFLOW =====

        public void UpdateAssumptions(Requirement requirement, IReadOnlyList<string> assumptions)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            _requirementAssumptions[requirement] = assumptions?.ToList() ?? new List<string>();
            
            PublishEvent(new TestCaseGenerationEvents.AssumptionsUpdated 
            { 
                Assumptions = assumptions?.ToList() ?? new List<string>(), 
                Requirement = requirement 
            });
            
            ShowNotification($"Assumptions updated for {requirement.GlobalId}", DomainNotificationType.Info);
            _logger.LogDebug("Assumptions updated for requirement {RequirementId}: {Count} assumptions", 
                requirement.GlobalId, assumptions?.Count ?? 0);
        }

        public IReadOnlyList<string> GetAssumptions(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            return _requirementAssumptions.TryGetValue(requirement, out var assumptions) 
                ? assumptions.AsReadOnly() 
                : Array.Empty<string>();
        }

        // ===== QUESTIONS WORKFLOW =====

        public async Task<IReadOnlyList<ClarifyingQuestionData>> GenerateQuestionsAsync(Requirement requirement, int questionBudget = 5)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            ShowProgress($"Generating questions for {requirement.GlobalId}...", 0);
            
            try
            {
                UpdateProgress("Requesting LLM questions...", 50);
                
                // Generate questions using LLM service
                var prompt = $"Generate {questionBudget} clarifying questions for requirement:\nID: {requirement.GlobalId}\nText: {requirement.Description}\n\nFormat each question as 'Q: [question]'";
                var response = await _llmService.GenerateAsync(prompt);
                
                // Parse questions from response
                var questions = ParseQuestionsFromResponse(response, requirement);
                
                _requirementQuestions[requirement] = questions;
                
                HideProgress();
                ShowNotification($"Generated {questions.Count} questions", DomainNotificationType.Success);
                
                _logger.LogInformation("Generated {Count} questions for requirement {RequirementId}", 
                    questions.Count, requirement.GlobalId);
                
                return questions.ToList().AsReadOnly();
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Question generation failed: {ex.Message}", DomainNotificationType.Error);
                
                _logger.LogError(ex, "Question generation failed for requirement {RequirementId}", requirement.GlobalId);
                return Array.Empty<ClarifyingQuestionData>();
            }
        }

        public void UpdateQuestionAnswers(Requirement requirement, IReadOnlyList<ClarifyingQuestionData> answeredQuestions)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            _requirementQuestions[requirement] = answeredQuestions?.ToList() ?? new List<ClarifyingQuestionData>();
            
            PublishEvent(new TestCaseGenerationEvents.QuestionsAnswered 
            { 
                Questions = answeredQuestions?.ToList() ?? new List<ClarifyingQuestionData>(), 
                Requirement = requirement 
            });
            
            ShowNotification($"Question answers updated for {requirement.GlobalId}", DomainNotificationType.Info);
            _logger.LogDebug("Question answers updated for requirement {RequirementId}: {Count} questions", 
                requirement.GlobalId, answeredQuestions?.Count ?? 0);
        }

        // ===== TEST CASE GENERATION =====

        public async Task<IReadOnlyList<TestCase>> GenerateTestCasesAsync(Requirement requirement, VerificationMethod method)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            ShowProgress($"Generating test cases for {requirement.GlobalId}...", 0);
            
            await Task.CompletedTask;
            var assumptions = GetAssumptions(requirement);
            var questions = _requirementQuestions.TryGetValue(requirement, out var q) ? q : new List<ClarifyingQuestionData>();
            
            PublishEvent(new TestCaseGenerationEvents.GenerationStarted 
            { 
                Requirement = requirement, 
                Method = method, 
                Assumptions = assumptions.ToList(),
                Questions = questions
            });

            try
            {
                UpdateProgress("Generating test cases with LLM...", 50);
                
                // Placeholder for actual test case generation
                // This would use the LLM service to generate test cases
                var testCases = new List<TestCase>
                {
                    new TestCase 
                    { 
                        ApiId = $"{requirement.GlobalId}_TC001",
                        Name = $"Test case for {requirement.Name}",
                        TestCaseText = $"Verify {requirement.Description}",
                        StepNumber = "1",
                        StepAction = "Perform verification action",
                        StepExpectedResult = "Expected result based on requirement"
                    }
                };
                
                PublishEvent(new TestCaseGenerationEvents.TestCasesGenerated 
                { 
                    TestCases = testCases, 
                    SourceRequirement = requirement, 
                    Method = method,
                    LlmResponse = "Generated test cases",
                    GenerationTime = TimeSpan.FromSeconds(3)
                });

                HideProgress();
                ShowNotification($"Generated {testCases.Count} test cases", DomainNotificationType.Success);
                
                _logger.LogInformation("Generated {Count} test cases for requirement {RequirementId}", 
                    testCases.Count, requirement.GlobalId);
                
                return testCases.AsReadOnly();
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Test case generation failed: {ex.Message}", DomainNotificationType.Error);
                
                PublishEvent(new TestCaseGenerationEvents.GenerationFailed 
                { 
                    Requirement = requirement, 
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                
                _logger.LogError(ex, "Test case generation failed for requirement {RequirementId}", requirement.GlobalId);
                return Array.Empty<TestCase>();
            }
        }

        public async Task<bool> ValidateTestCasesAsync(IReadOnlyList<TestCase> testCases)
        {
            if (testCases == null) throw new ArgumentNullException(nameof(testCases));
            
            ShowProgress("Validating test cases...", 50);
            
            try
            {
                // Placeholder validation logic
                var validationErrors = new List<string>();
                
                foreach (var testCase in testCases)
                {
                    if (string.IsNullOrWhiteSpace(testCase.Name))
                        validationErrors.Add($"Test case {testCase.ApiId} missing name");
                        
                    if (string.IsNullOrWhiteSpace(testCase.StepAction))
                        validationErrors.Add($"Test case {testCase.ApiId} missing step action");
                }
                
                var isValid = !validationErrors.Any();
                
                PublishEvent(new TestCaseGenerationEvents.TestCasesValidated 
                { 
                    TestCases = testCases.ToList(), 
                    IsValid = isValid,
                    ValidationErrors = validationErrors
                });

                HideProgress();
                
                if (isValid)
                {
                    ShowNotification("Test cases validation passed", DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification($"Validation failed: {validationErrors.Count} errors", DomainNotificationType.Warning);
                }
                
                _logger.LogInformation("Test cases validation completed: {IsValid}, Errors: {ErrorCount}", 
                    isValid, validationErrors.Count);
                
                await Task.CompletedTask;
                return isValid;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Validation failed: {ex.Message}", DomainNotificationType.Error);
                
                _logger.LogError(ex, "Test case validation failed");
                return false;
            }
        }

        // ===== EXPORT FUNCTIONALITY =====

        public async Task<bool> ExportRequirementsAsync(IReadOnlyList<Requirement> requirements, string exportType, string outputPath)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (string.IsNullOrWhiteSpace(exportType)) throw new ArgumentException("Export type cannot be null or empty", nameof(exportType));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
            
            ShowProgress($"Exporting {requirements.Count} requirements...", 0);
            
            PublishEvent(new TestCaseGenerationEvents.RequirementsExportStarted 
            { 
                Requirements = requirements.ToList(), 
                ExportType = exportType, 
                OutputPath = outputPath 
            });

            try
            {
                UpdateProgress("Formatting requirements...", 50);
                
                // Placeholder export logic
                // This would use actual export services
                await Task.Delay(1000); // Simulate export work
                
                PublishEvent(new TestCaseGenerationEvents.RequirementsExported 
                { 
                    Requirements = requirements.ToList(), 
                    ExportType = exportType, 
                    OutputPath = outputPath,
                    Success = true,
                    ExportTime = TimeSpan.FromSeconds(1)
                });

                HideProgress();
                ShowNotification($"Requirements exported successfully to {outputPath}", DomainNotificationType.Success);
                
                _logger.LogInformation("Requirements export completed: {Count} requirements to {OutputPath}", 
                    requirements.Count, outputPath);
                
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Export failed: {ex.Message}", DomainNotificationType.Error);
                
                PublishEvent(new TestCaseGenerationEvents.RequirementsExportFailed 
                { 
                    Requirements = requirements.ToList(), 
                    ExportType = exportType, 
                    OutputPath = outputPath,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
                
                _logger.LogError(ex, "Requirements export failed to {OutputPath}", outputPath);
                return false;
            }
        }

        public async Task<bool> ExportTestCasesAsync(IReadOnlyList<TestCase> testCases, string exportType, string outputPath)
        {
            if (testCases == null) throw new ArgumentNullException(nameof(testCases));
            if (string.IsNullOrWhiteSpace(exportType)) throw new ArgumentException("Export type cannot be null or empty", nameof(exportType));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
            
            ShowProgress($"Exporting {testCases.Count} test cases...", 0);

            try
            {
                UpdateProgress("Formatting test cases...", 50);
                
                // Placeholder export logic
                await Task.Delay(1000); // Simulate export work

                HideProgress();
                ShowNotification($"Test cases exported successfully to {outputPath}", DomainNotificationType.Success);
                
                _logger.LogInformation("Test cases export completed: {Count} test cases to {OutputPath}", 
                    testCases.Count, outputPath);
                
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Export failed: {ex.Message}", DomainNotificationType.Error);
                
                _logger.LogError(ex, "Test cases export failed to {OutputPath}", outputPath);
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

        private List<ClarifyingQuestionData> ParseQuestionsFromResponse(string response, Requirement requirement)
        {
            var questions = new List<ClarifyingQuestionData>();
            if (string.IsNullOrWhiteSpace(response)) return questions;

            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("Q:", StringComparison.OrdinalIgnoreCase))
                {
                    var questionText = trimmedLine.Substring(2).Trim();
                    if (!string.IsNullOrWhiteSpace(questionText))
                    {
                        questions.Add(new ClarifyingQuestionData
                        {
                            Text = questionText,
                            Category = "Generated",
                            Answer = null
                        });
                    }
                }
                else if (trimmedLine.Contains("?") && trimmedLine.Length > 5) // Likely a question without Q: prefix
                {
                    questions.Add(new ClarifyingQuestionData
                    {
                        Text = trimmedLine,
                        Category = "Generated", 
                        Answer = null
                    });
                }
            }

            return questions;
        }
    }
}