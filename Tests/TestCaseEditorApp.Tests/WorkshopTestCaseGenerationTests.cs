using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Moq;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;
using TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class WorkshopTestCaseGenerationTests
{
    private readonly Mock<IRequirementsMediator> _mediator = new();
    private readonly Mock<IWorkspaceContext> _context = new();
    private readonly Mock<ITestCaseGenerationService> _generation = new();
    private readonly Requirement _requirement = new()
    {
        Item = "LOCAL-1", ApiId = "1234", Name = "Warning", SourceDocumentLevel = RequirementSourceLevel.L0
    };
    private readonly Workspace _workspace = new() { AnythingLLMWorkspaceSlug = "demo" };

    [TestMethod]
    public void TestCaseTab_BindsGenerationCommandAndStatus()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TestCaseEditorApp.csproj")))
            root = root.Parent;
        Assert.IsNotNull(root);
        var view = XDocument.Load(Path.Combine(root.FullName, "MVVM", "Domains", "Workshop", "Views", "WorkshopDesignerReproView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var button = view.Descendants(presentation + "Button")
            .Single(element => (string?)element.Attribute("Content") == "Generate Test Cases");
        Assert.AreEqual("{Binding GenerateTestCasesCommand}", (string?)button.Attribute("Command"));
        Assert.IsTrue(view.Descendants(presentation + "TextBox").Any(element =>
            (string?)element.Attribute("Text") == "{Binding TestCaseGenerationStatus, Mode=OneWay}"));
    }

    [TestMethod]
    public async Task Generation_AppendsLinkedCasesWithStepsAndSurvivesSerialization()
    {
        _requirement.GeneratedTestCases.Add(new TestCase { Id = "EXISTING" });
        var vm = CreateViewModel();
        _generation.Setup(service => service.GenerateTestCasesForSingleRequirementAsync(
            _requirement, It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([GeneratedCase(), GeneratedCase()]);

        await vm.GenerateTestCasesCommand.ExecuteAsync(null);

        _generation.Verify(service => service.SetWorkspaceContext("demo"), Times.Once);
        Assert.AreEqual(2, _requirement.GeneratedTestCases.Count);
        Assert.AreEqual("TC-1", vm.SelectedTestCase?.Id);
        _mediator.Verify(mediator => mediator.UpdateRequirement(_requirement,
            It.Is<IReadOnlyList<string>>(fields => fields.Contains(nameof(Requirement.GeneratedTestCases)))), Times.Once);
        Requirement restored;
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(_workspace));
            var prepared = ProjectWorkspaceSave.Prepare(path, _mediator.Object.Requirements);
            File.WriteAllText(path, JsonSerializer.Serialize(prepared));
            restored = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path))!.Requirements.Single();
        }
        finally
        {
            File.Delete(path);
        }
        Assert.AreEqual("LOCAL-1", restored.Item);
        Assert.AreEqual("1234", restored.ApiId);
        var testCase = restored.GeneratedTestCases.Single(test => test.Id == "TC-1");
        Assert.AreEqual("Inject fault", testCase.Steps.Single().StepAction);
        Assert.AreEqual("Warning displayed", testCase.Steps.Single().StepExpectedResult);
        StringAssert.Contains(testCase.TestCaseText, "Power on");
        StringAssert.Contains(testCase.TestCaseText, "Clear fault");
        StringAssert.Contains(vm.TestCaseGenerationStatus, "Save Project");
        Assert.IsFalse(vm.IsGeneratingTestCases);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("wrong-link")]
    [DataRow("exception")]
    public async Task Generation_FailureDoesNotAddCases(string failure)
    {
        var vm = CreateViewModel();
        var setup = _generation.Setup(service => service.GenerateTestCasesForSingleRequirementAsync(
            _requirement, It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()));
        if (failure == "exception")
            setup.ThrowsAsync(new InvalidOperationException("Connection unavailable"));
        else
        {
            var result = GeneratedCase();
            result.CoveredRequirementIds = ["OTHER"];
            setup.ReturnsAsync(failure == "empty" ? [] : [result]);
        }

        await vm.GenerateTestCasesCommand.ExecuteAsync(null);

        Assert.AreEqual(0, _requirement.GeneratedTestCases.Count);
        Assert.IsFalse(vm.IsGeneratingTestCases);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.TestCaseGenerationStatus));
        _mediator.Verify(mediator => mediator.UpdateRequirement(
            It.IsAny<Requirement>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [TestMethod]
    public async Task Generation_RejectsResultsAfterProjectChangeAndPreventsConcurrentRuns()
    {
        var vm = CreateViewModel();
        var pending = new TaskCompletionSource<List<LLMTestCase>>();
        _generation.Setup(service => service.GenerateTestCasesForSingleRequirementAsync(
            _requirement, It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()))
            .Returns(pending.Task);

        var running = vm.GenerateTestCasesCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.GenerateTestCasesCommand.CanExecute(null));
        _context.SetupGet(context => context.CurrentWorkspace).Returns(new Workspace());
        pending.SetResult([GeneratedCase()]);
        await running;

        Assert.AreEqual(0, _requirement.GeneratedTestCases.Count);
        StringAssert.Contains(vm.TestCaseGenerationStatus, "project or requirement changed");
        Assert.IsTrue(vm.GenerateTestCasesCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Generation_WithoutWorkspaceAssociationReportsErrorWithoutCallingLlm()
    {
        var vm = CreateViewModel();
        _workspace.AnythingLLMWorkspaceSlug = null;
        await vm.GenerateTestCasesCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.TestCaseGenerationStatus, "no AnythingLLM workspace association");
        _generation.Verify(service => service.GenerateTestCasesForSingleRequirementAsync(
            It.IsAny<Requirement>(), It.IsAny<Action<string, int, int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private WorkshopReproViewModel CreateViewModel()
    {
        _workspace.Requirements = [_requirement];
        _mediator.SetupGet(mediator => mediator.Requirements).Returns(new ObservableCollection<Requirement> { _requirement });
        _mediator.SetupGet(mediator => mediator.CurrentRequirement).Returns(_requirement);
        _context.SetupGet(context => context.CurrentWorkspace).Returns(_workspace);
        return new WorkshopReproViewModel(_mediator.Object, Mock.Of<IWorkspaceDiagnosticsService>(),
            Mock.Of<IFileDialogService>(), Mock.Of<IJamaDocumentParserService>(), _context.Object,
            Mock.Of<IJamaConnectService>(), _generation.Object, Mock.Of<IPersistenceService>());
    }

    private static LLMTestCase GeneratedCase() => new()
    {
        Id = "TC-1", Title = "Verify warning", Description = "Fault warning test",
        Preconditions = "Power on", Postconditions = "Clear fault", ExpectedResult = "Warning displayed",
        CoveredRequirementIds = ["LOCAL-1"],
        Steps = [new LLMTestStep { StepNumber = 1, Action = "Inject fault", ExpectedResult = "Warning displayed" }]
    };
}
