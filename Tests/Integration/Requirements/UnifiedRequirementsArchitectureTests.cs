using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Enums;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests.Integration.Requirements
{
    /// <summary>
    /// Integration tests for Unified Requirements architecture.
    /// Verifies that the new source-agnostic unified approach works correctly
    /// with proper DI container resolution and view mode navigation.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("Requirements")]
    public class UnifiedRequirementsArchitectureTests
    {
        private IServiceProvider? _serviceProvider;

        [SetUp]
        public void Setup()
        {
            // Create minimal DI container for testing unified architecture
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            // Add core services (mocks for testing)
            services.AddSingleton<IPersistenceService, MockPersistenceService>();
            services.AddSingleton<ITextEditingDialogService, MockTextEditingDialogService>();
            services.AddSingleton<IRequirementsMediator, MockRequirementsMediator>();
            
            // Add the UnifiedRequirementsMainViewModel and its dependencies
            services.AddSingleton<RequirementsSearchAttachmentsViewModel>(provider =>
            {
                var mediator = provider.GetRequiredService<IRequirementsMediator>();
                var workspaceContext = new MockWorkspaceContext();
                var logger = provider.GetRequiredService<ILogger<RequirementsSearchAttachmentsViewModel>>();
                return new RequirementsSearchAttachmentsViewModel(mediator, workspaceContext, logger);
            });
            
            services.AddSingleton<UnifiedRequirementsMainViewModel>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
        }

        [Test]
        public void UnifiedRequirementsMainViewModel_CanBeResolved_FromDIContainer()
        {
            // Act
            var viewModel = _serviceProvider!.GetService<UnifiedRequirementsMainViewModel>();

            // Assert
            Assert.That(viewModel, Is.Not.Null, "UnifiedRequirementsMainViewModel should resolve from DI container");
            Assert.That(viewModel.SelectedViewMode, Is.EqualTo(RequirementViewMode.Details), "Should default to Details view mode");
        }

        [Test]
        public void UnifiedRequirementsMainViewModel_SupportsAllViewModes()
        {
            // Arrange
            var viewModel = _serviceProvider!.GetRequiredService<UnifiedRequirementsMainViewModel>();

            // Act & Assert for each view mode
            foreach (RequirementViewMode mode in Enum.GetValues<RequirementViewMode>())
            {
                viewModel.SelectedViewMode = mode;
                Assert.That(viewModel.SelectedViewMode, Is.EqualTo(mode), $"View mode should update to {mode}");
            }
        }

        [Test]
        public void UnifiedRequirementsMainViewModel_HasRequiredCommands()
        {
            // Arrange
            var viewModel = _serviceProvider!.GetRequiredService<UnifiedRequirementsMainViewModel>();

            // Assert - verify all critical commands exist
            Assert.That(viewModel.SelectDetailsCommand, Is.Not.Null, "SelectDetailsCommand should exist");
            Assert.That(viewModel.SelectTablesCommand, Is.Not.Null, "SelectTablesCommand should exist");
            Assert.That(viewModel.SelectAnalysisCommand, Is.Not.Null, "SelectAnalysisCommand should exist");
            Assert.That(viewModel.SelectRequirementsScraperCommand, Is.Not.Null, "SelectRequirementsScraperCommand should exist");
            
            Assert.That(viewModel.QuickAnalyzeCommand, Is.Not.Null, "QuickAnalyzeCommand should exist");
            Assert.That(viewModel.GenerateTestsCommand, Is.Not.Null, "GenerateTestsCommand should exist");
            Assert.That(viewModel.ViewInTestGenCommand, Is.Not.Null, "ViewInTestGenCommand should exist");
        }

        [Test]
        public void UnifiedRequirementsMainViewModel_TabCommandsChangeViewMode()
        {
            // Arrange
            var viewModel = _serviceProvider!.GetRequiredService<UnifiedRequirementsMainViewModel>();

            // Act & Assert
            viewModel.SelectDetailsCommand.Execute(null);
            Assert.That(viewModel.SelectedViewMode, Is.EqualTo(RequirementViewMode.Details));

            viewModel.SelectAnalysisCommand.Execute(null);
            Assert.That(viewModel.SelectedViewMode, Is.EqualTo(RequirementViewMode.Analysis));

            viewModel.SelectRequirementsScraperCommand.Execute(null);
            Assert.That(viewModel.SelectedViewMode, Is.EqualTo(RequirementViewMode.RequirementsScraper));
        }

        [Test]
        public void UnifiedRequirementsMainViewModel_IntegratesWithRequirementsSearchAttachmentsViewModel()
        {
            // Arrange
            var viewModel = _serviceProvider!.GetRequiredService<UnifiedRequirementsMainViewModel>();

            // Assert
            Assert.That(viewModel.RequirementsSearchAttachmentsViewModel, Is.Not.Null, 
                "Should have integrated RequirementsSearchAttachmentsViewModel");
        }
    }

    #region Mock Services for Testing

    public class MockPersistenceService : IPersistenceService
    {
        public T? Load<T>(string key) => default(T);
        public void Save<T>(string key, T data) { }
        public bool ContainsKey(string key) => false;
        public void Remove(string key) { }
    }

    public class MockTextEditingDialogService : ITextEditingDialogService
    {
        public string? ShowEditDialog(string initialText, string title = "Edit Text") => null;
    }

    public class MockRequirementsMediator : IRequirementsMediator
    {
        public void Subscribe<T>(Action<T> handler) where T : class { }
        public void Unsubscribe<T>(Action<T> handler) where T : class { }
        public void Publish<T>(T eventObj) where T : class { }
        public bool IsJamaDataSource() => false;
    }

    public class MockWorkspaceContext : TestCaseEditorApp.Services.IWorkspaceContext
    {
        public string? CurrentProjectPath => null;
        public string? CurrentProjectName => "Test Project";
        public bool HasOpenProject => false;
    }

    #endregion
}