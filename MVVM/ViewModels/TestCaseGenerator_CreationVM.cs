using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public class TestCaseGenerator_CreationVM : ObservableObject, IDisposable
    {
        private string? _llmOutput;
        private GeneratedTestCase? _selectedTestCase;
        private readonly ITestCaseGenerator_Navigator? _navigator;
        private Requirement? _currentRequirement;
        
        public TestCaseGenerator_CreationVM(ITestCaseGenerator_Navigator? navigator = null)
        {
            _navigator = navigator;
            TestCases = new ObservableCollection<GeneratedTestCase>();

            // Subscribe to navigator changes
            if (_navigator != null)
            {
                _navigator.PropertyChanged += Navigator_PropertyChanged;
                _currentRequirement = _navigator.CurrentRequirement;
                LoadTestCasesFromRequirement(_currentRequirement);
            }

            AddTestCaseCommand = new RelayCommand(() =>
            {
                var newTestCase = new GeneratedTestCase(_navigator as MainViewModel)
                {
                    Title = $"TC-{TestCases.Count + 1:000}: New Test Case",
                    Preconditions = "",
                    Steps = "1. ",
                    ExpectedResults = ""
                };
                TestCases.Add(newTestCase);
                SelectedTestCase = newTestCase;
                SaveTestCasesToRequirement();
                MarkWorkspaceDirty();
            });

            RemoveTestCaseCommand = new RelayCommand(() =>
            {
                if (SelectedTestCase != null)
                {
                    TestCases.Remove(SelectedTestCase);
                    SelectedTestCase = TestCases.FirstOrDefault();
                    SaveTestCasesToRequirement();
                    MarkWorkspaceDirty();
                }
            }, () => SelectedTestCase != null);

            SubmitTestCasesCommand = new RelayCommand(() =>
            {
                SaveTestCasesToRequirement();
                MarkWorkspaceDirty();
            }, () => TestCases.Any());
        }

        private void MarkWorkspaceDirty()
        {
            // Try to mark the main workspace as dirty via navigator
            if (_navigator is MainViewModel mainVm)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] MarkWorkspaceDirty called! Stack trace: {Environment.StackTrace}");
                mainVm.IsDirty = true;
            }
        }

        private void Navigator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Navigator_PropertyChanged: {e.PropertyName}");
            
            if (e.PropertyName == nameof(ITestCaseGenerator_Navigator.CurrentRequirement))
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] CurrentRequirement changed! Old: {_currentRequirement?.Item}, New: {_navigator?.CurrentRequirement?.Item}");
                
                // Only save if there are actually test cases to preserve
                if (_currentRequirement != null && TestCases.Count > 0)
                {
                    SaveTestCasesToRequirement(markDirty: false);
                }
                
                // Load test cases for new requirement
                _currentRequirement = _navigator?.CurrentRequirement;
                LoadTestCasesFromRequirement(_currentRequirement);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Loaded {TestCases.Count} test cases");
            }
        }

        private void LoadTestCasesFromRequirement(Requirement? requirement)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] LoadTestCasesFromRequirement called for: {requirement?.Item ?? "<null>"}");
            
            TestCases.Clear();
            SelectedTestCase = null; // Clear selection before loading new test cases

            if (requirement == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Requirement is null, clearing test cases");
                return;
            }

            // First, check if requirement has GeneratedTestCases (the primary source)
            if (requirement.GeneratedTestCases != null && requirement.GeneratedTestCases.Count > 0)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Found {requirement.GeneratedTestCases.Count} GeneratedTestCases, loading...");
                
            foreach (var tc in requirement.GeneratedTestCases)
            {
                // Convert TestCase.Steps collection to formatted string
                var stepsText = string.Join("\n", tc.Steps.Select((s, i) => $"{i + 1}. {s.StepAction}"));
                var expectedText = string.Join("\n", tc.Steps.Select((s, i) => $"{i + 1}. {s.StepExpectedResult}"));
                
                var testCase = new GeneratedTestCase(_navigator as MainViewModel);
                testCase.SetPropertiesForLoad(
                    tc.Name ?? "",
                    tc.TestCaseText ?? "",
                    stepsText,
                    expectedText
                );
                TestCases.Add(testCase);
            }                SelectedTestCase = TestCases.FirstOrDefault();
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Loaded {TestCases.Count} test cases from GeneratedTestCases");
                return;
            }

            // Fallback: Check if requirement has an LLM response with output (for backward compatibility)
            if (requirement.CurrentResponse != null && !string.IsNullOrWhiteSpace(requirement.CurrentResponse.Output))
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Found LLM output ({requirement.CurrentResponse.Output.Length} chars), parsing...");
                // Parse the LLM output to populate test cases
                LlmOutput = requirement.CurrentResponse.Output;
                
                // Always set SelectedTestCase to the first test case when loading a new requirement
                SelectedTestCase = TestCases.FirstOrDefault();
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Set SelectedTestCase to: {SelectedTestCase?.Title}");
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] No test cases or LLM output found for requirement {requirement.Item}");
                // Clear the view if there's no test case data
                SelectedTestCase = null;
            }
        }

        private void SaveTestCasesToRequirement(bool markDirty = true)
        {
            if (_currentRequirement == null)
                return;

            TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] SaveTestCasesToRequirement: Saving {TestCases.Count} test cases");

            // Save all test cases to GeneratedTestCases collection
            _currentRequirement.GeneratedTestCases.Clear();
            
            foreach (var tc in TestCases)
            {
                var testCase = new TestCase
                {
                    Name = tc.Title ?? "",
                    TestCaseText = tc.Preconditions ?? ""
                };
                
                // Parse steps and expected results into TestStep collection
                if (!string.IsNullOrWhiteSpace(tc.Steps))
                {
                    var stepLines = tc.Steps.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var expectedLines = tc.ExpectedResults?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                    
                    for (int i = 0; i < stepLines.Length; i++)
                    {
                        var stepText = stepLines[i].Trim();
                        // Remove numbering if present (e.g., "1. Action" -> "Action")
                        stepText = System.Text.RegularExpressions.Regex.Replace(stepText, @"^\d+\.\s*", "");
                        
                        var expectedText = i < expectedLines.Length ? expectedLines[i].Trim() : "";
                        expectedText = System.Text.RegularExpressions.Regex.Replace(expectedText, @"^\d+\.\s*", "");
                        
                        testCase.Steps.Add(new TestStep
                        {
                            StepNumber = i + 1,
                            StepAction = stepText,
                            StepExpectedResult = expectedText
                        });
                    }
                }
                
                _currentRequirement.GeneratedTestCases.Add(testCase);
            }

            // Also reconstruct LLM output from first test case for backward compatibility
            if (TestCases.Count > 0)
            {
                var tc = TestCases.First();
                var reconstructed = $"Title: {tc.Title}\n\n" +
                                   $"Preconditions: {tc.Preconditions}\n\n" +
                                   $"Test Steps:\n{tc.Steps}\n\n" +
                                   $"Expected Results:\n{tc.ExpectedResults}";
                
                // Save to CurrentResponse so it persists
                if (_currentRequirement.CurrentResponse == null)
                {
                    _currentRequirement.CurrentResponse = new Requirement.LlmDraft();
                }
                _currentRequirement.CurrentResponse.Output = reconstructed;
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Saved to GeneratedTestCases.Count = {_currentRequirement.GeneratedTestCases.Count}");
            
            // Only mark dirty if this is a user action, not navigation
            if (markDirty && _navigator is MainViewModel mainVm)
            {
                mainVm.IsDirty = true;
                
                // Notify that test cases have changed so Test Cases menu becomes selectable
                try
                {
                    var updateMethod = mainVm.GetType().GetMethod("UpdateTestCaseStepSelectability", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    updateMethod?.Invoke(mainVm, null);
                }
                catch { /* best effort */ }
            }
        }

        public void Dispose()
        {
            if (_navigator != null)
            {
                _navigator.PropertyChanged -= Navigator_PropertyChanged;
            }
        }

        public ObservableCollection<GeneratedTestCase> TestCases { get; }

        public GeneratedTestCase? SelectedTestCase
        {
            get => _selectedTestCase;
            set
            {
                if (SetProperty(ref _selectedTestCase, value))
                {
                    (RemoveTestCaseCommand as IRelayCommand)?.NotifyCanExecuteChanged();
                }
            }
        }

        public string? LlmOutput
        {
            get => _llmOutput;
            set
            {
                if (SetProperty(ref _llmOutput, value))
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] LlmOutput set, parsing into test cases...");
                    
                    // Save LLM response to requirement's CurrentResponse so HasGeneratedTestCase returns true
                    if (_currentRequirement != null && !string.IsNullOrWhiteSpace(value))
                    {
                        _currentRequirement.SaveResponse(value);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Saved LLM response to requirement's CurrentResponse");
                    }
                    
                    // Always parse, even if empty
                    ParseLlmOutputIntoTestCases(value);
                    
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] After parsing: TestCases.Count = {TestCases.Count}");
                    
                    // Ensure we always have at least one item
                    if (TestCases.Count == 0)
                    {
                        TestCases.Add(new GeneratedTestCase(_navigator as MainViewModel)
                        {
                            Title = "[DEBUG] No test cases parsed",
                            Steps = $"LLM Output was: {(string.IsNullOrEmpty(value) ? "NULL or EMPTY" : $"{value.Length} characters")}",
                            ExpectedResults = string.IsNullOrEmpty(value) ? "(no output)" : value
                        });
                        SelectedTestCase = TestCases.FirstOrDefault();
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[TestCaseGenerator_CreationVM] Added debug test case");
                    }
                    
                    // Save the test cases to the requirement immediately when LLM output is set
                    SaveTestCasesToRequirement(markDirty: false);
                    
                    // Force UI update
                    OnPropertyChanged(nameof(TestCases));
                }
            }
        }

        public ICommand AddTestCaseCommand { get; }
        public ICommand RemoveTestCaseCommand { get; }
        public ICommand SubmitTestCasesCommand { get; }

        private void ParseLlmOutputIntoTestCases(string? output)
        {
            TestCases.Clear();

            if (string.IsNullOrWhiteSpace(output))
            {
                return; // Will be caught by debug fallback in setter
            }

            // The LLM was instructed to generate a SINGLE test case with sections
            // Expected format:
            // Title: ...
            // Preconditions: ...
            // Test Steps: ...
            // Expected Results: ...
            
            var title = ExtractSimpleSection(output, "Title", "Test Case Title", "Test Case");
            var preconditions = ExtractSimpleSection(output, "Preconditions", "Precondition");
            var steps = ExtractSimpleSection(output, "Test Steps", "Steps", "Test Step");
            var expectedResults = ExtractSimpleSection(output, "Expected Results", "Expected Result", "Expected");

            // If we found at least a title or steps, create the test case
            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(steps))
            {
                TestCases.Add(new GeneratedTestCase(_navigator as MainViewModel)
                {
                    Title = string.IsNullOrWhiteSpace(title) ? "Generated Test Case" : title,
                    Preconditions = preconditions ?? "",
                    Steps = steps ?? "",
                    ExpectedResults = expectedResults ?? ""
                });
                SelectedTestCase = TestCases.FirstOrDefault();
                return;
            }

            // If structured parsing failed, just put the whole thing in one test case
            var firstLine = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            TestCases.Add(new GeneratedTestCase(_navigator as MainViewModel)
            {
                Title = firstLine ?? "Generated Test Case",
                Preconditions = "",
                Steps = output.Trim(),
                ExpectedResults = ""
            });

            SelectedTestCase = TestCases.FirstOrDefault();
        }

        private string? ExtractSimpleSection(string content, params string[] sectionHeaders)
        {
            foreach (var header in sectionHeaders)
            {
                // Look for "Header:" followed by content until we hit another section header or end
                // Made more flexible - matches content after header until next section or end of string
                var pattern = $@"(?:{header})\s*:\s*(.+?)(?=(?:\r?\n\s*(?:Title|Preconditions?|Test Steps?|Steps?|Expected (?:Results?|Outcome))\s*:)|$)";
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    var extracted = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        return extracted;
                    }
                }
            }
            
            // Fallback: Try without requiring colon after header
            foreach (var header in sectionHeaders)
            {
                var pattern = $@"(?:{header})\s*\r?\n(.+?)(?=(?:\r?\n\s*(?:Title|Preconditions?|Test Steps?|Steps?|Expected (?:Results?|Outcome)))|$)";
                var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (match.Success)
                {
                    var extracted = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        return extracted;
                    }
                }
            }
            
            return null;
        }
    }
}