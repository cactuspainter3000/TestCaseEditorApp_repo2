using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Prompts;

namespace TestCaseEditorApp.Tests.Phase4Services
{
    [TestClass]
    public class SystemCapabilityDerivationServiceTests
    {
        private Mock<ICapabilityDerivationPromptBuilder> _mockPromptBuilder;
        private Mock<ITextGenerationService> _mockLlmService;
        private Mock<ICapabilityAllocator> _mockCapabilityAllocator;
        private Mock<ITaxonomyValidator> _mockTaxonomyValidator;
        private SystemCapabilityDerivationService _service;

        [TestInitialize]
        public void Setup()
        {
            _mockPromptBuilder = new Mock<ICapabilityDerivationPromptBuilder>();
            _mockLlmService = new Mock<ITextGenerationService>();
            _mockCapabilityAllocator = new Mock<ICapabilityAllocator>();
            _mockTaxonomyValidator = new Mock<ITaxonomyValidator>();

            _service = new SystemCapabilityDerivationService(
                _mockPromptBuilder.Object,
                _mockLlmService.Object,
                _mockCapabilityAllocator.Object,
                _mockTaxonomyValidator.Object);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithValidInput_ReturnsSuccessfulResult()
        {
            // Arrange
            var requirementText = "The system shall verify JTAG boundary scan connectivity.";
            var options = new DerivationOptions { EnableQualityScoring = true };

            var expectedPrompt = "Analyze the following requirement for capabilities...";
            var llmResponse = @"[{""Name"":""JTAG Boundary Scan Verification"",""Description"":""Verify JTAG connectivity"",""Category"":""Hardware Test"",""Confidence"":0.95}]";

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                requirementText, null, null, options))
                .Returns(expectedPrompt);

            _mockLlmService.Setup(x => x.GenerateAsync(expectedPrompt, It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(1, result.DerivedCapabilities.Count);
            Assert.AreEqual("JTAG Boundary Scan Verification", result.DerivedCapabilities[0].Name);
            Assert.AreEqual("Hardware Test", result.DerivedCapabilities[0].Category);
            Assert.AreEqual(0.95, result.DerivedCapabilities[0].Confidence, 0.01);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithEmptyInput_ReturnsFailedResult()
        {
            // Arrange
            var emptyText = "";
            var options = new DerivationOptions();

            // Act
            var result = await _service.DeriveCapabilitiesAsync(emptyText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccessful);
            Assert.AreEqual(0, result.DerivedCapabilities.Count);
            Assert.IsTrue(result.ProcessingWarnings.Any(w => w.Contains("empty")));
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithLlmFailure_HandlesGracefully()
        {
            // Arrange
            var requirementText = "Test requirement";
            var options = new DerivationOptions();

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("LLM service unavailable"));

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccessful);
            Assert.IsTrue(result.ProcessingWarnings.Any(w => w.Contains("LLM service unavailable")));
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithInvalidJson_HandlesGracefully()
        {
            // Arrange
            var requirementText = "Test requirement";
            var options = new DerivationOptions();
            var invalidJsonResponse = "This is not valid JSON";

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(invalidJsonResponse);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccessful);
            Assert.IsTrue(result.ProcessingWarnings.Any(w => w.Contains("JSON") || w.Contains("parse")));
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithQualityScoring_CalculatesQualityScore()
        {
            // Arrange
            var requirementText = "The system shall perform automated tests.";
            var options = new DerivationOptions { EnableQualityScoring = true };

            var llmResponse = @"[{""Name"":""Automated Testing"",""Description"":""Perform tests automatically"",""Category"":""Testing"",""Confidence"":0.9}]";

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.IsTrue(result.QualityScore > 0); // Should have calculated a quality score
            Assert.AreEqual(1, result.DerivedCapabilities.Count);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithTaxonomyValidation_ValidatesCapabilities()
        {
            // Arrange
            var requirementText = "Test requirement";
            var options = new DerivationOptions { EnableTaxonomyValidation = true };

            var llmResponse = @"[{""Name"":""Test Capability"",""Description"":""Test description"",""Category"":""Testing"",""Confidence"":0.8}]";

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);

            _mockTaxonomyValidator.Setup(x => x.ValidateCapabilities(
                It.IsAny<List<DerivedCapability>>(), It.IsAny<TaxonomyValidationOptions>()))
                .Returns(new List<ValidationIssue>());

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            _mockTaxonomyValidator.Verify(x => x.ValidateCapabilities(
                It.IsAny<List<DerivedCapability>>(), It.IsAny<TaxonomyValidationOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithAllocationEnabled_AllocatesCapabilities()
        {
            // Arrange
            var requirementText = "Test requirement";
            var options = new DerivationOptions { EnableCapabilityAllocation = true };

            var llmResponse = @"[{""Name"":""Test Capability"",""Description"":""Test description"",""Category"":""Testing"",""Confidence"":0.8}]";
            var allocationResult = new AllocationResult 
            { 
                IsSuccessful = true, 
                AllocatedCapabilities = new List<AllocatedCapability>()
            };

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(llmResponse);

            _mockCapabilityAllocator.Setup(x => x.AllocateCapabilitiesAsync(
                It.IsAny<IEnumerable<DerivedCapability>>(), It.IsAny<CapabilityAllocationOptions>()))
                .ReturnsAsync(allocationResult);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(requirementText, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            _mockCapabilityAllocator.Verify(x => x.AllocateCapabilitiesAsync(
                It.IsAny<IEnumerable<DerivedCapability>>(), It.IsAny<CapabilityAllocationOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithCancellation_ThrowsOperationCancelledException()
        {
            // Arrange
            var requirementText = "Test requirement";
            var options = new DerivationOptions();
            var cancellationToken = new CancellationToken(true); // Already cancelled

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => _service.DeriveCapabilitiesAsync(requirementText, options, cancellationToken));
        }

        [TestMethod]
        public void Constructor_WithNullPromptBuilder_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new SystemCapabilityDerivationService(null, _mockLlmService.Object, 
                    _mockCapabilityAllocator.Object, _mockTaxonomyValidator.Object));
        }

        [TestMethod]
        public void Constructor_WithNullLlmService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new SystemCapabilityDerivationService(_mockPromptBuilder.Object, null, 
                    _mockCapabilityAllocator.Object, _mockTaxonomyValidator.Object));
        }

        [TestMethod]
        public async Task DeriveCapabilitiesAsync_WithComplexRequirement_ParsesMultipleCapabilities()
        {
            // Arrange
            var complexRequirement = "The system shall verify JTAG boundary scan connectivity and perform power supply tests at 3.3V and 5V levels.";
            var options = new DerivationOptions();

            var multiCapabilityResponse = @"[
                {""Name"":""JTAG Boundary Scan Verification"",""Description"":""Verify JTAG connectivity"",""Category"":""Hardware Test"",""Confidence"":0.95},
                {""Name"":""Power Supply Verification"",""Description"":""Test 3.3V and 5V power levels"",""Category"":""Power Test"",""Confidence"":0.90}
            ]";

            _mockPromptBuilder.Setup(x => x.BuildDerivationPrompt(
                It.IsAny<string>(), It.IsAny<ParsedATPStep>(), It.IsAny<List<DerivedCapability>>(), It.IsAny<DerivationOptions>()))
                .Returns("test prompt");

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(multiCapabilityResponse);

            // Act
            var result = await _service.DeriveCapabilitiesAsync(complexRequirement, options);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(2, result.DerivedCapabilities.Count);
            
            var jtagCapability = result.DerivedCapabilities.FirstOrDefault(c => c.Name.Contains("JTAG"));
            var powerCapability = result.DerivedCapabilities.FirstOrDefault(c => c.Name.Contains("Power"));
            
            Assert.IsNotNull(jtagCapability);
            Assert.IsNotNull(powerCapability);
            Assert.AreEqual("Hardware Test", jtagCapability.Category);
            Assert.AreEqual("Power Test", powerCapability.Category);
        }
    }
}