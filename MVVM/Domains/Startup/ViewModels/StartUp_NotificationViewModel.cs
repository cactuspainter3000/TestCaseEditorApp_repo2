using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// StartUp NotificationWorkspace ViewModel - Following AI Guide patterns
    /// </summary>
    public partial class StartUp_NotificationViewModel : BaseDomainViewModel
    {
        private new readonly IStartupMediator _mediator;
        
        [ObservableProperty]
        private string notificationTitle = "🚀 StartUp Notifications";
        
        [ObservableProperty]
        private string statusMessage = "Application ready to start";
        
        [ObservableProperty]
        private DateTime lastUpdated = DateTime.Now;
        
        public StartUp_NotificationViewModel(
            IStartupMediator mediator,
            ILogger<StartUp_NotificationViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        }
        
        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            NotificationTitle = "🚀 StartUp Notifications";
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