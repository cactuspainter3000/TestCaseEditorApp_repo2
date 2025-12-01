using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Manages a list of recently opened workspace files, persisted to AppData.
    /// </summary>
    public class RecentFilesService
    {
        private const int MaxRecentFiles = 10;
        private readonly string _settingsPath;
        private List<string> _recentFiles = new List<string>();

        public RecentFilesService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "TestCaseEditorApp");
            Directory.CreateDirectory(folder);
            _settingsPath = Path.Combine(folder, "recent-files.json");
            Load();
        }

        /// <summary>
        /// Gets the list of recent files (most recent first).
        /// </summary>
        public IReadOnlyList<string> GetRecentFiles() => _recentFiles.AsReadOnly();

        /// <summary>
        /// Adds or moves a file path to the top of the recent files list.
        /// Removes non-existent files during cleanup.
        /// </summary>
        public void AddRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            // Normalize path
            filePath = Path.GetFullPath(filePath);

            // Remove if already exists (will re-add at top)
            _recentFiles.Remove(filePath);

            // Add to front
            _recentFiles.Insert(0, filePath);

            // Clean up non-existent files
            _recentFiles = _recentFiles.Where(File.Exists).ToList();

            // Trim to max count
            if (_recentFiles.Count > MaxRecentFiles)
                _recentFiles = _recentFiles.Take(MaxRecentFiles).ToList();

            Save();
        }

        /// <summary>
        /// Removes a specific file from the recent files list.
        /// </summary>
        public void RemoveRecentFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            filePath = Path.GetFullPath(filePath);
            _recentFiles.Remove(filePath);
            Save();
        }

        /// <summary>
        /// Clears all recent files.
        /// </summary>
        public void ClearRecentFiles()
        {
            _recentFiles.Clear();
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return;

                var json = File.ReadAllText(_settingsPath);
                var files = JsonSerializer.Deserialize<List<string>>(json);
                if (files != null)
                {
                    // Clean up non-existent files on load
                    _recentFiles = files.Where(File.Exists).ToList();
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RecentFiles] Load failed: {ex.Message}");
                _recentFiles = new List<string>();
            }
        }

        private void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_recentFiles, options);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RecentFiles] Save failed: {ex.Message}");
            }
        }
    }
}
