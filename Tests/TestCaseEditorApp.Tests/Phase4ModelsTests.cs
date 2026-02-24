using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using TestCaseEditorApp.MVVM.Models;

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
                Assert.IsTrue(capability.Id.StartsWith("cap-"));
                Assert.AreEqual(string.Empty, capability.Name ?? string.Empty);
                Assert.AreEqual(string.Empty, capability.Description ?? string.Empty);
                Assert.AreEqual(0.0, capability.Confidence);
                Assert.IsNull(capability.RequirementSource);
                Assert.IsNull(capability.Category);
            }

            [TestMethod]
            public void DerivedCapability_SetProperties_StoresValuesCorrectly()
            {
                // Arrange
                var capability = new DerivedCapability();

                // Act
                capability.Name = "JTAG Boundary Scan";
                capability.Description = "Verifies JTAG boundary scan chain connectivity";
                capability.Category = "Hardware Test";
                capability.Confidence = 0.95;
                capability.RequirementSource = "REQ-001";

                // Assert
                Assert.AreEqual("JTAG Boundary Scan", capability.Name);
                Assert.AreEqual("Verifies JTAG boundary scan chain connectivity", capability.Description);
                Assert.AreEqual("Hardware Test", capability.Category);
                Assert.AreEqual(0.95, capability.Confidence);
                Assert.AreEqual("REQ-001", capability.RequirementSource);
            }

            [TestMethod]
            public void DerivedCapability_ConfidenceRange_AcceptsValidValues()
            {
                // Arrange
                var capability = new DerivedCapability();

                // Act & Assert
                capability.Confidence = 0.0;
                Assert.AreEqual(0.0, capability.Confidence);

                capability.Confidence = 0.5;
                Assert.AreEqual(0.5, capability.Confidence);

                capability.Confidence = 1.0;
                Assert.AreEqual(1.0, capability.Confidence);
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
                    new DerivedCapability { Name = "Cap 1", Confidence = 0.8 },
                    new DerivedCapability { Name = "Cap 2", Confidence = 0.9 }
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
        public class QualityMetricsTests
        {
            [TestMethod]
            public void QualityMetrics_DefaultValues_AreZero()
            {
                // Act
                var metrics = new QualityMetrics();

                // Assert
                Assert.AreEqual(0.0, metrics.OverallScore);
                Assert.AreEqual(0.0, metrics.ConfidenceScore);
                Assert.AreEqual(0.0, metrics.CompletenessScore);
                Assert.AreEqual(0.0, metrics.ConsistencyScore);
                Assert.AreEqual(0.0, metrics.ClarityScore);
            }

            [TestMethod]
            public void QualityMetrics_SetValidScores_StoresCorrectly()
            {
                // Arrange
                var metrics = new QualityMetrics();

                // Act
                metrics.OverallScore = 0.85;
                metrics.ConfidenceScore = 0.90;
                metrics.CompletenessScore = 0.95;
                metrics.ConsistencyScore = 0.80;
                metrics.ClarityScore = 0.88;

                // Assert
                Assert.AreEqual(0.85, metrics.OverallScore);
                Assert.AreEqual(0.90, metrics.ConfidenceScore);
                Assert.AreEqual(0.95, metrics.CompletenessScore);
                Assert.AreEqual(0.80, metrics.ConsistencyScore);
                Assert.AreEqual(0.88, metrics.ClarityScore);
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
                Assert.IsFalse(result.IsSuccessful);
                Assert.AreEqual(0.0, result.CoveragePercentage);
                Assert.IsNotNull(result.UncoveredCapabilities);
                Assert.IsNotNull(result.UntestedRequirements);
                Assert.IsNotNull(result.RequirementOverlaps);
                Assert.AreEqual(0, result.UncoveredCapabilities.Count);
                Assert.AreEqual(0, result.UntestedRequirements.Count);
                Assert.AreEqual(0, result.RequirementOverlaps.Count);
            }

            [TestMethod]
            public void GapAnalysisResult_WithFullCoverage_ShowsCorrectPercentage()
            {
                // Arrange
                var result = new GapAnalysisResult();

                // Act
                result.IsSuccessful = true;
                result.CoveragePercentage = 1.0;

                // Assert
                Assert.IsTrue(result.IsSuccessful);
                Assert.AreEqual(1.0, result.CoveragePercentage);
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
                Assert.IsNull(uncovered.CapabilityName);
                Assert.AreEqual(GapSeverity.Low, uncovered.Severity);
                Assert.IsNull(uncovered.Recommendation);
            }

            [TestMethod]
            public void UncoveredCapability_SetProperties_StoresCorrectly()
            {
                // Arrange
                var uncovered = new UncoveredCapability();

                // Act
                uncovered.CapabilityName = "Power Supply Test";
                uncovered.Severity = GapSeverity.High;
                uncovered.Recommendation = "Create power supply test requirement";

                // Assert
                Assert.AreEqual("Power Supply Test", uncovered.CapabilityName);
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
                // Act
                var analysis = new RequirementDerivationAnalysis();

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
                var analysis = new RequirementDerivationAnalysis();
                var requirement = new Requirement { Item = "REQ-001", Name = "Test" };

                // Act
                analysis.AnalyzedRequirement = requirement;
                analysis.HasATPContent = true;
                analysis.ATPDetectionConfidence = 0.85;
                analysis.DerivationQuality = 0.90;

                // Assert
                Assert.AreEqual(requirement, analysis.AnalyzedRequirement);
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
                Assert.AreEqual(3, options.MaxConcurrency); // Reasonable default
                Assert.IsTrue(options.ContinueOnFailure); // Good for batch processing
                Assert.AreEqual(TimeSpan.FromMinutes(2), options.AnalysisTimeout); // 2 minutes default
            }

            [TestMethod]
            public void BatchAnalysisOptions_SetCustomValues_StoresCorrectly()
            {
                // Arrange
                var options = new BatchAnalysisOptions();

                // Act
                options.MaxConcurrency = 5;
                options.ContinueOnFailure = false;
                options.AnalysisTimeout = TimeSpan.FromSeconds(30);

                // Assert
                Assert.AreEqual(5, options.MaxConcurrency);
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
                // Progress percentage would be calculated as CompletedCount / TotalCount * 100
                double expectedPercentage = (3.0 / 10.0) * 100.0;
                Assert.AreEqual(30.0, expectedPercentage);
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
                Assert.IsFalse(options.EnableQualityScoring);
                Assert.IsFalse(options.EnableTaxonomyValidation);
                Assert.IsFalse(options.EnableCapabilityAllocation);
                Assert.IsFalse(options.IncludeRejectionAnalysis);
            }

            [TestMethod]
            public void DerivationOptions_SetAllEnabled_StoresCorrectly()
            {
                // Arrange
                var options = new DerivationOptions();

                // Act
                options.EnableQualityScoring = true;
                options.EnableTaxonomyValidation = true;
                options.EnableCapabilityAllocation = true;
                options.IncludeRejectionAnalysis = true;

                // Assert
                Assert.IsTrue(options.EnableQualityScoring);
                Assert.IsTrue(options.EnableTaxonomyValidation);
                Assert.IsTrue(options.EnableCapabilityAllocation);
                Assert.IsTrue(options.IncludeRejectionAnalysis);
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
                Assert.IsTrue((int)GapSeverity.High < (int)GapSeverity.Critical);
            }

            [TestMethod]
            public void GapSeverity_AllValues_AreAccessible()
            {
                // Act & Assert
                var low = GapSeverity.Low;
                var medium = GapSeverity.Medium;
                var high = GapSeverity.High;
                var critical = GapSeverity.Critical;

                Assert.AreEqual(GapSeverity.Low, low);
                Assert.AreEqual(GapSeverity.Medium, medium);
                Assert.AreEqual(GapSeverity.High, high);
                Assert.AreEqual(GapSeverity.Critical, critical);
            }
        }
    }
}