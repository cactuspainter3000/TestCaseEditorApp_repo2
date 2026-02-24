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
        private Mock<CapabilityDerivationPromptBuilder> _mockPromptBuilder;
        private Mock<TaxonomyValidator> _mockTaxonomyValidator;
        private Mock<ICapabilityAllocator> _mockCapabilityAllocator;
        private SystemCapabilityDerivationService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockLlmService = new Mock<ITextGenerationService>();
            _mockLogger = new Mock<ILogger<SystemCapabilityDerivationService>>();
            _mockResponseParser = new Mock<ResponseParserManager>();
            _mockAtpParser = new Mock<ATPStepParser>();
            _mockPromptBuilder = new Mock<CapabilityDerivationPromptBuilder>();
            _mockTaxonomyValidator = new Mock<TaxonomyValidator>();
            _mockCapabilityAllocator = new Mock<ICapabilityAllocator>();

            _service = new SystemCapabilityDerivationService(
                _mockLlmService.Object,
                _mockLogger.Object,
                _mockResponseParser.Object,
                _mockAtpParser.Object,
                _mockPromptBuilder.Object,
                _mockTaxonomyValidator.Object,
                _mockCapabilityAllocator.Object);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithValidInput_ReturnsSuccessfulResult()
        {
            // Arrange
            var requirementText = "The system shall verify JTAG boundary scan connectivity.";
            var options = new DerivationOptions { EnableQualityScoring = true };

            var expectedPrompt = "Analyze the following requirement for capabilities...";
            var llmResponse = "Valid LLM response";
            var parsedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability
                {
                    Id = "CAP-001", 
                    RequirementText = "JTAG Boundary Scan Verification",
                    DerivationRationale = "Verify JTAG connectivity",
                    TaxonomyCategory = "Hardware Test",
                    ConfidenceScore = 0.95
                }
            };

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SystemRequirementTaxonomy>(), It.IsAny<DerivationOptions>()))
                .Returns(expectedPrompt);

            _mockLlmService.Setup(x => x.GenerateTextAsync(expectedPrompt, It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);

            _mockResponseParser.Setup(x => x.ParseLlmResponse<List<DerivedCapability>>(llmResponse))
                .Returns(parsedCapabilities);

            _mockTaxonomyValidator.Setup(x => x.ValidateCapabilities(parsedCapabilities))
                .Returns(parsedCapabilities);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(1, result.DerivedCapabilities.Count);
            Assert.AreEqual("JTAG Boundary Scan Verification", result.DerivedCapabilities[0].RequirementText);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithNullInput_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.DeriveCapabilitiesAsync(null));
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SystemRequirementTaxonomy>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("LLM service error"));

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccessful);
            Assert.IsNotNull(result.ErrorMessage);
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
                    _mockCapabilityAllocator.Object));
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
                _mockCapabilityAllocator.Object);

            // Assert
            Assert.IsNotNull(service);
        }
    }
}