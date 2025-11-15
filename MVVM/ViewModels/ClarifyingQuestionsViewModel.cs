using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public partial class ClarifyingQuestionsViewModel : ObservableObject, IDisposable
    {
        private readonly IPersistenceService _persistence;
        private readonly ITextGenerationService? _llm;
        private readonly TestCaseCreatorHeaderViewModel? _headerVm;
        private readonly CancellationTokenSource _cts = new();
        private const string PersistenceKey = "clarifying_questions";

        // Collections (legacy shape + new shape)
        // Legacy callers expect Questions : ObservableCollection<string>
        public ObservableCollection<string> Questions { get; } = new();

        // New/modern shape used by UI with ClarifyingQuestionVM
        public ObservableCollection<ClarifyingQuestionVM> PendingQuestions { get; } = new();

        public ObservableCollection<DefaultPreset> DefaultPresets { get; } = new();
        public ObservableCollection<DefaultItem> SuggestedDefaults { get; } = new();
        public ObservableCollection<Preset> Presets { get; } = new();

        // Synchronization guard to avoid re-entrant updates
        private bool _synchronizingCollections;

        // Source-generated properties
        [ObservableProperty] private string? selectedQuestion;
        [ObservableProperty] private bool isClarifyingCommandRunning;
        [ObservableProperty] private int questionBudget = 3;
        [ObservableProperty] private bool useIntegratedLlm = true;
        [ObservableProperty] private string clarifyingButtonLabel = "Ask Clarifying Questions";
        [ObservableProperty] private string? statusHint;
        [ObservableProperty] private string llmOutput = string.Empty;

        // Commands
        public IAsyncRelayCommand ClarifyCommand { get; }
        public ICommand AddQuestionCommand { get; }
        public ICommand MarkResolvedCommand { get; }
        public ICommand PasteQuestionsFromClipboardCommand { get; }

        // Forwarding / compatibility members added so XAML bindings resolve
        private Preset? _selectedPreset;
        public Preset? SelectedPreset
        {
            get => _selectedPreset;
            set => SetProperty(ref _selectedPreset, value);
        }

        public ICommand ClearPresetFilterCommand { get; }
        public ICommand SavePresetCommand { get; }

        public IEnumerable<DefaultItem> FilteredDefaults => SuggestedDefaults;

        // Forward IsLlmBusy to header VM so ProgressBar binding on this VM works.
        public bool IsLlmBusy => _headerVm?.IsLlmBusy ?? false;

        // Handlers map for ClarifyingQuestionVM property-changed subscriptions
        private readonly Dictionary<ClarifyingQuestionVM, PropertyChangedEventHandler> _vmHandlers = new();

        // Add or replace this constructor body in ClarifyingQuestionsViewModel
        public ClarifyingQuestionsViewModel(IPersistenceService persistence, ITextGenerationService? llm = null, TestCaseCreatorHeaderViewModel? headerVm = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _llm = llm;
            _headerVm = headerVm;

            // Initialize the simple command stubs
            ClearPresetFilterCommand = new RelayCommand(ClearPresetFilter);
            SavePresetCommand = new RelayCommand(SavePreset);

            // Hook header property changes so IsLlmBusy forwarding raises notifications
            if (_headerVm != null)
            {
                _headerVm.PropertyChanged += HeaderVm_PropertyChanged;
            }

            // Ensure FilteredDefaults consumers are notified when SuggestedDefaults changes
            SuggestedDefaults.CollectionChanged += (s, e) => OnPropertyChanged(nameof(FilteredDefaults));

            // Load persisted questions into legacy Questions collection (strings).
            var loaded = _persistence.Load<string[]>(PersistenceKey);
            if (loaded != null && loaded.Length > 0)
            {
                foreach (var q in loaded) Questions.Add(q);
            }
            else
            {
                //// seed default legacy questions
                //Questions.Add("CQ-001: What environment will this run in?");
                //Questions.Add("CQ-002: What browsers are supported?");
            }

            // Populate PendingQuestions from Questions
            foreach (var q in Questions) PendingQuestions.Add(new ClarifyingQuestionVM(q));

            // Hook collection changed handlers for two-way sync and persistence
            Questions.CollectionChanged += Questions_CollectionChanged;
            PendingQuestions.CollectionChanged += PendingQuestions_CollectionChanged;

            // Also watch individual ClarifyingQuestionVM.Text changes to keep Questions in sync
            foreach (var qvm in PendingQuestions) AttachQuestionVmHandler(qvm);

            // Commands
            ClarifyCommand = new AsyncRelayCommand(AskClarifyingQuestionsAsync, CanRunClarifying);
            // Legacy Add: add a string question (preserves old behavior)
            AddQuestionCommand = new RelayCommand(() => Questions.Add("New clarifying question"));
            // Legacy MarkResolved: remove by SelectedQuestion (string) if present. Also accept parameter as ClarifyingQuestionVM.
            MarkResolvedCommand = new RelayCommand<object?>(p =>
            {
                // If SelectedQuestion (string) exists, remove it
                if (!string.IsNullOrWhiteSpace(SelectedQuestion))
                {
                    RemoveQuestionByText(SelectedQuestion);
                    SelectedQuestion = null;
                    return;
                }

                // Otherwise, if parameter is ClarifyingQuestionVM, remove that
                if (p is ClarifyingQuestionVM qvm) PendingQuestions.Remove(qvm);
                // If parameter is a string, remove that too
                else if (p is string s && !string.IsNullOrWhiteSpace(s)) RemoveQuestionByText(s);
            });

            PasteQuestionsFromClipboardCommand = new RelayCommand(PasteQuestionsFromClipboard);

            // Observe global LLM connection manager so CanExecute updates
            LlmConnectionManager.ConnectionChanged += OnGlobalConnectionChanged;

            // Keep converted Presets in sync with DefaultPresets
            DefaultPresets.CollectionChanged += DefaultPresets_CollectionChanged;
            UpdatePresetsFromDefaultPresets();

            // Initialize command wiring (SetAsAssumption, SmartButton etc.) and session state
            InitializeCommands();
            UpdateSessionStateAfterQuestionsUpdate();
        }

        // Header property-changed forwarding
        private void HeaderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseCreatorHeaderViewModel.IsLlmBusy))
            {
                OnPropertyChanged(nameof(IsLlmBusy));
            }
        }

        // Minimal no-op command implementations (replace with real behavior if needed)
        private void ClearPresetFilter()
        {
            SelectedPreset = null;
            OnPropertyChanged(nameof(SelectedPreset));
        }

        private void SavePreset()
        {
            try
            {
                var p = new Preset { Name = SelectedPreset?.Name ?? "New Preset" };
                Presets.Add(p);
            }
            catch { /* swallow in stub */ }
        }

        private void DefaultPresets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePresetsFromDefaultPresets();

        private void UpdatePresetsFromDefaultPresets()
        {
            Presets.Clear();
            var conv = PresetConverter.ConvertDefaults(DefaultPresets);
            foreach (var p in conv) Presets.Add(p);
        }

        private void OnGlobalConnectionChanged(bool connected) => ClarifyCommand.NotifyCanExecuteChanged();

        private bool CanRunClarifying() => !IsClarifyingCommandRunning && UseIntegratedLlm && LlmConnectionManager.IsConnected && _llm != null;

        // --- Collection synchronization helpers ------------------
        private void Questions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_synchronizingCollections) return;
            try
            {
                _synchronizingCollections = true;
                // If items added to Questions, add to PendingQuestions as ClarifyingQuestionVM
                if (e.NewItems != null)
                {
                    foreach (var it in e.NewItems.OfType<string>())
                    {
                        PendingQuestions.Add(new ClarifyingQuestionVM(it));
                    }
                }

                // If items removed from Questions, remove matching PendingQuestions by text (first match)
                if (e.OldItems != null)
                {
                    foreach (var it in e.OldItems.OfType<string>())
                    {
                        var match = PendingQuestions.FirstOrDefault(q => string.Equals(q.Text, it, StringComparison.Ordinal));
                        if (match != null) PendingQuestions.Remove(match);
                    }
                }

                // Persist Questions to storage
                try
                {
                    _persistence.Save(PersistenceKey, Questions.ToArray());
                }
                catch
                {
                    StatusHint = "Failed to save clarifying questions.";
                }
            }
            finally
            {
                _synchronizingCollections = false;
            }
        }

        private void PendingQuestions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_synchronizingCollections) return;
            try
            {
                _synchronizingCollections = true;

                // Attach handlers for new items, detach for old
                if (e.NewItems != null)
                {
                    foreach (var nv in e.NewItems.OfType<ClarifyingQuestionVM>())
                    {
                        AttachQuestionVmHandler(nv);
                        // Add text representation to Questions if not already present
                        if (!Questions.Contains(nv.Text)) Questions.Add(nv.Text);
                    }
                }

                if (e.OldItems != null)
                {
                    foreach (var ov in e.OldItems.OfType<ClarifyingQuestionVM>())
                    {
                        DetachQuestionVmHandler(ov);
                        // Remove first matching string from Questions
                        var txt = ov.Text;
                        var existing = Questions.FirstOrDefault(s => string.Equals(s, txt, StringComparison.Ordinal));
                        if (existing != null) Questions.Remove(existing);
                    }
                }

                // Persist Questions to storage
                try
                {
                    _persistence.Save(PersistenceKey, Questions.ToArray());
                }
                catch
                {
                    StatusHint = "Failed to save clarifying questions.";
                }

                // Update HasPendingQuestions-like signals if needed
                OnPropertyChanged(nameof(IsClarifyingCommandRunning));
            }
            finally
            {
                _synchronizingCollections = false;
            }
        }

        private void AttachQuestionVmHandler(ClarifyingQuestionVM qvm)
        {
            if (qvm == null || _vmHandlers.ContainsKey(qvm)) return;
            if (qvm is INotifyPropertyChanged inpc)
            {
                PropertyChangedEventHandler h = (s, e) =>
                {
                    if (e.PropertyName == nameof(ClarifyingQuestionVM.Text))
                    {
                        // keep Questions string collection in sync when ClarifyingQuestionVM.Text changes
                        if (_synchronizingCollections) return;
                        try
                        {
                            _synchronizingCollections = true;
                            var oldEntry = Questions.FirstOrDefault(x => string.Equals(x, qvm.Text, StringComparison.Ordinal));
                            if (oldEntry != null)
                            {
                                // replace matching item (best-effort)
                                var idx = Questions.IndexOf(oldEntry);
                                if (idx >= 0) Questions[idx] = qvm.Text;
                            }
                            else
                            {
                                Questions.Add(qvm.Text);
                            }
                        }
                        finally { _synchronizingCollections = false; }
                    }
                };
                inpc.PropertyChanged += h;
                _vmHandlers[qvm] = h;
            }
        }

        private void DetachQuestionVmHandler(ClarifyingQuestionVM qvm)
        {
            if (qvm == null) return;
            if (_vmHandlers.TryGetValue(qvm, out var h))
            {
                if (qvm is INotifyPropertyChanged inpc) inpc.PropertyChanged -= h;
                _vmHandlers.Remove(qvm);
            }
        }

        private void RemoveQuestionByText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // remove from Questions (legacy list)
            var existing = Questions.FirstOrDefault(s => string.Equals(s, text, StringComparison.Ordinal));
            if (existing != null) Questions.Remove(existing);

            // also remove matching PendingQuestion
            var match = PendingQuestions.FirstOrDefault(q => string.Equals(q.Text, text, StringComparison.Ordinal));
            if (match != null) PendingQuestions.Remove(match);
        }

        // --- LLM / Clarifying flow -------------
        // Diagnostic replacement for AskClarifyingQuestionsAsync.
        // Paste into ClarifyingQuestionsViewModel replacing the existing method to capture debugging info
        // Replace the existing AskClarifyingQuestionsAsync method in ClarifyingQuestionsViewModel with this implementation.

        private async Task AskClarifyingQuestionsAsync()
        {
            if (_llm == null)
            {
                StatusHint = "LLM client not configured.";
                return;
            }

            if (IsClarifyingCommandRunning) return;

            // Validate requirement data up front: do NOT use defaults or filler values.
            var requirementDescription = _headerVm?.RequirementDescription;
            var methodEnum = _headerVm?.RequirementMethodEnum;

            if (string.IsNullOrWhiteSpace(requirementDescription))
            {
                if (_headerVm != null)
                {
                    _headerVm.RequirementDescriptionHighlight = true;
                    _headerVm.StatusMessage = "Cannot ask clarifying questions: requirement Description is missing. Please enter the Description in the workspace header.";
                }
                return;
            }

            if (methodEnum == null)
            {
                if (_headerVm != null)
                {
                    _headerVm.RequirementMethodHighlight = true;
                    _headerVm.StatusMessage = "Cannot ask clarifying questions: Verification Method is not set. Please set the Method for the requirement.";
                }
                return;
            }

            IsClarifyingCommandRunning = true;
            ClarifyCommand.NotifyCanExecuteChanged();

            if (_headerVm != null) _headerVm.IsLlmBusy = true;

            try
            {
                // Clear only the UI list (we'll update persisted Questions after LLM returns)
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    PendingQuestions.Clear();
                    StatusHint = null;
                });

                // Build assumptions list from currently enabled default items (PromptLine is the full prompt text).
                var enabledAssumptions = SuggestedDefaults
                    .Where(d => d.IsEnabled)
                    .Select(d => d.PromptLine)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                // Additionally include any assumptions associated with the requirement's Verification Method.
                var methodName = methodEnum.Value.ToString();
                var methodAssumptions = SuggestedDefaults
                    .Where(d => !string.IsNullOrWhiteSpace(d.Category) && string.Equals(d.Category, methodName, StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.PromptLine)
                    .Where(s => !string.IsNullOrWhiteSpace(s));

                foreach (var a in methodAssumptions)
                {
                    if (!enabledAssumptions.Any(existing => string.Equals(existing, a, StringComparison.OrdinalIgnoreCase)))
                        enabledAssumptions.Add(a);
                }

                // Build a temporary Requirement instance to pass to the prompt builder (expects Requirement?).
                var tempRequirement = new Requirement
                {
                    Description = requirementDescription,
                    Method = methodEnum.Value
                };

                // Build prompt including the temporary requirement object and enabled assumptions.
                var prompt = ClarifyingParsingHelpers.BuildBudgetedQuestionsPrompt(
                    tempRequirement,
                    questionBudget,
                    false,
                    enabledAssumptions,
                    Enumerable.Empty<TableDto>());

                string llmText;
                try
                {
                    // Call LLM off the UI thread
                    llmText = await _llm.GenerateAsync(prompt, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher?.Invoke(() => StatusHint = $"LLM error: {ex.Message}");
                    return;
                }

                LlmOutput = llmText ?? string.Empty;

                // Extract suggested keys and merge into SuggestedDefaults catalog (DefaultItem)
                var suggested = ClarifyingParsingHelpers.TryExtractSuggestedChipKeys(llmText, SuggestedDefaults);
                if (suggested.Any())
                {
                    Application.Current?.Dispatcher?.Invoke(() => MergeLlmSuggestedDefaults(suggested));
                }

                // Parse questions from LLM output
                var parsed = ClarifyingParsingHelpers.ParseQuestions(llmText) ?? Enumerable.Empty<ClarifyingQuestionVM>();
                var parsedList = parsed.ToList();

                // Deduplicate parsed questions by normalized text (trim/collapse whitespace, lowercase)
                static string Normalize(string? s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                    var t = s!.Trim();
                    while (t.Contains("  ")) t = t.Replace("  ", " ");
                    return t.ToLowerInvariant();
                }

                var distinctParsed = parsedList
                    .GroupBy(q => Normalize(q?.Text))
                    .Where(g => !string.IsNullOrEmpty(g.Key))
                    .Select(g => g.First())
                    .ToList();

                int totalParsed = parsedList.Count;
                int distinctCount = distinctParsed.Count;
                int duplicatesRemoved = Math.Max(0, totalParsed - distinctCount);

                // Apply parsed questions to UI and persist the legacy Questions list
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    PendingQuestions.Clear();
                    Questions.Clear();

                    foreach (var qvm in distinctParsed)
                    {
                        AttachQuestionVmHandler(qvm);
                        PendingQuestions.Add(qvm);

                        // Add question text to legacy Questions list (avoid duplicates just in case)
                        var normalizedText = Normalize(qvm.Text);
                        if (!Questions.Any(existing => Normalize(existing) == normalizedText))
                        {
                            Questions.Add(qvm.Text);
                        }
                    }

                    // Persist the new Questions array for future sessions
                    try
                    {
                        _persistence.Save(PersistenceKey, Questions.ToArray());
                    }
                    catch
                    {
                        StatusHint = "Failed to save clarifying questions.";
                    }

                    StatusHint = distinctCount > 0
                        ? $"Loaded {distinctCount} question(s). {duplicatesRemoved} duplicate(s) removed."
                        : "No valid questions detected.";
                });
            }
            finally
            {
                // Ensure UI-bound flags are cleared on the UI thread
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Invoke(() =>
                    {
                        if (_headerVm != null) _headerVm.IsLlmBusy = false;
                        IsClarifyingCommandRunning = false;
                        ClarifyCommand.NotifyCanExecuteChanged();
                    });
                }
                else
                {
                    if (_headerVm != null) _headerVm.IsLlmBusy = false;
                    IsClarifyingCommandRunning = false;
                    ClarifyCommand.NotifyCanExecuteChanged();
                }
            }
        }

        // Merge suggested keys into SuggestedDefaults: mark IsLlmSuggested and enable matching DefaultItem entries.
        private void MergeLlmSuggestedDefaults(IEnumerable<string> suggestedKeys)
        {
            if (SuggestedDefaults == null || suggestedKeys == null) return;
            var want = new HashSet<string>(suggestedKeys, StringComparer.OrdinalIgnoreCase);
            if (want.Count == 0) return;

            foreach (var item in SuggestedDefaults)
            {
                if (want.Contains(item.Key ?? string.Empty))
                {
                    if (!item.IsEnabled) item.IsEnabled = true;
                    if (!item.IsLlmSuggested) item.IsLlmSuggested = true;
                }
            }
        }

        private void PasteQuestionsFromClipboard()
        {
            string llmText = string.Empty;
            try
            {
                if (!Clipboard.ContainsText())
                {
                    StatusHint = "Clipboard is empty. Copy the LLM's questions then click Paste.";
                    return;
                }
                llmText = Clipboard.GetText(TextDataFormat.UnicodeText) ?? string.Empty;
            }
            catch (Exception ex)
            {
                StatusHint = "Couldn't read from clipboard: " + ex.Message;
                return;
            }

            var parsed = ClarifyingParsingHelpers.ParseQuestions(llmText);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                PendingQuestions.Clear();
                Questions.Clear();
                foreach (ClarifyingQuestionVM q in parsed)
                {
                    PendingQuestions.Add(q);
                    Questions.Add(q.Text);
                }
                StatusHint = PendingQuestions.Count > 0 ? $"Loaded {PendingQuestions.Count} question(s)." : "No questions detected.";
            });
        }

        // ==================== Commands and Session State Management ====================
        // (merged from ClarifyingQuestionsViewModel.Commands.cs)
        
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

            // NOTE: PendingQuestions.CollectionChanged is subscribed in the main constructor
            // (PendingQuestions.CollectionChanged += PendingQuestions_CollectionChanged;)
            // to avoid duplicate handlers we do NOT subscribe here.
        }

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

        public void Dispose()
        {
            LlmConnectionManager.ConnectionChanged -= OnGlobalConnectionChanged;
            Questions.CollectionChanged -= Questions_CollectionChanged;
            PendingQuestions.CollectionChanged -= PendingQuestions_CollectionChanged;
            DefaultPresets.CollectionChanged -= DefaultPresets_CollectionChanged;
            if (_headerVm != null) _headerVm.PropertyChanged -= HeaderVm_PropertyChanged;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}