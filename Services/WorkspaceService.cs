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
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
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
        logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Save invoked for: {path}", null, (s,e) => s ?? string.Empty);
        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Save invoked for: {path}");
        Debug.WriteLine($"[Save] Save invoked for: {path}");

        // Create backup of previous version before overwriting
        if (File.Exists(path))
        {
            try
            {
                    var backupPath = path + ".bak";
                File.Copy(path, backupPath, overwrite: true);
                Debug.WriteLine($"[Save] Created backup: {backupPath}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Created backup: {backupPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Save] Backup failed (continuing anyway): {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Backup failed (continuing anyway): {ex.Message}");
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
        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] JSON serialized ({json?.Length ?? 0} bytes)");
        Debug.WriteLine($"[Save] JSON serialized ({json?.Length ?? 0} bytes)");
        logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] JSON serialized ({json?.Length ?? 0} bytes)", null, (s,e) => s ?? string.Empty);

        // Always write a guaranteed local staging copy in %LOCALAPPDATA% so we
        // have a recoverable copy even if the final destination is redirected
        // (OneDrive) or a sync/antivirus agent interferes. This is a small
        // transparent copy kept next to the app's local data directory.
        try
        {
            var stagingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "Staging");
            Directory.CreateDirectory(stagingDir);
            var stagingPath = Path.Combine(stagingDir, Path.GetFileName(path));
                File.WriteAllText(stagingPath, json, Encoding.UTF8);
            Debug.WriteLine($"[Save] Wrote staging copy: {stagingPath}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote staging copy: {stagingPath}");
                logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote staging copy: {stagingPath}", null, (s,e) => s ?? string.Empty);

            // Also write a small staging meta so the file can be validated independently
            try
            {
                var stagingMeta = stagingPath + ".meta.txt";
                byte[] hashBytes;
                using (var sha = SHA256.Create())
                {
                    hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                }
                var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                var meta = new StringBuilder();
                meta.AppendLine($"SavedUtc: {DateTime.UtcNow:o}");
                meta.AppendLine($"Path: {stagingPath}");
                meta.AppendLine($"User: {Environment.UserName}");
                meta.AppendLine($"Bytes: {Encoding.UTF8.GetByteCount(json)}");
                meta.AppendLine($"SHA256: {hashHex}");
                File.WriteAllText(stagingMeta, meta.ToString(), Encoding.UTF8);
            }
                catch (Exception ex)
            {
                Debug.WriteLine($"[Save] Failed to write staging meta: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write staging meta: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Failed to write staging copy: {ex.Message}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write staging copy: {ex.Message}");
        }

#if DEBUG
            try
            {
                var debugPath = Path.ChangeExtension(path, ".debug.json");
                File.WriteAllText(debugPath, json);
                Debug.WriteLine($"[Save] Wrote debug snapshot: {debugPath}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote debug snapshot: {debugPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Save] Debug snapshot failed: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Debug snapshot failed: {ex.Message}");
            }
#endif

        // Write the workspace JSON using an atomic write technique: write to a
        // temporary file in the same directory and then replace/move into place.
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

            // Write a small meta diagnostic next to the saved file containing
            // the JSON byte length, SHA256 hash and a short preview so we can
            // rapidly diagnose cases where the on-disk file appears empty.
            try
            {
                var metaPath = path + ".meta.txt";
                byte[] hashBytes;
                using (var sha = SHA256.Create())
                {
                    hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                }
                var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                var preview = json.Length > 1024 ? json.Substring(0, 1024) : json;
                var meta = new StringBuilder();
                meta.AppendLine($"SavedUtc: {DateTime.UtcNow:o}");
                meta.AppendLine($"Path: {path}");
                meta.AppendLine($"User: {Environment.UserName}");
                meta.AppendLine($"Bytes: {Encoding.UTF8.GetByteCount(json)}");
                meta.AppendLine($"SHA256: {hashHex}");
                meta.AppendLine("PreviewStart:");
                meta.AppendLine(preview);
                File.WriteAllText(metaPath, meta.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Save] Failed to write meta: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write meta: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Write failed: {ex.Message}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Write failed: {ex.Message}");
            throw;
        }
        // Persist a small audit log of where workspaces were saved so we can diagnose
        // cases where the UI shows a toast but the file can't be found by the user.
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "where-saved.log");
            var entry = $"{DateTime.UtcNow:o}\tSaved workspace to: {path}\tUser:{Environment.UserName}" + Environment.NewLine;
            File.AppendAllText(logPath, entry);
            Debug.WriteLine($"[Save] Appended where-saved log: {logPath}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Appended where-saved log: {logPath}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Information, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Appended where-saved log: {logPath}", null, (s,e) => s ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Failed to write where-saved log: {ex.Message}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write where-saved log: {ex.Message}");
        }

        // Create a companion marker file next to the saved workspace to make the
        // saved location obvious in Explorer (helps when Desktop is redirected).
        try
        {
            var markerPath = path + ".saved.txt";
            var markerContent = $"Saved: {DateTime.UtcNow:o}\r\nPath: {path}\r\nUser: {Environment.UserName}\r\n";
            File.WriteAllText(markerPath, markerContent);
            Debug.WriteLine($"[Save] Wrote companion marker: {markerPath}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote companion marker: {markerPath}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Debug, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote companion marker: {markerPath}", null, (s,e) => s ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Failed to write companion marker: {ex.Message}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write companion marker: {ex.Message}");
        }

        // Fallback diagnostics: also append to a system-wide temp log and drop a
        // copy of the saved JSON into C:\Temp\TestCaseEditorApp so we can find
        // it even if Desktop is redirected or permissions differ.
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "TestCaseEditorApp");
            Directory.CreateDirectory(tmpDir);
            var tmpLog = Path.Combine(tmpDir, "where-saved.log");
            var tmpEntry = $"{DateTime.UtcNow:o}\tSaved workspace to: {path}\tUser:{Environment.UserName}" + Environment.NewLine;
            File.AppendAllText(tmpLog, tmpEntry);
            var tmpCopy = Path.Combine(tmpDir, Path.GetFileName(path));
            File.WriteAllText(tmpCopy, json);
            Debug.WriteLine($"[Save] Wrote fallback copies to: {tmpDir}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote fallback copies to: {tmpDir}");
            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Warning, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote fallback copies to: {tmpDir}", null, (s,e) => s ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Failed fallback diagnostic writes: {ex.Message}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed fallback diagnostic writes: {ex.Message}");
        }

        // Final verification & last-resort fallback:
        // If the final destination does not exist after the attempted write,
        // try a best-effort recovery: copy the guaranteed staging copy into
        // place (if present) or write the JSON directly to the destination.
        try
        {
            if (!File.Exists(path))
            {
                Debug.WriteLine($"[Save] Final destination missing after write: {path}. Attempting fallback.");
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Final destination missing after write: {path}. Attempting fallback.");

                var stagingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "Staging", Path.GetFileName(path));
                bool wroteFallback = false;
                if (File.Exists(stagingPath))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Path.GetTempPath());
                        File.Copy(stagingPath, path, overwrite: true);
                        Debug.WriteLine($"[Save] Restored from staging: {stagingPath} -> {path}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Restored from staging: {stagingPath} -> {path}");
                            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Information, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Restored from staging: {stagingPath} -> {path}", null, (s,e) => s ?? string.Empty);
                        wroteFallback = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Save] Failed to copy staging to destination: {ex.Message}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to copy staging to destination: {ex.Message}");
                    }
                }

                if (!wroteFallback)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Path.GetTempPath());
                        File.WriteAllText(path, json, Encoding.UTF8);
                        Debug.WriteLine($"[Save] Wrote direct fallback to destination: {path}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote direct fallback to destination: {path}");
                            logger?.Log<string>(Microsoft.Extensions.Logging.LogLevel.Information, new Microsoft.Extensions.Logging.EventId(0), $"[Save] Wrote direct fallback to destination: {path}", null, (s,e) => s ?? string.Empty);
                        wroteFallback = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Save] Direct fallback write failed: {ex.Message}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Direct fallback write failed: {ex.Message}");
                    }
                }

                // If fallback produced a file, attempt to write meta/marker/log entries
                if (wroteFallback && File.Exists(path))
                {
                    try
                    {
                        var metaPath = path + ".meta.txt";
                        byte[] hashBytes;
                        using (var sha = SHA256.Create())
                        {
                            hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                        }
                        var hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                        var preview = json.Length > 1024 ? json.Substring(0, 1024) : json;
                        var meta = new StringBuilder();
                        meta.AppendLine($"SavedUtc: {DateTime.UtcNow:o}");
                        meta.AppendLine($"Path: {path}");
                        meta.AppendLine($"User: {Environment.UserName}");
                        meta.AppendLine($"Bytes: {Encoding.UTF8.GetByteCount(json)}");
                        meta.AppendLine($"SHA256: {hashHex}");
                        meta.AppendLine("PreviewStart:");
                        meta.AppendLine(preview);
                        File.WriteAllText(metaPath, meta.ToString(), Encoding.UTF8);
                        Debug.WriteLine($"[Save] Wrote fallback meta: {metaPath}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote fallback meta: {metaPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Save] Failed to write fallback meta: {ex.Message}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write fallback meta: {ex.Message}");
                    }

                    try
                    {
                        var markerPath = path + ".saved.txt";
                        var markerContent = $"Saved (fallback): {DateTime.UtcNow:o}\r\nPath: {path}\r\nUser: {Environment.UserName}\r\n";
                        File.WriteAllText(markerPath, markerContent);
                        Debug.WriteLine($"[Save] Wrote fallback marker: {markerPath}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Wrote fallback marker: {markerPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Save] Failed to write fallback marker: {ex.Message}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to write fallback marker: {ex.Message}");
                    }

                    try
                    {
                        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp");
                        Directory.CreateDirectory(logDir);
                        var logPath = Path.Combine(logDir, "where-saved.log");
                        var entry = $"{DateTime.UtcNow:o}\tFallback saved workspace to: {path}\tUser:{Environment.UserName}" + Environment.NewLine;
                        File.AppendAllText(logPath, entry);
                        Debug.WriteLine($"[Save] Appended fallback where-saved log: {logPath}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Appended fallback where-saved log: {logPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Save] Failed to append fallback where-saved log: {ex.Message}");
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Failed to append fallback where-saved log: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Save] Final verification failed: {ex.Message}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Save] Final verification failed: {ex.Message}");
        }
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

