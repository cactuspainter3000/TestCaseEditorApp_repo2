using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;

namespace TestCaseEditorApp.MVVM.Domains.Dashboard.ViewModels
{
    /// <summary>
    /// Dashboard ViewModel - the landing page for the application.
    /// Users can create a new workshop or open an existing one.
    /// Also provides theme selection.
    /// </summary>
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly INewProjectMediator _newProjectMediator;
        private readonly IOpenProjectMediator _openProjectMediator;
        private readonly RecentFilesService _recentFilesService;
        private readonly INavigationMediator _navigationMediator;
        private readonly ILogger<DashboardViewModel> _logger;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string statusMessage = "Ready";

        [ObservableProperty]
        private ObservableCollection<DashboardRecentProjectInfo> recentProjects = new();

        [ObservableProperty]
        private DashboardRecentProjectInfo? selectedRecentProject;

        public bool HasRecentProjects => RecentProjects.Count > 0;
        public bool HasSelectedRecentProject => SelectedRecentProject != null;

        public DashboardViewModel(
            INewProjectMediator newProjectMediator,
            IOpenProjectMediator openProjectMediator,
            RecentFilesService recentFilesService,
            INavigationMediator navigationMediator,
            ILogger<DashboardViewModel> logger)
        {
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _recentFilesService = recentFilesService ?? throw new ArgumentNullException(nameof(recentFilesService));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            RefreshRecentProjects();
        }

        [RelayCommand]
        public async Task CreateWorkshop()
        {
            _logger.LogInformation("[DashboardViewModel] Create workshop requested");
            IsLoading = true;
            StatusMessage = "Creating new workshop...";

            try
            {
                await _newProjectMediator.CreateNewProjectAsync();
                _logger.LogInformation("[DashboardViewModel] New project workflow started");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DashboardViewModel] Error creating workshop");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public void OpenWorkshop()
        {
            _logger.LogInformation("[DashboardViewModel] Open workshop requested");
            IsLoading = true;
            StatusMessage = "Select a workshop file...";

            try
            {
                _ = _openProjectMediator.OpenProjectAsync();
                _logger.LogInformation("[DashboardViewModel] Open project browse flow started");
                RefreshRecentProjects();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DashboardViewModel] Error opening workshop");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task OpenSelectedProject()
        {
            if (SelectedRecentProject == null)
            {
                StatusMessage = "Select a workshop from the list first.";
                return;
            }

            await OpenRecentProject(SelectedRecentProject.FilePath);
        }

        [RelayCommand]
        public async Task OpenRecentProject(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                StatusMessage = "Selected workshop file no longer exists. Refreshing list.";
                _recentFilesService.RemoveRecentFile(filePath);
                RefreshRecentProjects();
                return;
            }

            IsLoading = true;
            StatusMessage = "Opening selected workshop...";

            try
            {
                var success = await _openProjectMediator.OpenProjectFileAsync(filePath);
                if (success)
                {
                    _recentFilesService.AddRecentFile(filePath);
                    RefreshRecentProjects();
                    StatusMessage = "Workshop opened successfully.";
                    _navigationMediator.NavigateToSection("requirements");
                }
                else
                {
                    StatusMessage = "Failed to open selected workshop.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DashboardViewModel] Error opening recent project: {FilePath}", filePath);
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void RefreshRecentProjects()
        {
            RecentProjects.Clear();

            foreach (var filePath in _recentFilesService.GetRecentFiles())
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                var projectName = Path.GetFileNameWithoutExtension(filePath);
                if (projectName.EndsWith(".tcex", StringComparison.OrdinalIgnoreCase))
                {
                    projectName = Path.GetFileNameWithoutExtension(projectName);
                }

                var fileInfo = new FileInfo(filePath);

                RecentProjects.Add(new DashboardRecentProjectInfo
                {
                    FilePath = filePath,
                    ProjectName = projectName,
                    LastModified = fileInfo.LastWriteTime
                });
            }

            OnPropertyChanged(nameof(HasRecentProjects));
        }

        partial void OnSelectedRecentProjectChanged(DashboardRecentProjectInfo? value)
        {
            OnPropertyChanged(nameof(HasSelectedRecentProject));
        }
    }

    public sealed class DashboardRecentProjectInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string LastModifiedText => LastModified.ToString("yyyy-MM-dd HH:mm");
    }
}
