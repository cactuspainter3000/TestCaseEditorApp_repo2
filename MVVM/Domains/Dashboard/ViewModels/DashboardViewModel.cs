using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

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
        private readonly INavigationMediator _navigationMediator;
        private readonly ILogger<DashboardViewModel> _logger;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string statusMessage = "Ready";

        public DashboardViewModel(INewProjectMediator newProjectMediator, INavigationMediator navigationMediator, ILogger<DashboardViewModel> logger)
        {
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            StatusMessage = "Loading workshop list...";

            try
            {
                // OpenProject screen already provides a selectable list of recent/available workshops.
                _navigationMediator.NavigateToSection("openproject");
                _logger.LogInformation("[DashboardViewModel] Navigated to open project workflow list");
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
    }
}
