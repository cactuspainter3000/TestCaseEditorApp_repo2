namespace TestCaseEditorApp.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Diagnostics;

    public class JsonPersistenceService : IPersistenceService
    {
        private readonly string _folder;

        public JsonPersistenceService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _folder = Path.Combine(appData, "TestCaseEditorApp");
            Directory.CreateDirectory(_folder);
        }

        private string PathFor(string key) =>
            Path.Combine(_folder, SanitizeFileName(key) + ".json");

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public void Save<T>(string key, T obj)
        {
            try
            {
                // If callers pass an absolute filesystem path as the 'key', treat it
                // as a real path and write the JSON there. Historically some parts
                // of the app passed a full path into the key-based Save API which
                // caused serialized JSON to be written under ApplicationData using
                // the sanitized key name. Detect and handle that case explicitly.
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(obj, options);

                if (Path.IsPathRooted(key))
                {
                    try
                    {
                        var absPath = key;
                        var dir = Path.GetDirectoryName(absPath);
                        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(absPath, json);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Saved JSON to absolute path: {absPath}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Fall back to the normal behavior below if absolute write fails
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Failed to write absolute path {key}: {ex.Message}");
                    }
                }

                var path = PathFor(key);
                File.WriteAllText(path, json);
            }
            catch
            {
                // swallow; persistence is best-effort. Consider logging in real app.
            }
        }

        public T? Load<T>(string key)
        {
            try
            {
                var path = PathFor(key);
                if (!File.Exists(path)) return default;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return default;
            }
        }

        public bool Exists(string key)
        {
            var path = PathFor(key);
            return File.Exists(path);
        }
    }
}