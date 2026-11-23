using System.Collections.ObjectModel;
using Moq;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class WorkshopJamaPublicationTests
{
    private readonly Mock<IRequirementsMediator> _mediator = new();
    private readonly Mock<IWorkspaceContext> _context = new();
    private readonly Mock<IJamaConnectService> _jama = new();
    private readonly Workspace _workspace = new() { JamaProjectId = 42, JamaProjectName = "Demo" };
    private readonly Requirement _requirement = new()
    {
        Item = "LOC-123", GlobalId = "LOC-123", Name = "System response", Description = "Respond in < 3 seconds.",
        SourceDocumentName = "Source & Data.docx", SourceDocumentLevel = RequirementSourceLevel.L0,
        SourceSection = "3.2", TraceReference = "document-123"
    };

    [TestMethod]
    public async Task Publication_RequiresConfirmationAndRecordsJamaIdWithoutChangingLocalKey()
    {
        var viewModel = CreateStagedViewModel();
        _jama.Setup(jama => jama.CreateRequirementAsync(42, _requirement, null, CancellationToken.None))
            .ReturnsAsync((true, "Created", 1234));

        viewModel.PreviewJamaPublishCommand.Execute(null);

        StringAssert.Contains(viewModel.JamaPublishPreviewText, "Jama project ID 42");
        StringAssert.Contains(viewModel.JamaPublishPreviewText, "Source document level: L0");
        StringAssert.Contains(viewModel.JamaPublishPreviewText, "Source document: Source & Data.docx");
        StringAssert.Contains(viewModel.JamaPublishPreviewText, "Source section: 3.2");
        VerifyNoWrites();
        Assert.IsFalse(viewModel.CommitStagedCommand.CanExecute(null));

        await viewModel.ConfirmJamaPublishCommand.ExecuteAsync(null);

        _jama.Verify(jama => jama.CreateRequirementAsync(42, _requirement, null, CancellationToken.None), Times.Once);
        _mediator.Verify(mediator => mediator.UpdateRequirement(_requirement,
            It.Is<IReadOnlyList<string>>(fields => fields.Contains(nameof(Requirement.ApiId)))), Times.Once);
        Assert.AreEqual("1234", _requirement.ApiId);
        Assert.AreEqual("LOC-123", _requirement.Item);
        Assert.IsFalse(viewModel.PreviewJamaPublishCommand.CanExecute(null));
        Assert.AreEqual(1, viewModel.CommittedCount);
    }

    [TestMethod]
    [DataRow("content")]
    [DataRow("destination")]
    [DataRow("workspace")]
    [DataRow("cancel")]
    public async Task Publication_DoesNotWriteIfPreviewIsStaleOrCanceled(string action)
    {
        var viewModel = CreateStagedViewModel();
        viewModel.PreviewJamaPublishCommand.Execute(null);
        switch (action)
        {
            case "content": _requirement.Description = "Changed"; break;
            case "destination": _workspace.JamaProjectId = 17; break;
            case "workspace": _context.SetupGet(context => context.CurrentWorkspace).Returns(new Workspace { JamaProjectId = 42 }); break;
            case "cancel": viewModel.CancelJamaPublishPreviewCommand.Execute(null); break;
        }

        await viewModel.ConfirmJamaPublishCommand.ExecuteAsync(null);

        VerifyNoWrites();
        Assert.IsFalse(viewModel.IsJamaPublishPreviewVisible);
        Assert.AreEqual(1, viewModel.StagedCount);
    }

    [TestMethod]
    public async Task Publication_ConflictingProjectIdsDoNotAllowConfirmation()
    {
        var viewModel = CreateStagedViewModel();
        _workspace.JamaProject = "17";

        viewModel.PreviewJamaPublishCommand.Execute(null);
        await viewModel.ConfirmJamaPublishCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsJamaPublishPreviewVisible);
        StringAssert.Contains(viewModel.JamaPublishStatus, "confirmed Jama project ID");
        VerifyNoWrites();
    }

    [TestMethod]
    public async Task Publication_NotConfirmedByJama_LeavesRequirementStaged()
    {
        var viewModel = CreateStagedViewModel();
        _jama.Setup(jama => jama.CreateRequirementAsync(42, _requirement, null, CancellationToken.None))
            .ReturnsAsync((false, "Placement rejected", (int?)null));
        viewModel.PreviewJamaPublishCommand.Execute(null);
        await viewModel.ConfirmJamaPublishCommand.ExecuteAsync(null);

        StringAssert.Contains(viewModel.JamaPublishStatus, "Placement rejected");
        Assert.AreEqual(1, viewModel.StagedCount);
        Assert.IsTrue(string.IsNullOrEmpty(_requirement.ApiId));
        Assert.IsTrue(viewModel.PreviewJamaPublishCommand.CanExecute(null));
    }

    [TestMethod]
    public void CreateContent_EncodesDescriptionAndIncludesOnlyKnownProvenance()
    {
        _requirement.SourceSection = "UNK";
        _requirement.SourceDocumentLevel = RequirementSourceLevel.L1;
        var content = JamaRequirementCreateContent.FromRequirement(_requirement);

        Assert.AreEqual("System response", content.Name);
        Assert.AreEqual("<p>Respond in &lt; 3 seconds.</p><p>Source document level: L1<br />" +
                        "Source document: Source &amp; Data.docx<br />Source reference: document-123</p>",
            content.Description);
        Assert.AreEqual("Respond in < 3 seconds.", _requirement.Description);
        _requirement.SourceDocumentLevel = RequirementSourceLevel.Unspecified;
        Assert.AreEqual(_requirement.Description, JamaRequirementCreateContent.FromRequirement(_requirement).Description);
    }

    private WorkshopReproViewModel CreateStagedViewModel()
    {
        _mediator.SetupGet(mediator => mediator.Requirements).Returns(new ObservableCollection<Requirement> { _requirement });
        _mediator.SetupGet(mediator => mediator.CurrentRequirement).Returns(_requirement);
        _context.SetupGet(context => context.CurrentWorkspace).Returns(_workspace);
        _jama.SetupGet(jama => jama.IsConfigured).Returns(true);
        var viewModel = new WorkshopReproViewModel(_mediator.Object, Mock.Of<IWorkspaceDiagnosticsService>(),
            Mock.Of<IFileDialogService>(), Mock.Of<IJamaDocumentParserService>(), _context.Object, _jama.Object,
            Mock.Of<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services.ITestCaseGenerationService>());
        viewModel.StageCommand.Execute(null);
        return viewModel;
    }

    private void VerifyNoWrites() => _jama.Verify(jama => jama.CreateRequirementAsync(
        It.IsAny<int>(), It.IsAny<Requirement>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
}
