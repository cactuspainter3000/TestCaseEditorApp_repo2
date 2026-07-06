using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// StartUp MainWorkspace ViewModel - Following AI Guide patterns
    /// </summary>
    public partial class StartUp_MainViewModel : BaseDomainViewModel
    {
        private new readonly IStartupMediator _mediator;
        private readonly INavigationMediator _navigationMediator;
        private readonly RecentFilesService _recentFilesService;
        private readonly IWorkspaceContext _workspaceContext;
        private readonly IJamaConnectService _jamaConnectService;
        private readonly AnythingLLMService _anythingLlmService;
        private readonly INewProjectMediator _newProjectMediator;
        private readonly IOpenProjectMediator _openProjectMediator;
        private readonly IRequirementsMediator _requirementsMediator;
        private readonly IWorkspaceDiagnosticsService _workspaceDiagnosticsService;
        
        [ObservableProperty]
        private string title = "Systems ATE APP";
        
        [ObservableProperty]
        private string description = "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";

        [ObservableProperty]
        private int openWorkshopsCount;

        [ObservableProperty]
        private int requirementsCount;

        [ObservableProperty]
        private int traceGapsCount;

        [ObservableProperty]
        private string jamaConnectionText = "Jama is not configured";

        [ObservableProperty]
        private string promptQualityText = "No quality signals yet";

        [ObservableProperty]
        private string systemHealthText = "Checking services...";

        [ObservableProperty]
        private bool autoExportForChatGpt;

        public ObservableCollection<StartupRecentWorkshopCard> RecentWorkshops { get; } = new();

        public string EmptyRecentWorkshopsMessage => HasRecentWorkshops ? string.Empty : "No recent workshops yet. Create one to get started.";

        public bool HasRecentWorkshops => RecentWorkshops.Count > 0;

        public StartUp_MainViewModel(
            IStartupMediator mediator,
            INavigationMediator navigationMediator,
            RecentFilesService recentFilesService,
            IWorkspaceContext workspaceContext,
            IJamaConnectService jamaConnectService,
            AnythingLLMService anythingLlmService,
            INewProjectMediator newProjectMediator,
            IOpenProjectMediator openProjectMediator,
            IRequirementsMediator requirementsMediator,
            IWorkspaceDiagnosticsService workspaceDiagnosticsService,
            ILogger<StartUp_MainViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _recentFilesService = recentFilesService ?? throw new ArgumentNullException(nameof(recentFilesService));
            _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _anythingLlmService = anythingLlmService ?? throw new ArgumentNullException(nameof(anythingLlmService));
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _requirementsMediator = requirementsMediator ?? throw new ArgumentNullException(nameof(requirementsMediator));
            _workspaceDiagnosticsService = workspaceDiagnosticsService ?? throw new ArgumentNullException(nameof(workspaceDiagnosticsService));

            _workspaceContext.WorkspaceChanged += OnWorkspaceChanged;

            RefreshDashboardData();
        }

        [RelayCommand]
        private void CreateWorkshop()
        {
            _navigationMediator.NavigateToSection("NewProject");
        }

        [RelayCommand]
        private void ImportFromJama()
        {
            _navigationMediator.NavigateToSection("NewProject", "JamaImport");
        }

        [RelayCommand]
        private void RunGapAnalysis()
        {
            _navigationMediator.NavigateToSection("requirements");
        }

        [RelayCommand]
        private void GenerateTestCases()
        {
            _navigationMediator.NavigateToSection("LLMTestCaseGenerator");
        }

        [RelayCommand]
        private void OpenWorkshopTools()
        {
            _navigationMediator.NavigateToSection("Project");
        }

        [RelayCommand]
        private void OpenRequirementsSection()
        {
            _navigationMediator.NavigateToSection("requirements");
        }

        [RelayCommand]
        private void OpenRequirementsSearchAttachments()
        {
            _navigationMediator.NavigateToSection("requirements");
            _requirementsMediator.NavigateToRequirementsSearchAttachments();
        }

        [RelayCommand(CanExecute = nameof(CanAccessWorkspaceActions))]
        private async Task SaveWorkshopAsync()
        {
            await _newProjectMediator.SaveProjectAsync();
            RefreshDashboardData();
        }

        [RelayCommand(CanExecute = nameof(CanAccessWorkspaceActions))]
        private async Task CloseWorkshopAsync()
        {
            await _newProjectMediator.CloseProjectAsync();
            RefreshDashboardData();
        }

        [RelayCommand]
        private void OpenDummyDomain()
        {
            _navigationMediator.NavigateToSection("Dummy");
        }

        [RelayCommand(CanExecute = nameof(CanAccessWorkspaceActions))]
        private async Task ImportAdditionalRequirementsAsync()
        {
            await _newProjectMediator.ImportAdditionalRequirementsAsync();
            RefreshDashboardData();
        }

        [RelayCommand]
        private void OpenLLMLearning()
        {
            _navigationMediator.NavigateToSection("llm learning");
        }

        [RelayCommand]
        private void OpenLLMTestCaseGenerator()
        {
            _navigationMediator.NavigateToSection("LLMTestCaseGenerator");
        }

        [RelayCommand]
        private void OpenTestCaseCreation()
        {
            _navigationMediator.NavigateToSection("TestCaseCreation");
        }

        [RelayCommand]
        private void GenerateAnalysisCommand()
        {
            _navigationMediator.NavigateToSection("requirements");
        }

        [RelayCommand]
        private void ExportForChatGpt()
        {
            _navigationMediator.NavigateToSection("LLMTestCaseGenerator");
        }

        [RelayCommand]
        private void ToggleAutoExportForChatGpt()
        {
            AutoExportForChatGpt = !AutoExportForChatGpt;
        }

        [RelayCommand]
        private void GenerateTestCaseCommand()
        {
            _navigationMediator.NavigateToSection("LLMTestCaseGenerator");
        }

        [RelayCommand]
        private void ImportToJamaConnect()
        {
            _navigationMediator.NavigateToSection("TestCaseCreation");
        }

        [RelayCommand]
        private async Task ExportAnalysisLogsAsync()
        {
            await _workspaceDiagnosticsService.ExportAnalysisLogsAsync();
        }

        [RelayCommand]
        private async Task ProbeJamaLookupFieldsAsync()
        {
            await _workspaceDiagnosticsService.ProbeJamaLookupFieldsAsync();
        }

        [RelayCommand]
        private async Task OpenRecentWorkshopAsync(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            await OpenWorkshopFileAsync(filePath);
        }

        [RelayCommand]
        private async Task BrowseWorkshopAsync()
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Open a Requirements Workshop",
                Filter = "Test Case Editor Session|*.tcex.json|JSON Files|*.json|All Files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (fileDialog.ShowDialog() != true)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(fileDialog.FileName))
            {
                await OpenWorkshopFileAsync(fileDialog.FileName);
            }
        }

        [RelayCommand]
        private async Task ViewAllWorkshopsAsync()
        {
            await BrowseWorkshopAsync();
        }

        private async Task OpenWorkshopFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var success = await _openProjectMediator.OpenProjectFileAsync(filePath);
            if (!success)
            {
                return;
            }

            _recentFilesService.AddRecentFile(filePath);
            RefreshDashboardData();
            _navigationMediator.NavigateToSection("requirements");
        }

        private void LoadRecentWorkshops()
        {
            RecentWorkshops.Clear();

            var recentFiles = _recentFilesService
                .GetRecentFiles()
                .Where(File.Exists)
                .Take(3)
                .ToList();

            foreach (var filePath in recentFiles)
            {
                RecentWorkshops.Add(BuildRecentWorkshopCard(filePath));
            }

            OnPropertyChanged(nameof(HasRecentWorkshops));
            OnPropertyChanged(nameof(EmptyRecentWorkshopsMessage));
        }

        private StartupRecentWorkshopCard BuildRecentWorkshopCard(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var workshopName = Path.GetFileNameWithoutExtension(filePath);
            if (workshopName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
            {
                workshopName = Path.GetFileNameWithoutExtension(workshopName);
            }

            var card = new StartupRecentWorkshopCard
            {
                FilePath = filePath,
                WorkshopName = workshopName,
                LastUpdatedText = FormatRelativeTime(fileInfo.LastWriteTime),
                SourceText = "Workshop File",
                RequirementCount = 0,
                TraceGapCount = 0,
                CoveragePercent = 0,
                StatusText = "Draft"
            };

            try
            {
                var jsonContent = File.ReadAllText(filePath);
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;

                if (root.TryGetProperty("JamaProjectId", out var jamaProjectIdElement)
                    && jamaProjectIdElement.ValueKind == JsonValueKind.Number
                    && jamaProjectIdElement.TryGetInt32(out var jamaProjectId)
                    && jamaProjectId > 0)
                {
                    card.SourceText = $"Jama Project {jamaProjectId}";
                }
                else if (root.TryGetProperty("JamaProject", out var jamaProjectElement)
                    && jamaProjectElement.ValueKind == JsonValueKind.String)
                {
                    var jamaProject = jamaProjectElement.GetString();
                    if (!string.IsNullOrWhiteSpace(jamaProject))
                    {
                        card.SourceText = $"Jama Project {jamaProject}";
                    }
                }

                if (root.TryGetProperty("Requirements", out var requirementsElement)
                    && requirementsElement.ValueKind == JsonValueKind.Array)
                {
                    var total = requirementsElement.GetArrayLength();
                    if (total > 0)
                    {
                        var analyzed = 0;
                        var traceGaps = 0;

                        foreach (var requirement in requirementsElement.EnumerateArray())
                        {
                            if (requirement.TryGetProperty("IsAnalyzed", out var analyzedElement)
                                && analyzedElement.ValueKind == JsonValueKind.True)
                            {
                                analyzed++;
                            }

                            if (IsTraceGap(requirement))
                            {
                                traceGaps++;
                            }
                        }

                        card.RequirementCount = total;
                        card.TraceGapCount = traceGaps;
                        card.CoveragePercent = (int)Math.Round((double)analyzed / total * 100);
                        card.StatusText = card.CoveragePercent >= 80 ? "In Review" : "Active";
                    }
                }
            }
            catch
            {
                // Keep fallback values if parsing fails.
            }

            return card;
        }

        private void RefreshDashboardData()
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                RefreshDashboardDataCore();
                return;
            }

            // Workspace change notifications may come from background threads.
            // Always marshal collection/property mutations to the UI Dispatcher.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                _ = dispatcher.InvokeAsync(RefreshDashboardDataCore);
                return;
            }

            // Fallback for unexpected contexts where no WPF Application is available.
            RefreshDashboardDataCore();
        }

        private void RefreshDashboardDataCore()
        {
            LoadRecentWorkshops();
            RefreshPulseMetrics();
            RefreshUtilityMetrics();
        }

        private void RefreshPulseMetrics()
        {
            var existingWorkshopFiles = _recentFilesService.GetRecentFiles().Count(File.Exists);
            OpenWorkshopsCount = existingWorkshopFiles;

            var currentWorkspace = _workspaceContext.CurrentWorkspace;
            if (currentWorkspace?.Requirements != null && currentWorkspace.Requirements.Count > 0)
            {
                RequirementsCount = currentWorkspace.Requirements.Count;
                TraceGapsCount = currentWorkspace.Requirements.Count(IsTraceGap);
                return;
            }

            RequirementsCount = RecentWorkshops.Sum(card => card.RequirementCount);
            TraceGapsCount = RecentWorkshops.Sum(card => card.TraceGapCount);
        }

        private void RefreshUtilityMetrics()
        {
            var workspace = _workspaceContext.CurrentWorkspace;
            if (_jamaConnectService.IsConfigured)
            {
                if (workspace?.JamaProjectId is int projectId && projectId > 0)
                {
                    JamaConnectionText = $"Connected to project {projectId}";
                }
                else if (!string.IsNullOrWhiteSpace(workspace?.JamaProjectName))
                {
                    JamaConnectionText = $"Connected to {workspace.JamaProjectName}";
                }
                else
                {
                    JamaConnectionText = "Jama configured and ready";
                }
            }
            else
            {
                JamaConnectionText = "Jama is not configured";
            }

            if (RecentWorkshops.Count > 0)
            {
                var strongCoverageCount = RecentWorkshops.Count(card => card.CoveragePercent >= 80);
                PromptQualityText = strongCoverageCount > 0
                    ? $"{strongCoverageCount} workshops above 80% coverage"
                    : "Coverage needs review across recent workshops";
            }
            else
            {
                PromptQualityText = "No quality signals yet";
            }

            var missingServices = new List<string>();
            if (!_jamaConnectService.IsConfigured)
            {
                missingServices.Add("Jama");
            }

            if (!_anythingLlmService.IsConfigured)
            {
                missingServices.Add("LLM");
            }

            SystemHealthText = missingServices.Count == 0
                ? "Core services available"
                : $"Missing: {string.Join(", ", missingServices)}";
        }

        private static bool IsTraceGap(Requirement requirement)
        {
            if (requirement == null)
            {
                return true;
            }

            var traceReference = requirement.TraceReference;
            return string.IsNullOrWhiteSpace(traceReference)
                && requirement.NumberOfUpstreamRelationships == 0
                && requirement.NumberOfDownstreamRelationships == 0;
        }

        private static bool IsTraceGap(JsonElement requirementElement)
        {
            var hasTraceReference = requirementElement.TryGetProperty("TraceReference", out var traceReferenceElement)
                && traceReferenceElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(traceReferenceElement.GetString());

            var upstreamCount = requirementElement.TryGetProperty("NumberOfUpstreamRelationships", out var upstreamElement)
                && upstreamElement.ValueKind == JsonValueKind.Number
                && upstreamElement.TryGetInt32(out var upstream)
                    ? upstream
                    : 0;

            var downstreamCount = requirementElement.TryGetProperty("NumberOfDownstreamRelationships", out var downstreamElement)
                && downstreamElement.ValueKind == JsonValueKind.Number
                && downstreamElement.TryGetInt32(out var downstream)
                    ? downstream
                    : 0;

            return !hasTraceReference && upstreamCount == 0 && downstreamCount == 0;
        }

        private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e)
        {
            RefreshDashboardData();
            SaveWorkshopCommand.NotifyCanExecuteChanged();
            CloseWorkshopCommand.NotifyCanExecuteChanged();
            ImportAdditionalRequirementsCommand.NotifyCanExecuteChanged();
        }

        private static string FormatRelativeTime(DateTime when)
        {
            var delta = DateTime.Now - when;
            if (delta.TotalMinutes < 1)
            {
                return "just now";
            }

            if (delta.TotalHours < 1)
            {
                return $"{Math.Max(1, (int)delta.TotalMinutes)} min ago";
            }

            if (delta.TotalDays < 1)
            {
                return $"{(int)delta.TotalHours} hours ago";
            }

            if (delta.TotalDays < 7)
            {
                return $"{(int)delta.TotalDays} days ago";
            }

            return when.ToString("MMM d");
        }
        
        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            Title = "Systems ATE APP";
        }
        
        protected override async Task RefreshAsync()
        {
            await _workspaceContext.RefreshAsync();
            RefreshDashboardData();
        }
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;

        private bool CanAccessWorkspaceActions()
        {
            return _workspaceContext.HasWorkspace;
        }

        public override void Dispose()
        {
            _workspaceContext.WorkspaceChanged -= OnWorkspaceChanged;
            base.Dispose();
        }
    }

    public class StartupRecentWorkshopCard
    {
        public string FilePath { get; set; } = string.Empty;
        public string WorkshopName { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public string LastUpdatedText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public int RequirementCount { get; set; }
        public int TraceGapCount { get; set; }
        public int CoveragePercent { get; set; }
    }
}