using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels
{
    /// <summary>
    /// Dummy Header ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's header area
    /// </summary>
    public partial class Dummy_HeaderViewModel : BaseDomainViewModel
    {
        private new readonly IDummyMediator _mediator;
        
        [ObservableProperty]
        private string sectionName = "Dummy Domain";
        
        [ObservableProperty]
        private string title = "📋 Context Header";
        
        [ObservableProperty]
        private string subtitle = "Context-specific header workspace";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public Dummy_HeaderViewModel(
            IDummyMediator mediator,
            ILogger<Dummy_HeaderViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // Subscribe to domain events
            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyWorkspaceChanged>(OnWorkspaceChanged);
        }
        
        private void OnWorkspaceChanged(Dummy.Events.DummyEvents.DummyWorkspaceChanged eventData)
        {
            if (eventData.WorkspaceName == "Header")
            {
                Subtitle = eventData.NewContent;
                LastUpdated = DateTime.Now;
            }
        }
        
        partial void OnSectionNameChanged(string value)
        {
            Title = $"📋 {value} Header";
            Subtitle = $"Context-specific header for {value} workflow";
            LastUpdated = DateTime.Now;
            
            _mediator.ChangeWorkspace("Header", Subtitle);
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