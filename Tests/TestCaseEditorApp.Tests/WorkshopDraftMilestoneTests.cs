using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class WorkshopDraftMilestoneTests
{
    private readonly Mock<IRequirementsMediator> _mediator = new();
    private readonly Mock<IWorkspaceContext> _context = new();
    private readonly Mock<ITestCaseGenerationService> _generation = new();
    private readonly Mock<IJamaConnectService> _jama = new();
    private readonly Mock<IPersistenceService> _persistence = new();
    private readonly Requirement _requirement = new() { Item = "DEMO-REQ-1", ApiId = "123", Name = "Warning" };
    private readonly Workspace _workspace = new() { JamaProjectId = 42, AnythingLLMWorkspaceSlug = "demo" };
    private string _path = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _path = Path.GetTempFileName();
        File.WriteAllText(_path, JsonSerializer.Serialize(_workspace));
        _mediator.SetupGet(m => m.Requirements).Returns(new ObservableCollection<Requirement> { _requirement });
        _context.SetupGet(c => c.CurrentWorkspace).Returns(_workspace);
        _context.SetupGet(c => c.CurrentWorkspaceInfo).Returns(new WorkspaceInfo { Path = _path });
        _persistence.Setup(p => p.Save(_path, It.IsAny<Workspace>()))
            .Callback<string, Workspace>((path, workspace) => File.WriteAllText(path, JsonSerializer.Serialize(workspace)));
        _jama.Setup(j => j.GetDraftPublicationOptionsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JamaDraftOptions([new(194, "Verification Case")], [new(501, "Verification", 194)], [new(7, "Verifies")]));
    }

    [TestCleanup]
    public void Cleanup() => File.Delete(_path);

    [TestMethod]
    public async Task SelectedGeneration_CheckpointsAndResumeSkipsExistingCases()
    {
        var unselected = new Requirement { Item = "OTHER" };
        _mediator.Object.Requirements.Add(unselected);
        var vm = Create();
        vm.DraftRequirements[0].IsSelected = true;
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(_requirement,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([Generated()]);

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);
        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        Assert.AreEqual(0, unselected.GeneratedTestCases.Count);
        Assert.AreEqual("TC-1", Load().Requirements.Single(r => r.Item == "DEMO-REQ-1").GeneratedTestCases.Single().Id);
        _generation.Verify(g => g.GenerateTestCasesForSingleRequirementAsync(_requirement,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task SelectedGeneration_FileCheckpointReloadsWorkspace_ContinuesAndPublishesPreview()
    {
        var management = new Mock<INewProjectMediator>();
        management.Setup(m => m.GetCurrentWorkspaceInfo()).Returns(new WorkspaceInfo { Path = _path });
        var context = new WorkspaceContextService(management.Object, NullLogger<WorkspaceContextService>.Instance);
        var vm = new WorkshopReproViewModel(_mediator.Object, Mock.Of<IWorkspaceDiagnosticsService>(),
            Mock.Of<IFileDialogService>(), Mock.Of<IJamaDocumentParserService>(), context, _jama.Object,
            _generation.Object, _persistence.Object);
        var original = context.CurrentWorkspace;
        var saveNumber = 0;
        _persistence.Setup(p => p.Save(_path, It.IsAny<Workspace>()))
            .Callback<string, Workspace>((path, workspace) =>
            {
                File.WriteAllText(path, JsonSerializer.Serialize(workspace));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(++saveNumber));
            });
        vm.LoadDraftRequirementsCommand.Execute(null);
        vm.DraftRequirements.Single().IsSelected = true;
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(_requirement,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([Generated()]);

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        Assert.AreNotSame(original, context.CurrentWorkspace);
        Assert.AreEqual(1, Load().Requirements.Single().GeneratedTestCases.Count);
        StringAssert.Contains(vm.DraftWorkflowStatus, "1 processed successfully");
        await Preview(vm);
        _jama.Setup(j => j.CreateDraftTestCaseAsync(42, 501, 194, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(900);
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.DraftWorkflowStatus, "Published and linked 1");
    }

    [TestMethod]
    public async Task SelectedGeneration_ProjectPathChangesAfterCheckpoint_StopsBeforeLlmRequest()
    {
        var vm = Create();
        vm.DraftRequirements.Single().IsSelected = true;
        _persistence.Setup(p => p.Save(_path, It.IsAny<Workspace>()))
            .Callback(() => _context.SetupGet(c => c.CurrentWorkspaceInfo)
                .Returns(new WorkspaceInfo { Path = _path + ".other" }));

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.DraftWorkflowStatus, "project or selected requirements changed");
        _generation.Verify(g => g.GenerateTestCasesForSingleRequirementAsync(It.IsAny<Requirement>(),
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SelectedGeneration_JamaAssociationChangesAfterCheckpoint_StopsBeforeLlmRequest()
    {
        var vm = Create();
        vm.DraftRequirements.Single().IsSelected = true;
        _persistence.Setup(p => p.Save(_path, It.IsAny<Workspace>()))
            .Callback(() => _workspace.JamaProjectId = 99);

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.DraftWorkflowStatus, "project or selected requirements changed");
        _generation.Verify(g => g.GenerateTestCasesForSingleRequirementAsync(It.IsAny<Requirement>(),
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FiftySelectedRequirements_AllProduceSavedLinkedDraftsWithoutPublishing()
    {
        _mediator.Object.Requirements.Clear();
        foreach (var id in Enumerable.Range(1, 50))
            _mediator.Object.Requirements.Add(new Requirement { Item = $"REQ-{id}", ApiId = (1000 + id).ToString() });
        var vm = Create();
        foreach (var row in vm.DraftRequirements) row.IsSelected = true;
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(It.IsAny<Requirement>(),
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Requirement requirement, Action<string, int, int>? _, CancellationToken _) =>
            {
                var test = Generated();
                test.CoveredRequirementIds = [requirement.Item];
                return new List<LLMTestCase> { test };
            });

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        var saved = Load();
        Assert.AreEqual(50, saved.Requirements.Count);
        Assert.IsTrue(saved.Requirements.All(requirement => requirement.GeneratedTestCases.Count == 1));
        StringAssert.Contains(vm.DraftWorkflowStatus, "50 processed successfully");
        _persistence.Verify(p => p.Save(_path, It.IsAny<Workspace>()), Times.Exactly(51));
        _jama.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Publication_RequiresPreviewAndSavesCreatedIdBeforeLinking()
    {
        var vm = CreateWithDraft();
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        _jama.Verify(j => j.CreateDraftTestCaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        await Preview(vm);
        _jama.Setup(j => j.CreateDraftTestCaseAsync(42, 501, 194, "[AI DRAFT] Verify warning",
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(900);
        _jama.Setup(j => j.LinkDraftTestCaseAsync(42, 123, 900, 7, It.IsAny<CancellationToken>()))
            .Callback(() => Assert.AreEqual("900", Load().Requirements.Single().GeneratedTestCases.Single().ApiId))
            .Returns(Task.CompletedTask);

        await vm.PublishDraftsCommand.ExecuteAsync(null);
        Assert.AreEqual("AI Draft - published and linked", Load().Requirements.Single().GeneratedTestCases.Single().Status);
        StringAssert.Contains(vm.DraftWorkflowStatus, "Published and linked 1");
    }

    [TestMethod]
    public async Task Publication_ContentChangedDuringPreflight_StopsBeforeCreation()
    {
        var vm = CreateWithDraft();
        await Preview(vm);
        _jama.Setup(j => j.ValidateDraftDestinationAsync(42, 501, 194,
            It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _requirement.GeneratedTestCases.Single().Name = "Changed after preview")
            .Returns(Task.CompletedTask);

        await vm.PublishDraftsCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.DraftWorkflowStatus, "changed during publication");
        _jama.Verify(j => j.CreateDraftTestCaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SelectedGeneration_RequirementEditedDuringRequest_DoesNotAttachStaleDrafts()
    {
        var vm = Create();
        vm.DraftRequirements.Single().IsSelected = true;
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(_requirement,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()))
            .Callback(() => _requirement.Description = "Changed during generation")
            .ReturnsAsync([Generated()]);

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        Assert.AreEqual(0, _requirement.GeneratedTestCases.Count);
        StringAssert.Contains(vm.DraftRequirements.Single().Result, "requirement changed");
        StringAssert.Contains(vm.DraftWorkflowStatus, "1 failed");
    }

    [TestMethod]
    public async Task UnknownCreation_IsSavedAndCannotBeAutomaticallyRetried()
    {
        var vm = CreateWithDraft();
        await Preview(vm);
        _jama.Setup(j => j.CreateDraftTestCaseAsync(42, 501, 194, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Response lost"));
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        Assert.AreEqual("AI Draft - creation outcome unconfirmed", Load().Requirements.Single().GeneratedTestCases.Single().Status);
        vm.PreviewDraftPublicationCommand.Execute(null);
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        _jama.Verify(j => j.CreateDraftTestCaseAsync(42, 501, 194, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task LinkFailure_ResumesKnownItemWithoutRecreating()
    {
        var vm = CreateWithDraft();
        await Preview(vm);
        _jama.Setup(j => j.CreateDraftTestCaseAsync(42, 501, 194, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(900);
        _jama.SetupSequence(j => j.LinkDraftTestCaseAsync(42, 123, 900, 7, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Link rejected")).Returns(Task.CompletedTask);
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        vm.PreviewDraftPublicationCommand.Execute(null);
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        _jama.Verify(j => j.CreateDraftTestCaseAsync(42, 501, 194, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("AI Draft - published and linked", Load().Requirements.Single().GeneratedTestCases.Single().Status);
    }

    [TestMethod]
    public async Task SaveFailure_StopsBeforeRemoteCreation()
    {
        var vm = CreateWithDraft();
        await Preview(vm);
        _persistence.Setup(p => p.Save(_path, It.IsAny<Workspace>())).Throws(new IOException("Disk full"));
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        _jama.Verify(j => j.CreateDraftTestCaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(vm.DraftWorkflowStatus, "Disk full");
    }

    [TestMethod]
    public async Task ChangedContent_InvalidatesPublicationPreview()
    {
        var vm = CreateWithDraft();
        await Preview(vm);
        _requirement.GeneratedTestCases.Single().Name = "Changed";
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        _jama.Verify(j => j.CreateDraftTestCaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(vm.DraftWorkflowStatus, "changed");
    }

    [TestMethod]
    public async Task SelectedGeneration_CancellationKeepsCompletedCheckpoint()
    {
        var second = new Requirement { ApiId = "124", Item = "DEMO-REQ-2" };
        _mediator.Object.Requirements.Add(second);
        var vm = Create();
        foreach (var row in vm.DraftRequirements) row.IsSelected = true;
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(_requirement,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([Generated()]);
        var pending = new TaskCompletionSource<List<LLMTestCase>>();
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(second,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()))
            .Callback(() => vm.GenerateSelectedDraftsCancelCommand.Execute(null)).Returns(pending.Task);

        var running = vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);
        pending.SetResult([]);
        await running;

        Assert.AreEqual(1, Load().Requirements.Single(r => r.ApiId == "123").GeneratedTestCases.Count);
        Assert.AreEqual(0, second.GeneratedTestCases.Count);
        StringAssert.Contains(vm.DraftWorkflowStatus, "canceled");
        Assert.IsFalse(vm.IsDraftWorkflowBusy);
    }

    [TestMethod]
    public async Task SelectedGeneration_ContinuesAfterInvalidResultAndCheckpointsLaterSuccess()
    {
        var second = new Requirement { ApiId = "124", Item = "DEMO-REQ-2" };
        _mediator.Object.Requirements.Add(second);
        var vm = Create();
        foreach (var row in vm.DraftRequirements) row.IsSelected = true;
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(_requirement,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var test = Generated();
        test.CoveredRequirementIds = [second.Item];
        _generation.Setup(g => g.GenerateTestCasesForSingleRequirementAsync(second,
            It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([test]);

        await vm.GenerateSelectedDraftsCommand.ExecuteAsync(null);

        Assert.AreEqual(0, Load().Requirements.Single(r => r.ApiId == "123").GeneratedTestCases.Count);
        Assert.AreEqual(1, Load().Requirements.Single(r => r.ApiId == "124").GeneratedTestCases.Count);
        StringAssert.Contains(vm.DraftWorkflowStatus, "1 failed");
    }

    [TestMethod]
    public async Task IncompatibleContainer_IsRejectedWithoutRemoteWrites()
    {
        var vm = CreateWithDraft();
        await vm.LoadDraftDestinationsCommand.ExecuteAsync(null);
        vm.SelectedDraftItemType = vm.DraftItemTypes.Single();
        vm.SelectedDraftContainer = new JamaDraftOption(502, "Requirements", 193);
        vm.SelectedDraftRelationshipType = vm.DraftRelationshipTypes.Single();
        vm.PreviewDraftPublicationCommand.Execute(null);
        await vm.PublishDraftsCommand.ExecuteAsync(null);
        Assert.IsTrue(string.IsNullOrEmpty(vm.DraftPublicationPreview));
        _jama.Verify(j => j.CreateDraftTestCaseAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void DeliveryTab_IsTopLevelAndUsesBoundCommands()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TestCaseEditorApp.csproj"))) root = root.Parent;
        Assert.IsNotNull(root);
        var view = XDocument.Load(Path.Combine(root.FullName, "MVVM", "Domains", "Workshop", "Views", "WorkshopDesignerReproView.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var tab = view.Descendants(p + "TabItem").Single(item => (string?)item.Attribute("Header") == "Draft Test Case Delivery");
        Assert.IsFalse(tab.Ancestors(p + "TabItem").Any());
        Assert.IsTrue(tab.Descendants(p + "Button").All(button => button.Attribute("Command") != null));
    }

    private WorkshopReproViewModel Create()
    {
        var vm = new WorkshopReproViewModel(_mediator.Object, Mock.Of<IWorkspaceDiagnosticsService>(),
            Mock.Of<IFileDialogService>(), Mock.Of<IJamaDocumentParserService>(), _context.Object, _jama.Object,
            _generation.Object, _persistence.Object);
        vm.LoadDraftRequirementsCommand.Execute(null);
        return vm;
    }

    private WorkshopReproViewModel CreateWithDraft()
    {
        _requirement.GeneratedTestCases.Add(Generated().ToWorkspaceTestCase());
        var vm = Create();
        vm.DraftRequirements.Single().IsSelected = true;
        return vm;
    }

    private async Task Preview(WorkshopReproViewModel vm)
    {
        await vm.LoadDraftDestinationsCommand.ExecuteAsync(null);
        vm.SelectedDraftItemType = vm.DraftItemTypes.Single();
        vm.SelectedDraftContainer = vm.DraftContainers.Single();
        vm.SelectedDraftRelationshipType = vm.DraftRelationshipTypes.Single();
        vm.PreviewDraftPublicationCommand.Execute(null);
        Assert.IsFalse(string.IsNullOrEmpty(vm.DraftPublicationPreview), vm.DraftWorkflowStatus);
    }

    private Workspace Load() => JsonSerializer.Deserialize<Workspace>(File.ReadAllText(_path))!;
    private static LLMTestCase Generated() => new()
    {
        Id = "TC-1", Title = "Verify warning", Description = "Display a warning",
        CoveredRequirementIds = ["DEMO-REQ-1"],
        Steps = [new() { StepNumber = 1, Action = "Inject fault", ExpectedResult = "Warning" }]
    };
}
