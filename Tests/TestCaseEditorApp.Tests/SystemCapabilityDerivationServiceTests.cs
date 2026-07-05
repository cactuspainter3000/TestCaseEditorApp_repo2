using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Parsing;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.Prompts;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Tests.Phase4Services
{
    [TestClass]
    public class SystemCapabilityDerivationServiceTests
    {
        private Mock<ITextGenerationService> _mockLlmService;
        private Mock<ILogger<SystemCapabilityDerivationService>> _mockLogger;
        private Mock<ResponseParserManager> _mockResponseParser;
        private Mock<ATPStepParser> _mockAtpParser;  
        private Mock<ICapabilityDerivationPromptBuilder> _mockPromptBuilder;
        private Mock<TaxonomyValidator> _mockTaxonomyValidator;
        private Mock<ICapabilityAllocator> _mockCapabilityAllocator;
        private Mock<IMBSERequirementClassifier> _mockMBSEClassifier;
        private Mock<IDerivationQualityScorer> _mockQualityScorer;
        private SystemCapabilityDerivationService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockLlmService = new Mock<ITextGenerationService>();
            _mockLogger = new Mock<ILogger<SystemCapabilityDerivationService>>();
            
            // Create mocks with constructor arguments for classes that need them
            var mockAtpLogger = new Mock<ILogger<ATPStepParser>>();
            _mockResponseParser = new Mock<ResponseParserManager>(); // Parameterless constructor
            _mockAtpParser = new Mock<ATPStepParser>(mockAtpLogger.Object);
            _mockPromptBuilder = new Mock<ICapabilityDerivationPromptBuilder>();
            var mockTaxonomyLogger = new Mock<ILogger<TaxonomyValidator>>();
            _mockTaxonomyValidator = new Mock<TaxonomyValidator>(mockTaxonomyLogger.Object);
            _mockCapabilityAllocator = new Mock<ICapabilityAllocator>();
            _mockMBSEClassifier = new Mock<IMBSERequirementClassifier>();
            _mockQualityScorer = new Mock<IDerivationQualityScorer>();

            _service = new SystemCapabilityDerivationService(
                _mockLlmService.Object,
                _mockLogger.Object,
                _mockResponseParser.Object,
                _mockAtpParser.Object,
                _mockPromptBuilder.Object,
                _mockTaxonomyValidator.Object,
                _mockCapabilityAllocator.Object,
                _mockMBSEClassifier.Object,
                _mockQualityScorer.Object,
                null // directRagService - optional parameter
            );
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithValidInput_ReturnsSuccessfulResult()
        {
            // Arrange
            var requirementText = "The system shall verify JTAG boundary scan connectivity.";
            var options = new DerivationOptions { EnableQualityScoring = true };

            var expectedPrompt = "Analyze the following requirement for capabilities...";
            var llmResponse = "{\"derivedCapabilities\":[{\"requirementText\":\"JTAG Boundary Scan Verification\",\"rationale\":\"Verify JTAG connectivity\",\"taxonomyCategory\":\"Hardware Test\",\"confidence\":0.95,\"sourceATPStep\":\"Step 1\"}]}";

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<string>()))
                .Returns(expectedPrompt);

            _mockLlmService.Setup(x => x.GenerateAsync(expectedPrompt, It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.DerivedCapabilities.Count >= 1);
            Assert.AreEqual("JTAG Boundary Scan Verification", result.DerivedCapabilities[0].RequirementText);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithNullInput_ReturnsFailureResult()
        {
            // Act
            var result = await _service.DeriveCapabilitiesAsync(null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.DerivedCapabilities.Count);
            Assert.IsTrue(result.ProcessingWarnings.Count > 0);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithEmptyInput_ReturnsEmptyResult()
        {
            // Arrange
            var emptyText = string.Empty;
            var options = new DerivationOptions();

            // Act
            var result = await _service.DeriveCapabilitiesAsync(emptyText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccessful);
            Assert.AreEqual(0, result.DerivedCapabilities.Count);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithLlmFailure_ReturnsFailureResult()
        {
            // Arrange
            var requirementText = "Test requirement";
            var options = new DerivationOptions();

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<string>(), It.IsAny<DerivationOptions>(), It.IsAny<string>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("LLM service error"));

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.DerivedCapabilities.Count);
        }

        [TestMethod]
        public void Constructor_WithNullLlmService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new SystemCapabilityDerivationService(
                    null,
                    _mockLogger.Object,
                    _mockResponseParser.Object,
                    _mockAtpParser.Object,
                    _mockPromptBuilder.Object,
                    _mockTaxonomyValidator.Object,
                    _mockCapabilityAllocator.Object,
                    _mockMBSEClassifier.Object,
                    _mockQualityScorer.Object,
                    null));
        }

        [TestMethod]
        public void Constructor_WithValidDependencies_InitializesSuccessfully()
        {
            // Act
            var service = new SystemCapabilityDerivationService(
                _mockLlmService.Object,
                _mockLogger.Object,
                _mockResponseParser.Object,
                _mockAtpParser.Object,
                _mockPromptBuilder.Object,
                _mockTaxonomyValidator.Object,
                _mockCapabilityAllocator.Object,
                _mockMBSEClassifier.Object,
                _mockQualityScorer.Object,
                null);

            // Assert
            Assert.IsNotNull(service);
        }
    }
}