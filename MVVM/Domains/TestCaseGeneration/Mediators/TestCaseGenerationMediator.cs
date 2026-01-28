using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services; // For IRequirementAnalysisService
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;

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
        private readonly SmartRequirementImporter _smartImporter;
        private readonly TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService _analysisService;
        private readonly ITextGenerationService _llmService;
        private readonly IRequirementDataScrubber _scrubber;
        
        // Workflow state
        private readonly Dictionary<Requirement, List<string>> _requirementAssumptions = new();
        private readonly Dictionary<Requirement, List<ClarifyingQuestionData>> _requirementQuestions = new();
        
        // Requirements collection for UI binding
        private readonly ObservableCollection<Requirement> _requirements = new();
        
        // Domain state management - replaces MainViewModel dependencies
        private Requirement? _currentRequirement;
        private bool _isDirty = false;
        private bool _isAnalyzing = false;
        
        // Requirement editing state management
        private bool _isEditingRequirement = false;
        private string _originalRequirementText = string.Empty;
        private string _currentEditingText = string.Empty;
        
        /// <summary>
        /// Whether currently editing a requirement
        /// </summary>
        public bool IsEditingRequirement => _isEditingRequirement;
        
        /// <summary>
        /// Whether the editing text differs from original (used for UI visibility)
        /// </summary>
        public bool HasUnsavedEditingChanges => _isEditingRequirement && 
                                               !string.IsNullOrEmpty(_currentEditingText) &&
                                               _currentEditingText.Trim() != _originalRequirementText.Trim();
        
        // Header ViewModel integration for project status updates
        private TestCaseGenerator_HeaderVM? _headerViewModel;
        private TestCaseGenerator_TitleVM? _titleViewModel;
        private object? _selectedStep;
        private object? _currentStepViewModel;
        private INewProjectMediator? _workspaceMediator;
        
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
                System.Diagnostics.Debug.WriteLine($"[TestCaseGenerationMediator] IsDirty setter called: {_isDirty} → {value}");
                if (_isDirty != value) 
                { 
                    _isDirty = value;
                    var evt = new TestCaseGenerationEvents.WorkflowStateChanged 
                    { 
                        PropertyName = nameof(IsDirty), 
                        NewValue = value 
                    };
                    System.Diagnostics.Debug.WriteLine($"[TestCaseGenerationMediator] Publishing WorkflowStateChanged event: PropertyName={evt.PropertyName}, NewValue={evt.NewValue}");
                    PublishEvent(evt);
                    _logger.LogDebug("IsDirty changed to: {IsDirty}", value);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TestCaseGenerationMediator] IsDirty value unchanged, not publishing event");
                }
            } 
        }
        
        /// <summary>
        /// Requirements collection for UI binding across the domain
        /// </summary>
        public ObservableCollection<Requirement> Requirements => _requirements;
        
        public bool IsAnalyzing 
        { 
            get => _isAnalyzing; 
            set 
            { 
                if (_isAnalyzing != value) 
                { 
                    _isAnalyzing = value;
                    PublishEvent(new TestCaseGenerationEvents.WorkflowStateChanged 
                    { 
                        PropertyName = nameof(IsAnalyzing), 
                        NewValue = value 
                    });
                    _logger.LogDebug("IsAnalyzing changed to: {IsAnalyzing}", value);
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
        
        /// <summary>
        /// HeaderVM instance created and managed by this mediator
        /// </summary>
        public TestCaseGenerator_HeaderVM? HeaderViewModel => _headerViewModel;
        
        /// <summary>
        /// TitleVM instance created and managed by this mediator
        /// </summary>
        public TestCaseGenerator_TitleVM? TitleViewModel => _titleViewModel;

        public TestCaseGenerationMediator(
            ILogger<TestCaseGenerationMediator> logger,
            IDomainUICoordinator uiCoordinator,
            IRequirementService requirementService,
            TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService analysisService,
            ITextGenerationService llmService,
            IRequirementDataScrubber scrubber,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Test Case Generator", performanceMonitor, eventReplay)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _smartImporter = new SmartRequirementImporter(requirementService, 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartRequirementImporter>.Instance);
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));

            // Subscribe to internal events for header updates
            Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelectedForHeader);
            
            // Subscribe to cross-domain requirements import events
            Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnRequirementsImported);
            
            // Project title updates will be handled via existing broadcast mechanism

            // Initialize HeaderVM directly (no legacy factory needed)
            InitializeHeaderViewModel();

            _logger.LogDebug("TestCaseGenerationMediator created with domain '{DomainName}'", _domainName);
        }
        
        /// <summary>
        /// Update project context for title and header ViewModels
        /// </summary>
        public void UpdateProjectContext(string? projectName)
        {
            // Broadcast project title change event to all subscribers
            PublishEvent(new TestCaseGenerationEvents.ProjectTitleChanged
            {
                ProjectName = projectName,
                Source = "ProjectContext",
                Timestamp = DateTime.Now
            });
            
            // Also update header if it has project tracking
            if (_headerViewModel != null)
            {
                var isProjectOpen = !string.IsNullOrWhiteSpace(projectName);
                _headerViewModel.UpdateProjectStatus(projectName, isProjectOpen);
            }
            
            _logger.LogDebug("Project context updated: {ProjectName}", projectName ?? "No Project");
        }

        /// <summary>
        /// Override Subscribe to provide auto-sync of current requirement selection state
        /// for new RequirementSelected subscribers. This ensures ViewModels get current state
        /// when they subscribe, maintaining UI consistency across workspace changes.
        /// </summary>
        public override void Subscribe<T>(Action<T> handler)
        {
            // Call base subscription first
            base.Subscribe(handler);
            
            // Auto-sync current requirement selection for new RequirementSelected subscribers
            // Defer the sync to avoid timing issues with ViewModel initialization
            if (typeof(T) == typeof(TestCaseGenerationEvents.RequirementSelected) && _currentRequirement != null)
            {
                // Use Dispatcher.BeginInvoke to defer auto-sync until after ViewModel construction completes
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Immediately notify new subscriber of current requirement selection
                        var currentEvent = new TestCaseGenerationEvents.RequirementSelected
                        {
                            Requirement = _currentRequirement,
                            SelectedBy = "MediatorAutoSync"
                        };
                        
                        // Cast and invoke - this ensures new subscriber gets current state
                        ((Action<TestCaseGenerationEvents.RequirementSelected>)(object)handler).Invoke(currentEvent);
                        
                        _logger.LogDebug("Auto-synced current requirement {RequirementId} to new subscriber", 
                            _currentRequirement.GlobalId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to auto-sync current requirement to new subscriber");
                    }
                }));
            }
        }

        /// <summary>
        /// Public wrapper for PublishEvent to satisfy interface requirements
        /// </summary>
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
            
            // Clear any stale analysis errors when navigating with a requirement
            if (requirement.Analysis != null)
            {
                requirement.Analysis.ErrorMessage = string.Empty;
            }
            
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
            
            // Clear any stale analysis errors when navigating with a requirement
            if (requirement.Analysis != null)
            {
                requirement.Analysis.ErrorMessage = string.Empty;
            }
            
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
            
            // Clear any stale analysis errors when navigating with a requirement
            if (requirement.Analysis != null)
            {
                requirement.Analysis.ErrorMessage = string.Empty;
            }
            
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



        // Editing state management methods
        
        /// <summary>
        /// Start editing a requirement - mediator manages the state
        /// </summary>
        public void StartEditingRequirement(Requirement requirement, string originalText)
        {
            _isEditingRequirement = true;
            _originalRequirementText = originalText;
            _currentEditingText = originalText;
            
            // Fire event for UI to reflect state change
            PublishEvent(new TestCaseGenerationEvents.RequirementEditStateChanged
            {
                Requirement = requirement,
                IsEditing = true,
                HasUnsavedChanges = false,
                OriginalText = originalText,
                CurrentText = originalText
            });
        }
        
        /// <summary>
        /// Update the current editing text - mediator tracks changes
        /// </summary>
        public void UpdateEditingText(string currentText)
        {
            if (!_isEditingRequirement) return;
            
            _currentEditingText = currentText;
            
            // Fire event when unsaved changes state changes
            var hasChanges = HasUnsavedEditingChanges;
            if (_currentRequirement != null)
            {
                PublishEvent(new TestCaseGenerationEvents.RequirementEditStateChanged
                {
                    Requirement = _currentRequirement,
                    IsEditing = true,
                    HasUnsavedChanges = hasChanges,
                    OriginalText = _originalRequirementText,
                    CurrentText = currentText
                });
            }
        }
        
        /// <summary>
        /// End editing (after save) - clear state
        /// </summary>
        public void EndEditingRequirement()
        {
            if (_currentRequirement != null && _isEditingRequirement)
            {
                PublishEvent(new TestCaseGenerationEvents.RequirementEditStateChanged
                {
                    Requirement = _currentRequirement,
                    IsEditing = false,
                    HasUnsavedChanges = false,
                    OriginalText = string.Empty,
                    CurrentText = string.Empty
                });
            }
            
            _isEditingRequirement = false;
            _originalRequirementText = string.Empty;
            _currentEditingText = string.Empty;
        }
        
        /// <summary>
        /// Cancel editing (without save) - clear state
        /// </summary>
        public void CancelEditingRequirement()
        {
            if (_currentRequirement != null && _isEditingRequirement)
            {
                PublishEvent(new TestCaseGenerationEvents.RequirementEditStateChanged
                {
                    Requirement = _currentRequirement,
                    IsEditing = false,
                    HasUnsavedChanges = false,
                    OriginalText = string.Empty,
                    CurrentText = string.Empty
                });
            }
            
            _isEditingRequirement = false;
            _originalRequirementText = string.Empty;
            _currentEditingText = string.Empty;
        }

        /// <summary>
        /// Shows detailed guidance dialog when import fails or finds no requirements
        /// </summary>
        private void ShowImportGuidanceDialog(DocumentFormatDetector.DetectionResult? analysis, string filePath)
        {
            if (analysis == null) return;

            try
            {
                var fileName = System.IO.Path.GetFileName(filePath);
                var dialogMessage = BuildGuidanceDialogMessage(analysis, fileName);
                
                // Use Windows MessageBox for now - could be replaced with custom dialog later
                System.Windows.MessageBox.Show(
                    dialogMessage,
                    "Import Guidance - Document Analysis",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                    
                _logger.LogInformation("Displayed import guidance dialog for {FileName} with format {Format}", 
                    fileName, analysis.Format);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing import guidance dialog");
            }
        }

        /// <summary>
        /// Builds a comprehensive guidance message based on document analysis
        /// </summary>
        private static string BuildGuidanceDialogMessage(DocumentFormatDetector.DetectionResult analysis, string fileName)
        {
            var message = new System.Text.StringBuilder();
            
            message.AppendLine($"📄 File: {fileName}");
            message.AppendLine($"🔍 Format Detected: {analysis.Description}");
            message.AppendLine();
            
            if (analysis.HasRequirements)
            {
                message.AppendLine($"✅ Found {analysis.EstimatedRequirementCount} requirement ID(s):");
                foreach (var id in analysis.FoundRequirementIds.Take(10))
                {
                    message.AppendLine($"   • {id}");
                }
                if (analysis.FoundRequirementIds.Count > 10)
                {
                    message.AppendLine($"   ... and {analysis.FoundRequirementIds.Count - 10} more");
                }
                message.AppendLine();
            }
            
            message.AppendLine("📋 Detection Details:");
            foreach (var reason in analysis.DetectionReasons)
            {
                message.AppendLine($"   • {reason}");
            }
            message.AppendLine();
            
            message.AppendLine("💡 Guidance:");
            message.AppendLine(analysis.UserGuidance);
            
            return message.ToString();
        }



        public void SelectRequirement(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            
            // Clear any stale analysis state when selecting a new requirement to ensure clean UI
            if (requirement.Analysis != null && !requirement.Analysis.IsAnalyzed)
            {
                // If analysis failed or is incomplete, remove it entirely for clean state
                requirement.Analysis = null;
                _logger.LogDebug("Removed failed analysis state for requirement: {RequirementId}", requirement.GlobalId);
            }
            
            // Track current requirement for auto-sync functionality
            _currentRequirement = requirement;
            
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
        
        /// <summary>
        /// Wire workspace management commands to the header ViewModel
        /// This enables cross-domain command integration while maintaining domain boundaries
        /// </summary>
        public void WireWorkspaceCommands(INewProjectMediator workspaceMediator)
        {
            if ((_headerViewModel == null && _titleViewModel == null) || workspaceMediator == null) return;
            _workspaceMediator = workspaceMediator;

            // Wire commands to both header and title ViewModels
            if (_headerViewModel != null)
            {
                _headerViewModel.SaveWorkspaceCommand = new AsyncRelayCommand(
                    async () => 
                    {
                        await workspaceMediator.SaveProjectAsync();
                        // Reset dirty state after successful save
                        IsDirty = false;
                    });
            }
            
            if (_titleViewModel != null)
            {
                _titleViewModel.SaveWorkspaceCommand = new AsyncRelayCommand(
                    async () => 
                    {
                        await workspaceMediator.SaveProjectAsync();
                    });
                
                _titleViewModel.UndoLastSaveCommand = new AsyncRelayCommand(
                    async () => 
                    {
                        await workspaceMediator.UndoLastSaveAsync();
                    }, 
                    () => workspaceMediator.CanUndoLastSave());
            }

            // Cross-domain event subscriptions removed - ViewModels manage their own state directly
            
            // Subscribe to internal workflow state changes to forward dirty state to workspace
            Subscribe<TestCaseGenerationEvents.WorkflowStateChanged>(e =>
            {
                if (e.PropertyName == "IsDirty" && e.NewValue is true)
                {
                    // Broadcast cross-domain event so workspace management can respond
                    Application.Current?.Dispatcher.BeginInvoke(() => 
                    {
                        // The workspace management mediator should subscribe to this notification
                        BroadcastToAllDomains(new TestCaseEditorApp.MVVM.Events.CrossDomainMessages.WorkspaceContextChanged 
                        { 
                            WorkspaceName = "Current Project", // Will be updated by workspace management
                            ChangeType = "RequirementDataChanged", 
                            OriginatingDomain = "TestCaseGeneration"
                        });
                    });
                }
            });

            // ViewModels handle their own initialization via constructor
            
            _logger.LogDebug("Workspace commands wired to TestCaseGenerator_HeaderVM");
        }
        private void InitializeHeaderViewModel()
        {
            _headerViewModel = new TestCaseGenerator_HeaderVM(this);
            _titleViewModel = new TestCaseGenerator_TitleVM(this);
            _logger.LogDebug("Header and Title ViewModels created and initialized for TestCaseGenerationMediator");
        }
        
        /// <summary>
        /// Handle requirement selection events to update the header ViewModel with requirement details
        /// </summary>
        private void OnRequirementSelectedForHeader(TestCaseGenerationEvents.RequirementSelected e)
        {
            // Track current requirement for mediator state consistency
            _currentRequirement = e.Requirement;
            
            if (_headerViewModel != null && e.Requirement != null)
            {
                _logger.LogDebug("Updating header with selected requirement: {RequirementId}", e.Requirement.GlobalId);
                
                // Update the header with requirement details
                _headerViewModel.RequirementDescription = e.Requirement.Description ?? string.Empty;
                _headerViewModel.RequirementMethod = e.Requirement.VerificationMethodText ?? e.Requirement.Method.ToString();
                _headerViewModel.RequirementMethodEnum = e.Requirement.Method;
                // Show the actual requirement description instead of "Item X - Name" format
                _headerViewModel.CurrentRequirementName = e.Requirement.Description ?? $"Requirement {e.Requirement.Item}";
                
                _logger.LogDebug("Header updated with requirement: Description={DescriptionLength} chars, Method={Method}", 
                    _headerViewModel.RequirementDescription.Length, _headerViewModel.RequirementMethod);
            }
        }
        
        /// <summary>
        /// Handle requirements imported events from cross-domain broadcasts (e.g., from WorkspaceManagementMediator during project creation)
        /// </summary>
        private void OnRequirementsImported(TestCaseGenerationEvents.RequirementsImported e)
        {
            if (e?.Requirements != null)
            {
                _logger.LogInformation("🔄 Handling cross-domain RequirementsImported with {Count} requirements", e.Requirements.Count);
                
                // Clear existing requirements and add new ones (sorted naturally by numeric suffix)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _requirements.Clear();
                    // Sort requirements using natural numeric order to ensure RC-5 comes before RC-12, etc.
                    var sortedRequirements = e.Requirements.OrderBy(r => r.Item ?? r.Name ?? string.Empty, 
                        new RequirementNaturalComparer()).ToList();
                    foreach (var requirement in sortedRequirements)
                    {
                        _requirements.Add(requirement);
                    }
                });
                
                // Set the first requirement as current if available - use SelectRequirement to ensure clean state
                if (e.Requirements.Count > 0)
                {
                    SelectRequirement(e.Requirements.First());
                    _logger.LogDebug("Selected first requirement during import: {RequirementId}", CurrentRequirement?.GlobalId);
                }
                
                _logger.LogInformation("✅ Successfully imported {Count} requirements from cross-domain event", e.Requirements.Count);
            }
            else
            {
                _logger.LogWarning("❌ Received RequirementsImported event with null or empty requirements");
            }
        }
        
        /// <summary>
        /// Handle broadcast notifications from other domains
        /// This is called by DomainCoordinator when other domains broadcast events
        /// </summary>
        public void HandleBroadcastNotification<T>(T notification) where T : class
        {
            _logger.LogInformation("🔔🔔🔔 BROADCAST RECEIVED: {NotificationType} - TestCaseGenerationMediator", typeof(T).Name);
            _logger.LogInformation("🔔 Received broadcast notification: {NotificationType}", typeof(T).Name);
            
            // Handle workspace management events
            if (notification is NewProjectEvents.ProjectCreated projectCreated)
            {
                _logger.LogInformation("HandleBroadcast: ProjectCreated - WorkspaceName: {WorkspaceName}, HeaderViewModel: {HeaderViewModel}", 
                    projectCreated.WorkspaceName, _headerViewModel?.GetType().Name ?? "NULL");
                    
                // Set workspace context for analysis service with project name
                _analysisService.SetWorkspaceContext(projectCreated.WorkspaceName);
                _logger.LogDebug("Set workspace context for analysis service: {WorkspaceName}", projectCreated.WorkspaceName);
                
                _headerViewModel?.UpdateProjectStatus(projectCreated.WorkspaceName, true);
                _logger.LogDebug("Updated header with project created: {ProjectName}", projectCreated.WorkspaceName);
                
                // Update project title
                UpdateProjectContext(projectCreated.WorkspaceName);
                
                // Load requirements for the created project if workspace data is available
                if (projectCreated.Workspace != null)
                {
                    _logger.LogInformation("🔄 About to load requirements for created project: {ProjectName}", projectCreated.WorkspaceName);
                    LoadProjectRequirements(projectCreated.WorkspaceName, projectCreated.Workspace);
                }
            }
            else if (notification is NewProjectEvents.ProjectOpened projectOpened)
            {
                _logger.LogInformation("🚀 HandleBroadcast: ProjectOpened - WorkspaceName: {WorkspaceName}, HeaderViewModel: {HeaderViewModel}", 
                    projectOpened.WorkspaceName, _headerViewModel?.GetType().Name ?? "NULL");
                    
                // Set workspace context for analysis service with project name
                _analysisService.SetWorkspaceContext(projectOpened.WorkspaceName);
                _logger.LogDebug("Set workspace context for analysis service: {WorkspaceName}", projectOpened.WorkspaceName);
                
                _headerViewModel?.UpdateProjectStatus(projectOpened.WorkspaceName, true);
                _logger.LogDebug("Updated header with project opened: {ProjectName}", projectOpened.WorkspaceName);
                
                // Update project title
                UpdateProjectContext(projectOpened.WorkspaceName);
                
                // Load requirements for the opened project
                _logger.LogInformation("🔄 About to load requirements for project: {ProjectName}", projectOpened.WorkspaceName);
                LoadProjectRequirements(projectOpened.WorkspaceName, projectOpened.Workspace);
            }
            else if (notification is OpenProjectEvents.ProjectOpened openedProject)
            {
                _logger.LogInformation("🚀 HandleBroadcast: OpenProjectEvents.ProjectOpened - WorkspaceName: {WorkspaceName}, HeaderViewModel: {HeaderViewModel}", 
                    openedProject.WorkspaceName, _headerViewModel?.GetType().Name ?? "NULL");
                    
                // DEBUG: Log workspace details
                if (openedProject.Workspace == null)
                {
                    _logger.LogError("🚨 OpenProjectEvents.ProjectOpened: Workspace is NULL!");
                }
                else
                {
                    _logger.LogInformation("📊 OpenProject Workspace Info: Version={Version}, Requirements.Count={RequirementCount}",
                        openedProject.Workspace.Version, openedProject.Workspace.Requirements?.Count ?? -1);
                }
                    
                // Set workspace context for analysis service with project name
                _analysisService.SetWorkspaceContext(openedProject.WorkspaceName);
                _logger.LogDebug("Set workspace context for analysis service: {WorkspaceName}", openedProject.WorkspaceName);
                
                _headerViewModel?.UpdateProjectStatus(openedProject.WorkspaceName, true);
                _logger.LogDebug("Updated header with project opened: {ProjectName}", openedProject.WorkspaceName);
                
                // Update project title
                UpdateProjectContext(openedProject.WorkspaceName);
                
                // Load requirements for the opened project
                _logger.LogInformation("🔄 About to load requirements for opened project: {ProjectName}", openedProject.WorkspaceName);
                LoadProjectRequirements(openedProject.WorkspaceName, openedProject.Workspace);
                _logger.LogInformation("✅ LoadProjectRequirements call completed for: {ProjectName}", openedProject.WorkspaceName);
            }
            else if (notification is NewProjectEvents.ProjectClosed)
            {
                _logger.LogInformation("🔔 RECEIVED ProjectClosed event - starting cleanup");
                _logger.LogInformation("HandleBroadcast: ProjectClosed - HeaderViewModel: {HeaderViewModel}", 
                    _headerViewModel?.GetType().Name ?? "NULL");
                    
                // Update project title to default
                UpdateProjectContext(null);
                
                _headerViewModel?.UpdateProjectStatus(null, false);
                
                // Clear requirements collection when project is closed (on UI thread)
                _logger.LogInformation("🔄 Clearing requirements collection on project close...");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _logger.LogInformation("🗑️ Before clear: Requirements.Count = {Count}", _requirements.Count);
                    _requirements.Clear();
                    _logger.LogInformation("✅ After clear: Requirements.Count = {Count}", _requirements.Count);
                    
                    var collectionEvent = new TestCaseGenerationEvents.RequirementsCollectionChanged
                    {
                        Action = "Clear",
                        AffectedRequirements = new List<Requirement>(),
                        NewCount = 0
                    };
                    
                    _logger.LogInformation("📢 Publishing RequirementsCollectionChanged event: Action=Clear, NewCount=0");
                    PublishEvent(collectionEvent);
                });
                
                _logger.LogDebug("Updated header with project closed and cleared requirements");
            }
            else
            {
                _logger.LogDebug("Broadcast notification not handled: {NotificationType}", typeof(T).Name);
            }
        }
        
        /// <summary>
        /// Loads requirements for the specified project with UI thread safety
        /// </summary>
        private void LoadProjectRequirements(string projectName, Workspace? workspace)
        {
            try
            {
                _logger.LogInformation("📋 Loading requirements for project: {ProjectName}", projectName);
                
                // DEBUG: Log workspace details
                if (workspace == null)
                {
                    _logger.LogError("🚨 Workspace is NULL in LoadProjectRequirements!");
                    return;
                }
                
                _logger.LogInformation("📊 Workspace Info: Version={Version}, SourceDoc={SourceDoc}, Requirements.Count={RequirementCount}",
                    workspace.Version, workspace.SourceDocPath, workspace.Requirements?.Count ?? -1);
                
                // Get actual requirements from the loaded workspace
                var actualRequirements = workspace?.Requirements?.ToList() ?? new List<Requirement>();
                
                _logger.LogInformation("✅ Found {Count} actual requirements in workspace", actualRequirements.Count);
                
                // DEBUG: Log first few requirement details
                if (actualRequirements.Count > 0)
                {
                    for (int i = 0; i < Math.Min(3, actualRequirements.Count); i++)
                    {
                        var req = actualRequirements[i];
                        _logger.LogInformation("📋 Requirement {Index}: GlobalId={GlobalId}, Item={Item}, Name={Name}",
                            i + 1, req.GlobalId, req.Item, req.Name);
                    }
                }
                
                // Update the Requirements collection on UI thread using Dispatcher.Invoke
                _logger.LogInformation("🧵 Updating UI on dispatcher thread...");
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _logger.LogInformation("🔄 Clearing existing requirements collection...");
                    var beforeCount = _requirements.Count;
                    _requirements.Clear();
                    _logger.LogInformation("🗑️ Cleared {BeforeCount} existing requirements", beforeCount);
                    
                    _logger.LogInformation("➕ Adding {Count} real requirements to collection (sorted naturally by numeric suffix)...", actualRequirements.Count);
                    // Sort requirements using natural numeric order to ensure RC-5 comes before RC-12, etc.
                    var sortedRequirements = actualRequirements.OrderBy(r => r.Item ?? r.Name ?? string.Empty, 
                        new RequirementNaturalComparer()).ToList();
                    foreach (var requirement in sortedRequirements)
                    {
                        // Preserve analysis data that was persisted in the project file
                        // Analysis will be null only if it wasn't previously performed
                        if (requirement.Analysis != null)
                        {
                            _logger.LogDebug("Preserved persisted analysis for requirement: {RequirementId}", requirement.GlobalId);
                        }
                        
                        _requirements.Add(requirement);
                    }
                    
                    var afterCount = _requirements.Count;
                    _logger.LogInformation("✅ Loaded {Count} requirements for project {ProjectName} - Collection now has {ActualCount} items", 
                        actualRequirements.Count, projectName, afterCount);
                    
                    // Publish event to notify NavigationVM that requirements collection has changed
                    PublishEvent(new TestCaseGenerationEvents.RequirementsCollectionChanged 
                    { 
                        AffectedRequirements = actualRequirements, 
                        Action = "ProjectOpened",
                        NewCount = actualRequirements.Count
                    });
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading requirements for project {ProjectName}", projectName);
            }
        }

    }

    /// <summary>
    /// Custom comparer for natural numeric sorting of requirements (same logic as RequirementsIndexViewModel and NavigationViewModel)
    /// Ensures DECAGON-REQ_RC-5 comes before DECAGON-REQ_RC-12, etc.
    /// </summary>
    internal class RequirementNaturalComparer : IComparer<string>
    {
        private static readonly Regex _trailingNumberRegex = new Regex(@"^(.*?)(\d+)$", RegexOptions.Compiled);

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Prefer 'Item' then 'Name' as the canonical id string (same as RequirementsIndexViewModel)
            var sa = (x ?? string.Empty).Trim();
            var sb = (y ?? string.Empty).Trim();

            // If identical strings, consider them equal
            if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)) return 0;

            var ma = _trailingNumberRegex.Match(sa);
            var mb = _trailingNumberRegex.Match(sb);

            if (ma.Success && mb.Success)
            {
                var prefixA = ma.Groups[1].Value;
                var prefixB = mb.Groups[1].Value;
                if (!string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase))
                {
                    // Compare prefixes alphabetically
                    return StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                }

                // Both prefixes equal – compare numeric suffix ascending so 5 comes before 12
                if (long.TryParse(ma.Groups[2].Value, out var na) && long.TryParse(mb.Groups[2].Value, out var nb))
                {
                    // Ascending numeric order
                    var numCompare = na.CompareTo(nb);
                    if (numCompare != 0) return numCompare;
                }

                // Fallback to full-string compare if numeric equal
                return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
            }

            // If one has numeric suffix and other not, place numeric-suffixed after/before depending on prefix
            if (ma.Success && !mb.Success)
            {
                var prefixA = ma.Groups[1].Value;
                var prefixB = sb;
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                if (cmp != 0) return cmp;
                // If prefixes same, treat the numeric-suffixed as less (so similar entries cluster)
                return -1;
            }
            if (!ma.Success && mb.Success)
            {
                var prefixA = sa;
                var prefixB = mb.Groups[1].Value;
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                if (cmp != 0) return cmp;
                return 1;
            }

            // No numeric suffixes – plain string compare
            return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
        }
    }
}
