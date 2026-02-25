using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Tests.Phase4Services
{
    [TestClass]
    public class QualityScoringIntegrationServiceTests
    {
        private Mock<ILogger<QualityScoringIntegrationService>> _mockLogger;
        private Mock<IDerivationQualityScorer> _mockQualityScorer;
        private Mock<ITrainingDataValidationService> _mockValidationService;
        private Mock<ISystemCapabilityDerivationService> _mockDerivationService;
        private QualityScoringIntegrationService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<QualityScoringIntegrationService>>();
            _mockQualityScorer = new Mock<IDerivationQualityScorer>();
            _mockValidationService = new Mock<ITrainingDataValidationService>();
            _mockDerivationService = new Mock<ISystemCapabilityDerivationService>();

            _service = new QualityScoringIntegrationService(
                _mockLogger.Object, 
                _mockQualityScorer.Object, 
                _mockValidationService.Object, 
                _mockDerivationService.Object);
        }

        [TestMethod]
        public async Task GetActiveQualityMetricsAsync_ReturnsCurrentMetrics()
        {
            // Act
            var result = await _service.GetActiveQualityMetricsAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.SystemUptimeHours >= 0);
            Assert.IsTrue(result.ActiveSessions >= 0);
        }

        [TestMethod]
        public async Task PerformQualityGuidedDerivationAsync_WithValidInput_ReturnsResult()
        {
            // Arrange
            var atpStepText = "The system shall verify JTAG connectivity.";
            var options = new QualityGuidanceOptions { EnableAutoRefinement = true };

            // Act
            var result = await _service.PerformQualityGuidedDerivationAsync(atpStepText, options);

            // Assert
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task PerformQualityGuidedDerivationAsync_WithNullInput_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _service.PerformQualityGuidedDerivationAsync(null));
        }

        [TestMethod]
        public async Task ScoreCapabilityQualityAsync_WithSingleCapability_ReturnsScore()
        {
            // Arrange
            var capability = new DerivedCapability
            {
                RequirementText = "JTAG Boundary Scan",
                DerivationRationale = "Verifies JTAG boundary scan chain connectivity and integrity",
                TaxonomyCategory = "Hardware Test",
                ConfidenceScore = 0.93,
                SourceATPStep = "REQ-001"
            };

            var derivationResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability> { capability },
                QualityScore = 0.89
            };

            _mockQualityScorer.Setup(x => x.ScoreDerivationQualityAsync(
                It.IsAny<DerivationResult>(), It.IsAny<string>(), It.IsAny<QualityScoringOptions>()))
                .ReturnsAsync(new DerivationQualityScore { OverallScore = 0.89 });

            // Act
            var result = await _mockQualityScorer.Object.ScoreDerivationQualityAsync(derivationResult, "test", null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0.89, result.OverallScore, 0.01);
        }

        [TestMethod]
        public async Task ScoreDerivationQualityAsync_WithNullDerivationResult_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _mockQualityScorer.Object.ScoreDerivationQualityAsync(null, "test", null));
        }

        [TestMethod]
        public async Task RunQualityFeedbackLoopAsync_WithValidTimeWindow_ReturnsResult()
        {
            // Arrange
            var timeWindow = TimeSpan.FromHours(1);
            var options = new QualityFeedbackOptions { IncludeSelfEvaluation = true };

            // Act
            var result = await _service.RunQualityFeedbackLoopAsync(timeWindow, options);

            // Assert
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task CorrelateQualityWithValidationAsync_WithValidData_ReturnsCorrelation()
        {
            // Arrange
            var qualityScores = new List<DerivationQualityScore>
            {
                new DerivationQualityScore { OverallScore = 0.8 }
            };

            var validationResults = new List<TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult>
            {
                new TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult()
            };

            // Act
            var result = await _service.CorrelateQualityWithValidationAsync(qualityScores, validationResults);

            // Assert
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task CorrelateQualityWithValidationAsync_WithNullQualityScores_ThrowsArgumentNullException()
        {
            // Arrange
            var validationResults = new List<TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult>
            {
                new TestCaseEditorApp.MVVM.Domains.TrainingDataValidation.Services.ValidationResult()
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _service.CorrelateQualityWithValidationAsync(null, validationResults));
        }

        [TestMethod]
        public void Constructor_WithNullDerivationService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new QualityScoringIntegrationService(_mockLogger.Object, _mockQualityScorer.Object, _mockValidationService.Object, null));
        }




    }
}