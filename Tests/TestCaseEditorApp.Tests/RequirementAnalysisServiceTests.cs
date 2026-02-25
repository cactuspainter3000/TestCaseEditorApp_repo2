using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Prompts;

namespace TestCaseEditorApp.Tests.Phase4Services
{
    [TestClass]
    public class RequirementAnalysisServiceTests
    {
        private Mock<ITextGenerationService> _mockLlmService;
        private Mock<RequirementAnalysisPromptBuilder> _mockPromptBuilder;
        private Mock<ResponseParserManager> _mockParserManager;
        private Mock<LlmServiceHealthMonitor> _mockHealthMonitor;
        private Mock<RequirementAnalysisCache> _mockCache;
        private Mock<AnythingLLMService> _mockAnythingLLMService;
        private Mock<ISystemCapabilityDerivationService> _mockDerivationService;
        private Mock<IRequirementGapAnalyzer> _mockGapAnalyzer;
        private RequirementAnalysisService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockLlmService = new Mock<ITextGenerationService>();
            _mockPromptBuilder = new Mock<RequirementAnalysisPromptBuilder>();
            _mockParserManager = new Mock<ResponseParserManager>();
            _mockHealthMonitor = new Mock<LlmServiceHealthMonitor>();
            _mockCache = new Mock<RequirementAnalysisCache>();
            _mockAnythingLLMService = new Mock<AnythingLLMService>();
            _mockDerivationService = new Mock<ISystemCapabilityDerivationService>();
            _mockGapAnalyzer = new Mock<IRequirementGapAnalyzer>();

            _service = new RequirementAnalysisService(
                _mockLlmService.Object,
                _mockPromptBuilder.Object,
                _mockParserManager.Object,
                _mockHealthMonitor.Object,
                _mockCache.Object,
                _mockAnythingLLMService.Object,
                _mockDerivationService.Object,
                _mockGapAnalyzer.Object);
        }

        [TestMethod]
        public async Task AnalyzeRequirementDerivationAsync_WithValidRequirement_ReturnsAnalysis()
        {
            // Arrange
            var requirement = new Requirement
            {
                Item = "REQ-001",
                Name = "Test Requirement", 
                Description = "The system shall perform ATP-001 to verify functionality."
            };

            var derivationResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability>
                {
                    new DerivedCapability
                    {
                        Id = "CAP-001",
                        RequirementText = "Functionality Verification",
                        DerivationRationale = "Verify system functionality",
                        ConfidenceScore = 0.9
                    }
                }
            };

            _mockDerivationService.Setup(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(), It.IsAny<DerivationOptions>()))
                .ReturnsAsync(derivationResult);

            // Act
            var result = await _service.AnalyzeRequirementDerivationAsync(requirement);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.HasATPContent);
            Assert.AreEqual(1, result.DerivedCapabilities.Count);
            Assert.IsTrue(result.DerivationQuality > 0);
        }

        [TestMethod]
        public async Task AnalyzeRequirementDerivationAsync_WithNullRequirement_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.AnalyzeRequirementDerivationAsync(null));
        }

        [TestMethod]
        public async Task AnalyzeRequirementGapAsync_WithValidInput_ReturnsGapAnalysis()
        {
            // Arrange
            var requirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "Test Req" }
            };
            var capabilities = new List<DerivedCapability>
            {
                new DerivedCapability { Id = "CAP-001", RequirementText = "Test Cap" }
            };

            var gapResult = new GapAnalysisResult
            {
                Success = true,
                TotalDerivedCapabilities = 1,
                TotalExistingRequirements = 1,
                UncoveredCapabilities = new List<UncoveredCapability>(),
                RequirementOverlaps = new List<RequirementOverlap>()
            };

            _mockGapAnalyzer.Setup(x => x.AnalyzeGapsAsync(
                It.IsAny<List<DerivedCapability>>(), It.IsAny<List<Requirement>>(), It.IsAny<GapAnalysisOptions>()))
                .ReturnsAsync(gapResult);

            // Act  
            var result = await _service.AnalyzeRequirementGapAsync(capabilities, requirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
        }

        [TestMethod]
        public async Task AnalyzeBatchDerivationAsync_WithValidRequirements_ReturnsResults()
        {
            // Arrange
            var requirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Description = "Test requirement 1" },
                new Requirement { Item = "REQ-002", Description = "Test requirement 2" }
            };

            var derivationResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability>()
            };

            _mockDerivationService.Setup(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(), It.IsAny<DerivationOptions>()))
                .ReturnsAsync(derivationResult);

            // Act
            var results = await _service.AnalyzeBatchDerivationAsync(requirements);

            // Assert
            Assert.IsNotNull(results);
            Assert.AreEqual(2, results.Count());
            Assert.IsTrue(results.All(r => r != null));
        }

        [TestMethod]
        public async Task ValidateTestingWorkflowAsync_WithValidRequirements_ReturnsValidation()
        {
            // Arrange
            var requirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Description = "Test requirement" }
            };

            var derivationResult = new DerivationResult
            {
                DerivedCapabilities = new List<DerivedCapability> 
                {
                    new DerivedCapability { RequirementText = "Test Capability" }
                },
                ProcessingWarnings = new List<string>() // Empty list makes IsSuccessful = true
            };

            var gapResult = new GapAnalysisResult
            {
                Success = true,
                Summary = "Gap analysis completed successfully"
            };

            _mockDerivationService.Setup(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(), It.IsAny<DerivationOptions>()))
                .ReturnsAsync(derivationResult);

            _mockGapAnalyzer.Setup(x => x.AnalyzeGapsAsync(
                It.IsAny<List<DerivedCapability>>(), It.IsAny<List<Requirement>>(), It.IsAny<GapAnalysisOptions>()))
                .ReturnsAsync(gapResult);

            // Act
            var result = await _service.ValidateTestingWorkflowAsync(requirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.OverallScore >= 0);
        }

        [TestMethod]
        public void Constructor_WithNullLlmService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequirementAnalysisService(
                    null,
                    _mockPromptBuilder.Object,
                    _mockParserManager.Object));
        }

        [TestMethod]
        public void Constructor_WithValidRequiredDependencies_InitializesSuccessfully()
        {
            // Act
            var service = new RequirementAnalysisService(
                _mockLlmService.Object,
                _mockPromptBuilder.Object,
                _mockParserManager.Object);

            // Assert
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public void Constructor_WithAllDependencies_InitializesSuccessfully()
        {
            // Act
            var service = new RequirementAnalysisService(
                _mockLlmService.Object,
                _mockPromptBuilder.Object,
                _mockParserManager.Object,
                _mockHealthMonitor.Object,
                _mockCache.Object,
                _mockAnythingLLMService.Object,
                _mockDerivationService.Object,
                _mockGapAnalyzer.Object);

            // Assert
            Assert.IsNotNull(service);
        }
    }
}