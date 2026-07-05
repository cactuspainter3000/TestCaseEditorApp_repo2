using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using Microsoft.Extensions.Logging;
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
        
        [ObservableProperty]
        private string title = "Systems ATE APP";
        
        [ObservableProperty]
        private string description = "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";

        public ObservableCollection<StartupRecentWorkshopCard> RecentWorkshops { get; } = new();

        public string EmptyRecentWorkshopsMessage => HasRecentWorkshops ? string.Empty : "No recent workshops yet. Create one to get started.";

        public bool HasRecentWorkshops => RecentWorkshops.Count > 0;

        public StartUp_MainViewModel(
            IStartupMediator mediator,
            INavigationMediator navigationMediator,
            RecentFilesService recentFilesService,
            ILogger<StartUp_MainViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _recentFilesService = recentFilesService ?? throw new ArgumentNullException(nameof(recentFilesService));

            LoadRecentWorkshops();
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
        private void OpenRecentWorkshop(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            _navigationMediator.NavigateToSection("OpenProject", filePath);
        }

        [RelayCommand]
        private void ViewAllWorkshops()
        {
            _navigationMediator.NavigateToSection("OpenProject");
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
                        foreach (var requirement in requirementsElement.EnumerateArray())
                        {
                            if (requirement.TryGetProperty("IsAnalyzed", out var analyzedElement)
                                && analyzedElement.ValueKind == JsonValueKind.True)
                            {
                                analyzed++;
                            }
                        }

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
            await Task.Delay(50);
        }
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;
    }

    public class StartupRecentWorkshopCard
    {
        public string FilePath { get; set; } = string.Empty;
        public string WorkshopName { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public string LastUpdatedText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public int CoveragePercent { get; set; }
    }
}