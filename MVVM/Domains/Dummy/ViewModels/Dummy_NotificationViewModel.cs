using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels
{
    /// <summary>
    /// Dummy NotificationWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's notification area
    /// </summary>
    public partial class Dummy_NotificationViewModel : BaseDomainViewModel
    {
        private new readonly IDummyMediator _mediator;
        
        [ObservableProperty]
        private string sectionName = "Dummy Domain";
        
        [ObservableProperty]
        private string notificationTitle = "🔔 Notifications";
        
        [ObservableProperty]
        private string statusMessage = "All systems operational";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public Dummy_NotificationViewModel(
            IDummyMediator mediator,
            ILogger<Dummy_NotificationViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // Subscribe to domain events
            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyStatusChanged>(OnStatusChanged);
        }
        
        private void OnStatusChanged(Dummy.Events.DummyEvents.DummyStatusChanged eventData)
        {
            StatusMessage = $"{eventData.Status}: {eventData.Message}";
            LastUpdated = DateTime.Now;
        }
        
        partial void OnSectionNameChanged(string value)
        {
            NotificationTitle = $"🔔 {value} Notifications";
            StatusMessage = $"All {value} systems operational";
            LastUpdated = DateTime.Now;
            
            _mediator.UpdateStatus("Ready", StatusMessage);
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