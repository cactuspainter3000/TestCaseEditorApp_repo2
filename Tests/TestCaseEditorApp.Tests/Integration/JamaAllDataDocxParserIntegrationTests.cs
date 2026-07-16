using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.Tests.Integration;

[TestClass]
[DoNotParallelize]
public class JamaAllDataDocxParserIntegrationTests
{
    [TestMethod]
    public void Parse_WhenCoreFieldsMissing_SetsUnhealthyContractDiagnostics()
    {
        var path = CreateTempJamaDocx(new Dictionary<string, string>
        {
            ["Item ID"] = "REQ-1",
            ["Name"] = "Requirement one",
            ["Requirement Description"] = "Description one",
            ["Status"] = "Draft"
        });

        try
        {
            var requirements = JamaAllDataDocxParser.Parse(path, debugDump: false);
            var diagnostics = JamaAllDataDocxParser.LastParseDiagnostics;

            Assert.AreEqual(1, requirements.Count);
            Assert.IsNotNull(diagnostics);
            Assert.IsFalse(diagnostics.IsContractHealthy);
            Assert.AreEqual(1, diagnostics.MissingCoreFieldsByRequirement.Count);

            var requirementKey = diagnostics.MissingCoreFieldsByRequirement.Keys.Single();
            CollectionAssert.Contains(diagnostics.MissingCoreFieldsByRequirement[requirementKey].ToList(), "Global ID");
            CollectionAssert.Contains(diagnostics.MissingCoreFieldsByRequirement[requirementKey].ToList(), "Item Type");
            CollectionAssert.Contains(diagnostics.MissingCoreFieldsByRequirement[requirementKey].ToList(), "Validation Method/s");
            CollectionAssert.Contains(diagnostics.MissingCoreFieldsByRequirement[requirementKey].ToList(), "Verification Method/s");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Parse_WhenCoreFieldsPresent_SetsHealthyContractDiagnostics()
    {
        var path = CreateTempJamaDocx(new Dictionary<string, string>
        {
            ["Item ID"] = "REQ-2",
            ["Name"] = "Requirement two",
            ["Requirement Description"] = "Description two",
            ["Global ID"] = "GID-2",
            ["Item Type"] = "System Requirement",
            ["Status"] = "Draft",
            ["Validation Method/s"] = "Analysis",
            ["Verification Method/s"] = "Test"
        });

        try
        {
            var requirements = JamaAllDataDocxParser.Parse(path, debugDump: false);
            var diagnostics = JamaAllDataDocxParser.LastParseDiagnostics;

            Assert.AreEqual(1, requirements.Count);
            Assert.IsNotNull(diagnostics);
            var missingSummary = string.Join(", ", diagnostics.MissingCoreFieldsByRequirement
                .SelectMany(kvp => kvp.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.IsTrue(diagnostics.IsContractHealthy, $"Missing core fields detected: {missingSummary}");
            Assert.AreEqual(0, diagnostics.MissingCoreFieldsByRequirement.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempJamaDocx(IReadOnlyDictionary<string, string> kvPairs)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jama-parser-it-{Guid.NewGuid():N}.docx");

        using (var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            body.AppendChild(new Paragraph(new Run(new Text("SYS-REQ_RC-1001 Sample System Requirement"))));
            body.AppendChild(new Paragraph(new Run(new Text("A short description paragraph."))));

            var table = new Table();
            foreach (var kvp in kvPairs)
            {
                var row = new TableRow(
                    new TableCell(new Paragraph(new Run(new Text(kvp.Key)))),
                    new TableCell(new Paragraph(new Run(new Text(kvp.Value)))));
                table.AppendChild(row);
            }

            body.AppendChild(table);
            mainPart.Document.Save();
        }

        return path;
    }
}
