using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using Microsoft.Extensions.DependencyInjection;

namespace TestCaseEditorApp.MVVM.Domains.Title.ViewModels
{
    /// <summary>
    /// Title bar ViewModel - manages project title display and global actions (save, undo)
    /// UI Infrastructure domain (like SideMenu) - coordinates across business domains
    /// </summary>
    public partial class TitleViewModel : ObservableObject
    {
        private readonly INewProjectMediator _newProjectMediator;
        private readonly IOpenProjectMediator _openProjectMediator;
        private readonly ILogger<TitleViewModel> _logger;

        [ObservableProperty]
        private string projectTitle = "Systems ATE APP";

        [ObservableProperty]
        private bool hasUnsavedChanges = false;

        [ObservableProperty]
        private bool hasProject = false;

        public ICommand SaveProjectCommand { get; }
        public ICommand UndoLastSaveCommand { get; }

        public TitleViewModel(
            INewProjectMediator newProjectMediator,
            IOpenProjectMediator openProjectMediator,
            ILogger<TitleViewModel> logger)
        {
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _openProjectMediator = openProjectMediator ?? throw new ArgumentNullException(nameof(openProjectMediator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, CanSaveProject);
            UndoLastSaveCommand = new RelayCommand(UndoLastSave, CanUndoLastSave);

            SubscribeToEvents();
            
            // Listen for property changes to update command canExecute
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(HasProject) || e.PropertyName == nameof(HasUnsavedChanges))
                {
                    ((AsyncRelayCommand)SaveProjectCommand).NotifyCanExecuteChanged();
                }
            };
        }

        private void SubscribeToEvents()
        {
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectCreated>(OnProjectCreated);
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectSaved>(OnProjectSaved);
            _newProjectMediator.Subscribe<NewProjectEvents.WorkspaceModified>(OnWorkspaceModified);
            _newProjectMediator.Subscribe<NewProjectEvents.ProjectClosed>(OnProjectClosed);
            
            _openProjectMediator.Subscribe<OpenProjectEvents.ProjectOpened>(OnProjectOpened);
        }

        private void OnProjectCreated(NewProjectEvents.ProjectCreated evt)
        {
            ProjectTitle = $"Test Case Generator - {evt.WorkspaceName}";
            HasProject = true;
            HasUnsavedChanges = false;
            _logger.LogInformation("[TitleVM] Project created: {Name}", evt.WorkspaceName);
        }

        private void OnProjectOpened(OpenProjectEvents.ProjectOpened evt)
        {
            ProjectTitle = $"Test Case Generator - {evt.WorkspaceName}";
            HasProject = true;
            HasUnsavedChanges = false;
            _logger.LogInformation("[TitleVM] Project opened: {Name}", evt.WorkspaceName);
        }

        private void OnProjectSaved(NewProjectEvents.ProjectSaved evt)
        {
            HasUnsavedChanges = false;
            _logger.LogInformation("[TitleVM] Project saved");
        }

        private void OnWorkspaceModified(NewProjectEvents.WorkspaceModified evt)
        {
            HasUnsavedChanges = true;
            _logger.LogInformation("💾 [TitleVM] Workspace modified: {Reason} - HasUnsavedChanges set to TRUE", evt.Reason);
        }

        private void OnProjectClosed(NewProjectEvents.ProjectClosed evt)
        {
            ProjectTitle = "Systems ATE APP";
            HasProject = false;
            HasUnsavedChanges = false;
            _logger.LogInformation("[TitleVM] Project closed");
        }

        private async Task SaveProjectAsync()
        {
            try
            {
                _logger.LogInformation("[TitleVM] Saving project");
                await _newProjectMediator.SaveProjectAsync();
                _logger.LogInformation("[TitleVM] Project saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TitleVM] Error saving project");
            }
        }

        private bool CanSaveProject() => HasProject;

        private void UndoLastSave()
        {
            _logger.LogInformation("[TitleVM] Undo last save - not yet implemented");
            // TODO: Implement undo functionality
        }

        private bool CanUndoLastSave() => false; // TODO: Implement when undo is ready
    }
}
