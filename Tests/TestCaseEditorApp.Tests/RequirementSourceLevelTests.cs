using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class RequirementSourceLevelTests
{
    [TestMethod]
    public void SourceDocumentLevel_RoundTripsThroughWorkspaceJson()
    {
        var requirement = new Requirement
        {
            SourceDocumentName = "System Specification.docx",
            SourceDocumentLevel = RequirementSourceLevel.L0
        };

        var json = JsonSerializer.Serialize(requirement);
        var restored = JsonSerializer.Deserialize<Requirement>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual("System Specification.docx", restored.SourceDocumentName);
        Assert.AreEqual(RequirementSourceLevel.L0, restored.SourceDocumentLevel);
    }

    [TestMethod]
    public void MissingSourceDocumentLevel_DefaultsToUnspecified()
    {
        var restored = JsonSerializer.Deserialize<Requirement>("{}");

        Assert.IsNotNull(restored);
        Assert.AreEqual(RequirementSourceLevel.Unspecified, restored.SourceDocumentLevel);
    }
}
