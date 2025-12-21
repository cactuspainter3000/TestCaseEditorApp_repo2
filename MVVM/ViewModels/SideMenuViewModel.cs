using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for the side navigation menu.
    /// Manages which menu section is currently selected and available menu items.
    /// </summary>
    public partial class SideMenuViewModel : ObservableObject
    {
        private readonly IWorkspaceManagementMediator? _workspaceManagementMediator;

        [ObservableProperty]
        private string? selectedSection;

        [ObservableProperty]
        private ObservableCollection<MenuItemViewModel> menuItems = new();

        // Project Management Commands
        public ICommand NewProjectCommand { get; private set; } = null!;
        public ICommand OpenProjectCommand { get; private set; } = null!;
        public ICommand SaveProjectCommand { get; private set; } = null!;
        public ICommand QuickImportCommand { get; private set; } = null!;

        // Availability properties
        [ObservableProperty]
        private bool isAnythingLLMAvailable = true;

        public SideMenuViewModel(IWorkspaceManagementMediator? workspaceManagementMediator = null)
        {
            _workspaceManagementMediator = workspaceManagementMediator;
            InitializeCommands();
            InitializeMenuItems();
        }

        private void InitializeCommands()
        {
            NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync);
            OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
            SaveProjectCommand = new RelayCommand(() => { /* TODO: Implement save */ });
            QuickImportCommand = new RelayCommand(() => { /* TODO: Implement quick import */ });
        }

        private async Task CreateNewProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.CreateNewProject called! ***");
            if (_workspaceManagementMediator != null)
            {
                await _workspaceManagementMediator.CreateNewProjectAsync();
            }
            else
            {
                Console.WriteLine("*** WorkspaceManagementMediator is null in SideMenuViewModel! ***");
            }
        }

        private async Task OpenProjectAsync()
        {
            Console.WriteLine("*** SideMenuViewModel.OpenProject called! ***");
            if (_workspaceManagementMediator != null)
            {
                await _workspaceManagementMediator.OpenProjectAsync();
            }
            else
            {
                Console.WriteLine("*** WorkspaceManagementMediator is null in SideMenuViewModel! ***");
            }
        }

        private void InitializeMenuItems()
        {
            MenuItems.Add(new MenuItemViewModel { Id = "Project", Title = "Project", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "Requirements", Title = "Requirements", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "TestCases", Title = "Test Cases", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "TestFlow", Title = "Test Flow", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "Import", Title = "Import", Badge = "" });
            MenuItems.Add(new MenuItemViewModel { Id = "NewProject", Title = "New Project", Badge = "" });
        }

        /// <summary>
        /// Updates badge for a specific menu item
        /// </summary>
        public void UpdateMenuItemBadge(string menuId, string badge)
        {
            var item = MenuItems.FirstOrDefault(m => m.Id == menuId);
            if (item != null)
            {
                item.Badge = badge;
            }
        }

        partial void OnSelectedSectionChanged(string? value)
        {
            // Notify other view models when section changes
            SectionChanged?.Invoke(value);
        }

        public event System.Action<string?>? SectionChanged;
    }

    public partial class MenuItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string badge = string.Empty;
    }
}