// WorkspaceService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

public static class WorkspaceService
{
    static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void Save(string path, Workspace ws)
    {
        ws.LastSavedUtc = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(ws, _json);

#if DEBUG
        try
        {
            var debugPath = Path.ChangeExtension(path, ".debug.json");
            File.WriteAllText(debugPath, json);
            Debug.WriteLine($"[Save] Wrote debug snapshot: {debugPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Debug snapshot failed: {ex.Message}");
        }
#endif

        File.WriteAllText(path, json);
    }


    public static Workspace Load(string path)
    {
        var json = File.ReadAllText(path);
        var ws = JsonSerializer.Deserialize<Workspace>(json, _json) ?? new Workspace();

        // Probe 3: log what came back
        try
        {
            var reqCount = ws.Requirements?.Count ?? 0;
            var withResponse = ws.Requirements?.Count(r =>
                                   !string.IsNullOrWhiteSpace(r?.CurrentResponse?.Output)) ?? 0;

            Debug.WriteLine($"[Load] Requirements: {reqCount}");
            Debug.WriteLine($"[Load] Reqs with saved response: {withResponse}");
        }
        catch { /* best-effort logging only */ }

        // simple forward-compat check
        if (ws.Version > Workspace.SchemaVersion)
            throw new InvalidOperationException(
                $"Workspace version {ws.Version} is newer than app schema {Workspace.SchemaVersion}.");

        return ws;
    }

}

