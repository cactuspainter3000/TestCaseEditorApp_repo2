using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Startup.Mediators;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// StartUp NavigationWorkspace ViewModel - Following AI Guide patterns
    /// </summary>
    public partial class StartUp_NavigationViewModel : BaseDomainViewModel
    {
        private new readonly IStartupMediator _mediator;
        
        [ObservableProperty]
        private string currentPath = "/ StartUp";
        
        [ObservableProperty]
        private string navigationStatus = "Ready";
        
        public StartUp_NavigationViewModel(
            IStartupMediator mediator,
            ILogger<StartUp_NavigationViewModel> logger)
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
            CurrentPath = "/ StartUp";
        }
        
        protected override async Task RefreshAsync()
        {
            NavigationStatus = "Refreshed";
            await Task.Delay(50);
        }
        
        protected override bool CanSave() => !IsBusy;
        protected override bool CanCancel() => true;
        protected override bool CanRefresh() => !IsBusy;
    }
}