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
            // Enhanced save with backup and atomic operations
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(obj, options);

            if (Path.IsPathRooted(key))
            {
                // Absolute path - workspace file save
                var absPath = key;
                
                // Create backup before save
                CreateBackup(absPath);
                
                // Atomic save operation
                SaveAtomically(absPath, json);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Saved JSON to absolute path: {absPath}");
                return;
            }

            // Key-based save to AppData
            var path = PathFor(key);
            CreateBackup(path);
            SaveAtomically(path, json);
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

        /// <summary>
        /// Create backup of existing file before overwriting
        /// </summary>
        private void CreateBackup(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupPath = $"{filePath}.bak_{timestamp}";
                File.Copy(filePath, backupPath);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Created backup: {backupPath}");
                
                // Clean up old backups (keep last 5)
                CleanupOldBackups(filePath);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Backup creation failed: {ex.Message}");
                // Don't fail the save operation if backup fails
            }
        }

        /// <summary>
        /// Perform atomic save operation using temp file
        /// </summary>
        private void SaveAtomically(string filePath, string content)
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Write to temp file first
            var tempFile = filePath + ".tmp";
            try
            {
                File.WriteAllText(tempFile, content);
                
                // Atomic move to final location
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempFile, filePath);
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Atomic save completed: {filePath}");
            }
            catch (Exception)
            {
                // Cleanup temp file if save failed
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
                throw; // Re-throw to let caller handle the error
            }
        }

        /// <summary>
        /// Remove old backup files, keeping only the most recent 5
        /// </summary>
        private void CleanupOldBackups(string originalFilePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(originalFilePath);
                if (string.IsNullOrEmpty(directory)) return;

                var fileName = Path.GetFileName(originalFilePath);
                var backupPattern = $"{fileName}.bak_*";
                
                var backupFiles = Directory.GetFiles(directory, backupPattern)
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(5); // Keep newest 5, delete the rest

                foreach (var oldBackup in backupFiles)
                {
                    File.Delete(oldBackup);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Cleaned up old backup: {oldBackup}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Backup cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Get available backup files for a given workspace file
        /// </summary>
        public string[] GetAvailableBackups(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory)) return Array.Empty<string>();

                var fileName = Path.GetFileName(filePath);
                var backupPattern = $"{fileName}.bak_*";
                
                return Directory.GetFiles(directory, backupPattern)
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Restore from backup file
        /// </summary>
        public void RestoreFromBackup(string filePath, string backupPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException($"Backup file not found: {backupPath}");

            // Create backup of current file before restore
            CreateBackup(filePath);
            
            // Copy backup to original location
            File.Copy(backupPath, filePath, overwrite: true);
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Restored from backup: {backupPath} -> {filePath}");
        }

        /// <summary>
        /// Check if undo is available (has backups)
        /// </summary>
        public bool CanUndo(string filePath)
        {
            var backups = GetAvailableBackups(filePath);
            return backups.Length > 0;
        }

        /// <summary>
        /// Undo last save by restoring from most recent backup
        /// </summary>
        public void UndoLastSave(string filePath)
        {
            var backups = GetAvailableBackups(filePath);
            if (backups.Length == 0)
                throw new InvalidOperationException("No backups available to restore from");

            var mostRecentBackup = backups[0]; // Already sorted by newest first
            RestoreFromBackup(filePath, mostRecentBackup);
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[JsonPersistence] Undid last save using backup: {mostRecentBackup}");
        }
    }
}