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
        private async Task AskClarifyingQuestionsAsync()
        {
            if (_llm == null)
            {
                StatusHint = "LLM client not configured.";
                return;
            }

            if (IsClarifyingCommandRunning) return;

            // These initial updates run on the caller's context (UI) before any await.
            IsClarifyingCommandRunning = true;
            ClarifyCommand.NotifyCanExecuteChanged();

            if (_headerVm != null) _headerVm.IsLlmBusy = true;

            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    PendingQuestions.Clear();
                    Questions.Clear();
                    StatusHint = null;
                });

                // Build prompt using helper
                var prompt = ClarifyingParsingHelpers.BuildBudgetedQuestionsPrompt(null, questionBudget, false, Enumerable.Empty<string>(), Enumerable.Empty<TableDto>());

                string llmText;
                try
                {
                    // Do the network/LLM call off the UI thread
                    llmText = await _llm.GenerateAsync(prompt, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher?.Invoke(() => StatusHint = $"LLM error: {ex.Message}");
                    return;
                }

                LlmOutput = llmText ?? string.Empty;

                // Extract suggested keys and merge them into SuggestedDefaults catalog (DefaultItem)
                var suggested = ClarifyingParsingHelpers.TryExtractSuggestedChipKeys(llmText, SuggestedDefaults);
                if (suggested.Any())
                {
                    Application.Current?.Dispatcher?.Invoke(() => MergeLlmSuggestedDefaults(suggested));
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

                    StatusHint = PendingQuestions.Count > 0 ? $"Loaded {PendingQuestions.Count} question(s)." : "No valid questions detected.";
                });
            }
            finally
            {
                // Ensure these UI-bound changes run on the UI thread
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
                    // Fallback if Application.Current is null (e.g., some test scenarios)
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