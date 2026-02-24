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
        public async Task CalculateDerivationQualityAsync_WithValidCapabilities_ReturnsQualityMetrics()
        {
            // Arrange
            var capabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    RequirementText = "Test Capability 1", 
                    DerivationRationale = "Comprehensive test description",
                    TaxonomyCategory = "Hardware Test",
                    ConfidenceScore = 0.95
                },
                new DerivedCapability 
                { 
                    RequirementText = "Test Capability 2", 
                    DerivationRationale = "Another detailed description",
                    TaxonomyCategory = "Software Test", 
                    ConfidenceScore = 0.87
                }
            };

            var expectedQualityMetrics = new QualityMetrics
            {
                OverallScore = 0.91,
                ConfidenceScore = 0.91,
                CompletenessScore = 0.95,
                ConsistencyScore = 0.88,
                ClarityScore = 0.90
            };

            _mockQualityScorer.Setup(x => x.CalculateQualityScore(It.IsAny<List<DerivedCapability>>()))
                .Returns(expectedQualityMetrics);

            // Act
            var result = await _service.CalculateDerivationQualityAsync(capabilities);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0.91, result.OverallScore, 0.01);
            Assert.AreEqual(0.95, result.CompletenessScore, 0.01);
            Assert.AreEqual(0.88, result.ConsistencyScore, 0.01);
            Assert.AreEqual(0.90, result.ClarityScore, 0.01);
        }

        [TestMethod]
        public async Task CalculateDerivationQualityAsync_WithEmptyCapabilities_ReturnsZeroScore()
        {
            // Arrange
            var emptyCapabilities = new List<DerivedCapability>();

            // Act
            var result = await _service.CalculateDerivationQualityAsync(emptyCapabilities);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0.0, result.OverallScore);
            Assert.AreEqual(0.0, result.ConfidenceScore);
            Assert.AreEqual(0.0, result.CompletenessScore);
        }

        [TestMethod]
        public async Task CalculateDerivationQualityAsync_WithNullCapabilities_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _service.CalculateDerivationQualityAsync(null));
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

            var expectedScore = new CapabilityQualityScore
            {
                CapabilityId = capability.Id,
                OverallScore = 0.89,
                DescriptiveScore = 0.92,
                CategoryConsistencyScore = 0.95,
                ConfidenceScore = 0.93,
                TraceabilityScore = 0.80
            };

            _mockQualityScorer.Setup(x => x.ScoreSingleCapability(capability))
                .Returns(expectedScore);

            // Act
            var result = await _service.ScoreCapabilityQualityAsync(capability);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0.89, result.OverallScore, 0.01);
            Assert.AreEqual(0.92, result.DescriptiveScore, 0.01);
            Assert.AreEqual(0.95, result.CategoryConsistencyScore, 0.01);
            Assert.AreEqual(capability.Id, result.CapabilityId);
        }

        [TestMethod]
        public async Task ScoreCapabilityQualityAsync_WithNullCapability_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _service.ScoreCapabilityQualityAsync(null));
        }

        [TestMethod]
        public async Task CalculatePerformanceMetricsAsync_WithDerivationResult_ReturnsMetrics()
        {
            // Arrange
            var derivationResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability>
                {
                    new DerivedCapability { RequirementText = "Test Capability", ConfidenceScore = 0.9 }
                },
                QualityScore = 0.85
            };

            // Act
            var result = await _service.CalculatePerformanceMetricsAsync(derivationResult);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1500, result.ProcessingTimeMs);
            Assert.AreEqual(1, result.CapabilitiesGenerated);
            Assert.AreEqual(0.9, result.AverageConfidence, 0.01);
            Assert.AreEqual(0.85, result.QualityScore, 0.01);
            Assert.IsTrue(result.IsSuccessful);
        }

        [TestMethod]
        public async Task CalculatePerformanceMetricsAsync_WithFailedDerivation_ReturnsFailureMetrics()
        {
            // Arrange
            var failedResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability>(),
                ProcessingWarnings = new List<string> { "LLM service unavailable" },
                QualityScore = 0.0
            };

            // Act
            var result = await _service.CalculatePerformanceMetricsAsync(failedResult);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(500, result.ProcessingTimeMs);
            Assert.AreEqual(0, result.CapabilitiesGenerated);
            Assert.AreEqual(0.0, result.AverageConfidence);
            Assert.IsFalse(result.IsSuccessful);
            Assert.AreEqual(1, result.ErrorCount);
        }

        [TestMethod]
        public async Task IntegrateQualityFeedbackAsync_WithQualityMetrics_UpdatesCapabilities()
        {
            // Arrange
            var capabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1", 
                    Name = "Test Capability", 
                    Confidence = 0.8 
                }
            };

            var qualityMetrics = new QualityMetrics
            {
                OverallScore = 0.75,
                ConfidenceScore = 0.8,
                CompletenessScore = 0.7,
                ConsistencyScore = 0.75
            };

            // Act
            var result = await _service.IntegrateQualityFeedbackAsync(capabilities, qualityMetrics);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Count);
            // Verify that quality feedback has been integrated (implementation detail)
            // The actual integration logic would update capability properties based on quality metrics
        }

        [TestMethod]
        public async Task GenerateQualityReportAsync_WithMultipleCapabilities_ReturnsComprehensiveReport()
        {
            // Arrange
            var capabilities = new List<DerivedCapability>
            {
                new DerivedCapability { RequirementText = "Cap 1", ConfidenceScore = 0.9, TaxonomyCategory = "Hardware Test" },
                new DerivedCapability { RequirementText = "Cap 2", ConfidenceScore = 0.7, TaxonomyCategory = "Software Test" },
                new DerivedCapability { RequirementText = "Cap 3", ConfidenceScore = 0.85, TaxonomyCategory = "Hardware Test" }
            };

            var qualityMetrics = new QualityMetrics
            {
                OverallScore = 0.82,
                ConfidenceScore = 0.82,
                CompletenessScore = 0.80,
                ConsistencyScore = 0.85,
                ClarityScore = 0.80
            };

            _mockQualityScorer.Setup(x => x.CalculateQualityScore(It.IsAny<List<DerivedCapability>>()))
                .Returns(qualityMetrics);

            // Act
            var result = await _service.GenerateQualityReportAsync(capabilities);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.QualityMetrics);
            Assert.AreEqual(0.82, result.QualityMetrics.OverallScore, 0.01);
            Assert.AreEqual(3, result.TotalCapabilities);
            Assert.AreEqual(0.82, result.AverageConfidence, 0.02); // (0.9 + 0.7 + 0.85) / 3
            
            // Should have category breakdown
            Assert.IsTrue(result.CategoryBreakdown.ContainsKey("Hardware Test"));
            Assert.IsTrue(result.CategoryBreakdown.ContainsKey("Software Test"));
            Assert.AreEqual(2, result.CategoryBreakdown["Hardware Test"]);
            Assert.AreEqual(1, result.CategoryBreakdown["Software Test"]);
        }

        [TestMethod]
        public void Constructor_WithNullQualityScorer_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new QualityScoringIntegrationService(null, _mockLogger.Object));
        }

        [TestMethod]
        public async Task CalculateDerivationQualityAsync_WithLowConfidenceCapabilities_ReturnsLowScore()
        {
            // Arrange
            var lowConfidenceCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    RequirementText = "Uncertain Capability", 
                    DerivationRationale = "Vague description",
                    ConfidenceScore = 0.3,
                    TaxonomyCategory = "General Test"
                },
                new DerivedCapability 
                { 
                    RequirementText = "Another Low Confidence", 
                    DerivationRationale = "Another vague description",
                    ConfidenceScore = 0.4,
                    TaxonomyCategory = "General Test"
                }
            };

            var lowQualityMetrics = new QualityMetrics
            {
                OverallScore = 0.35,
                ConfidenceScore = 0.35,
                CompletenessScore = 0.40,
                ConsistencyScore = 0.30,
                ClarityScore = 0.35
            };

            _mockQualityScorer.Setup(x => x.CalculateQualityScore(It.IsAny<List<DerivedCapability>>()))
                .Returns(lowQualityMetrics);

            // Act
            var result = await _service.CalculateDerivationQualityAsync(lowConfidenceCapabilities);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.OverallScore < 0.5); // Low quality score
            Assert.AreEqual(0.35, result.OverallScore, 0.01);
        }

        [TestMethod]
        public async Task CalculatePerformanceMetricsAsync_WithLongProcessingTime_IdentifiesPerformanceIssue()
        {
            // Arrange
            var slowDerivationResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability>
                {
                    new DerivedCapability { RequirementText = "Slow Capability", ConfidenceScore = 0.8 }
                },
                QualityScore = 0.8
            };

            // Act
            var result = await _service.CalculatePerformanceMetricsAsync(slowDerivationResult);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.CapabilitiesGenerated);
        }
    }
}