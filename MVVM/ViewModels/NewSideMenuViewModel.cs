using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Models.DataDrivenMenu;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the new side menu using the mediator architecture.
    /// Handles navigation commands and communicates via mediators.
    /// </summary>
    public partial class NewSideMenuViewModel : ObservableObject
    {
        private readonly INavigationMediator _navigationMediator;
        private readonly INewProjectMediator _workspaceManagementMediator;

        [ObservableProperty]
        private MenuSection? testCaseGeneratorMenuSection;

        [ObservableProperty]
        private string? sapStatus = "Systems App v2.0";

        // Navigation Commands
        public ICommand ProjectNavigationCommand { get; private set; } = null!;
        public ICommand NewProjectCommand { get; private set; } = null!;
        public ICommand OpenProjectCommand { get; private set; } = null!;

        public NewSideMenuViewModel(INavigationMediator navigationMediator, INewProjectMediator workspaceManagementMediator)
        {
            _navigationMediator = navigationMediator ?? throw new ArgumentNullException(nameof(navigationMediator));
            _workspaceManagementMediator = workspaceManagementMediator ?? throw new ArgumentNullException(nameof(workspaceManagementMediator));

            InitializeCommands();
            InitializeMenu();
        }

        private void InitializeCommands()
        {
            ProjectNavigationCommand = new RelayCommand(NavigateToProject);
            NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync);
            OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        }

        private void InitializeMenu()
        {
            // Create the data-driven menu structure
            TestCaseGeneratorMenuSection = new MenuSection
            {
                Id = "testcase-generator",
                Text = "Test Case Generator",
                Icon = "🧪",
                Items = new ObservableCollection<MenuContentItem>
                {
                    new MenuAction
                    {
                        Id = "project",
                        Text = "Project",
                        Icon = "📁",
                        Command = ProjectNavigationCommand,
                        IsDropdown = true,
                        Children = new ObservableCollection<MenuContentItem>
                        {
                            new MenuAction { Id = "project.new", Text = "New Project", Icon = "🗂️", Command = NewProjectCommand },
                            new MenuAction { Id = "project.open", Text = "Open Project", Icon = "📂", Command = OpenProjectCommand }
                        }
                    }
                }
            };
        }

        private void NavigateToProject()
        {
// ("*** NewSideMenuViewModel.NavigateToProject called! ***");
            // TODO: Use ViewModelFactory for proper dependency injection
            // var projectViewModel = _viewModelFactory.CreateProjectViewModel();
            // For now, placeholder until factory is integrated  
            throw new NotImplementedException("ProjectViewModel creation needs proper ViewModelFactory integration");
            // _navigationMediator.SetMainContent(projectViewModel);
        }

        private async Task CreateNewProjectAsync()
        {
// ("*** NewSideMenuViewModel.CreateNewProject called! ***");
            await _workspaceManagementMediator.CreateNewProjectAsync();
        }

        private async Task OpenProjectAsync()
        {
// ("*** NewSideMenuViewModel.OpenProject called! ***");
            await _workspaceManagementMediator.OpenProjectAsync();
        }
    }
}
