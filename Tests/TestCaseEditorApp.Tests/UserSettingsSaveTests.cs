using System.IO;
using System.Xml.Linq;
using Moq;
using TestCaseEditorApp.MVVM.Services.Theme;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.Tests;

[TestClass]
public class UserSettingsSaveTests
{
    [TestMethod]
    public async Task SettingsSaveButton_UsesGeneratedCommandAndPersistsEnteredKey()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "TestCaseEditorApp.csproj")))
            root = root.Parent;
        Assert.IsNotNull(root, "Could not locate the repository root.");

        var view = XDocument.Load(Path.Combine(root.FullName, "MVVM", "Views", "UserSettingsWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var binding = view.Descendants(presentation + "Button")
            .Select(button => (string?)button.Attribute("Command"))
            .SingleOrDefault(command => command == "{Binding SaveCommand}");
        Assert.IsNotNull(binding, "The Settings Save button must bind to the generated SaveCommand.");
        Assert.IsNotNull(typeof(UserSettingsViewModel).GetProperty("SaveCommand"));

        var stored = AppUserSettings.Empty();
        var settings = new Mock<IUserSettingsService>();
        settings.Setup(service => service.LoadSettings()).Returns(() => stored);
        settings.Setup(service => service.SaveSettings(It.IsAny<AppUserSettings>()))
            .Callback<AppUserSettings>(value => stored = value);
        var viewModel = new UserSettingsViewModel(settings.Object, new ThemeService());
        viewModel.AnythingLlmApiKey = "test-key-not-a-credential";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual("test-key-not-a-credential", stored.AnythingLlmApiKey);
        settings.Verify(service => service.ApplySettingsToEnvironment(stored), Times.Once);
        var reopened = new UserSettingsViewModel(settings.Object, new ThemeService());
        Assert.AreEqual("test-key-not-a-credential", reopened.AnythingLlmApiKey);
    }
}
