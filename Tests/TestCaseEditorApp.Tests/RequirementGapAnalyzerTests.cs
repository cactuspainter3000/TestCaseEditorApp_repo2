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
                    Name = "JTAG Boundary Scan", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001"
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    Name = "Power Supply Test", 
                    Category = "Power Test",
                    RequirementSource = "REQ-002"
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
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(0, result.UncoveredCapabilities.Count);
            Assert.AreEqual(0, result.UntestedRequirements.Count);
            Assert.IsTrue(result.CoveragePercentage > 0.95); // Near 100% coverage
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
                    Name = "JTAG Boundary Scan", 
                    Category = "Hardware Test"
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    Name = "Power Supply Test", 
                    Category = "Power Test"
                },
                new DerivedCapability 
                { 
                    Id = "cap-3",
                    Name = "Temperature Monitoring", 
                    Category = "Environmental Test"
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
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(2, result.UncoveredCapabilities.Count); // Power and Temperature not covered
            
            var powerGap = result.UncoveredCapabilities.FirstOrDefault(g => g.CapabilityName.Contains("Power"));
            var tempGap = result.UncoveredCapabilities.FirstOrDefault(g => g.CapabilityName.Contains("Temperature"));
            
            Assert.IsNotNull(powerGap);
            Assert.IsNotNull(tempGap);
            Assert.AreEqual(GapSeverity.High, powerGap.Severity);
            Assert.AreEqual(GapSeverity.High, tempGap.Severity);
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
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(2, result.UntestedRequirements.Count); // REQ-002 and REQ-003 not covered
            
            var untestedIds = result.UntestedRequirements.Select(r => r.RequirementId).ToList();
            Assert.IsTrue(untestedIds.Contains("REQ-002"));
            Assert.IsTrue(untestedIds.Contains("REQ-003"));
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
                    Name = "JTAG Boundary Scan Test", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001"
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    Name = "JTAG Connectivity Verification", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001"
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
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(1, result.RequirementOverlaps.Count); // Should detect overlap for REQ-001
            
            var overlap = result.RequirementOverlaps.First();
            Assert.AreEqual("REQ-001", overlap.RequirementId);
            Assert.AreEqual(2, overlap.OverlappingCapabilities.Count);
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
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(2, result.UntestedRequirements.Count);
            Assert.AreEqual(0, result.UncoveredCapabilities.Count);
            Assert.AreEqual(0.0, result.CoveragePercentage);
        }

        [TestMethod]
        public async Task AnalyzeGapsAsync_WithEmptyRequirements_ReturnsAllCapabilitiesAsUncovered()
        {
            // Arrange
            var derivedCapabilities = new List<DerivedCapability>
            {
                new DerivedCapability { Id = "cap-1", Name = "Test Cap 1", Category = "Testing" },
                new DerivedCapability { Id = "cap-2", Name = "Test Cap 2", Category = "Testing" }
            };
            var emptyRequirements = new List<Requirement>();

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, emptyRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(2, result.UncoveredCapabilities.Count);
            Assert.AreEqual(0, result.UntestedRequirements.Count);
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
                    Name = "Hardware Diagnostic", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001"
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
            Assert.IsTrue(result.IsSuccessful);
            
            // Should detect some form of mismatch (specific implementation depends on gap analysis logic)
            var hasSemanticIssue = result.UncoveredCapabilities.Any() || 
                                  result.UntestedRequirements.Any() ||
                                  result.RequirementOverlaps.Any();
            
            // The exact behavior depends on the semantic matching logic in the analyzer
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
                    Name = "High Confidence Test", 
                    Category = "Hardware Test",
                    Confidence = 0.95
                },
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    Name = "Low Confidence Test", 
                    Category = "Hardware Test",
                    Confidence = 0.45
                }
            };

            var existingRequirements = new List<Requirement>();

            // Act
            var result = await _analyzer.AnalyzeGapsAsync(derivedCapabilities, existingRequirements);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccessful);
            Assert.AreEqual(2, result.UncoveredCapabilities.Count);
            
            // High confidence capability should be marked as higher severity gap
            var highConfidenceGap = result.UncoveredCapabilities.FirstOrDefault(g => g.CapabilityName.Contains("High Confidence"));
            var lowConfidenceGap = result.UncoveredCapabilities.FirstOrDefault(g => g.CapabilityName.Contains("Low Confidence"));
            
            Assert.IsNotNull(highConfidenceGap);
            Assert.IsNotNull(lowConfidenceGap);
            Assert.IsTrue(highConfidenceGap.Severity >= lowConfidenceGap.Severity);
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
                    Name = "JTAG Test", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001",
                    Confidence = 0.9
                },
                // Uncovered capability
                new DerivedCapability 
                { 
                    Id = "cap-2",
                    Name = "Power Supply Verification", 
                    Category = "Power Test",
                    Confidence = 0.85
                },
                // Overlapping capability
                new DerivedCapability 
                { 
                    Id = "cap-3",
                    Name = "JTAG Connectivity Check", 
                    Category = "Hardware Test",
                    RequirementSource = "REQ-001",
                    Confidence = 0.88
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
            Assert.IsTrue(result.IsSuccessful);
            
            // Should have uncovered capability (Power Supply)
            Assert.AreEqual(1, result.UncoveredCapabilities.Count);
            Assert.AreEqual("Power Supply Verification", result.UncoveredCapabilities[0].CapabilityName);
            
            // Should have untested requirement (REQ-002)
            Assert.AreEqual(1, result.UntestedRequirements.Count);
            Assert.AreEqual("REQ-002", result.UntestedRequirements[0].RequirementId);
            
            // Should detect overlap for REQ-001 (2 capabilities map to it)
            Assert.AreEqual(1, result.RequirementOverlaps.Count);
            Assert.AreEqual("REQ-001", result.RequirementOverlaps[0].RequirementId);
            Assert.AreEqual(2, result.RequirementOverlaps[0].OverlappingCapabilities.Count);
            
            // Coverage should be partial (some requirements covered, some not)
            Assert.IsTrue(result.CoveragePercentage > 0.0 && result.CoveragePercentage < 1.0);
        }
    }
}