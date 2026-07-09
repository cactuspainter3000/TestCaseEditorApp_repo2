using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Services.Theme;
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
        private readonly ThemeService _themeService;
        private readonly ILogger<DashboardViewModel> _logger;

        [ObservableProperty]
        private ObservableCollection<ThemeConfig> availableThemes;

        [ObservableProperty]
        private ThemeConfig selectedTheme;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string statusMessage = "Ready";

        public DashboardViewModel(INewProjectMediator newProjectMediator, IOpenProjectMediator openProjectMediator, ThemeService themeService, ILogger<DashboardViewModel> logger)
        {
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Initialize available themes
            AvailableThemes = new ObservableCollection<ThemeConfig>(_themeService.GetAvailableThemes());
            SelectedTheme = _themeService.CurrentTheme;

            _logger.LogInformation("[DashboardViewModel] Initialized with {ThemeCount} available themes", AvailableThemes.Count);
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
        public async Task OpenWorkshop()
        {
            _logger.LogInformation("[DashboardViewModel] Open workshop requested");
            IsLoading = true;
            StatusMessage = "Opening workshop...";

            try
            {
                await _openProjectMediator.OpenProjectAsync();
                _logger.LogInformation("[DashboardViewModel] Open project workflow started");
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
        public void SelectTheme(string themeName)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                _logger.LogWarning("[DashboardViewModel] Theme name is null or empty");
                return;
            }

            try
            {
                _themeService.SetTheme(themeName);
                SelectedTheme = _themeService.CurrentTheme;
                StatusMessage = $"Theme changed to {themeName}";
                _logger.LogInformation("[DashboardViewModel] Theme changed to {ThemeName}", themeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DashboardViewModel] Error setting theme to {ThemeName}", themeName);
                StatusMessage = $"Error changing theme: {ex.Message}";
            }
        }
    }
}
