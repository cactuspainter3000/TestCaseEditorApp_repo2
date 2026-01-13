using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels
{
    /// <summary>
    /// Dummy NavigationWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's navigation area
    /// </summary>
    public partial class Dummy_NavigationViewModel : BaseDomainViewModel
    {
        private new readonly IDummyMediator _mediator;
        
        [ObservableProperty]
        private string sectionName = "Dummy Domain";
        
        [ObservableProperty]
        private string navigationTitle = "🧭 Navigation";
        
        [ObservableProperty]
        private string currentStep = "Active Section";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        [ObservableProperty]
        private string sharedMessage = "Ready for inter-workspace communication...";
        
        public ICommand TestButtonCommand { get; }
        
        public Dummy_NavigationViewModel(
            IDummyMediator mediator,
            ILogger<Dummy_NavigationViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            TestButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => {
                _mediator.ChangeWorkspace("AllWorkspaces", "Navigation view's button was clicked!");
            });
            
            // Subscribe to domain events
            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyWorkspaceChanged>(OnWorkspaceChanged);
        }
        
        private void OnWorkspaceChanged(Dummy.Events.DummyEvents.DummyWorkspaceChanged eventData)
        {
            if (eventData.WorkspaceName == "Navigation")
            {
                CurrentStep = eventData.NewContent;
                LastUpdated = DateTime.Now;
            }
            else if (eventData.WorkspaceName == "AllWorkspaces")
            {
                SharedMessage = eventData.NewContent;
            }
        }
        
        partial void OnSectionNameChanged(string value)
        {
            NavigationTitle = $"🧭 {value} Navigation";
            CurrentStep = $"Active: {value} workflow";
            LastUpdated = DateTime.Now;
            
            _mediator.ChangeWorkspace("Navigation", CurrentStep);
        }
        
        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            SectionName = "Dummy Domain";
        }
        
        protected override async Task RefreshAsync()
        {
            LastUpdated = DateTime.Now;
            await Task.Delay(50);
        }
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;
    }
}