// WorkspaceService.cs
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TestCaseEditorApp.MVVM.Models;

public static class WorkspaceService
{
    static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
        // Removed DefaultIgnoreCondition.WhenWritingNull - ImportSource should always be included
    };

    private static Microsoft.Extensions.Logging.ILogger? GetLogger()
    {
        try
        {
            var sp = TestCaseEditorApp.App.ServiceProvider;
            if (sp == null) return null;
            var factory = sp.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory)) as Microsoft.Extensions.Logging.ILoggerFactory;
            return factory?.CreateLogger("WorkspaceService");
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string path, Workspace ws)
    {
        var logger = GetLogger();
        logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Save invoked for: {path}", null, (s, e) => s ?? string.Empty);
        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Save invoked for: {path}");

        CreateBackupIfExists(path);
        UpdateWorkspaceMetadata(ws);
        UpdateRequirementStatusSummary(ws);

        string json = JsonSerializer.Serialize(ws, _json) ?? string.Empty;
        LogPersistenceDebug(ws, json);

        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] JSON serialized ({json.Length} bytes)");
        logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] JSON serialized ({json.Length} bytes)", null, (s, e) => s ?? string.Empty);

        WriteStagingCopy(path, json, logger);
        WriteDebugSnapshot(path, json);
        WriteWorkspaceAtomically(path, json);
        AppendWhereSavedLog(path, logger, isFallback: false);
        WriteCompanionMarker(path, logger, isFallback: false);
        WriteTempFallbackCopies(path, json, logger);
        VerifyFinalDestinationWithFallback(path, json, logger);
    }

    private static void CreateBackupIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var backupPath = path + ".bak";
            File.Copy(path, backupPath, overwrite: true);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Created backup: {backupPath}");
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Backup failed (continuing anyway): {ex.Message}");
        }
    }

    private static void UpdateWorkspaceMetadata(Workspace ws)
    {
        ws.LastSavedUtc = DateTime.UtcNow;
        ws.SaveCount++;
        if (string.IsNullOrEmpty(ws.CreatedBy))
        {
            ws.CreatedBy = Environment.UserName;
            ws.CreatedUtc = DateTime.UtcNow;
        }
    }

    private static void UpdateRequirementStatusSummary(Workspace ws)
    {
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
    }

    private static void LogPersistenceDebug(Workspace ws, string json)
    {
        var hasGeneratedTestCasesInJson = json.Contains("GeneratedTestCases") && json.Contains("\"Id\":");
        var totalTestCasesInWorkspace = ws.Requirements?.Sum(r => r.GeneratedTestCases?.Count ?? 0) ?? 0;
        TestCaseEditorApp.Services.Logging.Log.Info($"[Save] 🔍 PERSISTENCE DEBUG: Serializing {totalTestCasesInWorkspace} total GeneratedTestCases");
        TestCaseEditorApp.Services.Logging.Log.Info($"[Save] 🔍 PERSISTENCE DEBUG: JSON contains GeneratedTestCases data: {hasGeneratedTestCasesInJson}");

        try
        {
            TestCaseEditorApp.Services.Logging.Log.Info(
                $"[Save] Workspace identity: Project='{ws.Name ?? "<none>"}', AnythingLLMName='{ws.AnythingLLMWorkspaceName ?? "<none>"}', AnythingLLMSlug='{ws.AnythingLLMWorkspaceSlug ?? "<none>"}', JamaProjectId={(ws.JamaProjectId?.ToString() ?? "<none>")}, JamaProjectName='{ws.JamaProjectName ?? ws.JamaProject ?? "<none>"}'");
        }
        catch
        {
            // best-effort logging only
        }
    }

    private static void WriteStagingCopy(string path, string json, Microsoft.Extensions.Logging.ILogger? logger)
    {
        try
        {
            var stagingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "Staging");
            Directory.CreateDirectory(stagingDir);
            var stagingPath = Path.Combine(stagingDir, Path.GetFileName(path));
            File.WriteAllText(stagingPath, json, Encoding.UTF8);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote staging copy: {stagingPath}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote staging copy: {stagingPath}", null, (s, e) => s ?? string.Empty);

            try
            {
                var stagingMeta = stagingPath + ".meta.txt";
                WriteMetaFile(stagingMeta, stagingPath, json, includePreview: false);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write staging meta: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write staging copy: {ex.Message}");
        }
    }

    private static void WriteDebugSnapshot(string path, string json)
    {
#if DEBUG
        try
        {
            var debugPath = Path.ChangeExtension(path, ".debug.json");
            File.WriteAllText(debugPath, json);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote debug snapshot: {debugPath}");
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Debug snapshot failed: {ex.Message}");
        }
#endif
    }

    private static void WriteWorkspaceAtomically(string path, string json)
    {
        try
        {
            var targetDir = Path.GetDirectoryName(path) ?? Path.GetTempPath();
            Directory.CreateDirectory(targetDir);
            var tmpFile = Path.Combine(targetDir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(tmpFile, json, Encoding.UTF8);

            if (File.Exists(path))
            {
                try
                {
                    // Attempt an atomic replace (preserves attributes where possible)
                    var backupReplace = path + ".bakreplace";
                    File.Replace(tmpFile, path, backupReplace, ignoreMetadataErrors: true);
                    if (File.Exists(backupReplace)) File.Delete(backupReplace);
                }
                catch
                {
                    // Best-effort fallback: remove the destination then move
                    try { File.Delete(path); } catch { }
                    File.Move(tmpFile, path);
                }
            }
            else
            {
                File.Move(tmpFile, path);
            }

            try
            {
                var metaPath = path + ".meta.txt";
                WriteMetaFile(metaPath, path, json, includePreview: true);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write meta: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Write failed: {ex.Message}");
            throw;
        }
    }

    private static void AppendWhereSavedLog(string path, Microsoft.Extensions.Logging.ILogger? logger, bool isFallback)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "where-saved.log");
            var prefix = isFallback ? "Fallback saved workspace to" : "Saved workspace to";
            var entry = $"{DateTime.UtcNow:o}\t{prefix}: {path}\tUser:{Environment.UserName}" + Environment.NewLine;
            File.AppendAllText(logPath, entry);
            var message = isFallback
                ? $"[Save] Appended fallback where-saved log: {logPath}"
                : $"[Save] Appended where-saved log: {logPath}";
            TestCaseEditorApp.Services.Logging.Log.Debug(message);
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Information, new Microsoft.Extensions.Logging.EventId(0), message, null, (s, e) => s ?? string.Empty);
        }
        catch (Exception ex)
        {
            var errorLabel = isFallback ? "append fallback where-saved log" : "write where-saved log";
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to {errorLabel}: {ex.Message}");
        }
    }

    private static void WriteCompanionMarker(string path, Microsoft.Extensions.Logging.ILogger? logger, bool isFallback)
    {
        try
        {
            var markerPath = path + ".saved.txt";
            var label = isFallback ? "Saved (fallback)" : "Saved";
            var markerContent = $"{label}: {DateTime.UtcNow:o}\r\nPath: {path}\r\nUser: {Environment.UserName}\r\n";
            File.WriteAllText(markerPath, markerContent);
            var message = isFallback
                ? $"[Save] Wrote fallback marker: {markerPath}"
                : $"[Save] Wrote companion marker: {markerPath}";
            TestCaseEditorApp.Services.Logging.Log.Debug(message);
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), message, null, (s, e) => s ?? string.Empty);
        }
        catch (Exception ex)
        {
            var errorLabel = isFallback ? "fallback marker" : "companion marker";
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write {errorLabel}: {ex.Message}");
        }
    }

    private static void WriteTempFallbackCopies(string path, string json, Microsoft.Extensions.Logging.ILogger? logger)
    {
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "TestCaseEditorApp");
            Directory.CreateDirectory(tmpDir);
            var tmpLog = Path.Combine(tmpDir, "where-saved.log");
            var tmpEntry = $"{DateTime.UtcNow:o}\tSaved workspace to: {path}\tUser:{Environment.UserName}" + Environment.NewLine;
            File.AppendAllText(tmpLog, tmpEntry);
            var tmpCopy = Path.Combine(tmpDir, Path.GetFileName(path));
            File.WriteAllText(tmpCopy, json);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote fallback copies to: {tmpDir}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Warning, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote fallback copies to: {tmpDir}", null, (s, e) => s ?? string.Empty);
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed fallback diagnostic writes: {ex.Message}");
        }
    }

    private static void VerifyFinalDestinationWithFallback(string path, string json, Microsoft.Extensions.Logging.ILogger? logger)
    {
        try
        {
            if (File.Exists(path))
            {
                return;
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Final destination missing after write: {path}. Attempting fallback.");

            var stagingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "Staging", Path.GetFileName(path));
            var wroteFallback = TryRestoreFromStaging(stagingPath, path, logger);

            if (!wroteFallback)
            {
                wroteFallback = TryDirectFallbackWrite(path, json, logger);
            }

            if (wroteFallback && File.Exists(path))
            {
                try
                {
                    var metaPath = path + ".meta.txt";
                    WriteMetaFile(metaPath, path, json, includePreview: true);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote fallback meta: {metaPath}");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write fallback meta: {ex.Message}");
                }

                WriteCompanionMarker(path, logger, isFallback: true);
                AppendWhereSavedLog(path, logger, isFallback: true);
            }
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Final verification failed: {ex.Message}");
        }
    }

    private static bool TryRestoreFromStaging(string stagingPath, string destinationPath, Microsoft.Extensions.Logging.ILogger? logger)
    {
        if (!File.Exists(stagingPath))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Path.GetTempPath());
            File.Copy(stagingPath, destinationPath, overwrite: true);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Restored from staging: {stagingPath} -> {destinationPath}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Information, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Restored from staging: {stagingPath} -> {destinationPath}", null, (s, e) => s ?? string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to copy staging to destination: {ex.Message}");
            return false;
        }
    }

    private static bool TryDirectFallbackWrite(string path, string json, Microsoft.Extensions.Logging.ILogger? logger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Path.GetTempPath());
            File.WriteAllText(path, json, Encoding.UTF8);
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote direct fallback to destination: {path}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Information, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote direct fallback to destination: {path}", null, (s, e) => s ?? string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Direct fallback write failed: {ex.Message}");
            return false;
        }
    }

    private static void WriteMetaFile(string metaPath, string targetPath, string json, bool includePreview)
    {
        byte[] hashBytes;
        using (var sha = SHA256.Create())
        {
            hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json ?? string.Empty));
        }

        var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        var meta = new StringBuilder();
        meta.AppendLine($"SavedUtc: {DateTime.UtcNow:o}");
        meta.AppendLine($"Path: {targetPath}");
        meta.AppendLine($"User: {Environment.UserName}");
        meta.AppendLine($"Bytes: {Encoding.UTF8.GetByteCount(json ?? string.Empty)}");
        meta.AppendLine($"SHA256: {hashHex}");

        if (includePreview)
        {
            var safeJson = json ?? string.Empty;
            var preview = safeJson.Length > 1024 ? safeJson.Substring(0, 1024) : safeJson;
            meta.AppendLine("PreviewStart:");
            meta.AppendLine(preview);
        }

        File.WriteAllText(metaPath, meta.ToString(), Encoding.UTF8);
    }

    public static Workspace Load(string path)
    {
        var json = File.ReadAllText(path);
        
        // 🔍 PERSISTENCE DEBUG: Check if GeneratedTestCases exist in JSON before deserialization
        var hasGeneratedTestCasesInJson = json.Contains("GeneratedTestCases") && json.Contains("\"Id\":");
        TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 PERSISTENCE DEBUG: JSON contains GeneratedTestCases data: {hasGeneratedTestCasesInJson}");
        
        var ws = JsonSerializer.Deserialize<Workspace>(json, _json) ?? new Workspace();

        // Migration logic for future schema changes
        if (ws.Version < Workspace.SchemaVersion)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Load] Migrating workspace from v{ws.Version} to v{Workspace.SchemaVersion}");
            // Add migration methods here as schema evolves
            // Example: if (ws.Version == 1) MigrateV1ToV2(ws);
            ws.Version = Workspace.SchemaVersion;
        }

        ws.AnythingLLMWorkspaceName ??= ws.Name;
        ws.JamaProjectName ??= ws.JamaProject;

        // Auto-detect ImportSource for existing workspaces (helpful for troubleshooting)
        if (string.IsNullOrEmpty(ws.ImportSource))
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[Load] ImportSource is missing - attempting auto-detection...");
            
            // Check if this looks like a Jama workspace (has GlobalId values in requirements)
            var hasJamaIds = ws.Requirements?.Any(r => !string.IsNullOrEmpty(r.GlobalId)) ?? false;
            
            if (hasJamaIds)
            {
                ws.ImportSource = "Jama";
                TestCaseEditorApp.Services.Logging.Log.Info($"[Load] Auto-detected Jama workspace (has GlobalId values), set ImportSource = 'Jama'");
            }
            else if (!string.IsNullOrEmpty(ws.SourceDocPath))
            {
                ws.ImportSource = "Document";
                TestCaseEditorApp.Services.Logging.Log.Info($"[Load] Auto-detected Document workspace (has SourceDocPath), set ImportSource = 'Document'");
            }
            else
            {
                ws.ImportSource = "Manual";
                TestCaseEditorApp.Services.Logging.Log.Info($"[Load] Auto-detected Manual workspace (fallback), set ImportSource = 'Manual'");
            }
        }

        // 🔍 DEBUG: Log Jama project information from loaded workspace
        TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 JAMA DEBUG: JamaProject field: '{ws.JamaProject}' (null: {ws.JamaProject == null})");
        TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 JAMA DEBUG: JamaTestPlan field: '{ws.JamaTestPlan}' (null: {ws.JamaTestPlan == null})");
        TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 JAMA DEBUG: ImportSource: '{ws.ImportSource}'");

        // Probe: log what came back
        try
        {
            var reqCount = ws.Requirements?.Count ?? 0;
            var withResponse = ws.Requirements?.Count(r =>
                                   !string.IsNullOrWhiteSpace(r?.CurrentResponse?.Output)) ?? 0;
            var withQuestions = ws.Requirements?.Count(r =>
                                   r?.ClarifyingQuestions?.Count > 0) ?? 0;
            
            // 🔍 PERSISTENCE DEBUG: Check if GeneratedTestCases are loaded correctly
            var totalGeneratedTestCases = ws.Requirements?.Sum(r => r.GeneratedTestCases?.Count ?? 0) ?? 0;
            var requirementsWithGeneratedTestCases = ws.Requirements?.Count(r => r.GeneratedTestCases?.Any() == true) ?? 0;

            TestCaseEditorApp.Services.Logging.Log.Debug($"[Load] Requirements: {reqCount}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Load] Reqs with test cases (old model): {withResponse}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 PERSISTENCE DEBUG: Total GeneratedTestCases loaded: {totalGeneratedTestCases}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 PERSISTENCE DEBUG: Requirements with GeneratedTestCases: {requirementsWithGeneratedTestCases}/{reqCount}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Load] Reqs with questions: {withQuestions}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Load] Workspace saved {ws.SaveCount} times by {ws.CreatedBy ?? "unknown"}");
            
            // 🔍 PERSISTENCE DEBUG: Log specific test case IDs per requirement
            foreach (var req in ws.Requirements?.Where(r => r.GeneratedTestCases?.Any() == true) ?? Enumerable.Empty<Requirement>())
            {
                var testCaseIds = string.Join(", ", req.GeneratedTestCases?.Select(tc => tc.Id) ?? new List<string>());
                TestCaseEditorApp.Services.Logging.Log.Info($"[Load] 🔍 PERSISTENCE DEBUG: Requirement '{req.Item}' loaded {req.GeneratedTestCases?.Count ?? 0} test cases: [{testCaseIds}]");
            }
        }
        catch { /* best-effort logging only */ }

        // Forward-compatibility check
        if (ws.Version > Workspace.SchemaVersion)
            throw new InvalidOperationException(
                $"Workspace version {ws.Version} is newer than app schema {Workspace.SchemaVersion}. Please update the application.");

        return ws;
    }

}

