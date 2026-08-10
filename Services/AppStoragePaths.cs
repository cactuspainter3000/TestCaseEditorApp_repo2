using System;
using System.Collections.Generic;
using System.IO;

namespace TestCaseEditorApp.Services
{
    public static class AppStoragePaths
    {
        public const string DataRootEnvironmentVariable = "TESTCASEEDITORAPP_DATA_ROOT";

        public static string RootDirectory { get; } = ResolveRootDirectory();
        public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");
        public static string DocumentIndexesDirectory => Path.Combine(RootDirectory, "DocumentIndexes");
        public static string StagingDirectory => Path.Combine(RootDirectory, "Staging");
        public static string TrainingDataValidationDirectory => Path.Combine(RootDirectory, "TrainingDataValidation");
        public static string TempDirectory => Path.Combine(RootDirectory, "Temp");
        public static string ExtensionsDirectory => Path.Combine(RootDirectory, "Extensions");
        public static string LearningRepositoryDirectory => Path.Combine(RootDirectory, "LearningRepository");
        public static string WorkspaceDataDirectory => Path.Combine(RootDirectory, "WorkspaceData");

        public static void EnsureDirectoriesExist()
        {
            foreach (var directory in GetManagedDirectories())
            {
                Directory.CreateDirectory(directory);
            }
        }

        public static void MigrateLegacyData()
        {
            EnsureDirectoriesExist();

            var userProfileRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "TestCaseEditorApp");
            var roamingRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TestCaseEditorApp");
            var localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TestCaseEditorApp");
            var legacyExtensions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TestCaseEditor",
                "Extensions");
            var legacyTemp = Path.Combine(Path.GetTempPath(), "TestCaseEditorApp");

            TryCopyDirectory(userProfileRoot, RootDirectory);
            TryCopyDirectory(roamingRoot, RootDirectory);
            TryCopyDirectory(localRoot, RootDirectory);
            TryCopyDirectory(legacyExtensions, ExtensionsDirectory);
            TryCopyDirectory(legacyTemp, TempDirectory);

            var legacyRequirementWorkspace = Path.Combine(userProfileRoot, "requirement-workspace.json");
            TryCopyFile(legacyRequirementWorkspace, Path.Combine(WorkspaceDataDirectory, "requirement-workspace.json"));
        }

        private static string ResolveRootDirectory()
        {
            var configuredRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                var expandedRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));
                if (IsStorageRootAvailable(expandedRoot))
                {
                    return expandedRoot;
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TestCaseEditorApp");
        }

        private static bool IsStorageRootAvailable(string rootDirectory)
        {
            var pathRoot = Path.GetPathRoot(rootDirectory);
            if (string.IsNullOrWhiteSpace(pathRoot))
            {
                return false;
            }

            try
            {
                return new DriveInfo(pathRoot).IsReady;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetManagedDirectories()
        {
            yield return RootDirectory;
            yield return LogsDirectory;
            yield return DocumentIndexesDirectory;
            yield return StagingDirectory;
            yield return TrainingDataValidationDirectory;
            yield return TempDirectory;
            yield return ExtensionsDirectory;
            yield return LearningRepositoryDirectory;
            yield return WorkspaceDataDirectory;
        }

        private static void CopyDirectoryIfDifferent(string sourceDirectory, string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory) || PathsEqual(sourceDirectory, destinationDirectory))
            {
                return;
            }

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                CopyFileIfNewer(sourceFile, Path.Combine(destinationDirectory, relativePath));
            }
        }

        private static void TryCopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            try
            {
                CopyDirectoryIfDifferent(sourceDirectory, destinationDirectory);
            }
            catch
            {
                // Migration is best-effort; the configured storage root must not block startup.
            }
        }

        private static void TryCopyFile(string sourceFile, string destinationFile)
        {
            try
            {
                CopyFileIfNewer(sourceFile, destinationFile);
            }
            catch
            {
                // Migration is best-effort; the configured storage root must not block startup.
            }
        }

        private static void CopyFileIfNewer(string sourceFile, string destinationFile)
        {
            if (!File.Exists(sourceFile) || PathsEqual(sourceFile, destinationFile))
            {
                return;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (!File.Exists(destinationFile) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destinationFile))
            {
                File.Copy(sourceFile, destinationFile, overwrite: true);
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}