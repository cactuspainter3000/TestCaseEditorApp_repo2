using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services.Parsing;


namespace TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels
{

    /// <summary>
    /// Dummy MainWorkspace ViewModel used as a sandbox for testing isolated pipeline behavior.
    /// This can be used to debug prompt building, RAG calls, fallback LLM calls, and parser behavior
    /// without dragging the full Requirements domain into the workflow.
    /// </summary>
    public partial class Dummy_MainViewModel : BaseDomainViewModel
    {
        private new readonly IDummyMediator _mediator;
        private readonly ILogger<Dummy_MainViewModel> _logger;

        [ObservableProperty]
        private string sectionName = "Dummy Domain";

        [ObservableProperty]
        private string displayText = "🎯 Main Workspace - Working Perfectly!";

        [ObservableProperty]
        private string statusMessage = "This is the main content area. All workspace coordination is functioning.";

        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;

        [ObservableProperty]
        private string sharedMessage = "Ready for inter-workspace communication...";

        // ===== SANDBOX FIELDS =====

        /// <summary>
        /// Editable requirement ID for sandbox testing.
        /// </summary>
        [ObservableProperty]
        private string sandboxRequirementId = "DECAGON-REQ_RC-5";

        /// <summary>
        /// Editable requirement title/name for sandbox testing.
        /// </summary>
        [ObservableProperty]
        private string sandboxRequirementName = "Tier 1 Boundary Scan Coverage";

        /// <summary>
        /// Editable requirement text for sandbox testing.
        /// </summary>
        [ObservableProperty]
        private string sandboxRequirementText =
            "The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT.";

        /// <summary>
        /// Displays the exact prompt that would be sent to the LLM.
        /// </summary>
        [ObservableProperty]
        private string sandboxPrompt = string.Empty;

        /// <summary>
        /// Raw streaming chunks or incremental output from RAG / AnythingLLM.
        /// </summary>
        [ObservableProperty]
        private string sandboxRawStream = string.Empty;

        /// <summary>
        /// Final raw response returned by the selected pipeline.
        /// </summary>
        [ObservableProperty]
        private string sandboxFinalResponse = string.Empty;

        /// <summary>
        /// Parsed result or parser diagnostics.
        /// </summary>
        [ObservableProperty]
        private string sandboxParsedResult = string.Empty;

        /// <summary>
        /// Timing / execution summary for the most recent sandbox run.
        /// </summary>
        [ObservableProperty]
        private string sandboxTiming = "No tests run yet.";

        /// <summary>
        /// High-level sandbox status line.
        /// </summary>
        [ObservableProperty]
        private string sandboxStatus = "Sandbox idle.";

        [ObservableProperty]
        private string analysisPreview = "No analysis preview yet.";

        public ICommand TestButtonCommand { get; }
        public ICommand RunAnalysisDemoCommand { get; }
        public IRelayCommand BuildPromptCommand { get; }
        public IAsyncRelayCommand TestRagCommand { get; }
        public IAsyncRelayCommand TestOllamaCommand { get; }
        public IAsyncRelayCommand TestParserCommand { get; }
        public IAsyncRelayCommand TestFullPipelineCommand { get; }
        public IRelayCommand ClearSandboxCommand { get; }

        public Dummy_MainViewModel(
            IDummyMediator mediator,
            ILogger<Dummy_MainViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            TestButtonCommand = new RelayCommand(() =>
            {
                _mediator.ChangeWorkspace("AllWorkspaces", "Main view's button was clicked!");
            });

            RunAnalysisDemoCommand = new AsyncRelayCommand(async () =>
            {
                StatusMessage = "Starting demo analysis...";
                LastUpdated = DateTime.Now;
                _mediator.ChangeWorkspace("Main", StatusMessage);

                await Task.Delay(300);

                var demoRequirement = new Requirement
                {
                    Item = "DEMO-REQ-001",
                    Name = "Boundary Scan Coverage",
                    Description = "The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT."
                };

                StatusMessage = "Building analysis prompt...";
                LastUpdated = DateTime.Now;
                _mediator.ChangeWorkspace("Main", StatusMessage);

                await Task.Delay(300);

                var prompt = BuildDemoAnalysisPrompt(demoRequirement);

                AnalysisPreview = prompt;
                SharedMessage = "Demo result: prompt built successfully.";
                StatusMessage = "Prompt build completed successfully.";
                LastUpdated = DateTime.Now;

                _mediator.ChangeWorkspace("AllWorkspaces", SharedMessage);
                _mediator.ChangeWorkspace("Main", StatusMessage);
            });

            BuildPromptCommand = new RelayCommand(BuildSandboxPrompt);

            TestRagCommand = new AsyncRelayCommand(
                RunRagTestAsync,
                CanRunSandboxTests);

            TestOllamaCommand = new AsyncRelayCommand(
                RunOllamaTestAsync,
                CanRunSandboxTests);

            TestParserCommand = new AsyncRelayCommand(
                RunParserTestAsync,
                CanRunSandboxTests);

            TestFullPipelineCommand = new AsyncRelayCommand(
                RunFullPipelineTestAsync,
                CanRunSandboxTests);

            ClearSandboxCommand = new RelayCommand(ClearSandbox);

            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyDataUpdated>(OnDataUpdated);
            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyWorkspaceChanged>(OnWorkspaceChanged);

            BuildSandboxPrompt();
        }

        private string BuildDemoAnalysisPrompt(Requirement requirement)
        {
            var prompt = new StringBuilder();

            prompt.AppendLine("Analyze this single requirement for quality.");
            prompt.AppendLine("Focus on clarity, completeness, consistency, testability, and feasibility.");
            prompt.AppendLine("Return ONLY the analysis result.");
            prompt.AppendLine("Do NOT include markdown code fences.");
            prompt.AppendLine("Do NOT include citations, sources, document metadata, or commentary about retrieved context.");
            prompt.AppendLine("Do NOT restate the instructions.");
            prompt.AppendLine();

            prompt.AppendLine("Project Context: Dummy Demo Project");
            prompt.AppendLine();

            prompt.AppendLine($"Requirement ID: {requirement.Item}");
            prompt.AppendLine($"Requirement Name: {requirement.Name}");
            prompt.AppendLine("Requirement Text:");
            prompt.AppendLine(requirement.Description ?? string.Empty);
            prompt.AppendLine();

            prompt.AppendLine("Additional Context:");
            prompt.AppendLine("- Boundary scan shall support board-level interconnect verification.");
            prompt.AppendLine("- Tier 1 coverage may include programming and structural test access.");
            prompt.AppendLine();

            prompt.AppendLine("Supplemental Table:");
            prompt.AppendLine("| Signal Group | Coverage Type |");
            prompt.AppendLine("| JTAG Chain | Interconnect |");
            prompt.AppendLine("| CPLD Programming | Programming |");
            prompt.AppendLine();

            return prompt.ToString();
        }


        /// <summary>
        /// Handles data update events routed through the dummy mediator.
        /// </summary>
        private void OnDataUpdated(Dummy.Events.DummyEvents.DummyDataUpdated eventData)
        {
            if (eventData.PropertyName == "Main")
            {
                StatusMessage = eventData.NewValue?.ToString() ?? "Updated";
                LastUpdated = eventData.Timestamp;
            }
        }

        /// <summary>
        /// Handles workspace change notifications routed through the dummy mediator.
        /// </summary>
        private void OnWorkspaceChanged(Dummy.Events.DummyEvents.DummyWorkspaceChanged eventData)
        {
            if (eventData.WorkspaceName == "AllWorkspaces")
            {
                SharedMessage = eventData.NewContent;
            }
        }

        /// <summary>
        /// Rebuilds the sandbox prompt from the current requirement fields.
        /// </summary>
        private void BuildSandboxPrompt()
        {
            var sb = new StringBuilder();

            sb.AppendLine("Analyze this single requirement for quality.");
            sb.AppendLine("Focus on clarity, completeness, consistency, testability, and feasibility.");
            sb.AppendLine("Return ONLY the analysis result.");
            sb.AppendLine("Do NOT include markdown code fences.");
            sb.AppendLine();
            sb.AppendLine($"Requirement ID: {SandboxRequirementId}");
            sb.AppendLine($"Requirement Name: {SandboxRequirementName}");
            sb.AppendLine("Requirement Text:");
            sb.AppendLine(SandboxRequirementText);

            SandboxPrompt = sb.ToString();
            SandboxStatus = "Prompt rebuilt.";
            LastUpdated = DateTime.Now;
        }

        /// <summary>
        /// Clears sandbox output fields while preserving the current requirement inputs.
        /// </summary>
        private void ClearSandbox()
        {
            SandboxRawStream = string.Empty;
            SandboxFinalResponse = string.Empty;
            SandboxParsedResult = string.Empty;
            SandboxTiming = "Sandbox cleared.";
            SandboxStatus = "Sandbox idle.";
            LastUpdated = DateTime.Now;
        }

        /// <summary>
        /// Returns true when sandbox test commands are allowed to run.
        /// </summary>
        private bool CanRunSandboxTests()
        {
            return !IsBusy;
        }

        /// <summary>
        /// Simulates a RAG-only test path.
        /// Replace the body later with your direct TryRagAnalysisAsync call.
        /// </summary>
        private async Task RunRagTestAsync()
        {
            await RunSandboxOperationAsync(
                operationName: "RAG Test",
                simulatedResponse:
                    "RAG TEST PLACEHOLDER\n" +
                    "This is where the raw RAG response will appear once connected.");
        }

        /// <summary>
        /// Simulates an Ollama-only fallback test path.
        /// Replace the body later with your direct OllamaTextGenerationService call.
        /// </summary>
        private async Task RunOllamaTestAsync()
        {
            var start = DateTime.UtcNow;

            try
            {
                IsBusy = true;
                NotifySandboxCommandStates();

                SandboxStatus = "Running Ollama test...";
                SandboxTiming = "Ollama test in progress...";
                SandboxRawStream = string.Empty;
                SandboxFinalResponse = string.Empty;
                SandboxParsedResult = string.Empty;

                var demoRequirement = new Requirement
                {
                    Item = SandboxRequirementId,
                    Name = SandboxRequirementName,
                    Description = SandboxRequirementText
                };

                var systemMessage =
        @"You are a requirements analysis assistant.

Analyze the requirement for:
- Clarity
- Completeness
- Consistency
- Testability
- Feasibility

Return exactly this format:

QUALITY SCORE: <0-100>

ISSUES FOUND:
- <Issue Type> (<Severity>): <Description> | Fix: <Specific fix>

STRENGTHS:
- <Strength>

IMPROVED REQUIREMENT:
<single improved requirement statement>

RECOMMENDATIONS:
<brief recommendation>

OVERALL ASSESSMENT:
<brief summary>

Use only these Issue Types: Clarity, Completeness, Consistency, Testability, Feasibility
Use only these Severity levels: Low, Medium, High
Do not use markdown code fences.";

                var prompt = string.IsNullOrWhiteSpace(SandboxPrompt)
                    ? BuildDemoAnalysisPrompt(demoRequirement)
                    : SandboxPrompt;

                SandboxPrompt = prompt;

                SandboxRawStream = "Calling Ollama directly...";
                LastUpdated = DateTime.Now;

                var ollama = new OllamaTextGenerationService("phi4-mini:3.8b-q4_K_M");
                var response = await ollama.GenerateWithSystemAsync(systemMessage, prompt);

                SandboxFinalResponse = response ?? string.Empty;

                if (string.IsNullOrWhiteSpace(SandboxFinalResponse))
                {
                    SandboxParsedResult = "Ollama test completed, but response was empty.";
                    SandboxStatus = "Ollama test returned empty response.";
                }
                else
                {
                    SandboxParsedResult =
                        "OLLAMA RAW RESPONSE CAPTURED\n\n" +
                        SandboxFinalResponse;

                    SandboxStatus = "Ollama test completed successfully.";
                }

                var elapsed = DateTime.UtcNow - start;
                SandboxTiming = $"Ollama test completed in {elapsed.TotalMilliseconds:F0} ms.";
                LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Dummy Sandbox] Ollama test failed.");
                SandboxFinalResponse = string.Empty;
                SandboxParsedResult = $"Ollama test failed: {ex.Message}";
                SandboxStatus = "Ollama test failed.";
            }
            finally
            {
                IsBusy = false;
                NotifySandboxCommandStates();
            }
        }

        private async Task RunParserTestAsync()
        {
            var start = DateTime.UtcNow;

            try
            {
                IsBusy = true;
                SandboxStatus = "Running parser test...";
                SandboxTiming = "Parser test in progress...";
                SandboxParsedResult = string.Empty;

                await Task.Delay(100);

                var sampleResponse =
        @"QUALITY SCORE: 78

ISSUES FOUND:
- Clarity (Medium): ""Tier 1 Boundary Scan coverage"" is not defined in the requirement itself. | Fix: Define Tier 1 coverage or reference the governing definition source.
- Testability (Medium): The requirement does not specify measurable pass/fail criteria for coverage. | Fix: State how coverage will be verified and what constitutes acceptable completion.

STRENGTHS:
- The requirement clearly identifies the needed capability.
- The requirement uses mandatory language.

IMPROVED REQUIREMENT:
The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT in accordance with the project-defined Tier 1 Boundary Scan criteria.

RECOMMENDATIONS:
Add an objective definition of Tier 1 Boundary Scan coverage and specify how compliance will be verified during testing.

OVERALL ASSESSMENT:
This is a valid requirement, but it needs more definition to improve clarity and testability.";

                // Use existing SandboxFinalResponse if present, otherwise load the sample
                var responseToParse = string.IsNullOrWhiteSpace(SandboxFinalResponse)
                    ? sampleResponse
                    : SandboxFinalResponse;

                SandboxFinalResponse = responseToParse;

                var parserManager = new ResponseParserManager();
                var selectedParser = parserManager.GetSelectedParserName(responseToParse);
                var parsed = parserManager.ParseResponse(responseToParse, SandboxRequirementId);

                if (parsed == null)
                {
                    SandboxParsedResult =
                        "REAL PARSER TEST RESULT: parse failed\n\n" +
                        $"Selected Parser: {selectedParser}\n\n" +
                        "This response format is not currently supported by ResponseParserManager.\n\n" +
                        "Raw Response:\n" +
                        responseToParse;
                }
                else
                {
                    var sb = new StringBuilder();

                    sb.AppendLine("REAL PARSER TEST RESULT: parse succeeded");
                    sb.AppendLine();
                    sb.AppendLine($"Selected Parser: {selectedParser}");
                    sb.AppendLine();

                    sb.AppendLine($"OriginalQualityScore: {parsed.OriginalQualityScore}");
                    sb.AppendLine($"ImprovedRequirement: {parsed.ImprovedRequirement}");
                    sb.AppendLine($"FreeformFeedback: {parsed.FreeformFeedback}");
                    sb.AppendLine($"HallucinationCheck: {parsed.HallucinationCheck}");
                    sb.AppendLine($"IsAnalyzed: {parsed.IsAnalyzed}");
                    sb.AppendLine($"ErrorMessage: {parsed.ErrorMessage}");
                    sb.AppendLine();

                    sb.AppendLine($"Issues Count: {parsed.Issues?.Count ?? 0}");
                    if (parsed.Issues != null)
                    {
                        foreach (var issue in parsed.Issues)
                        {
                            sb.AppendLine(
                                $"- Issue | Category={issue.Category}, Severity={issue.Severity}, Description={issue.Description}, Fix={issue.Fix}");
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine($"Recommendations Count: {parsed.Recommendations?.Count ?? 0}");
                    if (parsed.Recommendations != null)
                    {
                        foreach (var rec in parsed.Recommendations)
                        {
                            sb.AppendLine(
                                $"- Recommendation | Category={rec.Category}, Description={rec.Description}, SuggestedEdit={rec.SuggestedEdit}");
                        }
                    }

                    SandboxParsedResult = sb.ToString();
                }

                var elapsed = DateTime.UtcNow - start;
                SandboxTiming = $"Parser test completed in {elapsed.TotalMilliseconds:F0} ms.";
                SandboxStatus = "Parser test completed.";
                LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Dummy Sandbox] Parser test failed.");
                SandboxParsedResult = $"Parser test failed: {ex.Message}";
                SandboxStatus = "Parser test failed.";
            }
            finally
            {
                IsBusy = false;
                NotifySandboxCommandStates();
            }
        }

        /// <summary>
        /// Runs the full placeholder flow in sequence: prompt -> simulated response -> simulated parse.
        /// Replace this with the real end-to-end path once each piece works independently.
        /// </summary>
        private async Task RunFullPipelineTestAsync()
        {
            await RunSandboxOperationAsync(
                operationName: "Full Pipeline Test",
                simulatedResponse:
                    "FULL PIPELINE PLACEHOLDER\n" +
                    "This is where the final end-to-end raw response will appear once connected.");

            await RunParserTestAsync();
        }

        /// <summary>
        /// Shared helper for sandbox test operations.
        /// </summary>
        private async Task RunSandboxOperationAsync(string operationName, string simulatedResponse)
        {
            var start = DateTime.UtcNow;

            try
            {
                IsBusy = true;
                NotifySandboxCommandStates();

                BuildSandboxPrompt();

                SandboxStatus = $"Running {operationName}...";
                SandboxTiming = $"{operationName} started...";
                SandboxRawStream = string.Empty;
                SandboxFinalResponse = string.Empty;
                SandboxParsedResult = string.Empty;

                _logger.LogInformation("[Dummy Sandbox] {Operation} started for requirement {RequirementId}",
                    operationName,
                    SandboxRequirementId);

                await Task.Delay(150);

                SandboxRawStream =
                    $"[{operationName}] Prompt length: {SandboxPrompt.Length}\n" +
                    $"[{operationName}] Simulated stream chunk 1...\n" +
                    $"[{operationName}] Simulated stream chunk 2...";

                await Task.Delay(150);

                SandboxFinalResponse = simulatedResponse;

                var elapsed = DateTime.UtcNow - start;
                SandboxTiming = $"{operationName} completed in {elapsed.TotalMilliseconds:F0} ms.";
                SandboxStatus = $"{operationName} completed.";
                LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Dummy Sandbox] {Operation} failed.", operationName);
                SandboxFinalResponse = $"{operationName} failed: {ex.Message}";
                SandboxStatus = $"{operationName} failed.";
            }
            finally
            {
                IsBusy = false;
                NotifySandboxCommandStates();
            }
        }

        /// <summary>
        /// Refreshes command enabled state when busy state changes.
        /// </summary>
        private void NotifySandboxCommandStates()
        {
            TestRagCommand.NotifyCanExecuteChanged();
            TestOllamaCommand.NotifyCanExecuteChanged();
            TestParserCommand.NotifyCanExecuteChanged();
            TestFullPipelineCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Updates the dummy workspace display when the section name changes.
        /// </summary>
        partial void OnSectionNameChanged(string value)
        {
            DisplayText = $"🎯 {value} Main Workspace - Working Perfectly!";
            StatusMessage = $"This is the main content area for {value}. All workspace coordination is functioning.";
            LastUpdated = DateTime.Now;

            _mediator.ChangeWorkspace("Main", StatusMessage);
        }

        /// <summary>
        /// Rebuild prompt when requirement ID changes.
        /// </summary>
        partial void OnSandboxRequirementIdChanged(string value) => BuildSandboxPrompt();

        /// <summary>
        /// Rebuild prompt when requirement name changes.
        /// </summary>
        partial void OnSandboxRequirementNameChanged(string value) => BuildSandboxPrompt();

        /// <summary>
        /// Rebuild prompt when requirement text changes.
        /// </summary>
        partial void OnSandboxRequirementTextChanged(string value) => BuildSandboxPrompt();

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====

        /// <summary>
        /// Dummy save implementation for sandbox testing.
        /// </summary>
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }

        /// <summary>
        /// Dummy cancel implementation resets visible dummy state.
        /// </summary>
        protected override void Cancel()
        {
            SectionName = "Dummy Domain";
            ClearSandbox();
        }

        /// <summary>
        /// Dummy refresh implementation updates timestamp.
        /// </summary>
        protected override async Task RefreshAsync()
        {
            LastUpdated = DateTime.Now;
            await Task.Delay(50);
        }

        /// <summary>
        /// Save is allowed whenever the view model is not busy.
        /// </summary>
        protected override bool CanSave()
        {
            return !IsBusy;
        }

        /// <summary>
        /// Cancel is always allowed in the dummy sandbox.
        /// </summary>
        protected override bool CanCancel()
        {
            return true;
        }

        /// <summary>
        /// Refresh is allowed whenever the view model is not busy.
        /// </summary>
        protected override bool CanRefresh()
        {
            return !IsBusy;
        }
    }


}