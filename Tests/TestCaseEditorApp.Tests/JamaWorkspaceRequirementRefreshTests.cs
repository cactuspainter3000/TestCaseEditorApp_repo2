using System.Text.Json;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class JamaWorkspaceRequirementRefreshTests
{
    [TestMethod]
    public void NewlyPublishedRequirement_OffersRefreshWithoutPriorSync()
    {
        var workspace = new Workspace
        {
            Requirements = [new Requirement { ApiId = "1234", SourceDocumentLevel = RequirementSourceLevel.L0 }]
        };
        Assert.IsTrue(JamaWorkspaceRequirementRefresh.ShouldOfferRefresh(workspace));
    }

    [TestMethod]
    public void PreviouslySyncedWorkspace_OffersRefreshRegardlessOfLocalSaveTime()
    {
        var workspace = new Workspace
        {
            LastJamaSyncUtc = DateTime.UtcNow,
            LastSavedUtc = DateTime.UtcNow.AddDays(-1)
        };
        Assert.IsTrue(JamaWorkspaceRequirementRefresh.ShouldOfferRefresh(workspace));
    }

    [TestMethod]
    public void UnpublishedLocalWorkspace_DoesNotOfferRefresh()
    {
        var workspace = new Workspace
        {
            Requirements = [new Requirement { ApiId = "LOCAL-1", SourceDocumentLevel = RequirementSourceLevel.L0 }]
        };
        Assert.IsFalse(JamaWorkspaceRequirementRefresh.ShouldOfferRefresh(workspace));
    }

    [TestMethod]
    [DataRow(RequirementSourceLevel.L0)]
    [DataRow(RequirementSourceLevel.L1)]
    public async Task PublishedDocumentRequirement_RetainsLocalWorkAfterJamaConversionAndWorkspaceReload(
        RequirementSourceLevel level)
    {
        var local = new Requirement
        {
            ApiId = "1234",
            Item = "LOC-123",
            GlobalId = "LOCAL-GLOBAL-123",
            Name = "Original name",
            Description = "The system shall log the start time.",
            SourceDocumentLevel = level,
            SourceDocumentName = "Specification.docx",
            SourceSection = "3.2",
            SourcePrefix = "REQ-42",
            SourcePrefixType = "document_id",
            TraceReference = "document-123",
            GeneratedTestCases = { new TestCase { Id = "TC-17", Name = "Verify start time" } },
            ClarifyingQuestions = { new ClarifyingQuestionData { Text = "Which time zone?", Answer = "UTC" } }
        };
        var workspace = Reload(new Workspace { Requirements = [local] });
        var publishedContent = JamaRequirementCreateContent.FromRequirement(local);
        var remoteRequirements = await new JamaConnectService("https://jama.invalid", "not-a-real-token")
            .ConvertToRequirementsAsync(
            [
                new JamaItem
                {
                    Id = 1234, DocumentKey = "DEMO-REQ-9", GlobalId = "JAMA-GLOBAL-9",
                    Name = "Updated in Jama", Description = publishedContent.Description
                }
            ]);

        Assert.AreEqual("1234", remoteRequirements.Single().ApiId);
        workspace.Requirements = JamaWorkspaceRequirementRefresh.Merge(workspace.Requirements, remoteRequirements);
        var restored = Reload(workspace).Requirements.Single();

        Assert.AreEqual("Updated in Jama", restored.Name);
        Assert.AreEqual("1234", restored.ApiId);
        Assert.AreEqual("LOC-123", restored.Item);
        Assert.AreEqual("LOCAL-GLOBAL-123", restored.GlobalId);
        Assert.AreEqual(level, restored.SourceDocumentLevel);
        Assert.AreEqual("Specification.docx", restored.SourceDocumentName);
        Assert.AreEqual("3.2", restored.SourceSection);
        Assert.AreEqual("REQ-42", restored.SourcePrefix);
        Assert.AreEqual("document-123", restored.TraceReference);
        Assert.AreEqual("TC-17", restored.GeneratedTestCases.Single().Id);
        Assert.AreEqual("UTC", restored.ClarifyingQuestions.Single().Answer);
    }

    [TestMethod]
    public void MissingJamaItemsAndDrafts_AreRetainedWithoutMatchingByTitleOrKey()
    {
        var draft = new Requirement { Name = "Same title", Item = "SAME-KEY", SourceDocumentLevel = RequirementSourceLevel.L0 };
        var missing = new Requirement { ApiId = "7", SourceDocumentLevel = RequirementSourceLevel.L1 };
        var oldJamaImport = new Requirement { ApiId = "9" };
        var remote = new Requirement { ApiId = "8", Name = draft.Name, Item = draft.Item };

        var result = JamaWorkspaceRequirementRefresh.Merge([draft, missing, oldJamaImport], [remote]);

        CollectionAssert.AreEqual(new[] { remote, draft, missing }, result);
        Assert.AreEqual(RequirementSourceLevel.Unspecified, remote.SourceDocumentLevel);
    }

    [TestMethod]
    public void AmbiguousJamaId_RejectsRefresh()
    {
        var first = new Requirement { ApiId = "7", SourceDocumentLevel = RequirementSourceLevel.L0 };
        var second = new Requirement { ApiId = "7", SourceDocumentLevel = RequirementSourceLevel.L1 };
        var remote = new Requirement { ApiId = "7" };

        Assert.ThrowsException<InvalidOperationException>(() =>
            JamaWorkspaceRequirementRefresh.Merge([first, second], [remote]));
    }

    private static Workspace Reload(Workspace workspace) =>
        JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(workspace))!;
}
