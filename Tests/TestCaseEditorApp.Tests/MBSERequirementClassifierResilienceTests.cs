using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.Models;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp.Tests.Phase4Services
{
    /// <summary>
    /// Comprehensive resilience tests for MBSE requirement classification.
    /// Tests the system's ability to distinguish true system-level requirements
    /// from component-level, implementation constraints, and malformed requirements.
    /// </summary>
    [TestClass]
    public class MBSERequirementClassifierResilienceTests
    {
        private Mock<ITextGenerationService> _mockLlmService;
        private Mock<ILogger<MBSERequirementClassifier>> _mockLogger;
        private MBSERequirementClassifier _classifier;

        [TestInitialize]
        public void Setup()
        {
            _mockLlmService = new Mock<ITextGenerationService>();
            _mockLogger = new Mock<ILogger<MBSERequirementClassifier>>();
            _classifier = new MBSERequirementClassifier(_mockLlmService.Object, _mockLogger.Object);
        }

        #region MBSE Criteria Tests - The Core Brittleness Tests

        [TestMethod]
        public async Task MBSEClassification_SystemLevelRequirements_CorrectlyIdentified()
        {
            // Test cases that SHOULD be classified as system-level
            var systemLevelRequirements = new[]
            {
                ("The system shall reject input signal noise up to 5ms duration at discrete input interfaces", "Boundary-based performance"),
                ("The system shall respond to external commands within 100ms via the primary interface", "Black-box verifiable timing"),
                ("The system shall operate continuously from -40°C to +85°C ambient temperature", "Environmental constraint at boundary"),
                ("The system shall detect and report interface faults within 2 seconds of occurrence", "Boundary-observable fault detection"),
                ("The system shall maintain output accuracy to ±0.1% under all specified operating conditions", "System-level performance specification"),
                ("The system shall accept configuration data via approved aircraft data bus protocols", "System interface requirement")
            };

            foreach (var (requirement, category) in systemLevelRequirements)
            {
                // Mock LLM response for system-level requirement
                _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(GenerateSystemLevelMockResponse(0.85));

                var capability = new DerivedCapability { RequirementText = requirement };
                var result = await _classifier.ClassifyRequirementAsync(capability);

                Assert.IsTrue(result.IsSystemLevelRequirement, 
                    $"Failed to identify system-level requirement ({category}): {requirement}");
                Assert.IsTrue(result.OverallMBSEScore >= 0.7, 
                    $"MBSE score too low for {category}: {result.OverallMBSEScore}");
            }
        }

        [TestMethod]
        public async Task MBSEClassification_ComponentLevelRequirements_CorrectlyRejected()
        {
            // Test cases that should NOT be classified as system-level
            var componentLevelRequirements = new[]
            {
                ("The FPGA shall debounce input signals by 10ms", "Component-specific implementation"),
                ("The PCB shall be implemented using 4-layer stackup", "Design/manufacturing constraint"),  
                ("The software shall use C++ programming language", "Implementation technology choice"),
                ("The JTAG controller shall support boundary scan chain", "Component-level functionality"),
                ("The power supply circuit shall provide 3.3V ±5%", "Internal circuit requirement"),
                ("The microcontroller shall execute at 100MHz clock frequency", "Component specification")
            };

            foreach (var (requirement, category) in componentLevelRequirements)
            {
                // Mock LLM response for component-level requirement
                _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(GenerateComponentLevelMockResponse(0.35));

                var capability = new DerivedCapability { RequirementText = requirement };
                var result = await _classifier.ClassifyRequirementAsync(capability);

                Assert.IsFalse(result.IsSystemLevelRequirement, 
                    $"Incorrectly classified as system-level ({category}): {requirement}");
                Assert.IsTrue(result.OverallMBSEScore < 0.7, 
                    $"MBSE score too high for {category}: {result.OverallMBSEScore}");
                Assert.IsTrue(result.BlockingIssues.Count > 0, 
                    $"Should have blocking issues for {category}");
            }
        }

        [TestMethod]
        public async Task BlackBoxVerificationTest_SystemRequirements_PassTest()
        {
            var blackBoxVerifiableRequirements = new[]
            {
                "The system shall respond to power-on within 5 seconds",
                "The system shall maintain output voltage to ±1% accuracy",
                "The system shall reject commands with invalid checksums"
            };

            foreach (var requirement in blackBoxVerifiableRequirements)
            {
                _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(@"{""passesBlackBoxTest"": true, ""reasoning"": ""Verifiable externally""}");

                var result = await _classifier.PassesBlackBoxVerificationTestAsync(requirement);
                
                Assert.IsTrue(result, $"Should pass black-box test: {requirement}");
            }
        }

        [TestMethod]
        public async Task BlackBoxVerificationTest_ComponentRequirements_FailTest()
        {
            var internalRequirements = new[]
            {
                "The CPU shall execute instructions at 2GHz",
                "The FPGA logic shall implement state machine X",
                "The internal bus shall operate at 100MHz"
            };

            foreach (var requirement in internalRequirements)
            {
                _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(@"{""passesBlackBoxTest"": false, ""reasoning"": ""Requires internal inspection""}");

                var result = await _classifier.PassesBlackBoxVerificationTestAsync(requirement);
                
                Assert.IsFalse(result, $"Should fail black-box test: {requirement}");
            }
        }

        #endregion

        #region Brittleness & Robustness Tests

        [TestMethod]
        public async Task MBSEClassification_WithMalformedInput_HandlesGracefully()
        {
            var malformedInputs = new[]
            {
                null,
                "",
                "   ",
                "Incomplete sentence without verb",
                "Random text with no requirement structure whatsoever and no clear action or specification"
            };

            foreach (var malformed in malformedInputs)
            {
                var capability = new DerivedCapability { RequirementText = malformed };
                var result = await _classifier.ClassifyRequirementAsync(capability);

                Assert.IsFalse(result.IsSystemLevelRequirement, 
                    $"Should reject malformed input: '{malformed}'");
                Assert.IsTrue(result.BlockingIssues.Count > 0, 
                    $"Should have blocking issues for malformed input");
            }
        }

        [TestMethod]
        public async Task MBSEClassification_WithLLMFailure_ReturnsGracefulFailure()
        {
            // Simulate LLM service failure
            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .ThrowsAsync(new TimeoutException("LLM service timeout"));

            var capability = new DerivedCapability 
            { 
                RequirementText = "The system shall respond to input within 100ms" 
            };

            var result = await _classifier.ClassifyRequirementAsync(capability);

            Assert.IsFalse(result.IsSystemLevelRequirement);
            Assert.IsTrue(result.BlockingIssues.Any(issue => issue.Contains("Classification error")));
        }

        [TestMethod]
        public async Task MBSEClassification_WithInvalidJSONResponse_HandlesGracefully()
        {
            // Simulate malformed LLM response
            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("This is not valid JSON at all");

            var capability = new DerivedCapability 
            { 
                RequirementText = "The system shall verify connectivity" 
            };

            var result = await _classifier.ClassifyRequirementAsync(capability);

            Assert.IsFalse(result.IsSystemLevelRequirement);
            Assert.IsTrue(result.BlockingIssues.Any(issue => issue.Contains("parse")));
        }

        [TestMethod]
        public async Task MBSEFilter_ConsistentClassification_AcrossMultipleCalls()
        {
            // Test consistency: same requirement should get same classification
            var requirement = "The system shall respond to external commands within 100ms";
            var capability = new DerivedCapability { RequirementText = requirement };

            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(GenerateSystemLevelMockResponse(0.82));

            var results = new List<MBSEClassificationResult>();
            for (int i = 0; i < 5; i++)
            {
                results.Add(await _classifier.ClassifyRequirementAsync(capability));
            }

            // All results should be consistent
            var allSystemLevel = results.All(r => r.IsSystemLevelRequirement);
            var scoreVariance = CalculateVariance(results.Select(r => r.OverallMBSEScore));

            Assert.IsTrue(allSystemLevel, "Classification should be consistent across calls");
            Assert.IsTrue(scoreVariance < 0.01, $"Score variance too high: {scoreVariance}");
        }

        #endregion

        #region Integration & Performance Tests

        [TestMethod]
        public async Task MBSEFilter_LargeCapabilitySet_PerformsWithinTimeout()
        {
            var largeCapabilitySet = GenerateLargeCapabilitySet(50);
            
            _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(GenerateSystemLevelMockResponse(0.8));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await _classifier.FilterToSystemLevelRequirementsAsync(largeCapabilitySet);
            stopwatch.Stop();

            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMinutes(2), 
                $"Filtering took too long: {stopwatch.Elapsed}");
            Assert.AreEqual(50, result.Statistics.TotalCandidates);
            Assert.IsTrue(result.Statistics.SystemLevelCount > 0);
        }

        [TestMethod]
        public async Task MBSEFilter_MixedRequirementTypes_CorrectStatistics()
        {
            var mixedCapabilities = new[]
            {
                new DerivedCapability { RequirementText = "The system shall respond within 100ms" }, // System
                new DerivedCapability { RequirementText = "The FPGA shall debounce inputs" },         // Component  
                new DerivedCapability { RequirementText = "The PCB shall use 4 layers" },            // Implementation
                new DerivedCapability { RequirementText = "The system shall operate -40°C to +85°C" } // System
            };

            // Mock responses based on requirement type
            _mockLlmService.SetupSequence(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(GenerateSystemLevelMockResponse(0.85))      // System requirement
                .ReturnsAsync(GenerateComponentLevelMockResponse(0.25))   // Component requirement
                .ReturnsAsync(GenerateComponentLevelMockResponse(0.15))   // Implementation constraint
                .ReturnsAsync(GenerateSystemLevelMockResponse(0.90));     // System requirement

            var result = await _classifier.FilterToSystemLevelRequirementsAsync(mixedCapabilities);

            Assert.AreEqual(4, result.Statistics.TotalCandidates);
            Assert.AreEqual(2, result.Statistics.SystemLevelCount);
            Assert.IsTrue(result.Statistics.SystemLevelPercentage == 50.0);
        }

        #endregion

        #region Helper Methods

        private string GenerateSystemLevelMockResponse(double score)
        {
            return $@"{{
              ""overallMBSEScore"": {score},
              ""isSystemLevel"": true,
              ""criteriaScores"": {{
                ""boundaryBased"": {score},
                ""implementationAgnostic"": {score},
                ""systemVerifiable"": {score},
                ""stakeholderTraceable"": {score - 0.1},
                ""allocatable"": {score},
                ""contextComplete"": {score}
              }},
              ""rationale"": ""Meets all MBSE system-level criteria"",
              ""blockingIssues"": [],
              ""improvements"": []
            }}";
        }

        private string GenerateComponentLevelMockResponse(double score)
        {
            return $@"{{
              ""overallMBSEScore"": {score},
              ""isSystemLevel"": false,
              ""criteriaScores"": {{
                ""boundaryBased"": {score},
                ""implementationAgnostic"": 0.1,
                ""systemVerifiable"": 0.2,
                ""stakeholderTraceable"": {score},
                ""allocatable"": 0.3,
                ""contextComplete"": {score}
              }},
              ""rationale"": ""Component-level or implementation constraint"",
              ""blockingIssues"": [""Specifies internal implementation"", ""Not verifiable at system boundary""],
              ""improvements"": [""Rewrite to focus on external behavior"", ""Remove implementation details""]
            }}";
        }

        private List<DerivedCapability> GenerateLargeCapabilitySet(int count)
        {
            var capabilities = new List<DerivedCapability>();
            
            for (int i = 0; i < count; i++)
            {
                capabilities.Add(new DerivedCapability
                {
                    RequirementText = $"Test requirement {i}: The system shall perform function {i}",
                    SourceATPStep = $"ATP-{i:000}",
                    TaxonomyCategory = "Test Category"
                });
            }
            
            return capabilities;
        }

        private double CalculateVariance(IEnumerable<double> values)
        {
            var mean = values.Average();
            var squaredDifferences = values.Select(val => Math.Pow(val - mean, 2));
            return squaredDifferences.Average();
        }

        #endregion
    }

    /// <summary>
    /// Additional brittleness tests specifically for the MBSE criteria evaluation.
    /// Tests edge cases and boundary conditions for each of the 6 MBSE criteria.
    /// </summary>
    [TestClass]
    public class MBSECriteriaBrittlenessTests
    {
        private Mock<ITextGenerationService> _mockLlmService;
        private MBSERequirementClassifier _classifier;

        [TestInitialize]
        public void Setup()
        {
            _mockLlmService = new Mock<ITextGenerationService>();
            var mockLogger = new Mock<ILogger<MBSERequirementClassifier>>();
            _classifier = new MBSERequirementClassifier(_mockLlmService.Object, mockLogger.Object);
        }

        [TestMethod]
        public async Task BoundaryBasedCriteria_EdgeCases_CorrectlyClassified()
        {
            var edgeCases = new[]
            {
                ("The system interface shall provide 5A current capability", true, "Clear boundary specification"),
                ("The internal power rail shall provide 5A", false, "Internal, not boundary-based"),
                ("The system shall interface with external sensors", true, "Boundary interaction"),
                ("The CPU cache shall store 1MB of data", false, "Internal component storage")
            };

            foreach (var (requirement, shouldPassBoundary, description) in edgeCases)
            {
                var boundaryScore = shouldPassBoundary ? 0.9 : 0.2;
                var mockResponse = GenerateCriteriaSpecificResponse(boundaryScore, 0.5, 0.5, 0.5, 0.5, 0.5);
                
                _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(mockResponse);

                var capability = new DerivedCapability { RequirementText = requirement };
                var result = await _classifier.ClassifyRequirementAsync(capability);

                if (shouldPassBoundary)
                {
                    Assert.IsTrue(result.CriteriaScores.BoundaryBasedScore > 0.7, 
                        $"Should pass boundary test ({description}): {requirement}");
                }
                else
                {
                    Assert.IsTrue(result.CriteriaScores.BoundaryBasedScore < 0.5, 
                        $"Should fail boundary test ({description}): {requirement}");
                }
            }
        }

        [TestMethod]
        public async Task ImplementationAgnosticCriteria_EdgeCases_CorrectlyClassified()
        {
            var edgeCases = new[]
            {
                ("The system shall process data within 100ms", true, "Performance without implementation"),
                ("The system shall use ARINC-429 protocol", false, "Specific protocol implementation"),
                ("The system shall support approved data bus protocols", true, "Implementation-agnostic interface"),
                ("The software shall be written in Ada", false, "Specific implementation language")
            };

            foreach (var (requirement, shouldPassAgnostic, description) in edgeCases)
            {
                var agnosticScore = shouldPassAgnostic ? 0.9 : 0.1;
                var mockResponse = GenerateCriteriaSpecificResponse(0.5, agnosticScore, 0.5, 0.5, 0.5, 0.5);
                
                _mockLlmService.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(mockResponse);

                var capability = new DerivedCapability { RequirementText = requirement };
                var result = await _classifier.ClassifyRequirementAsync(capability);

                if (shouldPassAgnostic)
                {
                    Assert.IsTrue(result.CriteriaScores.ImplementationAgnosticScore > 0.7, 
                        $"Should pass agnostic test ({description}): {requirement}");
                }
                else
                {
                    Assert.IsTrue(result.CriteriaScores.ImplementationAgnosticScore < 0.3, 
                        $"Should fail agnostic test ({description}): {requirement}");
                }
            }
        }

        private string GenerateCriteriaSpecificResponse(double boundary, double agnostic, double verifiable, 
            double traceable, double allocatable, double complete)
        {
            var overallScore = (boundary + agnostic + verifiable + traceable + allocatable + complete) / 6.0;
            
            return $@"{{
              ""overallMBSEScore"": {overallScore:F2},
              ""isSystemLevel"": {(overallScore > 0.7 ? "true" : "false")},
              ""criteriaScores"": {{
                ""boundaryBased"": {boundary:F2},
                ""implementationAgnostic"": {agnostic:F2},
                ""systemVerifiable"": {verifiable:F2},
                ""stakeholderTraceable"": {traceable:F2},
                ""allocatable"": {allocatable:F2},
                ""contextComplete"": {complete:F2}
              }},
              ""rationale"": ""Test criteria evaluation"",
              ""blockingIssues"": [],
              ""improvements"": []
            }}";
        }
    }
}