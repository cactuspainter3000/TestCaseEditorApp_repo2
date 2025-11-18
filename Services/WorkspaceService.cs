// WorkspaceService.cs
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
        // Create backup of previous version before overwriting
        if (File.Exists(path))
        {
            try
            {
                var backupPath = path + ".bak";
                File.Copy(path, backupPath, overwrite: true);
                Debug.WriteLine($"[Save] Created backup: {backupPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Save] Backup failed (continuing anyway): {ex.Message}");
            }
        }

        // Update metadata
        ws.LastSavedUtc = DateTime.UtcNow;
        ws.SaveCount++;
        if (string.IsNullOrEmpty(ws.CreatedBy))
        {
            ws.CreatedBy = Environment.UserName;
            ws.CreatedUtc = DateTime.UtcNow;
        }

        // Update requirement status summary for quick overview
        ws.RequirementStatus.Clear();
        foreach (var req in ws.Requirements ?? Enumerable.Empty<Requirement>())
        {
            var key = req.Item ?? req.GlobalId;
            if (!string.IsNullOrEmpty(key))
            {
                ws.RequirementStatus[key] = new WorkStatus
                {
                    HasQuestions = req.ClarifyingQuestions?.Count > 0,
                    HasTestCases = !string.IsNullOrWhiteSpace(req.CurrentResponse?.Output),
                    HasAssumptions = req.SelectedAssumptionKeys?.Count > 0,
                    LastModifiedUtc = DateTime.UtcNow
                };
            }
        }

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

        // Migration logic for future schema changes
        if (ws.Version < Workspace.SchemaVersion)
        {
            Debug.WriteLine($"[Load] Migrating workspace from v{ws.Version} to v{Workspace.SchemaVersion}");
            // Add migration methods here as schema evolves
            // Example: if (ws.Version == 1) MigrateV1ToV2(ws);
            ws.Version = Workspace.SchemaVersion;
        }

        // Probe: log what came back
        try
        {
            var reqCount = ws.Requirements?.Count ?? 0;
            var withResponse = ws.Requirements?.Count(r =>
                                   !string.IsNullOrWhiteSpace(r?.CurrentResponse?.Output)) ?? 0;
            var withQuestions = ws.Requirements?.Count(r =>
                                   r?.ClarifyingQuestions?.Count > 0) ?? 0;

            Debug.WriteLine($"[Load] Requirements: {reqCount}");
            Debug.WriteLine($"[Load] Reqs with test cases: {withResponse}");
            Debug.WriteLine($"[Load] Reqs with questions: {withQuestions}");
            Debug.WriteLine($"[Load] Workspace saved {ws.SaveCount} times by {ws.CreatedBy ?? "unknown"}");
        }
        catch { /* best-effort logging only */ }

        // Forward-compatibility check
        if (ws.Version > Workspace.SchemaVersion)
            throw new InvalidOperationException(
                $"Workspace version {ws.Version} is newer than app schema {Workspace.SchemaVersion}. Please update the application.");

        return ws;
    }

}

