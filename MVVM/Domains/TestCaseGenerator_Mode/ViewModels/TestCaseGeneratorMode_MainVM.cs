using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.ViewModels;
using System.Threading.Tasks;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.ViewModels
{
    /// <summary>
    /// Main ViewModel for TestCaseGenerator_Mode menu item.
    /// Simplified version focused on menu display logic.
    /// </summary>
    public partial class TestCaseGeneratorMode_MainVM : BaseDomainViewModel
    {
        private new readonly ITestCaseGenerationMediator _mediator;

        private new readonly ILogger<TestCaseGeneratorMode_MainVM> _logger;

        public TestCaseGeneratorMode_MainVM(
            ITestCaseGenerationMediator mediator,
            ILogger<TestCaseGeneratorMode_MainVM> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            
            _logger.LogDebug("[TestCaseGeneratorMode_MainVM] Initialized for menu display");
        }

        // Properties specific to the menu item display
        public string WelcomeMessage => "Test Case Generator";
        public string Description => "Generate comprehensive test cases using AI-powered analysis. Import requirements, analyze context, and automatically create detailed test scenarios to ensure thorough coverage of your application's functionality.";
        public string GetStartedText => "Get started by selecting an option from the side menu";

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override async Task SaveAsync()
        {
            await Task.Delay(100);
        }
        
        protected override void Cancel()
        {
            // No action needed for menu display
        }
        
        protected override async Task RefreshAsync()
        {
            await Task.Delay(50);
        }
        
        protected override bool CanSave() => false; // Menu display doesn't save
        protected override bool CanCancel() => false; // Menu display doesn't cancel
        protected override bool CanRefresh() => !IsBusy;
    }
}