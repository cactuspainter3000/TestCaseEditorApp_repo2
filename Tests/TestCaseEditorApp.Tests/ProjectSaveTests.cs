using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.Title.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class ProjectSaveTests
{
    [TestMethod]
    public async Task VisibleSaveButton_ExecutesProjectSaveAndReportsSuccessOrFailure()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TestCaseEditorApp.csproj")))
            root = root.Parent;
        Assert.IsNotNull(root);

        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var view = XDocument.Load(Path.Combine(root.FullName, "MVVM", "Views", "MainWindow.xaml"));
        var button = view.Descendants(presentation + "Button")
            .Single(element => (string?)element.Attribute("Content") == "Save Project");
        Assert.AreEqual("{Binding TitleVM.SaveProjectCommand}", (string?)button.Attribute("Command"));

        Action<NewProjectEvents.ProjectCreated>? created = null;
        Action<NewProjectEvents.ProjectSaved>? saved = null;
        Action<NewProjectEvents.ProjectOperationError>? error = null;
        var mediator = new Mock<INewProjectMediator>();
        mediator.Setup(m => m.Subscribe(It.IsAny<Action<NewProjectEvents.ProjectCreated>>()))
            .Callback<Action<NewProjectEvents.ProjectCreated>>(handler => created = handler);
        mediator.Setup(m => m.Subscribe(It.IsAny<Action<NewProjectEvents.ProjectSaved>>()))
            .Callback<Action<NewProjectEvents.ProjectSaved>>(handler => saved = handler);
        mediator.Setup(m => m.Subscribe(It.IsAny<Action<NewProjectEvents.ProjectOperationError>>()))
            .Callback<Action<NewProjectEvents.ProjectOperationError>>(handler => error = handler);
        mediator.Setup(m => m.SaveProjectAsync(true)).Returns(() =>
        {
            saved!(new NewProjectEvents.ProjectSaved());
            return Task.CompletedTask;
        });

        var vm = new TitleViewModel(mediator.Object, Mock.Of<IOpenProjectMediator>(),
            Mock.Of<ISettingsDialogService>(), NullLogger<TitleViewModel>.Instance);
        var command = (IAsyncRelayCommand)vm.SaveProjectCommand;
        Assert.IsFalse(command.CanExecute(null));
        created!(new NewProjectEvents.ProjectCreated { WorkspaceName = "Test" });
        Assert.IsTrue(command.CanExecute(null));
        await command.ExecuteAsync(null);
        Assert.AreEqual("Last save succeeded", vm.SaveStatus);
        mediator.Verify(m => m.SaveProjectAsync(true), Times.Once);

        error!(new NewProjectEvents.ProjectOperationError { Operation = "SaveProject", ErrorMessage = "Test failure" });
        Assert.AreEqual("Save failed: Test failure", vm.SaveStatus);
    }

    [TestMethod]
    public void PrepareProjectSave_PreservesProjectMetadataAndSavesCurrentRequirements()
    {
        var path = Path.GetTempFileName();
        try
        {
            var sync = DateTime.UtcNow.AddDays(-1);
            var original = new Workspace
            {
                Name = "Test workspace", JamaProjectId = 123, JamaProject = "123",
                JamaProjectName = "Non-production", AnythingLLMWorkspaceSlug = "test-workspace",
                LastJamaSyncUtc = sync, SaveCount = 4, SourceDocPath = "sample.txt"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(original));
            var requirement = new Requirement
            {
                Item = "LOCAL-1", ApiId = "42", SourceDocumentLevel = RequirementSourceLevel.L0,
                SourceDocumentName = "sample.txt",
                GeneratedTestCases = { new TestCase { Id = "TC-1" } }
            };

            var prepared = ProjectWorkspaceSave.Prepare(path, [requirement]);
            File.WriteAllText(path, JsonSerializer.Serialize(prepared));
            var restored = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path))!;

            Assert.AreEqual(123, restored.JamaProjectId);
            Assert.AreEqual("123", restored.JamaProject);
            Assert.AreEqual("Non-production", restored.JamaProjectName);
            Assert.AreEqual("test-workspace", restored.AnythingLLMWorkspaceSlug);
            Assert.AreEqual("sample.txt", restored.SourceDocPath);
            Assert.AreEqual(sync, restored.LastJamaSyncUtc);
            Assert.AreEqual(5, restored.SaveCount);
            Assert.IsTrue(WorkspaceService.ShouldPromptForRefresh(restored, restored.LastJamaSyncUtc));
            var savedRequirement = restored.Requirements.Single();
            Assert.AreEqual("LOCAL-1", savedRequirement.Item);
            Assert.AreEqual("42", savedRequirement.ApiId);
            Assert.AreEqual(RequirementSourceLevel.L0, savedRequirement.SourceDocumentLevel);
            Assert.AreEqual("TC-1", savedRequirement.GeneratedTestCases.Single().Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void PrepareProjectSave_RejectsMissingOrInvalidWorkspace()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "null");
            Assert.ThrowsException<InvalidDataException>(() => ProjectWorkspaceSave.Prepare(path, []));
            File.Delete(path);
            Assert.ThrowsException<FileNotFoundException>(() => ProjectWorkspaceSave.Prepare(path, []));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
