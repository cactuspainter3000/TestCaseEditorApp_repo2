using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    public partial class TestCaseGeneratorSplashViewModel : BaseDomainViewModel, IDisposable
    {
        // Domain mediator (properly typed)
        private new readonly ITestCaseGenerationMediator _mediator;

        [ObservableProperty]
        private string _welcomeMessage = "Welcome to Test Case Generator";
        
        [ObservableProperty]
        private string _description = "Generate comprehensive test cases from your requirements with AI assistance.";
        
        public TestCaseGeneratorSplashViewModel(
            ITestCaseGenerationMediator mediator,
            ILogger<TestCaseGeneratorSplashViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _logger.LogDebug("TestCaseGeneratorSplashViewModel initialized");
        }

        #region Abstract Method Implementations

        protected override bool CanSave() => false; // Splash screen doesn't save
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => false; // Splash screen doesn't need refresh
        protected override async Task RefreshAsync() => await Task.CompletedTask;
        protected override bool CanCancel() => false; // Splash screen doesn't cancel
        protected override void Cancel() { /* No-op */ }

        #endregion
    }
}