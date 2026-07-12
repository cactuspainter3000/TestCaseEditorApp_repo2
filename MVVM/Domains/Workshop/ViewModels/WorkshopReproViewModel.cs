using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using RequirementAnalysis = TestCaseEditorApp.MVVM.Models.RequirementAnalysis;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels
{
    public enum RequirementLifecycleStage
    {
        Edit,
        StagedForCommit,
        Committed
    }

    public partial class WorkshopReproViewModel : ObservableObject
    {
        private readonly IRequirementsMediator _mediator;
        private readonly DispatcherTimer _analysisHeartbeatTimer;
        private DateTime _analysisStartedUtc;
        private DateTime _statusStepStartedUtc;
        private string _statusBaseText = string.Empty;
        private CancellationTokenSource? _attachmentScraperCts;

        // Per-requirement lifecycle state — stored here, not on the model
        private readonly Dictionary<string, RequirementLifecycleStage> _lifecycleStates = new();

        [ObservableProperty]
        private Requirement? currentRequirement;

        [ObservableProperty]
        private string requirementNameDisplay = "Requirement: (Name (not set))";

        [ObservableProperty]
        private int editCount;

        [ObservableProperty]
        private int stagedCount;

        [ObservableProperty]
        private int committedCount;

        [ObservableProperty]
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisStatusText = string.Empty;

        [ObservableProperty]
        private double analysisProgressValue;

        [ObservableProperty]
        private bool isAnalysisModalOpen;

        [ObservableProperty]
        private RequirementAnalysis? analysisResults;

        [ObservableProperty]
        private bool isComplianceExpanded;

        [ObservableProperty]
        private bool isAttachmentScanning;

        [ObservableProperty]
        private bool isAttachmentScraping;

        [ObservableProperty]
        private string attachmentScraperStatusText = "Ready to scan Jama attachments.";

        [ObservableProperty]
        private string attachmentScraperOutputText = "No extraction output yet.";

        [ObservableProperty]
        private JamaAttachment? selectedScraperAttachment;

        [ObservableProperty]
        private ObservableCollection<JamaAttachment> availableScraperAttachments = new();

        [ObservableProperty]
        private ObservableCollection<Requirement> scrapedRequirements = new();

        public bool HasScrapedRequirements => ScrapedRequirements.Count > 0;

        public string AttachmentScraperSummary => HasScrapedRequirements
            ? $"Extracted {ScrapedRequirements.Count} requirement(s)."
            : "No extracted requirements yet.";

        public RequirementLifecycleStage CurrentRequirementStage => GetCurrentStage(CurrentRequirement);

        public int TotalCount => _mediator.Requirements.Count;

        public string CommitStagedButtonText => $"Commit Staged ({StagedCount})";

        public ObservableCollection<Requirement> Requirements => _mediator.Requirements;

        public Requirement? SelectedRequirement
        {
            get => CurrentRequirement;
            set
            {
                if (value != null && value != CurrentRequirement)
                    _mediator.SelectRequirement(value);
            }
        }

        public WorkshopReproViewModel(IRequirementsMediator mediator)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

            _analysisHeartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _analysisHeartbeatTimer.Tick += (_, __) => UpdateAnalysisHeartbeatText();

            _mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<RequirementsEvents.RequirementAnalysisStarted>(OnAnalysisStarted);
            _mediator.Subscribe<RequirementsEvents.AnalysisProgress>(OnAnalysisProgress);
            _mediator.Subscribe<RequirementsEvents.RequirementAnalyzed>(OnAnalysisCompleted);
            _mediator.Subscribe<RequirementsEvents.RAGAnalysisFallback>(OnRagFallback);
            _mediator.Requirements.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(TotalCount));
                RefreshLifecycleCounts();
                NotifyNavigationCanExecute();
            };

            // Restore existing current requirement if the mediator already has one loaded
            if (_mediator.CurrentRequirement != null)
                ApplyCurrentRequirement(_mediator.CurrentRequirement);
        }

        // ===== Event handlers =====

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            ApplyCurrentRequirement(e.Requirement);
        }

        private void OnAnalysisStarted(RequirementsEvents.RequirementAnalysisStarted e)
        {
            _analysisStartedUtc = DateTime.UtcNow;
            _statusStepStartedUtc = _analysisStartedUtc;
            _statusBaseText = "Analyzing…";
            AnalysisStatusText = _statusBaseText;
            AnalysisProgressValue = 0;
            IsAnalyzing = true;
            _analysisHeartbeatTimer.Start();
        }

        private void OnAnalysisProgress(RequirementsEvents.AnalysisProgress e)
        {
            if (e.Requirement == CurrentRequirement)
            {
                var nextStatus = string.IsNullOrWhiteSpace(e.StatusMessage) ? "Analyzing…" : e.StatusMessage;
                if (!string.Equals(_statusBaseText, nextStatus, StringComparison.Ordinal))
                {
                    _statusBaseText = nextStatus;
                    _statusStepStartedUtc = DateTime.UtcNow;
                }

                AnalysisStatusText = FormatHeartbeatStatusText(_statusBaseText, DateTime.UtcNow);
                AnalysisProgressValue = e.PercentComplete;
            }
        }

        private void OnAnalysisCompleted(RequirementsEvents.RequirementAnalyzed e)
        {
            _analysisHeartbeatTimer.Stop();

            // Complete the progress bar
            AnalysisProgressValue = 100;
            
            // Capture results from the event rather than polling after await
            if (e.Requirement == CurrentRequirement)
            {
                AnalysisResults = e.Analysis;
                IsAnalyzing = false;
            }

            if (e.Success)
                AnalysisStatusText = $"✓ Complete ({e.AnalysisTime.TotalSeconds:F1}s)";
            else
                AnalysisStatusText = $"⚠ Completed with warnings";
        }

        private void OnRagFallback(RequirementsEvents.RAGAnalysisFallback e)
        {
            _statusBaseText = "↻ Retrying via direct LLM…";
            _statusStepStartedUtc = DateTime.UtcNow;
            AnalysisStatusText = FormatHeartbeatStatusText(_statusBaseText, _statusStepStartedUtc);
        }

        private void UpdateAnalysisHeartbeatText()
        {
            if (!IsAnalyzing) return;

            var baseText = string.IsNullOrWhiteSpace(_statusBaseText) ? "Analyzing…" : _statusBaseText;
            AnalysisStatusText = FormatHeartbeatStatusText(baseText, DateTime.UtcNow);
        }

        private string FormatHeartbeatStatusText(string baseText, DateTime nowUtc)
        {
            if (_analysisStartedUtc == default)
            {
                _analysisStartedUtc = nowUtc;
            }

            if (_statusStepStartedUtc == default)
            {
                _statusStepStartedUtc = _analysisStartedUtc;
            }

            var stepSeconds = Math.Max(0, (nowUtc - _statusStepStartedUtc).TotalSeconds);
            var totalSeconds = Math.Max(0, (nowUtc - _analysisStartedUtc).TotalSeconds);

            return $"{baseText} ({stepSeconds:F0}s in current step, {totalSeconds:F0}s total)";
        }

        private void ApplyCurrentRequirement(Requirement? req)
        {
            CurrentRequirement = req;
            RequirementNameDisplay = req != null
                ? $"Requirement: {(!string.IsNullOrWhiteSpace(req.Name) ? req.Name : req.Item ?? "(not set)")}"
                : "Requirement: (Name (not set))";

            NotifyNavigationCanExecute();
            OnPropertyChanged(nameof(SelectedRequirement));
            OnPropertyChanged(nameof(CurrentRequirementStage));
        }

        // ===== Commands =====

        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private async Task LlmAnalyzeRequirement(CancellationToken ct)
        {
            if (CurrentRequirement == null) return;
            try
            {
                IsAnalyzing = true;
                AnalysisResults = null; // Clear previous results
                IsAnalysisModalOpen = true;
                AnalysisStatusText = "Preparing analysis…";
                
                // Yield to let UI render before the long call
                await Task.Yield();
                
                System.Diagnostics.Debug.WriteLine($"[Analysis] Starting for requirement: {CurrentRequirement.Name}");
                
                // Add timeout to prevent hanging
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(60));
                    await _mediator.AnalyzeRequirementAsync(CurrentRequirement);
                }
                // AnalysisResults and IsAnalyzing are set by OnAnalysisCompleted mediator event
            }
            catch (OperationCanceledException)
            {
                _analysisHeartbeatTimer.Stop();
                AnalysisStatusText = "✗ Analysis timed out (60s limit)";
                AnalysisResults = null;
                System.Diagnostics.Debug.WriteLine("[Analysis] ERROR: Timed out");
            }
            catch (Exception ex)
            {
                _analysisHeartbeatTimer.Stop();
                AnalysisStatusText = $"✗ Error: {ex.Message}";
                AnalysisResults = null;
                System.Diagnostics.Debug.WriteLine($"[Analysis] ERROR: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _analysisHeartbeatTimer.Stop();
                IsAnalyzing = false;
                System.Diagnostics.Debug.WriteLine("[Analysis] IsAnalyzing = false");
            }
        }

        private bool CanAnalyze() => CurrentRequirement != null && !IsAnalyzing;

        [RelayCommand(CanExecute = nameof(CanSearchAttachments))]
        private async Task SearchAttachmentsAsync()
        {
            try
            {
                IsAttachmentScanning = true;
                AttachmentScraperStatusText = "Scanning Jama project attachments...";
                AttachmentScraperOutputText = "Waiting for attachment scan results...";

                var projectId = await _mediator.GetCurrentProjectIdAsync();
                if (projectId <= 0)
                {
                    AttachmentScraperStatusText = "No active Jama project found.";
                    return;
                }

                var attachments = await _mediator.ScanProjectAttachmentsAsync(projectId);
                AvailableScraperAttachments.Clear();
                foreach (var attachment in attachments.OrderBy(a => a.FileName))
                {
                    AvailableScraperAttachments.Add(attachment);
                }

                SelectedScraperAttachment = AvailableScraperAttachments.FirstOrDefault(a => a.IsSupportedDocument)
                    ?? AvailableScraperAttachments.FirstOrDefault();

                AttachmentScraperStatusText = AvailableScraperAttachments.Count > 0
                    ? $"Found {AvailableScraperAttachments.Count} attachment(s)."
                    : "No attachments found for this project.";
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Attachment scan failed: {ex.Message}";
            }
            finally
            {
                IsAttachmentScanning = false;
            }
        }

        private bool CanSearchAttachments() => !IsAttachmentScanning && !IsAttachmentScraping;

        [RelayCommand(CanExecute = nameof(CanScrapeSelectedAttachment))]
        private async Task ScrapeSelectedAttachmentAsync()
        {
            if (SelectedScraperAttachment == null)
            {
                AttachmentScraperStatusText = "Select an attachment before scraping.";
                return;
            }

            if (!SelectedScraperAttachment.IsSupportedDocument)
            {
                AttachmentScraperStatusText = $"Unsupported document type: {SelectedScraperAttachment.MimeType}";
                return;
            }

            if (SelectedScraperAttachment.ScrapeBlocked)
            {
                AttachmentScraperStatusText = string.IsNullOrWhiteSpace(SelectedScraperAttachment.IndexValidationMessage)
                    ? "Attachment index is stale. Re-index before scraping."
                    : SelectedScraperAttachment.IndexValidationMessage;
                return;
            }

            try
            {
                _attachmentScraperCts?.Cancel();
                _attachmentScraperCts?.Dispose();
                _attachmentScraperCts = new CancellationTokenSource();

                IsAttachmentScraping = true;
                ScrapedRequirements.Clear();
                OnPropertyChanged(nameof(HasScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));

                var projectId = await _mediator.GetCurrentProjectIdAsync();
                if (projectId <= 0)
                {
                    AttachmentScraperStatusText = "No active Jama project found.";
                    return;
                }

                var statusBuffer = "Starting attachment extraction...";
                AttachmentScraperStatusText = statusBuffer;
                AttachmentScraperOutputText = "Parsing attachment...";

                var requirements = await _mediator.ParseAttachmentRequirementsAsync(
                    SelectedScraperAttachment,
                    projectId,
                    progressCallback: message =>
                    {
                        statusBuffer = message;
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            AttachmentScraperStatusText = statusBuffer;
                        }));
                    },
                    cancellationToken: _attachmentScraperCts.Token);

                foreach (var requirement in requirements)
                {
                    ScrapedRequirements.Add(requirement);
                }

                AttachmentScraperStatusText = $"Scrape complete: {ScrapedRequirements.Count} requirement(s) extracted.";
                AttachmentScraperOutputText = BuildScraperOutputText(ScrapedRequirements);
                OnPropertyChanged(nameof(HasScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));
            }
            catch (OperationCanceledException)
            {
                AttachmentScraperStatusText = "Attachment scrape canceled.";
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Attachment scrape failed: {ex.Message}";
            }
            finally
            {
                IsAttachmentScraping = false;
            }
        }

        private bool CanScrapeSelectedAttachment() =>
            !IsAttachmentScanning &&
            !IsAttachmentScraping &&
            SelectedScraperAttachment != null;

        [RelayCommand(CanExecute = nameof(CanImportScrapedRequirements))]
        private async Task ImportScrapedRequirementsAsync()
        {
            if (!HasScrapedRequirements)
            {
                AttachmentScraperStatusText = "No scraped requirements available to import.";
                return;
            }

            try
            {
                AttachmentScraperStatusText = "Importing extracted requirements into workspace...";
                await _mediator.ImportRequirementsAsync(ScrapedRequirements.ToList());
                AttachmentScraperStatusText = $"Imported {ScrapedRequirements.Count} requirement(s) into workspace.";
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Import failed: {ex.Message}";
            }
        }

        private bool CanImportScrapedRequirements() =>
            HasScrapedRequirements &&
            !IsAttachmentScanning &&
            !IsAttachmentScraping;

        [RelayCommand(CanExecute = nameof(CanCancelAttachmentScraper))]
        private void CancelAttachmentScraper()
        {
            _attachmentScraperCts?.Cancel();
            AttachmentScraperStatusText = "Cancel requested...";
        }

        private bool CanCancelAttachmentScraper() => IsAttachmentScanning || IsAttachmentScraping;

        private static string BuildScraperOutputText(IEnumerable<Requirement> requirements)
        {
            var list = requirements.ToList();
            if (list.Count == 0)
            {
                return "No requirements extracted from selected attachment.";
            }

            var lines = new List<string>
            {
                $"Extracted {list.Count} requirement(s):",
                string.Empty
            };

            var previewLimit = Math.Min(25, list.Count);
            for (var i = 0; i < previewLimit; i++)
            {
                var req = list[i];
                var id = string.IsNullOrWhiteSpace(req.Item) ? $"REQ-{i + 1}" : req.Item;
                var text = string.IsNullOrWhiteSpace(req.Description) ? req.Name : req.Description;
                if (text.Length > 180)
                {
                    text = text[..180] + "...";
                }

                lines.Add($"{i + 1}. {id}: {text}");
            }

            if (list.Count > previewLimit)
            {
                lines.Add(string.Empty);
                lines.Add($"... plus {list.Count - previewLimit} more requirement(s). Use import to bring all into workspace.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        [RelayCommand(CanExecute = nameof(CanStage))]
        private void Stage()
        {
            if (CurrentRequirement == null) return;
            var key = GetKey(CurrentRequirement);
            _lifecycleStates[key] = RequirementLifecycleStage.StagedForCommit;
            RefreshLifecycleCounts();
            OnPropertyChanged(nameof(CurrentRequirementStage));
        }

        private bool CanStage() =>
            CurrentRequirement != null &&
            GetCurrentStage(CurrentRequirement) == RequirementLifecycleStage.Edit;

        [RelayCommand(CanExecute = nameof(CanUnstage))]
        private void Unstage()
        {
            if (CurrentRequirement == null) return;
            var key = GetKey(CurrentRequirement);
            _lifecycleStates[key] = RequirementLifecycleStage.Edit;
            RefreshLifecycleCounts();
            OnPropertyChanged(nameof(CurrentRequirementStage));
        }

        private bool CanUnstage() =>
            CurrentRequirement != null &&
            GetCurrentStage(CurrentRequirement) == RequirementLifecycleStage.StagedForCommit;

        [RelayCommand(CanExecute = nameof(CanCommitStaged))]
        private void CommitStaged()
        {
            // Stub — Jama write integration will be wired here
            foreach (var key in new List<string>(_lifecycleStates.Keys))
            {
                if (_lifecycleStates[key] == RequirementLifecycleStage.StagedForCommit)
                    _lifecycleStates[key] = RequirementLifecycleStage.Committed;
            }
            RefreshLifecycleCounts();
            OnPropertyChanged(nameof(CurrentRequirementStage));
        }

        private bool CanCommitStaged() => StagedCount > 0;

        [RelayCommand]
        private void CloseAnalysisModal()
        {
            IsAnalysisModalOpen = false;
            AnalysisResults = null;
            AnalysisStatusText = string.Empty;
        }

        [RelayCommand]
        private void ToggleAnalysisModal()
        {
            IsAnalysisModalOpen = !IsAnalysisModalOpen;
            if (!IsAnalysisModalOpen)
            {
                AnalysisResults = null;
                AnalysisStatusText = string.Empty;
            }
        }

        [RelayCommand]
        private void ToggleComplianceExpanded()
        {
            IsComplianceExpanded = !IsComplianceExpanded;
        }

        [RelayCommand(CanExecute = nameof(CanApplyImprovedRequirement))]
        private void ApplyImprovedRequirement()
        {
            if (CurrentRequirement == null || AnalysisResults?.ImprovedRequirement == null) return;
            CurrentRequirement.Description = AnalysisResults.ImprovedRequirement;
            // Notify the mediator so RequirementUpdated event fires and IsDirty is set
            _mediator.UpdateRequirement(CurrentRequirement, new[] { "Description" });
            AnalysisStatusText = "✓ Applied improved requirement to description";
        }

        private bool CanApplyImprovedRequirement() =>
            CurrentRequirement != null &&
            AnalysisResults != null &&
            !string.IsNullOrWhiteSpace(AnalysisResults.ImprovedRequirement);

        [RelayCommand(CanExecute = nameof(CanNavigatePrevious))]
        private void PreviousRequirement()
        {
            _mediator.NavigateToPrevious();
        }

        private bool CanNavigatePrevious() =>
            _mediator.Requirements.Count > 0 && _mediator.GetCurrentRequirementIndex() > 0;

        [RelayCommand(CanExecute = nameof(CanNavigateNext))]
        private void NextRequirement()
        {
            _mediator.NavigateToNext();
        }

        private bool CanNavigateNext()
        {
            var idx = _mediator.GetCurrentRequirementIndex();
            return idx >= 0 && idx < _mediator.Requirements.Count - 1;
        }

        // ===== Helpers =====

        private static string GetKey(Requirement req) =>
            req.GlobalId ?? req.Item ?? req.GetHashCode().ToString();

        private RequirementLifecycleStage GetCurrentStage(Requirement? req)
        {
            if (req == null) return RequirementLifecycleStage.Edit;
            var key = GetKey(req);
            return _lifecycleStates.TryGetValue(key, out var stage) ? stage : RequirementLifecycleStage.Edit;
        }

        private void RefreshLifecycleCounts()
        {
            int edit = 0, staged = 0, committed = 0;
            foreach (var req in _mediator.Requirements)
            {
                switch (GetCurrentStage(req))
                {
                    case RequirementLifecycleStage.StagedForCommit: staged++; break;
                    case RequirementLifecycleStage.Committed: committed++; break;
                    default: edit++; break;
                }
            }
            EditCount = edit;
            StagedCount = staged;
            CommittedCount = committed;
            OnPropertyChanged(nameof(CommitStagedButtonText));
            ((RelayCommand)StageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnstageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CommitStagedCommand).NotifyCanExecuteChanged();
        }

        private void NotifyNavigationCanExecute()
        {
            ((RelayCommand)PreviousRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextRequirementCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LlmAnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnstageCommand).NotifyCanExecuteChanged();
        }

        private void NotifyAttachmentScraperCanExecute()
        {
            SearchAttachmentsCommand.NotifyCanExecuteChanged();
            ScrapeSelectedAttachmentCommand.NotifyCanExecuteChanged();
            ImportScrapedRequirementsCommand.NotifyCanExecuteChanged();
            CancelAttachmentScraperCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsAnalyzingChanged(bool value) =>
            ((AsyncRelayCommand)LlmAnalyzeRequirementCommand).NotifyCanExecuteChanged();

        partial void OnIsAttachmentScanningChanged(bool value) => NotifyAttachmentScraperCanExecute();

        partial void OnIsAttachmentScrapingChanged(bool value) => NotifyAttachmentScraperCanExecute();

        partial void OnSelectedScraperAttachmentChanged(JamaAttachment? value) => NotifyAttachmentScraperCanExecute();

        partial void OnScrapedRequirementsChanged(ObservableCollection<Requirement> value)
        {
            OnPropertyChanged(nameof(HasScrapedRequirements));
            OnPropertyChanged(nameof(AttachmentScraperSummary));
            NotifyAttachmentScraperCanExecute();
        }
    }
}
