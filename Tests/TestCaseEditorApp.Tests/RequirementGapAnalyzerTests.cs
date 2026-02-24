using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Tests.Phase4Services
{
    [TestClass]
    public class RequirementGapAnalyzerTests
    {
        private RequirementGapAnalyzer _analyzer;

        [TestInitialize]
        public void Setup()
        {
            _analyzer = new RequirementGapAnalyzer();
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithMatchingCapabilitiesAndRequirements_ReturnsNoGaps()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    RequirementText = "JTAG Boundary Scan", 
                    TaxonomyCategory = "Hardware Test",
                    SourceATPStep = "REQ-001"
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    RequirementText = "Power Supply Test", 
                    TaxonomyCategory = "Power Test",
                    SourceATPStep = "REQ-002"
                }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "JTAG Test", Description = "Test JTAG boundary scan" },
                new Requirement { Item = "REQ-002", Name = "Power Test", Description = "Test power supply levels" }
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.TotalDerivedCapabilities);
            Assert.AreEqual(2, result.TotalExistingRequirements);
            Assert.IsNotNull(result.CoverageMetrics);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithUncoveredCapabilities_IdentifiesGaps()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    RequirementText = "JTAG Boundary Scan", 
                    TaxonomyCategory = "Hardware Test"
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    RequirementText = "Power Supply Test", 
                    TaxonomyCategory = "Power Test"
                },
                new DerivedCapability 
                { 
                    Id = "cap-3",
                    RequirementText = "Temperature Monitoring", 
                    TaxonomyCategory = "Environmental Test"
                }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "JTAG Test", Description = "Test JTAG boundary scan" },
                // Missing requirements for power and temperature capabilities
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.TotalDerivedCapabilities);
            Assert.AreEqual(1, result.TotalExistingRequirements);
            Assert.IsNotNull(result.UncoveredCapabilities);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithUntestedRequirements_IdentifiesUntestedReqs()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    Name = "JTAG Boundary Scan", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001"
                }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "JTAG Test", Description = "Test JTAG boundary scan" },
                new Requirement { Item = "REQ-002", Name = "Power Test", Description = "Test power supply levels" },
                new Requirement { Item = "REQ-003", Name = "Memory Test", Description = "Test system memory" }
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.TotalDerivedCapabilities);
            Assert.AreEqual(3, result.TotalExistingRequirements);
            Assert.IsNotNull(result.RequirementOverlaps);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithOverlappingCapabilities_IdentifiesOverlaps()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    RequirementText = "JTAG Test", 
                    TaxonomyCategory = "Hardware Test",
                    SourceATPStep = "REQ-001"
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    RequirementText = "JTAG Boundary Scan", 
                    TaxonomyCategory = "Hardware Test",
                    SourceATPStep = "REQ-001"
                }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "JTAG Test", Description = "Test JTAG boundary scan connectivity" }
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.RequirementOverlaps);
            Assert.AreEqual(2, result.TotalDerivedCapabilities);
            Assert.AreEqual(1, result.TotalExistingRequirements);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithEmptyCapabilities_ReturnsAllRequirementsAsUntested()
        {
            // Arrange
            var emptyCapabilities = new List<DerivedCapability>();
            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "Test 1", Description = "Description 1" },
                new Requirement { Item = "REQ-002", Name = "Test 2", Description = "Description 2" }
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(emptyCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.TotalDerivedCapabilities);
            Assert.AreEqual(2, result.TotalExistingRequirements);
            Assert.IsNotNull(result.UncoveredCapabilities);
            Assert.IsNotNull(result.CoverageMetrics);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithEmptyRequirements_ReturnsAllCapabilitiesAsUncovered()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability { Id = "cap-1", RequirementText = "Test Cap 1", TaxonomyCategory = "Testing" },
                new DerivedCapability { Id = "cap-2", RequirementText = "Test Cap 2", TaxonomyCategory = "Testing" }
            };
            var emptyRequirements = new List<Requirement>();

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, emptyRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.TotalDerivedCapabilities);
            Assert.AreEqual(0, result.TotalExistingRequirements);
            Assert.IsNotNull(result.UncoveredCapabilities);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithNullCapabilities_ThrowsArgumentNullException()
        {
            // Arrange
            var requirements = new List<Requirement>();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _analyzer.AnalyzeGapsAsync(null, requirements));
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithNullRequirements_ThrowsArgumentNullException()
        {
            // Arrange
            var capabilities = new List<DerivedCapability>();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _analyzer.AnalyzeGapsAsync(capabilities, null));
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithCategoryMismatch_IdentifiesSemanticGaps()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    RequirementText = "Hardware Diagnostic", 
                    TaxonomyCategory = "Hardware Test",
                    SourceATPStep = "REQ-001"
                }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement 
                { 
                    Item = "REQ-001", 
                    Name = "Software Verification", 
                    Description = "Verify software functionality" 
                }
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.TotalDerivedCapabilities);
            Assert.AreEqual(1, result.TotalExistingRequirements);
            Assert.IsNotNull(result.SpecificationInconsistencies);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithHighConfidenceCapabilities_PrioritizesCorrectly()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    RequirementText = "High Confidence Test", 
                    TaxonomyCategory = "Hardware Test",
                    ConfidenceScore = 0.95
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    RequirementText = "Low Confidence Test", 
                    TaxonomyCategory = "Hardware Test",
                    ConfidenceScore = 0.45
                }
            };

            var existingRequirements = new List<Requirement>();

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.TotalDerivedCapabilities);
            Assert.AreEqual(0, result.TotalExistingRequirements);
            Assert.IsNotNull(result.UncoveredCapabilities);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithComplexScenario_ReturnsComprehensiveAnalysis()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                // Covered capability
                new DerivedCapability 
                { 
                    Id = "cap-1",
                    RequirementText = "JTAG Test", 
                    TaxonomyCategory = "Hardware Test",
                    SourceATPStep = "REQ-001",
                    ConfidenceScore = 0.9
                },
                // Uncovered capability
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    RequirementText = "Power Supply Verification", 
                    TaxonomyCategory = "Power Test",
                    ConfidenceScore = 0.85
                },
                // Overlapping capability
                new DerivedCapability 
                { 
                    Id = "cap-3",
                    RequirementText = "JTAG Connectivity Check", 
                    TaxonomyCategory = "Hardware Test",
                    SourceATPStep = "REQ-001",
                    ConfidenceScore = 0.88
                }
            };

            var existingRequirements = new List<Requirement>
            {
                new Requirement { Item = "REQ-001", Name = "JTAG Test", Description = "Test JTAG functionality" },
                new Requirement { Item = "REQ-002", Name = "Memory Test", Description = "Test system memory" } // Untested
            };

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.TotalDerivedCapabilities);
            Assert.AreEqual(2, result.TotalExistingRequirements);
            Assert.IsNotNull(result.UncoveredCapabilities);
            Assert.IsNotNull(result.RequirementOverlaps);
            Assert.IsNotNull(result.CoverageMetrics);
        }
    }
}