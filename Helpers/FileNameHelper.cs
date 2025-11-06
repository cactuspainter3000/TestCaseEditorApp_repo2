// Helpers/FileNameHelper.cs
using System;
using System.IO;
using System.Linq;

namespace TestCaseEditorApp.Helpers
{
    internal static class FileNameHelper
    {
        /// Build a safe, unique file name like: Session_2025-09-25_08-12-33_cd91a2e3.json
        public static string GenerateUniqueFileName(string baseName = "Session", string extension = ".json")
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeBase = new string((baseName ?? "Session").Where(c => !invalid.Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(safeBase)) safeBase = "Session";

            // Use local time for user readability
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var shortGuid = Guid.NewGuid().ToString("N")[..8];

            if (!extension.StartsWith(".")) extension = "." + extension;
            return $"{safeBase}_{stamp}_{shortGuid}{extension}";
        }

        /// If a file already exists, append -1, -2, ... until unique.
        public static string EnsureUniquePath(string folder, string fileName)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path)) return path;

            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            int i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(folder, $"{name}-{i}{ext}");
                i++;
            } while (File.Exists(candidate));

            return candidate;
        }
    }
}

