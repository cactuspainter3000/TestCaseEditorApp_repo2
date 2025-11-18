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
    // Serializable DTO for persisting clarifying questions with full metadata
    public class PersistedQuestion
    {
        public string Text { get; set; } = string.Empty;
        public string? Answer { get; set; }
        public string? Category { get; set; }
        public string Severity { get; set; } = "OPTIONAL";
        public string? Rationale { get; set; }
        public bool MarkedAsAssumption { get; set; }
        public string[] Options { get; set; } = Array.Empty<string>();
    }

    public partial class TestCaseGenerator_QuestionsVM : ObservableObject, IDisposable
    {
        private readonly IPersistenceService _persistence;
        private readonly ITextGenerationService? _llm;
        private readonly TestCaseGenerator_HeaderVM? _headerVm;
        private readonly MainViewModel? _mainVm;
        private readonly CancellationTokenSource _cts = new();
        private const string PersistenceKey = "clarifying_questions";
        private Requirement? _currentRequirement;

        // Collections (legacy shape + new shape)
        // Legacy callers expect Questions : ObservableCollection<string>
        public ObservableCollection<string> Questions { get; } = new();

        // New/modern shape used by UI with ClarifyingQuestionVM
        public ObservableCollection<ClarifyingQuestionVM> PendingQuestions { get; } = new();

        /// <summary>
        /// Access HeaderVM's shared SuggestedDefaults collection.
        /// This ensures assumptions persist across the Assumptions and Questions tabs.
        /// </summary>
        public ObservableCollection<DefaultItem> SuggestedDefaults => _headerVm?.SuggestedDefaults ?? new();
        
        /// <summary>
        /// Access HeaderVM's shared DefaultPresets collection.
        /// </summary>
        public ObservableCollection<DefaultPreset> DefaultPresets => _headerVm?.DefaultPresets ?? new();
        
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

        // Add or replace this constructor body in TestCaseGenerator_QuestionsVM
        public TestCaseGenerator_QuestionsVM(IPersistenceService persistence, ITextGenerationService? llm = null, TestCaseGenerator_HeaderVM? headerVm = null, MainViewModel? mainVm = null)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _llm = llm;
            _headerVm = headerVm;
            _mainVm = mainVm;

            // Track current requirement and subscribe to changes
            if (_mainVm != null)
            {
                _currentRequirement = _mainVm.CurrentRequirement;
                _mainVm.PropertyChanged += MainVm_PropertyChanged;
            }

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

            // Load persisted questions for current requirement
            LoadQuestionsForRequirement(_currentRequirement);

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

            // Load defaults catalog only if HeaderVM collections are empty
            if (SuggestedDefaults.Count == 0)
            {
                LoadDefaultsCatalog();
            }
        }

        /// <summary>
        /// Load the defaults catalog (chips/assumptions) and presets.
        /// Uses DefaultsHelper to load from Config/defaults.catalog.template.json or hardcoded fallback.
        /// </summary>
        private void LoadDefaultsCatalog()
        {
            try
            {
                var catalog = DefaultsHelper.LoadProjectDefaultsTemplate();

                // Populate SuggestedDefaults from catalog Items
                SuggestedDefaults.Clear();
                if (catalog?.Items != null)
                {
                    foreach (var item in catalog.Items)
                    {
                        SuggestedDefaults.Add(item);
                    }
                }

                // Populate DefaultPresets from catalog Presets
                DefaultPresets.Clear();
                if (catalog?.Presets != null)
                {
                    foreach (var preset in catalog.Presets)
                    {
                        DefaultPresets.Add(preset);
                    }
                }

                StatusHint = $"Loaded {SuggestedDefaults.Count} defaults and {DefaultPresets.Count} presets.";
            }
            catch (Exception ex)
            {
                StatusHint = $"Failed to load defaults catalog: {ex.Message}";
            }
        }

        // Header property-changed forwarding
        private void HeaderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestCaseGenerator_HeaderVM.IsLlmBusy))
            {
                OnPropertyChanged(nameof(IsLlmBusy));
            }
        }

        private void MainVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e?.PropertyName == nameof(MainViewModel.CurrentRequirement))
            {
                // Save current questions before switching
                SaveQuestionsForRequirement(_currentRequirement);
                
                // Load questions for new requirement
                _currentRequirement = _mainVm?.CurrentRequirement;
                LoadQuestionsForRequirement(_currentRequirement);
            }
        }

        private void LoadQuestionsForRequirement(Requirement? requirement)
        {
            // Clear current questions
            PendingQuestions.Clear();
            Questions.Clear();

            if (requirement == null)
                return;

            // Load from requirement's ClarifyingQuestions property (persisted in workspace JSON)
            if (requirement.ClarifyingQuestions != null && requirement.ClarifyingQuestions.Count > 0)
            {
                _synchronizingCollections = true;
                try
                {
                    foreach (var data in requirement.ClarifyingQuestions)
                    {
                        var qvm = new ClarifyingQuestionVM
                        {
                            Text = data.Text,
                            Answer = data.Answer,
                            Category = data.Category,
                            Severity = data.Severity,
                            Rationale = data.Rationale,
                            MarkedAsAssumption = data.MarkedAsAssumption
                        };
                        
                        if (data.Options != null)
                        {
                            qvm.Options.Clear();
                            foreach (var opt in data.Options)
                                qvm.Options.Add(opt);
                        }

                        PendingQuestions.Add(qvm);
                        AttachQuestionVmHandler(qvm);
                    }
                }
                finally
                {
                    _synchronizingCollections = false;
                }
            }
        }

        private void SaveQuestionsForRequirement(Requirement? requirement)
        {
            if (requirement == null)
                return;

            // Save to requirement's ClarifyingQuestions property (will be persisted in workspace JSON)
            requirement.ClarifyingQuestions.Clear();
            foreach (var q in PendingQuestions)
            {
                requirement.ClarifyingQuestions.Add(new ClarifyingQuestionData
                {
                    Text = q.Text,
                    Answer = q.Answer,
                    Category = q.Category,
                    Severity = q.Severity,
                    Rationale = q.Rationale,
                    MarkedAsAssumption = q.MarkedAsAssumption,
                    Options = q.Options.ToList()
                });
            }
            
            // Mark workspace as dirty
            if (_mainVm != null)
            {
                _mainVm.IsDirty = true;
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

        private bool CanRunClarifying()
        {
            // Cannot run if already running
            if (IsClarifyingCommandRunning) return false;
            
            // Cannot run if not using integrated LLM or not connected
            if (!UseIntegratedLlm || !LlmConnectionManager.IsConnected || _llm == null) return false;
            
            // Cannot run if no requirement description or method
            var hasDescription = !string.IsNullOrWhiteSpace(_headerVm?.RequirementDescription);
            var hasMethod = _headerVm?.RequirementMethodEnum != null;
            
            return hasDescription && hasMethod;
        }

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
                    SaveQuestionsToStorage();
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
                    SaveQuestionsToStorage();
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
        // Paste into TestCaseGenerator_QuestionsVM replacing the existing method to capture debugging info
        // Replace the existing AskClarifyingQuestionsAsync method in TestCaseGenerator_QuestionsVM with this implementation.

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
                    StatusHint = "Requesting clarifying questions...";
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
                var customInstructions = DefaultsHelper.GetUserInstructions(methodEnum.Value);
                var prompt = ClarifyingParsingHelpers.BuildBudgetedQuestionsPrompt(
                    tempRequirement,
                    questionBudget,
                    false,
                    enabledAssumptions,
                    Enumerable.Empty<TableDto>(),
                    customInstructions);

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

                // Note: Malformed patterns are now automatically cleaned by ParseQuestions helper

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
                        SaveQuestionsToStorage();
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
        // (merged from TestCaseGenerator_QuestionsVM.Commands.cs)
        
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
        public ICommand SkipQuestionsCommand { get; private set; } = null!;
        public ICommand ResetAssumptionsCommand { get; private set; } = null!;

        // Small helper properties bound by the view (computed)
        public string SmartButtonLabel => ComputeSmartButtonLabel();
        public bool CanExecuteSmartButton => _sessionState != SessionState.Generating && (_headerVm == null || !_headerVm.IsLlmBusy);

        // ThinkingMode placeholder (string-backed; you can replace with enum later)
        [ObservableProperty] private string thinkingMode = "Quick";

        partial void OnIsClarifyingCommandRunningChanged(bool value)
        {
            // If ClarifyCommand is running, set session state appropriately
            if (value) _sessionState = SessionState.Generating;
            else if (PendingQuestions != null && PendingQuestions.Count > 0) _sessionState = SessionState.QuestionsDisplayed;
            else _sessionState = SessionState.Idle;

            NotifySmartButtonProperties();
            ClarifyCommand.NotifyCanExecuteChanged();
        }

        // Constructor extension: call this at end of existing constructor to initialize commands.
        private void InitializeCommands()
        {
            SetAsAssumptionCommand = new RelayCommand<ClarifyingQuestionVM?>(q => { if (q != null) SetAsAssumption(q); });
            AcceptQuestionCommand = new RelayCommand<ClarifyingQuestionVM?>(q => { if (q != null) AcceptQuestion(q); });

            // Use AsyncRelayCommand so CanExecute notifications and async flow behave properly
            RegenerateCommand = new AsyncRelayCommand(RegenerateAsync, () => !IsClarifyingCommandRunning);
            SmartButtonCommand = new AsyncRelayCommand(ExecuteSmartButtonAsync, () => CanExecuteSmartButton);
            SkipQuestionsCommand = new AsyncRelayCommand(SkipToTestCaseGenerationAsync, () => true); // Always allow skipping to test case generation

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
            else
            {
                // Only count questions that haven't been submitted (not fading out or removed)
                var activeQuestions = PendingQuestions.Where(q => !q.IsSubmitted).ToList();
                
                if (activeQuestions.Count == 0)
                {
                    // All questions have been submitted, ready to generate
                    _sessionState = SessionState.ReadyToGenerate;
                }
                else
                {
                    // Check if all remaining active questions are OPTIONAL (low priority)
                    var hasHighPriorityQuestions = activeQuestions.Any(q => 
                        !q.IsAnswered && 
                        !string.Equals(q.Severity, "OPTIONAL", StringComparison.OrdinalIgnoreCase));
                    
                    if (!hasHighPriorityQuestions)
                    {
                        // Only OPTIONAL questions remain (or all questions answered) - ready to generate
                        _sessionState = SessionState.ReadyToGenerate;
                    }
                    else if (activeQuestions.Any(q => !q.IsAnswered))
                    {
                        // Some active high-priority questions still need answers
                        _sessionState = SessionState.QuestionsDisplayed;
                    }
                    else
                    {
                        // All active questions have been answered but not submitted yet
                        _sessionState = SessionState.ReadyToGenerate;
                    }
                }
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
            ClarifyCommand.NotifyCanExecuteChanged();
        }

        private string ComputeSmartButtonLabel()
        {
            return _sessionState switch
            {
                SessionState.Idle => "Ask Clarifying Questions",
                SessionState.QuestionsDisplayed => "Submit Answers",
                SessionState.AwaitingAnswers => "Submit Answers",
                SessionState.ReadyToGenerate => "Generate Test Cases",
                SessionState.Generating => "Working…",
                _ => "Ask Clarifying Questions",
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
                    // Ask clarifying questions
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

        private async void SetAsAssumption(ClarifyingQuestionVM q)
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

                // Start fade-out animation
                q.IsFadingOut = true;
            });

            UpdateSessionStateAfterQuestionsUpdate();

            // Request replacement question in background
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // Wait for fade-out animation
                await RequestReplacementQuestionAsync(q);
            });
        }

        /// <summary>
        /// Request a single replacement question from the LLM after a question is answered or marked as assumption.
        /// This provides a continuous flow experience without waiting for all questions to be answered.
        /// </summary>
        /// <param name="oldQuestion">The question being replaced</param>
        /// <param name="keepOriginal">If true, keeps the original question visible (for answered questions); if false, removes it (for assumptions)</param>
        private async Task RequestReplacementQuestionAsync(ClarifyingQuestionVM oldQuestion, bool keepOriginal = false)
        {
            if (_llm == null || _headerVm == null) return;

            try
            {
                // Gather context for the prompt
                var requirementDescription = _headerVm.RequirementDescription;
                var methodEnum = _headerVm.RequirementMethodEnum;
                if (string.IsNullOrWhiteSpace(requirementDescription) || methodEnum == null)
                {
                    // Remove old question without replacement (only if not keeping original)
                    if (!keepOriginal)
                    {
                        Application.Current?.Dispatcher?.Invoke(() => PendingQuestions.Remove(oldQuestion));
                    }
                    return;
                }

                // Build list of enabled assumptions
                var enabledAssumptions = SuggestedDefaults
                    .Where(d => d.IsEnabled)
                    .Select(d => d.PromptLine)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

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

                // Add already answered questions as context to avoid duplicate questions
                var answeredQuestions = PendingQuestions
                    .Where(q => !string.IsNullOrWhiteSpace(q.Answer) || q.MarkedAsAssumption)
                    .Select(q => $"Q: {q.Text} | A: {q.Answer ?? "(marked as assumption)"}")
                    .ToList();
                
                foreach (var aq in answeredQuestions)
                {
                    enabledAssumptions.Add(aq);
                }

                // Build temporary requirement
                var tempRequirement = new Requirement
                {
                    Description = requirementDescription,
                    Method = methodEnum.Value
                };

                // Request only 1 question as replacement
                var customInstructions = DefaultsHelper.GetUserInstructions(methodEnum.Value);
                var basePrompt = ClarifyingParsingHelpers.BuildBudgetedQuestionsPrompt(
                    tempRequirement,
                    1, // Single question
                    false,
                    enabledAssumptions,
                    Enumerable.Empty<TableDto>(),
                    customInstructions);

                // Add explicit instruction to return ONLY ONE question
                var prompt = basePrompt + "\n\nIMPORTANT: Return EXACTLY ONE question in a JSON array with a single object. Do not return multiple questions. Format: [{\"text\":\"...\",\"category\":\"...\",\"severity\":\"...\",\"rationale\":\"...\"}]";

                // Call LLM
                string? llmText = await _llm.GenerateAsync(prompt, _cts.Token).ConfigureAwait(false);

                // Parse the response - ParseQuestions will automatically clean up malformed patterns
                var parsed = ClarifyingParsingHelpers.ParseQuestions(llmText) ?? Enumerable.Empty<ClarifyingQuestionVM>();
                var parsedList = parsed.ToList();

                // Update UI on dispatcher thread
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Remove old question only if not keeping original
                    if (!keepOriginal)
                    {
                        PendingQuestions.Remove(oldQuestion);
                    }

                    // Add new questions (could be one from replacement request, or multiple if we extracted from malformed response)
                    if (parsedList != null && parsedList.Count > 0)
                    {
                        foreach (var newQuestion in parsedList)
                        {
                            // Check for duplicates against both pending questions AND saved assumptions
                            var normalized = NormalizeText(newQuestion.Text);
                            var isDuplicateOfPending = PendingQuestions.Any(q => NormalizeText(q.Text) == normalized);
                            
                            // For assumptions, extract the question text from ContentLine format "Q: {text} | A: {answer}"
                            var isDuplicateOfAssumption = SuggestedDefaults
                                .Where(d => d.IsEnabled)
                                .Any(d => {
                                    var nameMatch = NormalizeText(d.Name) == normalized;
                                    if (nameMatch) return true;
                                    
                                    // Extract question from ContentLine if it exists
                                    if (d.ContentLine != null && d.ContentLine.Contains("Q:"))
                                    {
                                        var qPart = d.ContentLine.Split(new[] { " | A:" }, StringSplitOptions.None)[0];
                                        if (qPart.StartsWith("Q: "))
                                        {
                                            var extractedQuestion = qPart.Substring(3);
                                            return NormalizeText(extractedQuestion) == normalized;
                                        }
                                    }
                                    
                                    return false;
                                });
                            
                            if (!isDuplicateOfPending && !isDuplicateOfAssumption)
                            {
                                PendingQuestions.Add(newQuestion);
                            }
                            else if (isDuplicateOfAssumption)
                            {
                                // Log that we skipped a duplicate
                                StatusHint = "Skipped duplicate question (already answered as assumption)";
                            }
                        }
                    }

                    UpdateSessionStateAfterQuestionsUpdate();
                });
            }
            catch (OperationCanceledException)
            {
                // User canceled, remove old question only if not keeping original
                if (!keepOriginal)
                {
                    Application.Current?.Dispatcher?.Invoke(() => PendingQuestions.Remove(oldQuestion));
                }
            }
            catch (Exception ex)
            {
                // On error, remove old question (if not keeping) and show status
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (!keepOriginal)
                    {
                        PendingQuestions.Remove(oldQuestion);
                    }
                    StatusHint = $"Error getting replacement question: {ex.Message}";
                });
            }
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var t = text!.Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t.ToLowerInvariant();
        }

        private async void AcceptQuestion(ClarifyingQuestionVM q)
        {
            if (q == null) return;

            // Prevent submitting if LLM is already busy
            if (_headerVm != null && _headerVm.IsLlmBusy)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusHint = "Please wait for the current LLM query to complete before submitting another answer.";
                });
                return;
            }

            bool hasAnswer = !string.IsNullOrWhiteSpace(q.Answer);
            bool isMarkedAsAssumption = q.MarkedAsAssumption;

            // Validation: must have either an answer or be marked as assumption
            if (!hasAnswer && !isMarkedAsAssumption)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusHint = "Please provide an answer or mark the question as an assumption before submitting.";
                });
                return;
            }

            // If marked as assumption, save to global assumptions catalog
            if (isMarkedAsAssumption)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Build the DefaultItem with answer included in ContentLine for proper prompt formatting
                    var baseItem = q.ToDefaultItem();
                    
                    // Create ContentLine that includes both question and answer
                    string contentLine;
                    if (hasAnswer)
                    {
                        contentLine = $"Q: {q.Text} | A: {q.Answer}";
                    }
                    else
                    {
                        contentLine = $"Assumption: {q.Text}";
                    }

                    var di = new DefaultItem
                    {
                        Key = baseItem.Key,
                        Name = baseItem.Name,
                        Description = baseItem.Description,
                        ContentLine = contentLine,  // This is what gets used in PromptLine
                        IsEnabled = true
                    };
                    
                    if (!SuggestedDefaults.Any(d => string.Equals(d.Key, di.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Name, di.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        SuggestedDefaults.Add(di);
                    }
                });
            }

            // Determine fade behavior: complete fade+remove if assumption-only, keep visible if answered
            bool shouldRemove = isMarkedAsAssumption && !hasAnswer;
            bool shouldKeepVisible = hasAnswer;
            
            // Check if we should request a replacement question
            // Don't request new questions if this was OPTIONAL or if only OPTIONAL questions will remain
            bool isOptionalQuestion = string.Equals(q.Severity, "OPTIONAL", StringComparison.OrdinalIgnoreCase);
            bool shouldRequestReplacement = !isOptionalQuestion;

            // Set LLM busy state BEFORE updating session state and UI (only if requesting replacement)
            if (shouldRequestReplacement && _headerVm != null) 
                _headerVm.IsLlmBusy = true;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                q.IsSubmitted = true;
                if (shouldRemove)
                {
                    q.IsFadingOut = true;
                }
            });

            UpdateSessionStateAfterQuestionsUpdate();

            // Request replacement question in background (only for high-priority questions)
            if (shouldRequestReplacement)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (shouldRemove)
                        {
                            await Task.Delay(1000); // Wait for fade-out animation
                            // Remove the question after fade completes
                            Application.Current?.Dispatcher?.Invoke(() => PendingQuestions.Remove(q));
                        }
                        await RequestReplacementQuestionAsync(q, keepOriginal: shouldKeepVisible);
                    }
                    finally
                    {
                        // Clear busy state when done
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (_headerVm != null) _headerVm.IsLlmBusy = false;
                        });
                    }
                });
            }
            else
            {
                // For OPTIONAL questions, just remove if needed but don't request replacement
                if (shouldRemove)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000); // Wait for fade-out animation
                        Application.Current?.Dispatcher?.Invoke(() => PendingQuestions.Remove(q));
                    });
                }
            }
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

        private async Task SkipToTestCaseGenerationAsync()
        {
            // Skip all questions and go straight to test case generation
            StatusHint = "Generating test cases with current assumptions...";
            await GenerateTestCasesAsync();
        }

        private async Task GenerateTestCasesAsync()
        {
            _sessionState = SessionState.Generating;
            NotifySmartButtonProperties();

            // Set spinner message
            if (_headerVm != null)
            {
                _headerVm.IsLlmBusy = true;
                StatusHint = "Generating test cases...";
            }

            try
            {
                // Build context from requirement and answered questions
                var requirementDescription = _headerVm?.RequirementDescription ?? "(no description)";
                var methodEnum = _headerVm?.RequirementMethodEnum;
                var methodName = methodEnum?.ToString() ?? "(none)";

                // Gather all enabled assumptions
                var enabledAssumptions = SuggestedDefaults
                    .Where(d => d.IsEnabled)
                    .Select(d => d.PromptLine)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                // Add answered questions to context
                var answeredQuestions = PendingQuestions
                    .Where(q => !string.IsNullOrWhiteSpace(q.Answer))
                    .Select(q => $"Q: {q.Text}\nA: {q.Answer}")
                    .ToList();

                // Build the prompt
                var prompt = new System.Text.StringBuilder();
                prompt.AppendLine("Generate test cases for the following requirement:");
                prompt.AppendLine();
                prompt.AppendLine($"Requirement: {requirementDescription}");
                prompt.AppendLine($"Verification Method: {methodName}");
                prompt.AppendLine();

                if (enabledAssumptions.Any())
                {
                    prompt.AppendLine("Known Assumptions:");
                    foreach (var assumption in enabledAssumptions)
                    {
                        prompt.AppendLine($"  - {assumption}");
                    }
                    prompt.AppendLine();
                }

                if (answeredQuestions.Any())
                {
                    prompt.AppendLine("Answered Clarifying Questions:");
                    foreach (var qa in answeredQuestions)
                    {
                        prompt.AppendLine(qa);
                    }
                    prompt.AppendLine();
                }

                prompt.AppendLine("Generate a single comprehensive test case that verifies this requirement.");
                prompt.AppendLine();
                prompt.AppendLine("Format your response EXACTLY as follows:");
                prompt.AppendLine();
                prompt.AppendLine("Title: [Your test case title]");
                prompt.AppendLine();
                prompt.AppendLine("Preconditions:");
                prompt.AppendLine("[List all preconditions needed before test execution]");
                prompt.AppendLine();
                prompt.AppendLine("Test Steps:");
                prompt.AppendLine("[List the test steps - use as many or as few as needed to be thorough]");
                prompt.AppendLine();
                prompt.AppendLine("Expected Results:");
                prompt.AppendLine("[Describe what should happen if the test passes - be specific about expected outcomes]");

                // Call LLM
                StatusHint = "Generating test cases from your answers...";
                string llmResponse = await _llm.GenerateAsync(prompt.ToString(), _cts.Token).ConfigureAwait(false);

                // Store raw output for inspection
                LlmOutput = llmResponse ?? "(no response)";
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusHint = $"Generated {llmResponse?.Length ?? 0} characters of test cases. Check LLM Output for raw response.";
                });

                // Navigate to the test case creation view and pass the LLM output
                if (_mainVm != null)
                {
                    var creationStep = _mainVm.TestCaseGeneratorSteps.FirstOrDefault(s => s.Id == "testcase-creation");
                    if (creationStep != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _mainVm.SelectedStep = creationStep;
                            
                            // Pass LLM output to the creation VM (which gets set in CurrentStepViewModel)
                            if (_mainVm.CurrentStepViewModel is TestCaseGenerator_CreationVM creationVm)
                            {
                                creationVm.LlmOutput = llmResponse;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusHint = $"Error generating test cases: {ex.Message}";
            }
            finally
            {
                // Clear busy state and update UI state on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_headerVm != null) _headerVm.IsLlmBusy = false;
                    UpdateSessionStateAfterQuestionsUpdate();
                });
            }
        }

        /// <summary>
        /// Save PendingQuestions to storage with full metadata.
        /// </summary>
        private void SaveQuestionsToStorage()
        {
            // Save to requirement-specific key if we have a current requirement
            if (_currentRequirement != null)
            {
                SaveQuestionsForRequirement(_currentRequirement);
            }
            else
            {
                // Fallback to global key for backward compatibility
                SaveQuestionsToStorageWithKey(PersistenceKey);
            }
        }

        private void SaveQuestionsToStorageWithKey(string key)
        {
            var toSave = PendingQuestions.Select(q => new PersistedQuestion
            {
                Text = q.Text,
                Answer = q.Answer,
                Category = q.Category,
                Severity = q.Severity,
                Rationale = q.Rationale,
                MarkedAsAssumption = q.MarkedAsAssumption,
                Options = q.Options.ToArray()
            }).ToArray();

            _persistence.Save(key, toSave);
        }

        public void Dispose()
        {
            LlmConnectionManager.ConnectionChanged -= OnGlobalConnectionChanged;
            Questions.CollectionChanged -= Questions_CollectionChanged;
            PendingQuestions.CollectionChanged -= PendingQuestions_CollectionChanged;
            DefaultPresets.CollectionChanged -= DefaultPresets_CollectionChanged;
            if (_headerVm != null)
            {
                _headerVm.PropertyChanged -= HeaderVm_PropertyChanged;
            }
            if (_mainVm != null)
            {
                _mainVm.PropertyChanged -= MainVm_PropertyChanged;
            }
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}