using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

class Program
{
    static int Main()
    {
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "TCE_Runner", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            var fileName = Guid.NewGuid().ToString("N") + ".tcex.json";
            var path = Path.Combine(tmpDir, fileName);

            // Minimal workspace payload
            var payload = new { JamaProject = "RunnerProject", Requirements = Array.Empty<object>() };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

            // Write staging copy
            var stagingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp", "Staging");
            Directory.CreateDirectory(stagingDir);
            var stagingPath = Path.Combine(stagingDir, Path.GetFileName(path));
            File.WriteAllText(stagingPath, json, Encoding.UTF8);

            // staging meta
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
            catch { }

            // Atomic write to destination
            var targetDir = Path.GetDirectoryName(path) ?? Path.GetTempPath();
            Directory.CreateDirectory(targetDir);
            var tmpFile = Path.Combine(targetDir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(tmpFile, json, Encoding.UTF8);
            File.Move(tmpFile, path);

            // meta next to saved file
            var metaPath = path + ".meta.txt";
            try
            {
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
            catch { }

            // where-saved log
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditorApp");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "where-saved.log");
            var entry = $"{DateTime.UtcNow:o}	Saved workspace to: {path}	User:{Environment.UserName}" + Environment.NewLine;
            File.AppendAllText(logPath, entry);

            // marker file
            var markerPath = path + ".saved.txt";
            var markerContent = $"Saved: {DateTime.UtcNow:o}\r\nPath: {path}\r\nUser: {Environment.UserName}\r\n";
            File.WriteAllText(markerPath, markerContent);

            // basic assertions
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("ERROR: saved file missing");
                return 2;
            }
            if (!File.Exists(metaPath))
            {
                Console.Error.WriteLine("ERROR: meta file missing");
                return 3;
            }
            if (!File.Exists(stagingPath))
            {
                Console.Error.WriteLine("ERROR: staging copy missing: " + stagingPath);
                return 4;
            }
            if (!File.Exists(logPath))
            {
                Console.Error.WriteLine("ERROR: where-saved.log missing: " + logPath);
                return 5;
            }

            Console.WriteLine("Runner: all checks passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("EXCEPTION: " + ex);
            return 99;
        }
    }
}
