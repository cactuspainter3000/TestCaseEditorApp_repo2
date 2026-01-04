using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Domains.WorkspaceManagement.Mediators;
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Tests
{
    /// <summary>
    /// Tests for the domain-specific notification area switching functionality.
    /// Verifies that notification areas change correctly when navigating between different domains.
    /// </summary>
    [TestClass]
    public class NotificationAreaSwitchingTests
    {
        private ViewAreaCoordinator? _coordinator;
        private Mock<IViewModelFactory>? _mockFactory;
        private Mock<INavigationMediator>? _mockNavigationMediator;
        private Mock<IWorkspaceManagementMediator>? _mockWorkspaceMediator;
        private Mock<ITestCaseGenerationMediator>? _mockTestCaseMediator;
        private DefaultNotificationViewModel? _defaultNotification;
        private TestCaseGeneratorNotificationViewModel? _testCaseNotification;

        [TestInitialize]
        public void Setup()
        {
            // Create mocks
            _mockFactory = new Mock<IViewModelFactory>();
            _mockNavigationMediator = new Mock<INavigationMediator>();
            _mockWorkspaceMediator = new Mock<IWorkspaceManagementMediator>();
            _mockTestCaseMediator = new Mock<ITestCaseGenerationMediator>();

            // Create notification ViewModels
            var mockLogger = new Mock<ILogger<DefaultNotificationViewModel>>();
            _defaultNotification = new DefaultNotificationViewModel(mockLogger.Object);

            var mockTestCaseLogger = new Mock<ILogger<TestCaseGeneratorNotificationViewModel>>();
            var mockAnythingLLMMediator = new Mock<IAnythingLLMMediator>();
            _testCaseNotification = new TestCaseGeneratorNotificationViewModel(
                mockTestCaseLogger.Object, 
                mockAnythingLLMMediator.Object);

            // Setup factory to return our notification ViewModels
            _mockFactory.Setup(f => f.CreateDefaultNotificationViewModel())
                       .Returns(_defaultNotification);
            _mockFactory.Setup(f => f.CreateTestCaseGeneratorNotificationViewModel())
                       .Returns(_testCaseNotification);

            // Setup factory for other ViewModels (minimal mocks)
            _mockFactory.Setup(f => f.CreateWorkspaceHeaderViewModel())
                       .Returns(new Mock<WorkspaceHeaderViewModel>().Object);
            _mockFactory.Setup(f => f.CreateTestCaseGenerator_HeaderViewModel())
                       .Returns(new Mock<TestCaseGenerator_HeaderVM>().Object);
        }

        [TestMethod]
        public void ViewAreaCoordinator_InitialState_ShouldUseDefaultNotification()
        {
            // Arrange & Act
            _coordinator = new ViewAreaCoordinator(
                _mockFactory!.Object,
                _mockNavigationMediator!.Object,
                _mockWorkspaceMediator!.Object,
                _mockTestCaseMediator!.Object);

            // Assert
            Assert.IsNotNull(_coordinator.NotificationArea, "NotificationArea should not be null");
            Assert.IsInstanceOfType(_coordinator.NotificationArea, typeof(DefaultNotificationViewModel), 
                "Initial notification area should be DefaultNotificationViewModel");
            
            var defaultNotification = (DefaultNotificationViewModel)_coordinator.NotificationArea;
            Assert.AreEqual("Ready", defaultNotification.DefaultText, 
                "Default notification should show 'Ready' text");
        }

        [TestMethod]
        public void ViewAreaCoordinator_NavigateToTestCaseGenerator_ShouldSwitchToTestCaseNotification()
        {
            // Arrange
            _coordinator = new ViewAreaCoordinator(
                _mockFactory!.Object,
                _mockNavigationMediator!.Object,
                _mockWorkspaceMediator!.Object,
                _mockTestCaseMediator!.Object);

            // Verify initial state
            Assert.IsInstanceOfType(_coordinator.NotificationArea, typeof(DefaultNotificationViewModel));

            // Act - Navigate to Test Case Generator
            _coordinator.NavigateToTestCaseGenerator();

            // Assert
            Assert.IsInstanceOfType(_coordinator.NotificationArea, typeof(TestCaseGeneratorNotificationViewModel), 
                "Notification area should switch to TestCaseGeneratorNotificationViewModel");
        }

        [TestMethod]
        public void ViewAreaCoordinator_NavigateToProject_ShouldSwitchBackToDefaultNotification()
        {
            // Arrange
            _coordinator = new ViewAreaCoordinator(
                _mockFactory!.Object,
                _mockNavigationMediator!.Object,
                _mockWorkspaceMediator!.Object,
                _mockTestCaseMediator!.Object);

            // Navigate to Test Case Generator first
            _coordinator.NavigateToTestCaseGenerator();
            Assert.IsInstanceOfType(_coordinator.NotificationArea, typeof(TestCaseGeneratorNotificationViewModel));

            // Act - Navigate back to Project (default)
            _coordinator.NavigateToProject();

            // Assert
            Assert.IsInstanceOfType(_coordinator.NotificationArea, typeof(DefaultNotificationViewModel), 
                "Notification area should switch back to DefaultNotificationViewModel");
        }

        [TestMethod]
        public void NotificationAreaSwitching_ShouldDisposeOldNotificationIfDisposable()
        {
            // Arrange
            var mockDisposableNotification = new Mock<IDisposableNotification>();
            mockDisposableNotification.Setup(x => x.Dispose());

            _mockFactory!.Setup(f => f.CreateDefaultNotificationViewModel())
                        .Returns(mockDisposableNotification.Object);

            _coordinator = new ViewAreaCoordinator(
                _mockFactory.Object,
                _mockNavigationMediator!.Object,
                _mockWorkspaceMediator!.Object,
                _mockTestCaseMediator!.Object);

            // Act - Switch to Test Case Generator (should dispose old notification)
            _coordinator.NavigateToTestCaseGenerator();

            // Assert
            mockDisposableNotification.Verify(x => x.Dispose(), Times.Once, 
                "Old notification should be disposed when switching");
        }

        [TestCleanup]
        public void Cleanup()
        {
            _defaultNotification?.Dispose();
            _testCaseNotification?.Dispose();
        }
    }

    // Helper interface for testing disposal
    public interface IDisposableNotification : IDisposable
    {
        string DefaultText { get; }
        bool IsVisible { get; }
    }
}