using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using TestCaseEditorApp.MVVM.Domains.Dummy.Events;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels
{
    /// <summary>
    /// Dummy Title ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's title area
    /// </summary>
    public partial class Dummy_TitleViewModel : BaseDomainViewModel
    {
        private new readonly IDummyMediator _mediator;
        
        [ObservableProperty]
        private string sectionName = "Dummy Domain";
        
        [ObservableProperty]
        private string pageTitle = "✨ Generic Section";
        
        [ObservableProperty]
        private string breadcrumb = "Home > Section";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        [ObservableProperty]
        private string sharedMessage = "Ready for inter-workspace communication...";
        
        public ICommand TestButtonCommand { get; }
        
        public Dummy_TitleViewModel(
            IDummyMediator mediator,
            ILogger<Dummy_TitleViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            TestButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => {
                _mediator.ChangeWorkspace("AllWorkspaces", "Title view's button was clicked!");
            });
            
            // Subscribe to domain events
            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyWorkspaceChanged>(OnWorkspaceChanged);
        }
        
        private void OnWorkspaceChanged(Dummy.Events.DummyEvents.DummyWorkspaceChanged eventData)
        {
            if (eventData.WorkspaceName == "Title")
            {
                Breadcrumb = eventData.NewContent;
                LastUpdated = DateTime.Now;
            }
            else if (eventData.WorkspaceName == "AllWorkspaces")
            {
                SharedMessage = eventData.NewContent;
            }
        }
        
        partial void OnSectionNameChanged(string value)
        {
            PageTitle = $"✨ {value}";
            Breadcrumb = $"Home > {value}";
            LastUpdated = DateTime.Now;
            
            _mediator.ChangeWorkspace("Title", Breadcrumb);
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