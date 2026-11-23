using System.IO;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services;

public static class ProjectWorkspaceSave
{
    public static Workspace Prepare(string path, IEnumerable<Requirement> requirements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(requirements);

        var workspace = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The project file does not contain a workspace.");
        workspace.Requirements = requirements.ToList();
        workspace.LastSavedUtc = DateTime.UtcNow;
        workspace.SaveCount++;
        return workspace;
    }
}
