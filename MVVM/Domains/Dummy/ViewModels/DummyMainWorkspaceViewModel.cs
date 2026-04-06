using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Dummy.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels
{
    /// <summary>
    /// Dummy MainWorkspace ViewModel - Reference implementation following AI Guide patterns
    /// Use this as a template for any domain's main content area
    /// </summary>
    public partial class DummyMainWorkspaceViewModel : BaseDomainViewModel
    {
        private new readonly IDummyMediator _mediator;
        
        [ObservableProperty]
        private string sectionName = "Dummy Domain";
        
        [ObservableProperty]
        private string displayText = "🎯 Main Workspace - Working Perfectly!";
        
        [ObservableProperty]
        private string statusMessage = "This is the main content area. All workspace coordination is functioning.";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public DummyMainWorkspaceViewModel(
            IDummyMediator mediator,
            ILogger<DummyMainWorkspaceViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            
            // Subscribe to domain events following AI Guide patterns
            _mediator.Subscribe<Dummy.Events.DummyEvents.DummyDataUpdated>(OnDataUpdated);
        }
        
        private void OnDataUpdated(Dummy.Events.DummyEvents.DummyDataUpdated eventData)
        {
            if (eventData.PropertyName == "Main")
            {
                StatusMessage = eventData.NewValue?.ToString() ?? "Updated";
                LastUpdated = eventData.Timestamp;
            }
        }
        
        partial void OnSectionNameChanged(string value)
        {
            DisplayText = $"🎯 {value} Main Workspace - Working Perfectly!";
            StatusMessage = $"This is the main content area for {value}. All workspace coordination is functioning.";
            LastUpdated = DateTime.Now;
            
            _mediator.ChangeWorkspace("Main", StatusMessage);
        }
        
        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            // Dummy implementation for testing
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            // Dummy implementation for testing
            SectionName = "Dummy Domain"; // Reset to default
        }
        
        protected override async Task RefreshAsync()
        {
            // Dummy implementation for testing
            LastUpdated = DateTime.Now;
            await Task.Delay(50);
        }
        
        protected override bool CanSave()
        {
            return !IsBusy; // Can save when not busy
        }
        
        protected override bool CanCancel()
        {
            return true; // Can always cancel in dummy
        }
        
        protected override bool CanRefresh()
        {
            return !IsBusy; // Can refresh when not busy
        }
    }
}