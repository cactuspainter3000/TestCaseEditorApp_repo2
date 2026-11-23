using System.Collections.ObjectModel;
using Moq;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class WorkshopDocumentIntakeTests
{
    private const string DocumentPath = @"C:\engineering\system-specification.docx";
    private readonly Mock<IRequirementsMediator> _mediator = new();
    private readonly Mock<IFileDialogService> _fileDialog = new();
    private readonly Mock<IJamaDocumentParserService> _parser = new();

    [TestMethod]
    [DataRow(RequirementSourceLevel.L0)]
    [DataRow(RequirementSourceLevel.L1)]
    public async Task ClassifiedExtraction_ImportsOnlyExplicitlySelectedCandidates(RequirementSourceLevel level)
    {
        var viewModel = CreateViewModel();
        _fileDialog.Setup(dialog => dialog.ShowOpenFile($"Select {level} source document", It.IsAny<string>(), null))
            .Returns(DocumentPath);
        var first = new Requirement { Item = "REQ-1", SourceSection = "3.2" };
        var second = new Requirement { Item = "REQ-2", SourceSection = "3.3" };
        _parser.Setup(parser => parser.ParseLocalDocumentAsync(
                DocumentPath, It.IsAny<Action<string>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Requirement> { first, second });

        Assert.IsFalse(viewModel.ExtractClassifiedDocumentCommand.CanExecute(null));
        viewModel.SelectedLocalDocumentLevel = level;
        Assert.IsTrue(viewModel.ExtractClassifiedDocumentCommand.CanExecute(null));

        await viewModel.ExtractClassifiedDocumentCommand.ExecuteAsync(null);

        Assert.AreEqual(2, viewModel.ScrapedRequirements.Count);
        Assert.IsNull(viewModel.SelectedLocalDocumentLevel);
        foreach (var candidate in viewModel.ScrapedRequirements)
        {
            Assert.AreEqual(level, candidate.Requirement.SourceDocumentLevel);
            Assert.AreEqual("system-specification.docx", candidate.Requirement.SourceDocumentName);
            Assert.IsFalse(candidate.IsSelected);
        }
        Assert.AreEqual("3.2", first.SourceSection);
        Assert.IsFalse(viewModel.ImportScrapedRequirementsCommand.CanExecute(null));
        _mediator.Verify(mediator => mediator.ImportRequirementsAsync(It.IsAny<List<Requirement>>()), Times.Never);

        viewModel.ScrapedRequirements[0].IsSelected = true;
        Assert.IsTrue(viewModel.ImportScrapedRequirementsCommand.CanExecute(null));
        await viewModel.ImportScrapedRequirementsCommand.ExecuteAsync(null);

        _mediator.Verify(mediator => mediator.ImportRequirementsAsync(It.Is<List<Requirement>>(
            requirements => requirements.Count == 1 && requirements[0] == first &&
                            requirements[0].SourceDocumentLevel == level)), Times.Once);
    }

    [TestMethod]
    public async Task DismissingFilePicker_DoesNotExtractOrReplaceReview()
    {
        var viewModel = CreateViewModel();
        var previousCandidate = new ScrapedRequirementCandidate();
        viewModel.ScrapedRequirements.Add(previousCandidate);
        viewModel.SelectedLocalDocumentLevel = RequirementSourceLevel.L0;

        await viewModel.ExtractClassifiedDocumentCommand.ExecuteAsync(null);

        Assert.AreSame(previousCandidate, viewModel.ScrapedRequirements.Single());
        Assert.AreEqual(RequirementSourceLevel.L0, viewModel.SelectedLocalDocumentLevel);
        _parser.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(true, "Local extraction canceled.")]
    [DataRow(false, "Local extraction failed: Document is empty.")]
    public async Task UnsuccessfulExtraction_DoesNotReplaceReview(bool canceled, string expectedStatus)
    {
        var viewModel = CreateViewModel();
        var previousCandidate = new ScrapedRequirementCandidate();
        viewModel.ScrapedRequirements.Add(previousCandidate);
        viewModel.SelectedLocalDocumentLevel = RequirementSourceLevel.L0;
        _fileDialog.Setup(dialog => dialog.ShowOpenFile("Select L0 source document", It.IsAny<string>(), null))
            .Returns(DocumentPath);
        Exception failure = canceled ? new OperationCanceledException() : new System.IO.InvalidDataException("Document is empty.");
        _parser.Setup(parser => parser.ParseLocalDocumentAsync(
                DocumentPath, It.IsAny<Action<string>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        await viewModel.ExtractClassifiedDocumentCommand.ExecuteAsync(null);

        Assert.AreEqual(expectedStatus, viewModel.AttachmentScraperStatusText);
        Assert.AreSame(previousCandidate, viewModel.ScrapedRequirements.Single());
        Assert.IsFalse(viewModel.IsAttachmentScraping);
    }

    private WorkshopReproViewModel CreateViewModel()
    {
        _mediator.SetupGet(mediator => mediator.Requirements).Returns(new ObservableCollection<Requirement>());
        return new WorkshopReproViewModel(_mediator.Object, Mock.Of<IWorkspaceDiagnosticsService>(),
            _fileDialog.Object, _parser.Object, Mock.Of<IWorkspaceContext>(), Mock.Of<IJamaConnectService>(),
            Mock.Of<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services.ITestCaseGenerationService>());
    }
}
