using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    public partial class RequirementEditorTableDraft : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string tableText = string.Empty;
    }

    public partial class ScrapedRequirementCandidate : ObservableObject
    {
        [ObservableProperty]
        private Requirement requirement = new();

        [ObservableProperty]
        private bool isSelected = true;
    }

    public enum RequirementLifecycleStage
    {
        Edit,
        StagedForCommit,
        Committed
    }

    public partial class WorkshopReproViewModel : ObservableObject
    {
        private readonly IRequirementsMediator _mediator;
        private readonly IWorkspaceDiagnosticsService _workspaceDiagnosticsService;
        private readonly IFileDialogService _fileDialogService;
        private readonly IJamaDocumentParserService _jamaDocumentParserService;
        private readonly DispatcherTimer _analysisHeartbeatTimer;
        private DateTime _analysisStartedUtc;
        private DateTime _statusStepStartedUtc;
        private string _statusBaseText = string.Empty;
        private CancellationTokenSource? _attachmentScraperCts;
        private readonly List<string> _attachmentLogLines = new();
        private DateTime _lastAttachmentScanLogUtc = DateTime.MinValue;
        private int _lastAttachmentScanLogCount = -1;

        // Per-requirement lifecycle state — stored here, not on the model
        private readonly Dictionary<string, RequirementLifecycleStage> _lifecycleStates = new();

        [ObservableProperty]
        private Requirement? currentRequirement;

        [ObservableProperty]
        private TestCase? selectedTestCase;

        [ObservableProperty]
        private int activeObjectTabIndex;

        [ObservableProperty]
        private string requirementNameDisplay = "Requirement: (Name (not set))";

        [ObservableProperty]
        private bool isRequirementEditorOpen;

        [ObservableProperty]
        private string requirementEditorDescription = string.Empty;

        [ObservableProperty]
        private ObservableCollection<RequirementEditorTableDraft> requirementEditorTables = new();

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
        private string attachmentScraperStatusText = "Ready for requirement extraction.";

        [ObservableProperty]
        private string attachmentScraperOutputText = "No extraction results yet.";

        [ObservableProperty]
        private double extractionOverallProgress;

        [ObservableProperty]
        private double extractionCurrentStepProgress;

        [ObservableProperty]
        private string extractionOverallLabel = "Overall Completeness";

        [ObservableProperty]
        private string extractionCurrentStepLabel = "Current Process";

        [ObservableProperty]
        private JamaAttachment? selectedScraperAttachment;

        [ObservableProperty]
        private ObservableCollection<JamaAttachment> availableScraperAttachments = new();

        [ObservableProperty]
        private ObservableCollection<ScrapedRequirementCandidate> scrapedRequirements = new();

        public bool HasScrapedRequirements => ScrapedRequirements.Count > 0;

        public int SelectedScrapedRequirementCount => ScrapedRequirements.Count(c => c.IsSelected);

        public bool HasSelectedScrapedRequirements => SelectedScrapedRequirementCount > 0;

        public string AttachmentScraperSummary => HasScrapedRequirements
            ? $"Requirement Candidates: {ScrapedRequirements.Count} (Selected: {SelectedScrapedRequirementCount})"
            : "Requirement Candidates: 0";

        public RequirementLifecycleStage CurrentRequirementStage => GetCurrentStage(CurrentRequirement);

        public RequirementLifecycleStage CurrentTestCaseStage => GetCurrentStage(SelectedTestCase);

        public bool IsTestCaseTabActive => ActiveObjectTabIndex == 1;

        public string CurrentObjectLabel => IsTestCaseTabActive ? "Current Test Case" : "Current Requirement";

        public int TotalCount => _mediator.Requirements.Count;

        public string CommitStagedButtonText => $"Commit Staged ({StagedCount})";

        public int StagedTestCaseCount => _mediator.Requirements
            .SelectMany(req => req.GeneratedTestCases ?? Enumerable.Empty<TestCase>())
            .Count(tc => GetCurrentStage(tc) == RequirementLifecycleStage.StagedForCommit);

        public string StagedGlobalSummaryText =>
            $"Staged Requirements ({StagedCount}) and Test Cases ({StagedTestCaseCount})";

        public string StageToggleButtonText => GetActiveStage() switch
        {
            RequirementLifecycleStage.StagedForCommit => "Click to Edit",
            RequirementLifecycleStage.Committed => "Committed",
            _ => "Click to Stage"
        };

        public string StageToggleDescription => GetActiveStage() switch
        {
            RequirementLifecycleStage.StagedForCommit => "Currently staged and ready for Jama import. Click to switch back to Edit Mode.",
            RequirementLifecycleStage.Committed => "Committed objects are locked from stage/draft toggling.",
            _ => "Currently in Edit Mode. Click to stage this object for Jama import."
        };

        public string StageToggleToolTip => GetActiveStage() switch
        {
            RequirementLifecycleStage.StagedForCommit => "Switch selected item to Edit Mode.",
            RequirementLifecycleStage.Committed => "Committed items cannot be moved back to Draft or Staged.",
            _ => "Stage selected item for Jama import."
        };

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

        public WorkshopReproViewModel(
            IRequirementsMediator mediator,
            IWorkspaceDiagnosticsService workspaceDiagnosticsService,
            IFileDialogService fileDialogService,
            IJamaDocumentParserService jamaDocumentParserService)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _workspaceDiagnosticsService = workspaceDiagnosticsService ?? throw new ArgumentNullException(nameof(workspaceDiagnosticsService));
            _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
            _jamaDocumentParserService = jamaDocumentParserService ?? throw new ArgumentNullException(nameof(jamaDocumentParserService));

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
            SelectedTestCase = req?.GeneratedTestCases?.FirstOrDefault();
            RequirementNameDisplay = req != null
                ? $"Requirement: {(!string.IsNullOrWhiteSpace(req.Name) ? req.Name : req.Item ?? "(not set)")}"
                : "Requirement: (Name (not set))";

            NotifyNavigationCanExecute();
            OnPropertyChanged(nameof(SelectedRequirement));
            NotifyLifecycleStateUiChanged();
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

        [RelayCommand(CanExecute = nameof(CanOpenRequirementEditor))]
        private void OpenRequirementEditor()
        {
            if (CurrentRequirement == null)
            {
                return;
            }

            RequirementEditorDescription = CurrentRequirement.Description ?? string.Empty;
            RequirementEditorTables = BuildTableDrafts(CurrentRequirement.Tables);
            IsRequirementEditorOpen = true;
        }

        private bool CanOpenRequirementEditor() => CurrentRequirement != null;

        [RelayCommand]
        private void CloseRequirementEditor()
        {
            IsRequirementEditorOpen = false;
        }

        [RelayCommand(CanExecute = nameof(CanUpdateRequirementFromEditor))]
        private void UpdateRequirementFromEditor()
        {
            if (CurrentRequirement == null)
            {
                return;
            }

            CurrentRequirement.Description = RequirementEditorDescription ?? string.Empty;
            CurrentRequirement.Tables = BuildTablesFromDrafts(RequirementEditorTables);

            _mediator.UpdateRequirement(CurrentRequirement, new[] { nameof(Requirement.Description), nameof(Requirement.Tables) });
            RequirementNameDisplay = !string.IsNullOrWhiteSpace(CurrentRequirement.Name)
                ? $"Requirement: {CurrentRequirement.Name}"
                : $"Requirement: {CurrentRequirement.Item}";
            IsRequirementEditorOpen = false;
        }

        private bool CanUpdateRequirementFromEditor() => CurrentRequirement != null;

        [RelayCommand(CanExecute = nameof(CanSearchAttachments))]
        private async Task SearchAttachmentsAsync()
        {
            try
            {
                IsAttachmentScanning = true;
                ExtractionOverallProgress = 0;
                ExtractionCurrentStepProgress = 0;
                AttachmentScraperStatusText = "Searching for Jama project attachments...";
                ResetAttachmentLog("Requirement Extraction Log");
                AppendAttachmentLog("Started attachment search.");
                ExtractionOverallLabel = "Overall Completeness: 0%";
                ExtractionCurrentStepLabel = "Current Process: Scanning attachments";
                _lastAttachmentScanLogUtc = DateTime.MinValue;
                _lastAttachmentScanLogCount = -1;

                var projectId = await _mediator.GetCurrentProjectIdAsync();
                if (projectId <= 0)
                {
                    AttachmentScraperStatusText = "No active Jama project found.";
                    AppendAttachmentLog("No active Jama project found.");
                    return;
                }

                var scanProgress = new Progress<AttachmentScanProgressData>(p =>
                {
                    var total = Math.Max(1, p.Total);
                    var ratio = Math.Clamp((double)p.Current / total, 0, 1);
                    var percent = ratio * 100.0;
                    ExtractionOverallProgress = percent;
                    ExtractionCurrentStepProgress = percent;
                    ExtractionOverallLabel = $"Overall Completeness: {percent:F0}%";
                    ExtractionCurrentStepLabel = $"Current Process: scanning attachments ({p.Current}/{total})";

                    var now = DateTime.UtcNow;
                    var shouldLog = p.Current <= 1 ||
                                    p.Current >= total ||
                                    p.Current - _lastAttachmentScanLogCount >= 5 ||
                                    (now - _lastAttachmentScanLogUtc).TotalMilliseconds >= 500;

                    if (shouldLog)
                    {
                        AppendAttachmentLog($"Scan progress {p.Current}/{total}: {p.ProgressText}");
                        _lastAttachmentScanLogUtc = now;
                        _lastAttachmentScanLogCount = p.Current;
                    }
                });

                var attachments = await _mediator.ScanProjectAttachmentsAsync(projectId, scanProgress);
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
                AppendAttachmentLog(AttachmentScraperStatusText);
                ExtractionOverallProgress = 100;
                ExtractionCurrentStepProgress = 100;
                ExtractionOverallLabel = "Overall Completeness: 100%";
                ExtractionCurrentStepLabel = "Current Process: attachment scan complete";
            }
            catch (HttpRequestException httpEx) when (httpEx.InnerException is System.Net.Sockets.SocketException socketEx && socketEx.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
            {
                var configuredBaseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL") ?? "(not set)";
                AttachmentScraperStatusText = "Cannot resolve Jama server host. Check VPN and Jama Base URL.";
                AppendAttachmentLog("Network connectivity error: Jama host name could not be resolved.");
                AppendAttachmentLog($"Configured JAMA_BASE_URL: {configuredBaseUrl}");
                AppendAttachmentLog("Verify VPN connection and the Jama Base URL in settings.");
            }
            catch (HttpRequestException httpEx)
            {
                AttachmentScraperStatusText = $"Attachment search failed: {httpEx.Message}";
                AppendAttachmentLog($"Attachment search failed: {httpEx.Message}");
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Attachment search failed: {ex.Message}";
                AppendAttachmentLog($"Attachment search failed: {ex.Message}");
            }
            finally
            {
                IsAttachmentScanning = false;
            }
        }

        private bool CanSearchAttachments() => !IsAttachmentScanning && !IsAttachmentScraping;

        [RelayCommand(CanExecute = nameof(CanExtractLocalDocument))]
        private async Task ExtractLocalDocumentAsync()
        {
            try
            {
                var selectedPath = _fileDialogService.ShowOpenFile(
                    "Select source document for local extraction",
                    "Supported documents (*.docx;*.doc;*.pdf;*.xlsx;*.xls;*.txt;*.md;*.csv)|*.docx;*.doc;*.pdf;*.xlsx;*.xls;*.txt;*.md;*.csv|All files (*.*)|*.*");

                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    AppendAttachmentLog("Local extraction canceled before file selection.");
                    return;
                }

                _attachmentScraperCts?.Cancel();
                _attachmentScraperCts?.Dispose();
                _attachmentScraperCts = new CancellationTokenSource();

                IsAttachmentScraping = true;
                ExtractionOverallProgress = 0;
                ExtractionCurrentStepProgress = 0;
                ExtractionOverallLabel = "Overall Completeness: 0%";
                ExtractionCurrentStepLabel = "Current Process: initializing";
                ScrapedRequirements.Clear();
                OnPropertyChanged(nameof(HasScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));

                var statusBuffer = $"Starting local extraction for {System.IO.Path.GetFileName(selectedPath)}...";
                AttachmentScraperStatusText = statusBuffer;
                AppendAttachmentLog(statusBuffer);
                UpdateExtractionProgressFromMessage(statusBuffer);

                var requirements = await _jamaDocumentParserService.ParseLocalDocumentAsync(
                    selectedPath,
                    progressCallback: message =>
                    {
                        statusBuffer = message;
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            UpdateExtractionProgressFromMessage(statusBuffer);
                            AppendAttachmentLog(statusBuffer);
                        }));
                    },
                    cancellationToken: _attachmentScraperCts.Token);

                foreach (var requirement in requirements)
                {
                    var candidate = new ScrapedRequirementCandidate
                    {
                        Requirement = requirement,
                        IsSelected = true
                    };
                    candidate.PropertyChanged += OnScrapedCandidatePropertyChanged;
                    ScrapedRequirements.Add(candidate);
                }

                AttachmentScraperStatusText = $"Local extraction complete: {ScrapedRequirements.Count} candidate(s) found.";
                ExtractionOverallProgress = 100;
                ExtractionCurrentStepProgress = 100;
                ExtractionOverallLabel = "Overall Completeness: 100%";
                ExtractionCurrentStepLabel = "Current Process: extraction complete";
                AppendAttachmentLog(AttachmentScraperStatusText);
                AppendAttachmentLog(BuildScraperOutputText(ScrapedRequirements.Select(c => c.Requirement)), force: true);
                OnPropertyChanged(nameof(HasScrapedRequirements));
                OnPropertyChanged(nameof(SelectedScrapedRequirementCount));
                OnPropertyChanged(nameof(HasSelectedScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));
            }
            catch (OperationCanceledException)
            {
                AttachmentScraperStatusText = "Local extraction canceled.";
                ExtractionCurrentStepLabel = "Current Process: canceled";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Local extraction failed: {ex.Message}";
                ExtractionCurrentStepLabel = "Current Process: failed";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
            finally
            {
                IsAttachmentScraping = false;
            }
        }

        private bool CanExtractLocalDocument() => !IsAttachmentScanning && !IsAttachmentScraping;

        [RelayCommand(CanExecute = nameof(CanScrapeSelectedAttachment))]
        private async Task ScrapeSelectedAttachmentAsync()
        {
            if (SelectedScraperAttachment == null)
            {
                AttachmentScraperStatusText = "Analyze Source Document: select an attachment first.";
                AppendAttachmentLog(AttachmentScraperStatusText);
                return;
            }

            if (!SelectedScraperAttachment.IsSupportedDocument)
            {
                AttachmentScraperStatusText = $"Unsupported document type: {SelectedScraperAttachment.MimeType}";
                AppendAttachmentLog(AttachmentScraperStatusText);
                return;
            }

            if (SelectedScraperAttachment.ScrapeBlocked)
            {
                AttachmentScraperStatusText = string.IsNullOrWhiteSpace(SelectedScraperAttachment.IndexValidationMessage)
                    ? "Attachment index is stale. Re-index before extraction."
                    : SelectedScraperAttachment.IndexValidationMessage;
                AppendAttachmentLog(AttachmentScraperStatusText);
                return;
            }

            try
            {
                _attachmentScraperCts?.Cancel();
                _attachmentScraperCts?.Dispose();
                _attachmentScraperCts = new CancellationTokenSource();

                IsAttachmentScraping = true;
                ExtractionOverallProgress = 0;
                ExtractionCurrentStepProgress = 0;
                ExtractionOverallLabel = "Overall Completeness: 0%";
                ExtractionCurrentStepLabel = "Current Process: initializing";
                ScrapedRequirements.Clear();
                OnPropertyChanged(nameof(HasScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));

                var projectId = await _mediator.GetCurrentProjectIdAsync();
                if (projectId <= 0)
                {
                    AttachmentScraperStatusText = "No active Jama project found.";
                    AppendAttachmentLog(AttachmentScraperStatusText);
                    return;
                }

                var statusBuffer = "Starting attachment extraction...";
                AttachmentScraperStatusText = statusBuffer;
                AppendAttachmentLog($"Started extraction for attachment {SelectedScraperAttachment.Id} ({SelectedScraperAttachment.FileName}).");
                UpdateExtractionProgressFromMessage(statusBuffer);

                var requirements = await _mediator.ParseAttachmentRequirementsAsync(
                    SelectedScraperAttachment,
                    projectId,
                    progressCallback: message =>
                    {
                        statusBuffer = message;
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            UpdateExtractionProgressFromMessage(statusBuffer);
                            AppendAttachmentLog(statusBuffer);
                        }));
                    },
                    cancellationToken: _attachmentScraperCts.Token);

                foreach (var requirement in requirements)
                {
                    var candidate = new ScrapedRequirementCandidate
                    {
                        Requirement = requirement,
                        IsSelected = true
                    };
                    candidate.PropertyChanged += OnScrapedCandidatePropertyChanged;
                    ScrapedRequirements.Add(candidate);
                }

                AttachmentScraperStatusText = $"Extraction complete: {ScrapedRequirements.Count} candidate(s) found.";
                ExtractionOverallProgress = 100;
                ExtractionCurrentStepProgress = 100;
                ExtractionOverallLabel = "Overall Completeness: 100%";
                ExtractionCurrentStepLabel = "Current Process: extraction complete";
                AppendAttachmentLog(AttachmentScraperStatusText);
                AppendAttachmentLog(BuildScraperOutputText(ScrapedRequirements.Select(c => c.Requirement)), force: true);
                OnPropertyChanged(nameof(HasScrapedRequirements));
                OnPropertyChanged(nameof(SelectedScrapedRequirementCount));
                OnPropertyChanged(nameof(HasSelectedScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));
            }
            catch (OperationCanceledException)
            {
                AttachmentScraperStatusText = "Requirement extraction canceled.";
                ExtractionCurrentStepLabel = "Current Process: canceled";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Requirement extraction failed: {ex.Message}";
                ExtractionCurrentStepLabel = "Current Process: failed";
                AppendAttachmentLog(AttachmentScraperStatusText);
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
                AttachmentScraperStatusText = "No requirement candidates available for qualification review.";
                AppendAttachmentLog(AttachmentScraperStatusText);
                return;
            }

            var selectedRequirements = ScrapedRequirements
                .Where(c => c.IsSelected)
                .Select(c => c.Requirement)
                .ToList();

            if (selectedRequirements.Count == 0)
            {
                AttachmentScraperStatusText = "No candidates selected for import.";
                AppendAttachmentLog(AttachmentScraperStatusText);
                return;
            }

            try
            {
                AttachmentScraperStatusText = "Qualification Review complete. Importing accepted requirements...";
                AppendAttachmentLog(AttachmentScraperStatusText);

                // Imported candidates start as Unstaged for Jama Object workflow.
                foreach (var req in selectedRequirements)
                {
                    req.Status = "Unstaged";

                    if (string.IsNullOrWhiteSpace(req.RelationshipStatus))
                    {
                        req.RelationshipStatus = req.Status;
                    }

                    if (string.IsNullOrWhiteSpace(req.ItemType))
                    {
                        req.ItemType = !string.IsNullOrWhiteSpace(req.RequirementType)
                            ? req.RequirementType
                            : "Requirement";
                    }
                }

                await _mediator.ImportRequirementsAsync(selectedRequirements);
                AttachmentScraperStatusText = $"Accepted Requirements imported: {selectedRequirements.Count}.";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Import failed: {ex.Message}";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
        }

        private bool CanImportScrapedRequirements() =>
            HasSelectedScrapedRequirements &&
            !IsAttachmentScanning &&
            !IsAttachmentScraping;

        [RelayCommand(CanExecute = nameof(CanCancelAttachmentScraper))]
        private void CancelAttachmentScraper()
        {
            _attachmentScraperCts?.Cancel();
            AttachmentScraperStatusText = "Cancel requested...";
            AppendAttachmentLog(AttachmentScraperStatusText);
        }

        private bool CanCancelAttachmentScraper() => IsAttachmentScanning || IsAttachmentScraping;

        [RelayCommand]
        private async Task ExportExtractionLogsAsync()
        {
            try
            {
                AppendAttachmentLog("Exporting analysis logs to diagnostics report.");
                await _workspaceDiagnosticsService.ExportAnalysisLogsAsync();
                AttachmentScraperStatusText = "Analysis logs exported and git sync attempted.";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Log export failed: {ex.Message}";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
        }

        [RelayCommand]
        private async Task CommitSelectedZipToGitAsync()
        {
            try
            {
                var selectedZip = _fileDialogService.ShowOpenFile(
                    "Select a Word document to commit",
                    "Word documents (*.docx)|*.docx|All files (*.*)|*.*");

                if (string.IsNullOrWhiteSpace(selectedZip))
                {
                    AppendAttachmentLog("Commit to git canceled before file selection.");
                    return;
                }

                if (!selectedZip.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    AttachmentScraperStatusText = "Please select a .docx file.";
                    AppendAttachmentLog(AttachmentScraperStatusText);
                    return;
                }

                AppendAttachmentLog($"Committing selected document to git: {selectedZip}");
                await _workspaceDiagnosticsService.CommitSelectedArtifactAsync(selectedZip);
                AttachmentScraperStatusText = "Selected document committed and pushed to git.";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
            catch (Exception ex)
            {
                AttachmentScraperStatusText = $"Commit document failed: {ex.Message}";
                AppendAttachmentLog(AttachmentScraperStatusText);
            }
        }

        private static string BuildScraperOutputText(IEnumerable<Requirement> requirements)
        {
            var list = requirements.ToList();
            if (list.Count == 0)
            {
                return "Requirement Candidates: none extracted from selected source document.";
            }

            var lines = new List<string>
            {
                $"Requirement Candidates ({list.Count}):",
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
                lines.Add($"... plus {list.Count - previewLimit} more candidate(s). Complete Qualification Review to accept all requirements.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void UpdateExtractionProgressFromMessage(string message)
        {
            var cleaned = string.IsNullOrWhiteSpace(message) ? "Processing" : message.Trim();
            var (phaseStart, phaseEnd) = GetExtractionPhaseWindow(cleaned);
            var friendly = BuildFriendlyExtractionStatus(cleaned);

            // Prefer explicit percentages if present in provider messages.
            var explicitPercentMatch = Regex.Match(cleaned, @"\b(?<pct>\d{1,3})%\b");
            if (explicitPercentMatch.Success &&
                int.TryParse(explicitPercentMatch.Groups["pct"].Value, out var explicitPercent))
            {
                explicitPercent = Math.Clamp(explicitPercent, 0, 100);
                ExtractionCurrentStepProgress = explicitPercent;
                var overallFromPhase = phaseStart + ((phaseEnd - phaseStart) * (explicitPercent / 100.0));
                ExtractionOverallProgress = Math.Max(ExtractionOverallProgress, overallFromPhase);
                AttachmentScraperStatusText = friendly;
                ExtractionOverallLabel = $"Overall Completeness: {ExtractionOverallProgress:F0}%";
                ExtractionCurrentStepLabel = $"Current Process: {friendly} ({explicitPercent:F0}%)";
                return;
            }

            // Parse counter-style progress such as "(3/10)".
            var fractionMatch = Regex.Match(cleaned, @"(?<current>\d+)\s*/\s*(?<total>\d+)");
            if (fractionMatch.Success &&
                int.TryParse(fractionMatch.Groups["current"].Value, out var current) &&
                int.TryParse(fractionMatch.Groups["total"].Value, out var total) &&
                total > 0)
            {
                var fractionPercent = Math.Clamp((double)current / total * 100.0, 0, 100);
                ExtractionCurrentStepProgress = fractionPercent;
                var overallFromPhase = phaseStart + ((phaseEnd - phaseStart) * (fractionPercent / 100.0));
                ExtractionOverallProgress = Math.Max(ExtractionOverallProgress, overallFromPhase);
                AttachmentScraperStatusText = friendly;
                ExtractionOverallLabel = $"Overall Completeness: {ExtractionOverallProgress:F0}%";
                ExtractionCurrentStepLabel = $"Current Process: {friendly} ({current}/{total})";
                return;
            }

            // Parse elapsed-time updates such as "(1m 45s elapsed)" from embedding/analyzing loops.
            var elapsedMatch = Regex.Match(cleaned, @"\((?<min>\d+)m\s+(?<sec>\d+)s(?:\s+elapsed)?\)");
            if (elapsedMatch.Success &&
                int.TryParse(elapsedMatch.Groups["min"].Value, out var minutes) &&
                int.TryParse(elapsedMatch.Groups["sec"].Value, out var seconds))
            {
                var elapsedSeconds = Math.Max(0, (minutes * 60) + seconds);
                var expectedPhaseSeconds = friendly.Contains("Embedding", StringComparison.OrdinalIgnoreCase)
                    ? 180.0
                    : 150.0;
                var elapsedPercent = Math.Clamp((elapsedSeconds / expectedPhaseSeconds) * 100.0, 0, 100);
                var overallFromPhase = phaseStart + ((phaseEnd - phaseStart) * (elapsedPercent / 100.0));

                ExtractionCurrentStepProgress = elapsedPercent;
                ExtractionOverallProgress = Math.Max(ExtractionOverallProgress, overallFromPhase);
                AttachmentScraperStatusText = friendly;
                ExtractionOverallLabel = $"Overall Completeness: {ExtractionOverallProgress:F0}%";
                ExtractionCurrentStepLabel = $"Current Process: {friendly} ({minutes}m {seconds}s)";
                return;
            }

            // Fallback to stage-based graduation when only descriptive text exists.
            var phaseRange = Math.Max(1, phaseEnd - phaseStart);
            var nudgedOverall = Math.Min(phaseEnd - 0.5, Math.Max(phaseStart, ExtractionOverallProgress + 1.25));
            ExtractionOverallProgress = Math.Max(ExtractionOverallProgress, nudgedOverall);
            var inPhasePercent = ((ExtractionOverallProgress - phaseStart) / phaseRange) * 100.0;
            ExtractionCurrentStepProgress = Math.Clamp(inPhasePercent, 0, 100);
            AttachmentScraperStatusText = friendly;
            ExtractionOverallLabel = $"Overall Completeness: {ExtractionOverallProgress:F0}%";
            ExtractionCurrentStepLabel = $"Current Process: {friendly}";
        }

        private static string BuildFriendlyExtractionStatus(string message)
        {
            var normalized = message.ToLowerInvariant();
            if (normalized.Contains("starting attachment extraction") || normalized.Contains("preparing to extract") || normalized.Contains("fallback requirement extraction")) return "Preparing extraction";
            if (normalized.Contains("preparing")) return "Preparing extraction";
            if (normalized.Contains("downloading")) return "Downloading source document";
            if (normalized.Contains("upload")) return "Uploading source document";
            if (normalized.Contains("embedding")) return "Embedding content";
            if (normalized.Contains("workspace")) return "Preparing analysis workspace";
            if (normalized.Contains("checking jama") || normalized.Contains("previously saved") || normalized.Contains("duplicate")) return "Checking for duplicates";
            if (normalized.Contains("analyzing")) return "Analyzing source document";
            if (normalized.Contains("extract")) return "Extracting requirement candidates";
            if (normalized.Contains("save progress") || normalized.Contains("saving") || normalized.Contains("retry save") || normalized.Contains("retrying")) return "Saving candidates to Jama";
            if (normalized.Contains("complete") || normalized.Contains("success")) return "Extraction complete";
            return "Processing";
        }

        private static (double Start, double End) GetExtractionPhaseWindow(string message)
        {
            var normalized = message.ToLowerInvariant();
            if (normalized.Contains("starting attachment extraction") || normalized.Contains("preparing to extract") || normalized.Contains("fallback requirement extraction")) return (4, 14);
            if (normalized.Contains("preparing")) return (2, 8);
            if (normalized.Contains("downloading")) return (8, 20);
            if (normalized.Contains("workspace")) return (20, 34);
            if (normalized.Contains("upload")) return (34, 46);
            if (normalized.Contains("embedding")) return (46, 68);
            if (normalized.Contains("analyzing")) return (68, 82);
            if (normalized.Contains("extract")) return (82, 90);
            if (normalized.Contains("checking jama") || normalized.Contains("previously saved") || normalized.Contains("duplicate")) return (90, 94);
            if (normalized.Contains("save progress") || normalized.Contains("saving") || normalized.Contains("retry save") || normalized.Contains("retrying")) return (94, 99);
            if (normalized.Contains("complete") || normalized.Contains("success")) return (100, 100);
            return (4, 12);
        }

        private void ResetAttachmentLog(string header)
        {
            _attachmentLogLines.Clear();
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _attachmentLogLines.Add($"[{timestamp}] {header}");
            _attachmentLogLines.Add(new string('-', 72));
            AttachmentScraperOutputText = string.Join(Environment.NewLine, _attachmentLogLines);
        }

        private void AppendAttachmentLog(string message, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            // Avoid repeating noisy progress lines unless explicitly forced.
            if (!force && _attachmentLogLines.Count > 0)
            {
                var last = _attachmentLogLines[^1];
                if (last.EndsWith(message, StringComparison.Ordinal))
                {
                    return;
                }
            }

            foreach (var rawLine in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                _attachmentLogLines.Add($"[{timestamp}] {rawLine.Trim()}");
            }

            if (_attachmentLogLines.Count > 500)
            {
                var keep = _attachmentLogLines.Skip(_attachmentLogLines.Count - 500).ToList();
                _attachmentLogLines.Clear();
                _attachmentLogLines.AddRange(keep);
            }

            AttachmentScraperOutputText = string.Join(Environment.NewLine, _attachmentLogLines);
        }

        [RelayCommand(CanExecute = nameof(CanStage))]
        private void Stage()
        {
            if (CurrentRequirement == null) return;
            var key = GetKey(CurrentRequirement);
            _lifecycleStates[key] = RequirementLifecycleStage.StagedForCommit;
            RefreshLifecycleCounts();
            NotifyLifecycleStateUiChanged();
        }

        private bool CanStage() =>
            CurrentRequirement != null &&
            GetCurrentStage(CurrentRequirement) == RequirementLifecycleStage.Edit;

        [RelayCommand(CanExecute = nameof(CanToggleStage))]
        private void ToggleStage()
        {
            var stage = GetActiveStage();

            if (stage == RequirementLifecycleStage.Edit)
            {
                SetActiveStage(RequirementLifecycleStage.StagedForCommit);
            }
            else if (stage == RequirementLifecycleStage.StagedForCommit)
            {
                SetActiveStage(RequirementLifecycleStage.Edit);
            }
            else
            {
                return;
            }

            RefreshLifecycleCounts();
            NotifyLifecycleStateUiChanged();
        }

        private bool CanToggleStage() =>
            GetActiveObjectExists() &&
            GetActiveStage() != RequirementLifecycleStage.Committed;

        [RelayCommand(CanExecute = nameof(CanDeleteCurrentRequirement))]
        private void DeleteCurrentRequirement()
        {
            if (!GetActiveObjectExists())
            {
                return;
            }

            var isTestCase = IsTestCaseTabActive;
            var objectTypeLabel = isTestCase ? "test case" : "requirement";
            var objectLabel = isTestCase
                ? (!string.IsNullOrWhiteSpace(SelectedTestCase?.Name) ? SelectedTestCase?.Name : SelectedTestCase?.Id ?? "(unnamed test case)")
                : (!string.IsNullOrWhiteSpace(CurrentRequirement?.Name) ? CurrentRequirement?.Name : CurrentRequirement?.Item ?? "(unnamed requirement)");

            var confirm = MessageBox.Show(
                $"Delete this {objectTypeLabel}?\n\n{objectLabel}\n\nThis cannot be undone.",
                $"Delete {objectTypeLabel[..1].ToUpper()}{objectTypeLabel[1..]}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (isTestCase)
            {
                if (CurrentRequirement == null || SelectedTestCase == null)
                {
                    return;
                }

                var deleted = SelectedTestCase;
                _lifecycleStates.Remove(GetKey(deleted));
                CurrentRequirement.GeneratedTestCases.Remove(deleted);
                SelectedTestCase = CurrentRequirement.GeneratedTestCases.FirstOrDefault();
                _mediator.UpdateRequirement(CurrentRequirement, new[] { nameof(Requirement.GeneratedTestCases) });
            }
            else
            {
                if (CurrentRequirement == null)
                {
                    return;
                }

                var requirementToDelete = CurrentRequirement;
                _lifecycleStates.Remove(GetKey(requirementToDelete));
                _mediator.RemoveRequirement(requirementToDelete);

                // Keep Workshop selection in sync because mediator does not publish RequirementSelected on remove.
                ApplyCurrentRequirement(_mediator.CurrentRequirement);
            }

            RefreshLifecycleCounts();
            NotifyLifecycleStateUiChanged();
        }

        private bool CanDeleteCurrentRequirement() => CurrentRequirement != null;

        [RelayCommand(CanExecute = nameof(CanUnstage))]
        private void Unstage()
        {
            if (CurrentRequirement == null) return;
            var key = GetKey(CurrentRequirement);
            _lifecycleStates[key] = RequirementLifecycleStage.Edit;
            RefreshLifecycleCounts();
            NotifyLifecycleStateUiChanged();
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
                    NotifyLifecycleStateUiChanged();
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

        private static string GetKey(TestCase testCase) =>
            !string.IsNullOrWhiteSpace(testCase.Id)
                ? $"TC:{testCase.Id}"
                : (!string.IsNullOrWhiteSpace(testCase.Name)
                    ? $"TC:{testCase.Name}"
                    : $"TC:{testCase.GetHashCode()}");

        private RequirementLifecycleStage GetCurrentStage(Requirement? req)
        {
            if (req == null) return RequirementLifecycleStage.Edit;
            var key = GetKey(req);
            return _lifecycleStates.TryGetValue(key, out var stage) ? stage : RequirementLifecycleStage.Edit;
        }

        private RequirementLifecycleStage GetCurrentStage(TestCase? testCase)
        {
            if (testCase == null) return RequirementLifecycleStage.Edit;
            var key = GetKey(testCase);
            return _lifecycleStates.TryGetValue(key, out var stage) ? stage : RequirementLifecycleStage.Edit;
        }

        private RequirementLifecycleStage GetActiveStage() =>
            IsTestCaseTabActive ? GetCurrentStage(SelectedTestCase) : GetCurrentStage(CurrentRequirement);

        private bool GetActiveObjectExists() =>
            IsTestCaseTabActive ? SelectedTestCase != null : CurrentRequirement != null;

        private void SetActiveStage(RequirementLifecycleStage stage)
        {
            if (IsTestCaseTabActive)
            {
                if (SelectedTestCase == null) return;
                _lifecycleStates[GetKey(SelectedTestCase)] = stage;
                if (CurrentRequirement != null)
                {
                    _mediator.UpdateRequirement(CurrentRequirement, new[] { nameof(Requirement.GeneratedTestCases) });
                }
            }
            else
            {
                if (CurrentRequirement == null) return;
                _lifecycleStates[GetKey(CurrentRequirement)] = stage;
            }
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
            OnPropertyChanged(nameof(StagedTestCaseCount));
            OnPropertyChanged(nameof(StagedGlobalSummaryText));
            ((RelayCommand)StageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnstageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ToggleStageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)DeleteCurrentRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CommitStagedCommand).NotifyCanExecuteChanged();
        }

        private void NotifyNavigationCanExecute()
        {
            ((RelayCommand)PreviousRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextRequirementCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LlmAnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)OpenRequirementEditorCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)UnstageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ToggleStageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)DeleteCurrentRequirementCommand).NotifyCanExecuteChanged();
        }

        private void NotifyLifecycleStateUiChanged()
        {
            OnPropertyChanged(nameof(CurrentObjectLabel));
            OnPropertyChanged(nameof(CurrentRequirementStage));
            OnPropertyChanged(nameof(CurrentTestCaseStage));
            OnPropertyChanged(nameof(StageToggleButtonText));
            OnPropertyChanged(nameof(StageToggleDescription));
            OnPropertyChanged(nameof(StageToggleToolTip));
            ((RelayCommand)ToggleStageCommand).NotifyCanExecuteChanged();
            ((RelayCommand)DeleteCurrentRequirementCommand).NotifyCanExecuteChanged();
        }

        partial void OnActiveObjectTabIndexChanged(int value) => NotifyLifecycleStateUiChanged();

        partial void OnSelectedTestCaseChanged(TestCase? value) => NotifyLifecycleStateUiChanged();

        private static ObservableCollection<RequirementEditorTableDraft> BuildTableDrafts(List<RequirementTable>? sourceTables)
        {
            var drafts = new ObservableCollection<RequirementEditorTableDraft>();
            if (sourceTables == null)
            {
                return drafts;
            }

            foreach (var table in sourceTables)
            {
                drafts.Add(new RequirementEditorTableDraft
                {
                    Title = table.EditableTitle ?? string.Empty,
                    TableText = SerializeTableRows(table.Table)
                });
            }

            return drafts;
        }

        private static List<RequirementTable> BuildTablesFromDrafts(IEnumerable<RequirementEditorTableDraft> drafts)
        {
            var tables = new List<RequirementTable>();

            foreach (var draft in drafts ?? Enumerable.Empty<RequirementEditorTableDraft>())
            {
                var table = new RequirementTable
                {
                    EditableTitle = draft.Title ?? string.Empty,
                    Table = ParseTableRows(draft.TableText)
                };
                table.AnalyzeConfidence();
                tables.Add(table);
            }

            return tables;
        }

        private static string SerializeTableRows(List<List<string>>? rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine,
                rows.Select(r => string.Join("\t", (r ?? new List<string>()).Select(c => c ?? string.Empty))));
        }

        private static List<List<string>> ParseTableRows(string? text)
        {
            var result = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                if (raw == null)
                {
                    continue;
                }

                var cells = raw.Split('\t').Select(c => c.TrimEnd('\r')).ToList();
                if (cells.Count == 1 && string.IsNullOrWhiteSpace(cells[0]))
                {
                    continue;
                }

                result.Add(cells);
            }

            return result;
        }

        private void NotifyAttachmentScraperCanExecute()
        {
            SearchAttachmentsCommand.NotifyCanExecuteChanged();
            ExtractLocalDocumentCommand.NotifyCanExecuteChanged();
            ScrapeSelectedAttachmentCommand.NotifyCanExecuteChanged();
            ImportScrapedRequirementsCommand.NotifyCanExecuteChanged();
            CancelAttachmentScraperCommand.NotifyCanExecuteChanged();
        }

        private void OnScrapedCandidatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScrapedRequirementCandidate.IsSelected))
            {
                OnPropertyChanged(nameof(SelectedScrapedRequirementCount));
                OnPropertyChanged(nameof(HasSelectedScrapedRequirements));
                OnPropertyChanged(nameof(AttachmentScraperSummary));
                NotifyAttachmentScraperCanExecute();
            }
        }

        partial void OnIsAnalyzingChanged(bool value) =>
            ((AsyncRelayCommand)LlmAnalyzeRequirementCommand).NotifyCanExecuteChanged();

        partial void OnIsAttachmentScanningChanged(bool value) => NotifyAttachmentScraperCanExecute();

        partial void OnIsAttachmentScrapingChanged(bool value) => NotifyAttachmentScraperCanExecute();

        partial void OnSelectedScraperAttachmentChanged(JamaAttachment? value) => NotifyAttachmentScraperCanExecute();

        partial void OnScrapedRequirementsChanged(ObservableCollection<ScrapedRequirementCandidate> value)
        {
            OnPropertyChanged(nameof(HasScrapedRequirements));
            OnPropertyChanged(nameof(SelectedScrapedRequirementCount));
            OnPropertyChanged(nameof(HasSelectedScrapedRequirements));
            OnPropertyChanged(nameof(AttachmentScraperSummary));
            NotifyAttachmentScraperCanExecute();
        }
    }
}
