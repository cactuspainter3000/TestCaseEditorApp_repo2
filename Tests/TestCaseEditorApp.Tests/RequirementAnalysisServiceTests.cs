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
        private RequirementAnalysisPromptBuilder _promptBuilder;
        private Mock<ResponseParserManager> _mockParserManager;
        private Mock<ISystemCapabilityDerivationService> _mockDerivationService;
        private Mock<IRequirementGapAnalyzer> _mockGapAnalyzer;
        private RequirementAnalysisService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockLlmService = new Mock<ITextGenerationService>();
            _promptBuilder = new RequirementAnalysisPromptBuilder();
            _mockParserManager = new Mock<ResponseParserManager>();
            _mockDerivationService = new Mock<ISystemCapabilityDerivationService>();
            _mockGapAnalyzer = new Mock<IRequirementGapAnalyzer>();

            _service = new RequirementAnalysisService(
                _mockLlmService.Object,
                _promptBuilder,
                _mockParserManager.Object,
                null,
                null,
                null,
                null, // directRagService
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
                QualityScore = 0.85,
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
                It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<Action<string>>(), It.IsAny<Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>>(), It.IsAny<Action<Requirement>?>()))
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
                It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<Action<string>>(), It.IsAny<Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>>(), It.IsAny<Action<Requirement>?>()))
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
                It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<Action<string>>(), It.IsAny<Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>>(), It.IsAny<Action<Requirement>?>()))
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
        public async Task ValidateTestingWorkflowAsync_EndToEndIntegration_ComputesCoverageAndValidity()
        {
            // Arrange: use real gap analyzer to exercise derivation + gap + scoring end-to-end.
            var requirements = new List<Requirement>
            {
                new Requirement
                {
                    Item = "REQ-100",
                    Name = "Power-on response",
                    Description = "REQ-100: The system shall verify power-on sequence within 5 seconds using automated test procedure."
                },
                new Requirement
                {
                    Item = "REQ-200",
                    Name = "Command handling",
                    Description = "REQ-200: The system shall validate command checksum and verify response timing in test step."
                }
            };

            _mockDerivationService.Setup(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(),
                It.IsAny<DerivationOptions>(),
                It.IsAny<Action<string>>(),
                It.IsAny<Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>>(),
                It.IsAny<Action<Requirement>?>()))
                .ReturnsAsync((string atpContent, DerivationOptions? _, Action<string>? _, Func<List<SkippedAtpStep>, Task<TimeoutRetryDecision>>? _, Action<Requirement>? _) =>
                {
                    var capabilityId = atpContent.Contains("REQ-100", StringComparison.OrdinalIgnoreCase)
                        ? "REQ-100"
                        : atpContent.Contains("REQ-200", StringComparison.OrdinalIgnoreCase)
                            ? "REQ-200"
                            : "UNKNOWN";

                    return new DerivationResult
                    {
                        QualityScore = 0.92,
                        ProcessingWarnings = new List<string>(),
                        DerivedCapabilities = new List<DerivedCapability>
                        {
                            new DerivedCapability
                            {
                                Id = capabilityId,
                                RequirementText = $"Derived capability for {capabilityId}",
                                ConfidenceScore = 0.95,
                                TaxonomyCategory = "Functional Performance"
                            }
                        }
                    };
                });

            var integrationService = new RequirementAnalysisService(
                _mockLlmService.Object,
                _promptBuilder,
                _mockParserManager.Object,
                null,
                null,
                null,
                null,
                _mockDerivationService.Object,
                new RequirementGapAnalyzer());

            // Act
            var result = await integrationService.ValidateTestingWorkflowAsync(requirements);

            // Assert
            Assert.IsNotNull(result.CoverageAnalysis);
            Assert.AreEqual(1.0, result.CoverageAnalysis.CoveragePercentage, 0.0001);
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.OverallScore >= 0.9, "Expected high workflow score for complete ATP coverage");
            Assert.AreEqual(0, result.Issues.Count(i => i.Severity == TestCaseEditorApp.MVVM.Domains.Requirements.Services.ValidationSeverity.Critical));
        }

        [TestMethod]
        public void GeneratePromptForInspection_WithExtractionTriageFields_IncludesTriageContext()
        {
            var requirement = new Requirement
            {
                Item = "REQ-777",
                Name = "Timing verification",
                Description = "The test solution shall verify command response timing within 25 ms.",
                AnalysisPriority = "High",
                FixType = "Missing Explicit Identifier",
                DispositionRecommendation = "NeedsReview - add exact identifier mapping",
                SuggestedRewrite = "The test solution shall verify command response timing within 25 ms for requirement ID CMD-25.",
                SourcePrefix = "4.1.2",
                SourcePrefixEvidence = "Section 4.1.2 command timing",
                SourcePrefixConfidence = 0.89
            };

            var prompt = _service.GeneratePromptForInspection(requirement);

            Assert.IsTrue(prompt.Contains("EXTRACTION TRIAGE CONTEXT:", StringComparison.Ordinal), "Prompt should include extraction triage section.");
            Assert.IsTrue(prompt.Contains("Analysis Priority: High", StringComparison.Ordinal), "Prompt should include extraction priority.");
            Assert.IsTrue(prompt.Contains("Fix Type: Missing Explicit Identifier", StringComparison.Ordinal), "Prompt should include fix type guidance.");
            Assert.IsTrue(prompt.Contains("Disposition Recommendation: NeedsReview", StringComparison.Ordinal), "Prompt should include disposition recommendation.");
            Assert.IsTrue(prompt.Contains("Suggested Rewrite from Extraction:", StringComparison.Ordinal), "Prompt should include extraction rewrite guidance.");
            Assert.IsTrue(prompt.Contains("Source Prefix Evidence: Section 4.1.2 command timing", StringComparison.Ordinal), "Prompt should include source evidence.");
        }

        [TestMethod]
        public void Constructor_WithNullLlmService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequirementAnalysisService(
                    null,
                    _promptBuilder,
                    _mockParserManager.Object));
        }

        [TestMethod]
        public void Constructor_WithValidRequiredDependencies_InitializesSuccessfully()
        {
            // Act
            var service = new RequirementAnalysisService(
                _mockLlmService.Object,
                _promptBuilder,
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
                _promptBuilder,
                _mockParserManager.Object,
                null,
                null,
                null,
                null, // directRagService
                _mockDerivationService.Object,
                _mockGapAnalyzer.Object);

            // Assert
            Assert.IsNotNull(service);
        }
    }
}