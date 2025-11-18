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
                var newTestCase = new GeneratedTestCase
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
                mainVm.IsDirty = true;
            }
        }

        private void Navigator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ITestCaseGenerator_Navigator.CurrentRequirement))
            {
                // Save current test cases before switching
                SaveTestCasesToRequirement();
                
                // Load test cases for new requirement
                _currentRequirement = _navigator?.CurrentRequirement;
                LoadTestCasesFromRequirement(_currentRequirement);
            }
        }

        private void LoadTestCasesFromRequirement(Requirement? requirement)
        {
            TestCases.Clear();

            if (requirement == null)
                return;

            // Check if requirement has an LLM response with output
            if (requirement.CurrentResponse != null && !string.IsNullOrWhiteSpace(requirement.CurrentResponse.Output))
            {
                // Parse the LLM output to populate test cases
                LlmOutput = requirement.CurrentResponse.Output;
            }
        }

        private void SaveTestCasesToRequirement()
        {
            if (_currentRequirement == null)
                return;

            // Recreate LLM output from current test cases for persistence
            // This allows the test cases to be reloaded when navigating back
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
                    // Always parse, even if empty
                    ParseLlmOutputIntoTestCases(value);
                    
                    // Ensure we always have at least one item
                    if (TestCases.Count == 0)
                    {
                        TestCases.Add(new GeneratedTestCase
                        {
                            Title = "[DEBUG] No test cases parsed",
                            Steps = $"LLM Output was: {(string.IsNullOrEmpty(value) ? "NULL or EMPTY" : $"{value.Length} characters")}",
                            ExpectedResults = string.IsNullOrEmpty(value) ? "(no output)" : value
                        });
                        SelectedTestCase = TestCases.FirstOrDefault();
                    }
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
                TestCases.Add(new GeneratedTestCase
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
            TestCases.Add(new GeneratedTestCase
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