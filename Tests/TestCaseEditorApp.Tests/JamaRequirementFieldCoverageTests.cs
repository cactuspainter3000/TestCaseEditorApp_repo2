using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class JamaRequirementFieldCoverageTests
{
    [TestMethod]
    public void AnalyzeExportKeys_ReportsMissingAndUnexpectedFields()
    {
        var coverage = JamaRequirementFieldCoverage.AnalyzeExportKeys(new[]
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Status",
            "Custom Jama Field",
            "Tags:"
        });

        Assert.IsFalse(coverage.IsComplete);
        CollectionAssert.Contains(coverage.ExpectedFields.ToList(), "Item ID");
        CollectionAssert.Contains(coverage.ActualFields.ToList(), "Tags");
        CollectionAssert.Contains(coverage.MissingExpectedFields.ToList(), "Global ID");
        CollectionAssert.Contains(coverage.UnexpectedActualFields.ToList(), "Custom Jama Field");
    }

    [TestMethod]
    public void AnalyzeFieldDictionary_UsesCommonFieldNameAliases()
    {
        using var document = JsonDocument.Parse("[{\"name\":\"Item ID\"},{\"fieldName\":\"Name\"},{\"apiName\":\"Requirement Description\"},{\"key\":\"Status\"}]");

        var coverage = JamaRequirementFieldCoverage.AnalyzeFieldDictionary(document.RootElement);

        Assert.IsTrue(coverage.ActualFields.Contains("Item ID"));
        Assert.IsTrue(coverage.ActualFields.Contains("Name"));
        Assert.IsTrue(coverage.ActualFields.Contains("Requirement Description"));
        Assert.IsTrue(coverage.ActualFields.Contains("Status"));
    }

    [TestMethod]
    public void Summarize_AggregatesMissingAndUnexpectedCounts()
    {
        var first = JamaRequirementFieldCoverage.AnalyzeExportKeys(new[]
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Status",
            "Custom One"
        });

        var second = JamaRequirementFieldCoverage.AnalyzeExportKeys(new[]
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Global ID",
            "Status",
            "Custom Two"
        });

        var summary = JamaRequirementFieldCoverage.Summarize(new[] { first, second });

        Assert.AreEqual(2, summary.TotalRequirements);
        Assert.AreEqual(0, summary.CompleteRequirements);
        Assert.AreEqual(2, summary.IncompleteRequirements);
        Assert.AreEqual(2, summary.RequirementsWithUnexpectedFields);
        Assert.IsTrue(summary.MissingFieldCounts.ContainsKey("API ID"));
        Assert.IsTrue(summary.MissingFieldCounts["API ID"] >= 2);
        Assert.AreEqual(1, summary.UnexpectedFieldCounts["Custom One"]);
        Assert.AreEqual(1, summary.UnexpectedFieldCounts["Custom Two"]);
    }

    [TestMethod]
    public void FormatCoverageResultForReport_UsesNoneWhenNoIssues()
    {
        var coverage = JamaRequirementFieldCoverage.AnalyzeExportKeys(JamaRequirementFieldCoverage.GetExpectedFields());

        var reportText = JamaRequirementFieldCoverage.FormatCoverageResultForReport(coverage);

        StringAssert.Contains(reportText, "- Expected fields: 56");
        StringAssert.Contains(reportText, "- Contract status: PASS");
        StringAssert.Contains(reportText, "- Missing core fields: none");
        StringAssert.Contains(reportText, "- Missing expected fields: none");
        StringAssert.Contains(reportText, "- Unexpected actual fields: none");
    }

    [TestMethod]
    public void FormatCoverageResultForReport_ListsMissingAndUnexpectedFields()
    {
        var coverage = JamaRequirementFieldCoverage.AnalyzeExportKeys(new[]
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Status",
            "Custom Export Field"
        });

        var reportText = JamaRequirementFieldCoverage.FormatCoverageResultForReport(coverage, maxListItems: 5);

        StringAssert.Contains(reportText, "- Actual fields discovered: 5");
        StringAssert.Contains(reportText, "- Contract status: FAIL");
        StringAssert.Contains(reportText, "- Missing core fields:");
        StringAssert.Contains(reportText, "Global ID");
        StringAssert.Contains(reportText, "- Missing expected fields:");
        Assert.IsFalse(reportText.Contains("- Missing expected fields: none", System.StringComparison.Ordinal));
        StringAssert.Contains(reportText, "Custom Export Field");
    }

    [TestMethod]
    public void EvaluateCoverageHealth_FindsMissingCoreContractFields()
    {
        var coverage = JamaRequirementFieldCoverage.AnalyzeExportKeys(new[]
        {
            "Item ID",
            "Name",
            "Requirement Description",
            "Status"
        });

        var health = JamaRequirementFieldCoverage.EvaluateCoverageHealth(coverage);

        Assert.IsFalse(health.IsContractHealthy);
        CollectionAssert.Contains(health.MissingCoreFields.ToList(), "Global ID");
        CollectionAssert.Contains(health.MissingCoreFields.ToList(), "Item Type");
        CollectionAssert.Contains(health.MissingCoreFields.ToList(), "Validation Method/s");
        CollectionAssert.Contains(health.MissingCoreFields.ToList(), "Verification Method/s");
    }
}