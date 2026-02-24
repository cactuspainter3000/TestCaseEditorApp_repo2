using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.Tests.Phase4Models
{
    [TestClass]
    public class Phase4ModelsTests
    {
        [TestClass]
        public class DerivedCapabilityTests
        {
            [TestMethod]
            public void DerivedCapability_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var capability = new DerivedCapability();

                // Assert
                Assert.IsNotNull(capability.Id);
                Assert.AreEqual(string.Empty, capability.SourceATPStep);
                Assert.AreEqual(string.Empty, capability.RequirementText);
                Assert.AreEqual(string.Empty, capability.TaxonomyCategory);
                Assert.AreEqual(string.Empty, capability.TaxonomySubcategory);
                Assert.AreEqual(string.Empty, capability.DerivationRationale);
            }

            [TestMethod]
            public void DerivedCapability_SetProperties_StoresValuesCorrectly()
            {
                // Arrange
                var capability = new DerivedCapability();

                // Act
                capability.RequirementText = "JTAG Boundary Scan";
                capability.DerivationRationale = "Verifies JTAG boundary scan chain connectivity";
                capability.TaxonomyCategory = "Hardware Test";
                capability.SourceATPStep = "REQ-001";

                // Assert
                Assert.AreEqual("JTAG Boundary Scan", capability.RequirementText);
                Assert.AreEqual("Verifies JTAG boundary scan chain connectivity", capability.DerivationRationale);
                Assert.AreEqual("Hardware Test", capability.TaxonomyCategory);
                Assert.AreEqual("REQ-001", capability.SourceATPStep);
            }

            [TestMethod]
            public void DerivedCapability_ConfidenceRange_AcceptsValidValues()
            {
                // Arrange
                var capability = new DerivedCapability();

                // Act & Assert
                capability.ConfidenceScore = 0.0;
                Assert.AreEqual(0.0, capability.ConfidenceScore);

                capability.ConfidenceScore = 0.5;
                Assert.AreEqual(0.5, capability.ConfidenceScore);

                capability.ConfidenceScore = 1.0;
                Assert.AreEqual(1.0, capability.ConfidenceScore);
            }
        }

        [TestClass]
        public class DerivationResultTests
        {
            [TestMethod]
            public void DerivationResult_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var result = new DerivationResult();

                // Assert
                Assert.IsFalse(result.IsSuccessful);
                Assert.AreEqual(0.0, result.QualityScore);
                Assert.IsNotNull(result.DerivedCapabilities);
                Assert.AreEqual(0, result.DerivedCapabilities.Count);
                Assert.IsNotNull(result.ProcessingWarnings);
                Assert.AreEqual(0, result.ProcessingWarnings.Count);
            }

            [TestMethod]
            public void DerivationResult_WithCapabilities_CalculatesCorrectCount()
            {
                // Arrange
                var result = new DerivationResult();
                var capabilities = new List<DerivedCapability>
                {
                    new DerivedCapability { RequirementText = "Cap 1", ConfidenceScore = 0.8 },
                    new DerivedCapability { RequirementText = "Cap 2", ConfidenceScore = 0.9 }
                };

                // Act
                result.DerivedCapabilities = capabilities;

                // Assert
                Assert.AreEqual(2, result.DerivedCapabilities.Count);
            }

            [TestMethod]
            public void DerivationResult_WithWarnings_StoresWarningsCorrectly()
            {
                // Arrange
                var result = new DerivationResult();
                var warnings = new List<string> { "Warning 1", "Warning 2" };

                // Act
                result.ProcessingWarnings = warnings;

                // Assert
                Assert.AreEqual(2, result.ProcessingWarnings.Count);
                Assert.IsTrue(result.ProcessingWarnings.Contains("Warning 1"));
                Assert.IsTrue(result.ProcessingWarnings.Contains("Warning 2"));
            }
        }

        [TestClass]
        public class CoverageMetricsTests
        {
            [TestMethod]
            public void CoverageMetrics_DefaultValues_AreZero()
            {
                // Act
                var metrics = new CoverageMetrics();

                // Assert
                Assert.AreEqual(0.0, metrics.QualityScore);
                Assert.AreEqual(0.0, metrics.OverallCoveragePercentage);
                Assert.AreEqual(0, metrics.TotalCapabilities);
                Assert.AreEqual(0, metrics.CoveredCapabilities);
                Assert.AreEqual(0, metrics.UncoveredCapabilities);
            }

            [TestMethod]
            public void CoverageMetrics_SetValidScores_StoresCorrectly()
            {
                // Arrange
                var metrics = new CoverageMetrics();

                // Act
                metrics.QualityScore = 0.85;
                metrics.OverallCoveragePercentage = 0.90;
                metrics.TotalCapabilities = 10;
                metrics.CoveredCapabilities = 8;
                metrics.UncoveredCapabilities = 2;

                // Assert
                Assert.AreEqual(0.85, metrics.QualityScore);
                Assert.AreEqual(0.90, metrics.OverallCoveragePercentage);
                Assert.AreEqual(10, metrics.TotalCapabilities);
                Assert.AreEqual(8, metrics.CoveredCapabilities);
                Assert.AreEqual(2, metrics.UncoveredCapabilities);
            }
        }

        [TestClass]
        public class GapAnalysisResultTests
        {
            [TestMethod]
            public void GapAnalysisResult_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var result = new GapAnalysisResult();

                // Assert
                Assert.IsTrue(result.Success);
                Assert.IsNotNull(result.UncoveredCapabilities);
                Assert.IsNotNull(result.RequirementOverlaps);
                Assert.AreEqual(0, result.UncoveredCapabilities.Count);
                Assert.AreEqual(0, result.RequirementOverlaps.Count);
            }

            [TestMethod]
            public void GapAnalysisResult_WithFullCoverage_ShowsCorrectPercentage()
            {
                // Arrange
                var result = new GapAnalysisResult();

                // Act
                result.Success = true;

                // Assert
                Assert.IsTrue(result.Success);
            }
        }

        [TestClass]
        public class UncoveredCapabilityTests
        {
            [TestMethod]
            public void UncoveredCapability_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var uncovered = new UncoveredCapability();

                // Assert
                Assert.IsNotNull(uncovered.Capability);
                Assert.AreEqual(GapSeverity.Low, uncovered.Severity);
                Assert.AreEqual(string.Empty, uncovered.Recommendation);
            }

            [TestMethod]
            public void UncoveredCapability_SetProperties_StoresCorrectly()
            {
                // Arrange
                var uncovered = new UncoveredCapability();

                // Act
                uncovered.Capability = new DerivedCapability { RequirementText = "Power Supply Test" };
                uncovered.Severity = GapSeverity.High;
                uncovered.Recommendation = "Create power supply test requirement";

                // Assert
                Assert.AreEqual("Power Supply Test", uncovered.Capability.RequirementText);
                Assert.AreEqual(GapSeverity.High, uncovered.Severity);
                Assert.AreEqual("Create power supply test requirement", uncovered.Recommendation);
            }
        }

        [TestClass]
        public class RequirementDerivationAnalysisTests
        {
            [TestMethod]
            public void RequirementDerivationAnalysis_DefaultValues_AreInitializedCorrectly()
            {
                // Arrange
                var requirement = new Requirement { Item = "REQ-001", Name = "Test" };

                // Act
                var analysis = new RequirementDerivationAnalysis
                {
                    AnalyzedRequirement = requirement
                };

                // Assert
                Assert.IsFalse(analysis.HasATPContent);
                Assert.AreEqual(0.0, analysis.ATPDetectionConfidence);
                Assert.AreEqual(0.0, analysis.DerivationQuality);
                Assert.IsNotNull(analysis.DerivedCapabilities);
                Assert.IsNotNull(analysis.DerivationIssues);
                Assert.IsNotNull(analysis.Recommendations);
                Assert.AreEqual(0, analysis.DerivedCapabilities.Count);
                Assert.AreEqual(0, analysis.DerivationIssues.Count);
                Assert.AreEqual(0, analysis.Recommendations.Count);
            }

            [TestMethod]
            public void RequirementDerivationAnalysis_WithATPContent_SetsPropertiesCorrectly()
            {
                // Arrange
                var requirement1 = new Requirement { Item = "REQ-001", Name = "Test" };
                var requirement2 = new Requirement { Item = "REQ-002", Name = "Test2" };
                var analysis = new RequirementDerivationAnalysis
                {
                    AnalyzedRequirement = requirement1
                };

                // Act
                analysis.AnalyzedRequirement = requirement2;
                analysis.HasATPContent = true;
                analysis.ATPDetectionConfidence = 0.85;
                analysis.DerivationQuality = 0.90;

                // Assert
                Assert.AreEqual(requirement2, analysis.AnalyzedRequirement);
                Assert.IsTrue(analysis.HasATPContent);
                Assert.AreEqual(0.85, analysis.ATPDetectionConfidence);
                Assert.AreEqual(0.90, analysis.DerivationQuality);
            }
        }

        [TestClass]
        public class TestingWorkflowValidationResultTests
        {
            [TestMethod]
            public void TestingWorkflowValidationResult_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var result = new TestingWorkflowValidationResult();

                // Assert
                Assert.IsFalse(result.IsValid);
                Assert.AreEqual(0.0, result.OverallScore);
                Assert.IsNull(result.CoverageAnalysis);
                Assert.IsNotNull(result.Issues);
                Assert.IsNotNull(result.Recommendations);
                Assert.AreEqual(0, result.Issues.Count);
                Assert.AreEqual(0, result.Recommendations.Count);
            }

            [TestMethod]
            public void TestingWorkflowValidationResult_WithHighScore_IsValid()
            {
                // Arrange
                var result = new TestingWorkflowValidationResult();

                // Act
                result.OverallScore = 0.85;
                result.IsValid = true;

                // Assert
                Assert.IsTrue(result.IsValid);
                Assert.AreEqual(0.85, result.OverallScore);
            }
        }

        [TestClass]
        public class TestingCoverageAnalysisTests
        {
            [TestMethod]
            public void TestingCoverageAnalysis_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var analysis = new TestingCoverageAnalysis();

                // Assert
                Assert.AreEqual(0.0, analysis.CoveragePercentage);
                Assert.IsNotNull(analysis.UncoveredRequirements);
                Assert.IsNotNull(analysis.TestingGaps);
                Assert.AreEqual(0, analysis.UncoveredRequirements.Count);
                Assert.AreEqual(0, analysis.TestingGaps.Count);
            }

            [TestMethod]
            public void TestingCoverageAnalysis_WithCoverageData_StoresCorrectly()
            {
                // Arrange
                var analysis = new TestingCoverageAnalysis();

                // Act
                analysis.CoveragePercentage = 0.75;
                analysis.UncoveredRequirements.Add("REQ-001");
                analysis.UncoveredRequirements.Add("REQ-002");
                analysis.TestingGaps.Add("Missing power supply tests");

                // Assert
                Assert.AreEqual(0.75, analysis.CoveragePercentage);
                Assert.AreEqual(2, analysis.UncoveredRequirements.Count);
                Assert.AreEqual(1, analysis.TestingGaps.Count);
                Assert.IsTrue(analysis.UncoveredRequirements.Contains("REQ-001"));
                Assert.IsTrue(analysis.TestingGaps.Contains("Missing power supply tests"));
            }
        }

        [TestClass]
        public class BatchAnalysisOptionsTests
        {
            [TestMethod]
            public void BatchAnalysisOptions_DefaultValues_AreReasonable()
            {
                // Act
                var options = new BatchAnalysisOptions();

                // Assert
                Assert.AreEqual(5, options.MaxConcurrency); // Default is 5
                Assert.IsTrue(options.ContinueOnFailure); // Good for batch processing
                Assert.AreEqual(TimeSpan.FromMinutes(2), options.AnalysisTimeout); // 2 minutes default
            }

            [TestMethod]
            public void BatchAnalysisOptions_SetCustomValues_StoresCorrectly()
            {
                // Arrange
                var options = new BatchAnalysisOptions();

                // Act
                options.MaxConcurrency = 10;
                options.ContinueOnFailure = false;
                options.AnalysisTimeout = TimeSpan.FromSeconds(30);

                // Assert
                Assert.AreEqual(10, options.MaxConcurrency);
                Assert.IsFalse(options.ContinueOnFailure);
                Assert.AreEqual(TimeSpan.FromSeconds(30), options.AnalysisTimeout);
            }
        }

        [TestClass]
        public class BatchAnalysisProgressTests
        {
            [TestMethod]
            public void BatchAnalysisProgress_DefaultValues_AreInitializedCorrectly()
            {
                // Act
                var progress = new BatchAnalysisProgress();

                // Assert
                Assert.AreEqual(0, progress.TotalCount);
                Assert.AreEqual(0, progress.CompletedCount);
                Assert.AreEqual(0, progress.FailedCount);
                Assert.IsNull(progress.CurrentRequirement);
                Assert.AreEqual(0.0, progress.ProgressPercentage);
            }

            [TestMethod]
            public void BatchAnalysisProgress_CalculatesProgressPercentage()
            {
                // Arrange
                var progress = new BatchAnalysisProgress();

                // Act
                progress.TotalCount = 10;
                progress.CompletedCount = 3;

                // Assert
                Assert.AreEqual(10, progress.TotalCount);
                Assert.AreEqual(3, progress.CompletedCount);
                // Progress percentage is 0.0 - 1.0 (not 0-100)
                double expectedPercentage = 3.0 / 10.0;
                Assert.AreEqual(0.3, progress.ProgressPercentage, 0.001);
            }
        }

        [TestClass]
        public class DerivationOptionsTests
        {
            [TestMethod]
            public void DerivationOptions_DefaultValues_AreAppropriate()
            {
                // Act
                var options = new DerivationOptions();

                // Assert
                Assert.IsTrue(options.EnableQualityScoring); // Default is true
                Assert.IsTrue(options.IncludeRejectionAnalysis); // Default is true
                Assert.AreEqual("avionics", options.SystemType); // Default system type
                Assert.AreEqual(TimeSpan.FromMinutes(5), options.MaxProcessingTime);
            }

            [TestMethod]
            public void DerivationOptions_SetAllEnabled_StoresCorrectly()
            {
                // Arrange
                var options = new DerivationOptions();

                // Act
                options.EnableQualityScoring = false;
                options.IncludeRejectionAnalysis = false;
                options.SystemType = "automotive";
                options.MaxProcessingTime = TimeSpan.FromMinutes(2);

                // Assert
                Assert.IsFalse(options.EnableQualityScoring);
                Assert.IsFalse(options.IncludeRejectionAnalysis);
                Assert.AreEqual("automotive", options.SystemType);
                Assert.AreEqual(TimeSpan.FromMinutes(2), options.MaxProcessingTime);
            }
        }

        [TestClass]
        public class GapSeverityTests
        {
            [TestMethod]
            public void GapSeverity_EnumValues_AreOrdered()
            {
                // Assert
                Assert.IsTrue((int)GapSeverity.Low < (int)GapSeverity.Medium);
                Assert.IsTrue((int)GapSeverity.Medium < (int)GapSeverity.High);
            }

            [TestMethod]
            public void GapSeverity_AllValues_AreAccessible()
            {
                // Act & Assert
                var low = GapSeverity.Low;
                var medium = GapSeverity.Medium;
                var high = GapSeverity.High;

                Assert.AreEqual(GapSeverity.Low, low);
                Assert.AreEqual(GapSeverity.Medium, medium);
                Assert.AreEqual(GapSeverity.High, high);
            }
        }
    }
}