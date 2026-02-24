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
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.Services.Parsing;

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
        public async Task AnalyzeRequirementAsync_WithValidRequirement_ReturnsAnalysis()
        {
            // Arrange
            var requirement = new Requirement
            {
                Item = "REQ-001",
                Name = "JTAG Test",
                Description = "The system shall verify JTAG boundary scan connectivity."
            };

            var expectedAnalysis = new RequirementAnalysis
            {
                IsAnalyzed = true,
                OriginalQualityScore = 8,
                Issues = new List<AnalysisIssue>(),
                Recommendations = new List<AnalysisRecommendation>(),
                FreeformFeedback = "Good requirement structure"
            };

            _mockPromptBuilder.Setup(x => x.GetSystemPrompt()).Returns("System prompt");
            _mockPromptBuilder.Setup(x => x.BuildContextPrompt(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<List<RequirementTable>>(), It.IsAny<LooseContent>(), It.IsAny<string>()))
                .Returns("Context prompt");

            _mockLlmService.Setup(x => x.GenerateWithSystemAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("LLM response");

            _mockParserManager.Setup(x => x.ParseResponse(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(expectedAnalysis);

            // Act
            var result = await _service.AnalyzeRequirementAsync(requirement);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsAnalyzed);
            Assert.AreEqual(8, result.OriginalQualityScore);
            Assert.AreEqual("Good requirement structure", result.FreeformFeedback);
        }

        [TestMethod]
        public async Task AnalyzeRequirementDerivationAsync_WithATPContent_ReturnsDerivationAnalysis()
        {
            // Arrange
            var requirement = new Requirement
            {
                Item = "REQ-001",
                Name = "Automated JTAG Test",
                Description = "Connect JTAG interface, apply test vectors, and verify boundary scan chain integrity."
            };

            var expectedDerivationResult = new DerivationResult
            {
                IsSuccessful = true,
                DerivedCapabilities = new List<DerivedCapability>
                {
                    new DerivedCapability 
                    { 
                        Name = "JTAG Boundary Scan", 
                        Category = "Hardware Test", 
                        Confidence = 0.9 
                    }
                },
                QualityScore = 0.85
            };

            _mockDerivationService.Setup(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedDerivationResult);

            // Act
            var result = await _service.AnalyzeRequirementDerivationAsync(requirement);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.HasATPContent);
            Assert.AreEqual(1, result.DerivedCapabilities.Count);
            Assert.AreEqual("JTAG Boundary Scan", result.DerivedCapabilities[0].Name);
            Assert.AreEqual(0.85, result.DerivationQuality, 0.01);
        }

        [TestMethod]
        public async Task AnalyzeRequirementDerivationAsync_WithoutATPContent_ReturnsNoCapabilities()
        {
            // Arrange
            var requirement = new Requirement
            {
                Item = "REQ-002",
                Name = "System Overview",
                Description = "The system provides comprehensive testing capabilities."
            };

            // Act
            var result = await _service.AnalyzeRequirementDerivationAsync(requirement);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.HasATPContent);
            Assert.AreEqual(0, result.DerivedCapabilities.Count);
        }

        [TestMethod]
        public async Task AnalyzeRequirementGapAsync_WithCapabilitiesAndRequirements_ReturnsGapAnalysis()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability { Name = "JTAG Test", Category = "Hardware Test" },
                new DerivedCapability { Name = "Power Test", Category = "Power Test" }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "JTAG Test", Description = "JTAG testing" }
                // Missing requirement for Power Test - should be identified as gap
            };

            var expectedGapResult = new GapAnalysisResult
            {
                IsSuccessful = true,
                UncoveredCapabilities = new List<UncoveredCapability>
                {
                    new UncoveredCapability 
                    { 
                        CapabilityName = "Power Test", 
                        Severity = GapSeverity.High,
                        Recommendation = "Create requirement for power testing capability"
                    }
                },
                CoveragePercentage = 0.5
            };

            _mockGapAnalyzer.Setup(x => x.AnalyzeGapsAsync(
                It.IsAny<List<DerivedCapability>>(), It.IsAny<List<Requirement>>()))
                .ReturnsAsync(expectedGapResult);

            // Act
            var result = await _service.AnalyzeRequirementGapAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(1, result.GapAnalysisResult.UncoveredCapabilities.Count);
            Assert.AreEqual("Power Test", result.GapAnalysisResult.UncoveredCapabilities[0].CapabilityName);
            Assert.AreEqual(0.5, result.GapAnalysisResult.CoveragePercentage, 0.01);
        }

        [TestMethod]
        public async Task ValidateTestingWorkflowAsync_WithValidWorkflow_ReturnsValidationResult()
        {
            // Arrange
            var requirements = new List<Requirement>
            {
                new Requirement 
                { 
                    Item = "REQ-001", 
                    Name = "JTAG Test", 
                    Description = "Connect JTAG and verify boundary scan chain."
                },
                new Requirement 
                { 
                    Item = "REQ-002", 
                    Name = "Power Test", 
                    Description = "Measure power supply voltages at 3.3V and 5V."
                }
            };

            // Mock derivation service to return capabilities for both requirements
            var derivationResult1 = new DerivationResult
            {
                IsSuccessful = true,
                DerivedCapabilities = new List<DerivedCapability>
                {
                    new DerivedCapability { Name = "JTAG Verification", Category = "Hardware Test", Confidence = 0.9 }
                },
                QualityScore = 0.85
            };

            var derivationResult2 = new DerivationResult
            {
                IsSuccessful = true,
                DerivedCapabilities = new List<DerivedCapability>
                {
                    new DerivedCapability { Name = "Power Supply Test", Category = "Power Test", Confidence = 0.88 }
                },
                QualityScore = 0.82
            };

            _mockDerivationService.SetupSequence(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(derivationResult1)
                .ReturnsAsync(derivationResult2);

            // Mock gap analysis to show good coverage
            var gapResult = new GapAnalysisResult
            {
                IsSuccessful = true,
                CoveragePercentage = 0.95,
                UncoveredCapabilities = new List<UncoveredCapability>()
            };

            _mockGapAnalyzer.Setup(x => x.AnalyzeGapsAsync(
                It.IsAny<List<DerivedCapability>>(), It.IsAny<List<Requirement>>()))
                .ReturnsAsync(gapResult);

            // Act
            var result = await _service.ValidateTestingWorkflowAsync(requirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.OverallScore > 0.8); // High score due to good coverage
            Assert.IsNotNull(result.CoverageAnalysis);
            Assert.IsTrue(result.CoverageAnalysis.CoveragePercentage > 0.9);
        }

        [TestMethod]
        public async Task AnalyzeBatchDerivationAsync_WithMultipleRequirements_ProcessesAllRequirements()
        {
            // Arrange
            var requirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "Test 1", Description = "JTAG test procedure" },
                new Requirement { Item = "REQ-002", Name = "Test 2", Description = "Power supply verification" },
                new Requirement { Item = "REQ-003", Name = "Test 3", Description = "Memory diagnostic test" }
            };

            var batchOptions = new BatchAnalysisOptions 
            { 
                MaxConcurrency = 2, 
                ContinueOnFailure = true,
                AnalysisTimeout = TimeSpan.FromSeconds(30)
            };

            // Mock derivation service to return different results for each requirement
            _mockDerivationService.Setup(x => x.DeriveCapabilitiesAsync(
                It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DerivationResult
                {
                    IsSuccessful = true,
                    DerivedCapabilities = new List<DerivedCapability>
                    {
                        new DerivedCapability { Name = "Test Capability", Confidence = 0.8 }
                    },
                    QualityScore = 0.75
                });

            var progressUpdates = new List<BatchAnalysisProgress>();

            // Act
            var results = await _service.AnalyzeBatchDerivationAsync(
                requirements, 
                batchOptions, 
                progress => progressUpdates.Add(progress));

            // Assert
            Assert.IsNotNull(results);
            var resultsList = results.ToList();
            Assert.AreEqual(3, resultsList.Count);
            
            // Verify all requirements were processed
            var processedItems = resultsList.Select(r => r.AnalyzedRequirement.Item).ToList();
            Assert.IsTrue(processedItems.Contains("REQ-001"));
            Assert.IsTrue(processedItems.Contains("REQ-002"));
            Assert.IsTrue(processedItems.Contains("REQ-003"));
            
            // Verify progress updates were received
            Assert.IsTrue(progressUpdates.Count > 0);
            Assert.AreEqual(3, progressUpdates.Last().CompletedCount);
        }

        [TestMethod]
        public async Task AnalyzeRequirementWithStreamingAsync_WithProgressCallback_CallsProgressUpdates()
        {
            // Arrange
            var requirement = new Requirement
            {
                Item = "REQ-001",
                Name = "Test Requirement",
                Description = "Test description"
            };

            var progressUpdates = new List<string>();
            var partialResults = new List<string>();

            _mockPromptBuilder.Setup(x => x.GetSystemPrompt()).Returns("System prompt");
            _mockPromptBuilder.Setup(x => x.BuildContextPrompt(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<RequirementTable>>(), It.IsAny<LooseContent>(), It.IsAny<string>()))
                .Returns("Context prompt");

            // Mock AnythingLLM service for streaming
            _mockAnythingLLMService.Setup(x => x.SendChatMessageStreamingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action<string>>(),
                It.IsAny<Action<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult("Streaming response"))
                .Callback<string, string, Action<string>, Action<string>, string, CancellationToken>(
                    (ws, prompt, onChunk, onProgress, thread, ct) =>
                    {
                        onProgress?.Invoke("Processing...");
                        onChunk?.Invoke("Partial result");
                    });

            _mockParserManager.Setup(x => x.ParseResponse(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new RequirementAnalysis { IsAnalyzed = true, OriginalQualityScore = 7 });

            // Act
            var result = await _service.AnalyzeRequirementWithStreamingAsync(
                requirement,
                onPartialResult: partial => partialResults.Add(partial),
                onProgressUpdate: progress => progressUpdates.Add(progress));

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(progressUpdates.Count > 0);
            // Progress updates should include status messages
            Assert.IsTrue(progressUpdates.Any(p => p.Contains("Processing") || p.Contains("Starting")));
        }

        [TestMethod]
        public void SetWorkspaceContext_WithWorkspaceName_SetsContext()
        {
            // Arrange
            var workspaceName = "Test Project Workspace";

            // Act
            _service.SetWorkspaceContext(workspaceName);

            // Assert
            // Verification would depend on internal implementation
            // This should not throw any exceptions
        }

        [TestMethod]
        public async Task ValidateServiceAsync_WithHealthyService_ReturnsTrue()
        {
            // Arrange
            var healthReport = new LlmServiceHealthMonitor.HealthReport
            {
                Status = LlmServiceHealthMonitor.HealthStatus.Healthy,
                ResponseTimeMs = 500
            };

            _mockHealthMonitor.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(healthReport);

            // Act
            var result = await _service.ValidateServiceAsync();

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task ValidateServiceAsync_WithUnhealthyService_ReturnsFalse()
        {
            // Arrange
            var healthReport = new LlmServiceHealthMonitor.HealthReport
            {
                Status = LlmServiceHealthMonitor.HealthStatus.Unhealthy,
                ResponseTimeMs = 10000
            };

            _mockHealthMonitor.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(healthReport);

            // Act
            var result = await _service.ValidateServiceAsync();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void InvalidateCache_WithRequirementId_CallsCacheInvalidation()
        {
            // Arrange
            var requirementId = "REQ-001";

            // Act
            _service.InvalidateCache(requirementId);

            // Assert
            _mockCache.Verify(x => x.Invalidate(requirementId), Times.Once);
        }

        [TestMethod]
        public void ClearAnalysisCache_CallsCacheClear()
        {
            // Act
            _service.ClearAnalysisCache();

            // Assert
            _mockCache.Verify(x => x.Clear(), Times.Once);
        }

        [TestMethod]
        public void Constructor_WithNullLlmService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequirementAnalysisService(null, _mockPromptBuilder.Object, 
                    _mockParserManager.Object));
        }

        [TestMethod]
        public void Constructor_WithNullPromptBuilder_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequirementAnalysisService(_mockLlmService.Object, null, 
                    _mockParserManager.Object));
        }

        [TestMethod]
        public void Constructor_WithNullParserManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new RequirementAnalysisService(_mockLlmService.Object, _mockPromptBuilder.Object, 
                    null));
        }
    }
}