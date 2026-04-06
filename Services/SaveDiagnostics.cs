using System;
using System.IO;
using System.Linq;

namespace TestCaseEditorApp.Services
{
    internal static class SaveDiagnostics
    {
        public static void Probe(string path)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Probing saved path: {path}");

                try
                {
                    var fi = new FileInfo(path);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] File Exists={fi.Exists}, Length={(fi.Exists ? fi.Length.ToString() : "N/A")}, LastWriteUtc={(fi.Exists ? fi.LastWriteTimeUtc.ToString("o") : "N/A")} ");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] File probe failed: {ex.Message}");
                }

                var metaPath = path + ".meta.txt";
                try
                {
                    if (File.Exists(metaPath))
                    {
                        var lines = File.ReadAllLines(metaPath);
                        var preview = string.Join(Environment.NewLine, lines.Take(Math.Min(20, lines.Length)));
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Meta exists: {metaPath} (lines={lines.Length})");
                        TestCaseEditorApp.Services.Logging.Log.Debug(preview);
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Meta missing: {metaPath}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Meta probe failed: {ex.Message}");
                }

                var markerPath = path + ".saved.txt";
                try
                {
                    if (File.Exists(markerPath))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Marker exists: {markerPath}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Marker missing: {markerPath}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Marker probe failed: {ex.Message}");
                }

                try
                {
                    var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "where-saved.log");
                    if (File.Exists(logPath))
                    {
                        var all = File.ReadAllLines(logPath);
                        var last = all.Skip(Math.Max(0, all.Length - 20)).ToArray();
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Local where-saved.log last lines:\n{string.Join(Environment.NewLine, last)}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Local where-saved.log missing: {logPath}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] where-saved.log probe failed: {ex.Message}");
                }

                try
                {
                    var tmpDir = Path.Combine(Path.GetTempPath(), "TestCaseEditorApp");
                    var tmpCopy = Path.Combine(tmpDir, Path.GetFileName(path));
                    if (File.Exists(tmpCopy))
                    {
                        var tfi = new FileInfo(tmpCopy);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Temp copy exists: {tmpCopy} (Length={tfi.Length})");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Temp copy missing: {tmpCopy}");
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Temp-copy probe failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[PostSave] Unexpected diagnostic error: {ex.Message}");
            }
        }
    }
}
