using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class ClarifyingQuestionsViewModel
    {
        // Session state machine for the smart button
        private enum SessionState
        {
            Idle,
            QuestionsDisplayed,
            AwaitingAnswers,
            ReadyToGenerate,
            Generating
        }

        private SessionState _sessionState = SessionState.Idle;

        // Selected question binding (for ListView SelectedItem)
        [ObservableProperty] private ClarifyingQuestionVM? selectedClarifyingQuestion;

        // Commands expected by the view
        public ICommand SetAsAssumptionCommand { get; private set; } = null!;
        public ICommand AcceptQuestionCommand { get; private set; } = null!;
        public ICommand RegenerateCommand { get; private set; } = null!;
        public ICommand SmartButtonCommand { get; private set; } = null!;
        public ICommand ResetAssumptionsCommand { get; private set; } = null!;

        // Small helper properties bound by the view (computed)
        public string SmartButtonLabel => ComputeSmartButtonLabel();
        public bool CanExecuteSmartButton => _sessionState != SessionState.Generating;

        // ThinkingMode placeholder (string-backed; you can replace with enum later)
        [ObservableProperty] private string thinkingMode = "Quick";

        partial void OnIsClarifyingCommandRunningChanged(bool value)
        {
            // If ClarifyCommand is running, set session state appropriately
            if (value) _sessionState = SessionState.Generating;
            else if (PendingQuestions != null && PendingQuestions.Count > 0) _sessionState = SessionState.QuestionsDisplayed;
            else _sessionState = SessionState.Idle;

            NotifySmartButtonProperties();
        }

        // Constructor extension: call this at end of existing constructor to initialize commands.
        private void InitializeCommands()
        {
            SetAsAssumptionCommand = new RelayCommand<ClarifyingQuestionVM?>(q => { if (q != null) SetAsAssumption(q); });
            AcceptQuestionCommand = new RelayCommand<ClarifyingQuestionVM?>(q => { if (q != null) AcceptQuestion(q); });

            // Use AsyncRelayCommand so CanExecute notifications and async flow behave properly
            RegenerateCommand = new AsyncRelayCommand(RegenerateAsync, () => !IsClarifyingCommandRunning);
            SmartButtonCommand = new AsyncRelayCommand(ExecuteSmartButtonAsync, () => CanExecuteSmartButton);

            ResetAssumptionsCommand = new RelayCommand(ResetAssumptions);

            // When pending questions change, update session state so SmartButton updates
            PendingQuestions.CollectionChanged += (s, e) =>
            {
                UpdateSessionStateAfterQuestionsUpdate();
            };
        }

        // Call from the existing constructor (after collections are set up)
        // e.g. InitializeCommands();

        private void UpdateSessionStateAfterQuestionsUpdate()
        {
            if (PendingQuestions.Count == 0)
            {
                _sessionState = SessionState.Idle;
            }
            else if (PendingQuestions.Any(q => !q.IsAnswered))
            {
                _sessionState = SessionState.QuestionsDisplayed;
            }
            else
            {
                _sessionState = SessionState.ReadyToGenerate;
            }

            NotifySmartButtonProperties();
        }

        private void NotifySmartButtonProperties()
        {
            OnPropertyChanged(nameof(SmartButtonLabel));
            OnPropertyChanged(nameof(CanExecuteSmartButton));

            // Notify CanExecute on commands if they support it
            (RegenerateCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (SmartButtonCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }

        private string ComputeSmartButtonLabel()
        {
            return _sessionState switch
            {
                SessionState.Idle => "Ask Verifying Questions",
                SessionState.QuestionsDisplayed => "Submit Answers",
                SessionState.AwaitingAnswers => "Submit Answers",
                SessionState.ReadyToGenerate => "Generate Test Cases",
                SessionState.Generating => "Working…",
                _ => "Ask Verifying Questions",
            };
        }

        private async Task RegenerateAsync()
        {
            if (IsClarifyingCommandRunning) return;
            // Re-run question generation with current assumptions
            if (ClarifyCommand != null)
            {
                _sessionState = SessionState.Generating;
                NotifySmartButtonProperties();
                try
                {
                    await ClarifyCommand.ExecuteAsync(null);
                }
                catch
                {
                    // swallow; ClarifyCommand local error handling will set StatusHint
                }
                finally
                {
                    UpdateSessionStateAfterQuestionsUpdate();
                }
            }
        }

        private async Task ExecuteSmartButtonAsync()
        {
            if (!CanExecuteSmartButton) return;

            switch (_sessionState)
            {
                case SessionState.Idle:
                    // Ask verifying questions
                    if (ClarifyCommand != null)
                    {
                        _sessionState = SessionState.Generating;
                        NotifySmartButtonProperties();
                        await ClarifyCommand.ExecuteAsync(null);
                        UpdateSessionStateAfterQuestionsUpdate();
                    }
                    break;

                case SessionState.QuestionsDisplayed:
                case SessionState.AwaitingAnswers:
                    // Submit answers and apply any marked-as-assumption
                    SubmitAnswersAndApplyAssumptions();
                    break;

                case SessionState.ReadyToGenerate:
                    // Generate test cases (stub placeholder)
                    await GenerateTestCasesAsync();
                    break;

                case SessionState.Generating:
                default:
                    break;
            }
        }

        private void SubmitAnswersAndApplyAssumptions()
        {
            // Persist answers into LlmOutput or other storage as needed.
            // For now we treat marked-as-assumption questions as new assumptions and add them to SuggestedDefaults.
            bool anyNewAssumptions = false;

            foreach (var q in PendingQuestions.ToList())
            {
                if (q.MarkedAsAssumption)
                {
                    // Convert question into a DefaultItem via the helper on ClarifyingQuestionVM
                    var di = q.ToDefaultItem();
                    var exists = SuggestedDefaults.Any(d => string.Equals(d.Key, di.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Name, di.Name, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            SuggestedDefaults.Add(di);
                        });
                    }
                    anyNewAssumptions = true;
                    // Mark the question as resolved/assumed (IsAnswered will reflect it)
                }
            }

            // If any new assumptions were added, regenerate questions (automatic pass)
            if (anyNewAssumptions)
            {
                // Re-run generation to let LLM respect new assumptions
                _ = RegenerateAsync();
            }
            else
            {
                // No new assumptions: move to ReadyToGenerate if all questions answered
                if (PendingQuestions.All(q => q.IsAnswered))
                {
                    _sessionState = SessionState.ReadyToGenerate;
                    NotifySmartButtonProperties();
                }
                else
                {
                    // still waiting for answers
                    _sessionState = SessionState.QuestionsDisplayed;
                    NotifySmartButtonProperties();
                }
            }
        }

        private void SetAsAssumption(ClarifyingQuestionVM q)
        {
            if (q == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                q.MarkedAsAssumption = true;
                // Use the VM helper to produce a DefaultItem and add if not present
                var di = q.ToDefaultItem();
                if (!SuggestedDefaults.Any(d => string.Equals(d.Key, di.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Name, di.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    SuggestedDefaults.Add(di);
                }
            });

            UpdateSessionStateAfterQuestionsUpdate();
        }

        private void AcceptQuestion(ClarifyingQuestionVM q)
        {
            if (q == null) return;
            // Accepting simply marks as answered if answer exists, or set a default "Accepted" marker
            if (string.IsNullOrWhiteSpace(q.Answer))
            {
                q.Answer = "Accepted"; // minimal placeholder if user clicked Accept without typing; adjust UX if undesired
            }
            UpdateSessionStateAfterQuestionsUpdate();
        }

        private void ResetAssumptions()
        {
            // Minimal behavior: disable all SuggestedDefaults
            foreach (var d in SuggestedDefaults.ToList())
            {
                d.IsEnabled = false;
            }
            StatusHint = "Assumptions reset (disabled).";
            // If you want to remove newly added items instead, implement removal logic here.
        }

        private async Task GenerateTestCasesAsync()
        {
            _sessionState = SessionState.Generating;
            NotifySmartButtonProperties();

            try
            {
                // Minimal placeholder: your real implementation should build a prompt using assumptions + answers
                StatusHint = "Generating test cases (not implemented)";

                await Task.Delay(400); // small UI pause so user sees the working state

                // TODO: implement real test-case generation using _llm.GenerateAsync and parse results to populate TestCaseCreationViewModel
                StatusHint = "Test-case generation is not yet implemented.";
            }
            catch (Exception ex)
            {
                StatusHint = $"Error generating test cases: {ex.Message}";
            }
            finally
            {
                UpdateSessionStateAfterQuestionsUpdate();
            }
        }
    }
}