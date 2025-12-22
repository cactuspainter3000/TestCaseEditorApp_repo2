using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel for test case creation and editing - refactored to use domain mediator
    /// </summary>
    public class TestCaseGenerator_CreationVM : BaseDomainViewModel, IDisposable
    {
        private new readonly ITestCaseGenerationMediator _mediator;
        private string? _llmOutput;
        private GeneratedTestCase? _selectedTestCase;
        private Requirement? _currentRequirement;
        
        public TestCaseGenerator_CreationVM(ITestCaseGenerationMediator mediator, ILogger<TestCaseGenerator_CreationVM> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            TestCases = new ObservableCollection<GeneratedTestCase>();

            // Subscribe to domain events
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<TestCaseGenerationEvents.TestCasesGenerated>(OnTestCasesGenerated);

            // Commands
            AddTestCaseCommand = new RelayCommand(AddTestCase);
            RemoveTestCaseCommand = new RelayCommand(RemoveTestCase, () => SelectedTestCase != null);
            SubmitTestCasesCommand = new RelayCommand(SaveTestCases, () => TestCases.Any());
            
            Title = "Test Case Creation";
            _logger.LogDebug("TestCaseGenerator_CreationVM created with domain mediator");
        }

        // ===== PROPERTIES =====

        public ObservableCollection<GeneratedTestCase> TestCases { get; }

        public GeneratedTestCase? SelectedTestCase
        {
            get => _selectedTestCase;
            set => SetProperty(ref _selectedTestCase, value);
        }

        public string? LlmOutput
        {
            get => _llmOutput;
            set => SetProperty(ref _llmOutput, value);
        }

        /// <summary>
        /// Current requirement for context
        /// </summary>
        public Requirement? CurrentRequirement
        {
            get => _currentRequirement;
            private set => SetProperty(ref _currentRequirement, value);
        }

        // ===== COMMANDS =====

        public ICommand AddTestCaseCommand { get; }
        public ICommand RemoveTestCaseCommand { get; }
        public ICommand SubmitTestCasesCommand { get; }

        private void AddTestCase()
        {
            var newTestCase = new GeneratedTestCase(null) // TODO: Pass proper reference when available
            {
                Title = $"TC-{TestCases.Count + 1:000}: New Test Case",
                Preconditions = "",
                Steps = "1. ",
                ExpectedResults = ""
            };
            
            TestCases.Add(newTestCase);
            SelectedTestCase = newTestCase;
            
            _logger.LogDebug("Added new test case: {Title}", newTestCase.Title);
            
            // Auto-save to current requirement if available
            SaveTestCasesToRequirement();
        }

        private void RemoveTestCase()
        {
            if (SelectedTestCase != null)
            {
                var title = SelectedTestCase.Title;
                TestCases.Remove(SelectedTestCase);
                SelectedTestCase = TestCases.FirstOrDefault();
                
                _logger.LogDebug("Removed test case: {Title}", title);
                
                // Auto-save changes
                SaveTestCasesToRequirement();
            }
        }

        private void SaveTestCases()
        {
            SaveTestCasesToRequirement();
            _logger.LogDebug("Test cases saved for requirement {RequirementId}", CurrentRequirement?.GlobalId);
        }

        // ===== EVENT HANDLERS =====

        /// <summary>
        /// Handle requirement selection from domain mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            _logger.LogDebug("Requirement selected in CreationVM: {RequirementId}", e.Requirement.GlobalId);
            
            // Save current test cases before switching
            if (CurrentRequirement != null && TestCases.Count > 0)
            {
                SaveTestCasesToRequirement(markDirty: false);
            }
            
            // Load test cases for new requirement
            CurrentRequirement = e.Requirement;
            LoadTestCasesFromRequirement(e.Requirement);
        }

        /// <summary>
        /// Handle test cases generated from domain mediator
        /// </summary>
        private void OnTestCasesGenerated(TestCaseGenerationEvents.TestCasesGenerated e)
        {
            _logger.LogDebug("Test cases generated: {Count} for requirement {RequirementId}", 
                e.TestCases.Count, e.SourceRequirement.GlobalId);
            
            // Update LLM output if available
            LlmOutput = e.LlmResponse;
            
            // Load the generated test cases if they're for current requirement
            if (ReferenceEquals(CurrentRequirement, e.SourceRequirement))
            {
                LoadGeneratedTestCases(e.TestCases);
            }
        }

        // ===== PRIVATE METHODS =====

        /// <summary>
        /// Load test cases from a requirement
        /// </summary>
        private void LoadTestCasesFromRequirement(Requirement? requirement)
        {
            _logger.LogDebug("Loading test cases from requirement: {RequirementId}", requirement?.GlobalId ?? "<null>");
            
            TestCases.Clear();
            SelectedTestCase = null;

            if (requirement == null)
            {
                _logger.LogDebug("Requirement is null, clearing test cases");
                return;
            }

            // Check if requirement has GeneratedTestCases
            if (requirement.GeneratedTestCases != null && requirement.GeneratedTestCases.Count > 0)
            {
                _logger.LogDebug("Found {Count} GeneratedTestCases, loading...", requirement.GeneratedTestCases.Count);
                
                foreach (var tc in requirement.GeneratedTestCases)
                {
                    // Convert TestCase.Steps collection to formatted string
                    var stepsText = string.Join("\n", tc.Steps.Select((s, i) => $"{i + 1}. {s.StepAction}"));
                    
                    var generatedTestCase = new GeneratedTestCase(null) // TODO: Pass proper reference
                    {
                        Title = tc.Name ?? $"TC-{TestCases.Count + 1:000}",
                        Preconditions = "", // TestCase model doesn't have Preconditions
                        Steps = stepsText,
                        ExpectedResults = "" // TestCase model doesn't have ExpectedResult
                    };
                    
                    TestCases.Add(generatedTestCase);
                }
                
                SelectedTestCase = TestCases.FirstOrDefault();
            }
            
            _logger.LogDebug("Loaded {Count} test cases", TestCases.Count);
        }

        /// <summary>
        /// Load generated test cases from mediator event
        /// </summary>
        private void LoadGeneratedTestCases(System.Collections.Generic.IReadOnlyList<TestCase> testCases)
        {
            TestCases.Clear();
            
            foreach (var tc in testCases)
            {
                var stepsText = string.Join("\\n", tc.Steps.Select((s, i) => $"{i + 1}. {s.StepAction}"));
                
                var generatedTestCase = new GeneratedTestCase(null) // TODO: Pass proper reference
                {
                    Title = tc.Name ?? $"TC-{TestCases.Count + 1:000}",
                    Preconditions = "", // TestCase model doesn't have Preconditions
                    Steps = stepsText,
                    ExpectedResults = "" // TestCase model doesn't have ExpectedResult
                };
                
                TestCases.Add(generatedTestCase);
            }
            
            SelectedTestCase = TestCases.FirstOrDefault();
            _logger.LogDebug("Loaded {Count} generated test cases", TestCases.Count);
        }

        /// <summary>
        /// Parse test cases from text format
        /// </summary>
        private void ParseTestCasesFromText(string testCasesText)
        {
            if (string.IsNullOrWhiteSpace(testCasesText)) return;
            
            try
            {
                // Simple parsing - look for test case patterns
                var testCasePattern = @"(?:Test Case|TC)\s*(\d+)\s*:?\s*([^\n\r]+)";
                var matches = Regex.Matches(testCasesText, testCasePattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                
                if (matches.Count > 0)
                {
                    foreach (Match match in matches)
                    {
                        var tcNumber = match.Groups[1].Value;
                        var tcTitle = match.Groups[2].Value;
                        
                        var generatedTestCase = new GeneratedTestCase(null) // TODO: Pass proper reference
                        {
                            Title = $"TC-{tcNumber.PadLeft(3, '0')}: {tcTitle}",
                            Preconditions = "",
                            Steps = "1. ",
                            ExpectedResults = ""
                        };
                        
                        TestCases.Add(generatedTestCase);
                    }
                }
                else
                {
                    // Fallback: create single test case with text content
                    var generatedTestCase = new GeneratedTestCase(null) // TODO: Pass proper reference
                    {
                        Title = $"TC-001: Imported Test Case",
                        Preconditions = "",
                        Steps = testCasesText,
                        ExpectedResults = ""
                    };
                    
                    TestCases.Add(generatedTestCase);
                }
                
                SelectedTestCase = TestCases.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse test cases from text");
            }
        }

        /// <summary>
        /// Save test cases back to current requirement
        /// </summary>
        private void SaveTestCasesToRequirement(bool markDirty = true)
        {
            if (CurrentRequirement == null) return;
            
            try
            {
                // Convert GeneratedTestCase objects back to TestCase objects
                var testCases = TestCases.Select(gtc => new TestCase
                {
                    Name = gtc.Title,
                    TestCaseText = gtc.Preconditions, // Map Preconditions to TestCaseText
                    StepExpectedResult = gtc.ExpectedResults, // Map to available property
                    Steps = new ObservableCollection<TestStep>(ParseStepsFromText(gtc.Steps))
                }).ToList();
                
                // Update requirement's GeneratedTestCases
                CurrentRequirement.GeneratedTestCases = testCases;
                
                // Notify mediator of requirement update
                _mediator.UpdateRequirement(CurrentRequirement, new[] { "GeneratedTestCases" });
                
                if (markDirty)
                {
                    // TODO: Use proper workspace dirty tracking via mediator when available
                    _logger.LogDebug("Test cases saved and workspace marked dirty");
                }
                
                _logger.LogDebug("Saved {Count} test cases to requirement {RequirementId}", 
                    TestCases.Count, CurrentRequirement.GlobalId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save test cases to requirement");
            }
        }

        /// <summary>
        /// Parse steps text back into TestStep objects
        /// </summary>
        private System.Collections.Generic.List<TestStep> ParseStepsFromText(string stepsText)
        {
            var steps = new System.Collections.Generic.List<TestStep>();
            
            if (string.IsNullOrWhiteSpace(stepsText)) return steps;
            
            // Split by lines and parse numbered steps
            var lines = stepsText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine)) continue;
                
                // Remove step numbers (1., 2., etc.)
                var stepText = Regex.Replace(trimmedLine, @"^\d+\.\s*", "");
                
                steps.Add(new TestStep
                {
                    StepNumber = steps.Count + 1,
                    StepAction = stepText
                });
            }
            
            return steps;
        }

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override bool CanSave() => TestCases.Any();
        
        protected override async Task SaveAsync()
        {
            SaveTestCasesToRequirement();
            await Task.CompletedTask;
        }
        
        protected override bool CanRefresh() => true;
        
        protected override async Task RefreshAsync()
        {
            LoadTestCasesFromRequirement(CurrentRequirement);
            await Task.CompletedTask;
        }
        
        protected override bool CanCancel() => false;
        protected override void Cancel() { /* No-op */ }

        // ===== DISPOSE =====

        public new void Dispose()
        {
            // Unsubscribe from mediator events
            try
            {
                _mediator.Unsubscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
                _mediator.Unsubscribe<TestCaseGenerationEvents.TestCasesGenerated>(OnTestCasesGenerated);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error unsubscribing from mediator events during dispose");
            }

            base.Dispose();
        }
    }
}