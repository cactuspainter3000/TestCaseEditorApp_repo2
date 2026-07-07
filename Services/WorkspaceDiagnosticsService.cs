using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Views.Dialogs;

namespace TestCaseEditorApp.Services
{
    public sealed class WorkspaceDiagnosticsService : IWorkspaceDiagnosticsService
    {
        private readonly IJamaConnectService _jamaConnectService;
        private readonly ILogger<WorkspaceDiagnosticsService> _logger;

        public WorkspaceDiagnosticsService(
            IJamaConnectService jamaConnectService,
            ILogger<WorkspaceDiagnosticsService> logger)
        {
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ExportAnalysisLogsAsync()
        {
            try
            {
                var result = await Task.Run(ExportAndPushLogs);

                var summaryBuilder = new StringBuilder();
                summaryBuilder.AppendLine("Logs exported successfully.");
                summaryBuilder.AppendLine($"Desktop zip: {result.ZipPath}");
                summaryBuilder.AppendLine($"Repo folder: {result.RepositoryExportPath}");
                summaryBuilder.AppendLine($"Full details: {result.SummaryPath}");

                if (result.GitPushSucceeded)
                {
                    summaryBuilder.AppendLine($"Git push: completed{(string.IsNullOrWhiteSpace(result.CommitHash) ? string.Empty : $" (commit {result.CommitHash})")}");
                }
                else
                {
                    summaryBuilder.AppendLine("Git push: not completed (see export-summary.txt)");
                }

                MessageBox.Show(
                    summaryBuilder.ToString(),
                    "Export Analysis Logs",
                    MessageBoxButton.OK,
                    result.GitPushSucceeded ? MessageBoxImage.Information : MessageBoxImage.Warning);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{result.SummaryPath}\"",
                    UseShellExecute = true
                });

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{result.ZipPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WorkspaceDiagnosticsService] Failed to export analysis logs");
                MessageBox.Show(
                    $"Failed to export logs: {ex.Message}",
                    "Export Analysis Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public async Task ProbeJamaLookupFieldsAsync()
        {
            try
            {
                _logger.LogInformation("[WorkspaceDiagnosticsService] Starting Jama lookup field probe");

                var (connectionSuccess, connectionMessage) = await _jamaConnectService.TestConnectionAsync();
                if (!connectionSuccess)
                {
                    MessageBox.Show(
                        $"Failed to connect to Jama before lookup probe.\n\n{connectionMessage}",
                        "Jama Lookup Probe",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var projects = await _jamaConnectService.GetProjectsAsync();
                if (projects == null || projects.Count == 0)
                {
                    MessageBox.Show(
                        "No Jama projects available for lookup probe.",
                        "Jama Lookup Probe",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var projectDialog = new JamaProjectSelectionDialog(projects);
                if (projectDialog.ShowDialog() != true || projectDialog.SelectedProject == null)
                {
                    return;
                }

                var selectedProject = projectDialog.SelectedProject;
                var report = await _jamaConnectService.ProbeRequirementLookupFieldsAsync(selectedProject.Id, 30);

                if (!report.Success)
                {
                    MessageBox.Show(
                        report.Message,
                        "Jama Lookup Probe",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var fieldsWithOptions = report.Fields.Where(f => f.EndpointAvailable && f.OptionCount > 0).ToList();
                var invalidDefaults = report.Fields.Where(f => f.IsCurrentDefaultValid == false).ToList();
                var failedFields = report.Fields.Where(f => !f.EndpointAvailable).ToList();

                foreach (var field in report.Fields)
                {
                    _logger.LogInformation(
                        "[JamaLookupProbe] Field={Field} ResolvedField={ResolvedField} Endpoint={Endpoint} Options={Options} Default={DefaultLookupId} DefaultValid={DefaultValid} Error={Error}",
                        field.FieldName,
                        field.ResolvedFieldName ?? field.FieldName,
                        field.EndpointAvailable,
                        field.OptionCount,
                        field.CurrentDefaultLookupId,
                        field.IsCurrentDefaultValid,
                        field.Error ?? string.Empty);
                }

                var reportText = new StringBuilder();
                reportText.AppendLine("Jama Lookup Probe");
                reportText.AppendLine($"Generated UTC: {DateTime.UtcNow:O}");
                reportText.AppendLine($"Project: {selectedProject.ProjectKey} (ID {selectedProject.Id})");
                reportText.AppendLine($"Requirement Item Type: {report.RequirementItemTypeId?.ToString() ?? "unknown"}");
                reportText.AppendLine($"Fields checked: {report.Fields.Count}");
                reportText.AppendLine($"Fields with options: {fieldsWithOptions.Count}");
                reportText.AppendLine($"Invalid defaults: {invalidDefaults.Count}");
                reportText.AppendLine($"Endpoint failures: {failedFields.Count}");

                if (invalidDefaults.Count > 0)
                {
                    reportText.AppendLine();
                    reportText.AppendLine("Invalid default mappings:");
                    foreach (var invalid in invalidDefaults)
                    {
                        reportText.AppendLine($"- {invalid.FieldName} (resolved {invalid.ResolvedFieldName ?? invalid.FieldName}): default {invalid.CurrentDefaultLookupId}");
                    }
                }

                if (failedFields.Count > 0)
                {
                    reportText.AppendLine();
                    reportText.AppendLine("Failed lookup endpoints:");
                    foreach (var failed in failedFields)
                    {
                        reportText.AppendLine($"- {failed.FieldName} (resolved {failed.ResolvedFieldName ?? failed.FieldName}): {failed.Error ?? "request failed"}");
                    }
                }

                reportText.AppendLine();
                reportText.AppendLine("Field details:");
                foreach (var field in report.Fields.OrderBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase))
                {
                    var samples = field.SampleOptionIds.Count > 0
                        ? string.Join(",", field.SampleOptionIds)
                        : "none";

                    reportText.AppendLine(
                        $"- field={field.FieldName}, resolved={field.ResolvedFieldName ?? field.FieldName}, endpoint={field.EndpointAvailable}, options={field.OptionCount}, firstOption={field.FirstOptionId}, default={field.CurrentDefaultLookupId}, defaultValid={field.IsCurrentDefaultValid}, samples=[{samples}], error={field.Error ?? string.Empty}");
                }

                var reportDialog = new JamaLookupProbeResultDialog(reportText.ToString())
                {
                    Owner = Application.Current?.MainWindow
                };
                reportDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WorkspaceDiagnosticsService] Jama lookup probe failed");
                MessageBox.Show(
                    $"Lookup probe failed: {ex.Message}",
                    "Jama Lookup Probe",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private LogExportResult ExportAndPushLogs()
        {
            var rootResolution = ResolveProjectRoot();
            var projectRoot = rootResolution.RootPath;
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var stagingDir = Path.Combine(Path.GetTempPath(), $"TestCaseEditorApp-logs-{timestamp}");
            Directory.CreateDirectory(stagingDir);

            var copiedAny = CollectKnownLogs(projectRoot, stagingDir);
            var traceabilityInfo = CollectTraceabilityReports(projectRoot, stagingDir);

            if (!copiedAny && traceabilityInfo.ReportCount == 0)
            {
                throw new InvalidOperationException("No known log files were found to export.");
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zipPath = Path.Combine(desktop, $"TestCaseEditorApp-AnalysisLogs-{timestamp}.zip");

            var repoExportRoot = Path.Combine(projectRoot, "exports", "analysis-logs");
            var repoExportPath = Path.Combine(repoExportRoot, timestamp);
            Directory.CreateDirectory(repoExportPath);

            foreach (var file in Directory.GetFiles(stagingDir))
            {
                var destination = Path.Combine(repoExportPath, Path.GetFileName(file));
                File.Copy(file, destination, overwrite: true);
            }

            var summaryPath = Path.Combine(repoExportPath, "export-summary.txt");
            var detailedSummary = new StringBuilder();
            detailedSummary.AppendLine("TestCaseEditorApp Analysis Log Export");
            detailedSummary.AppendLine($"Created: {DateTime.Now:O}");
            detailedSummary.AppendLine($"Source machine: {Environment.MachineName}");
            detailedSummary.AppendLine($"Zip: {zipPath}");
            detailedSummary.AppendLine($"Repo folder: {repoExportPath}");
            detailedSummary.AppendLine($"DetectedRoot: {projectRoot}");
            detailedSummary.AppendLine($"RootResolution: {rootResolution.ResolutionInfo}");
            detailedSummary.AppendLine($"AppBaseDirectory: {AppContext.BaseDirectory}");
            detailedSummary.AppendLine($"CurrentDirectory: {Environment.CurrentDirectory}");
            detailedSummary.AppendLine();
            detailedSummary.AppendLine($"TraceReportsIncluded: {traceabilityInfo.ReportCount}");
            detailedSummary.AppendLine($"LatestTraceReport: {traceabilityInfo.LatestReportPath ?? "(none)"}");
            detailedSummary.AppendLine("GitPushResult: recorded in export dialog (computed after commit/push step)");
            File.WriteAllText(summaryPath, detailedSummary.ToString());

            // Include summary in desktop zip as well.
            File.Copy(summaryPath, Path.Combine(stagingDir, "export-summary.txt"), overwrite: true);

            var gitResult = TryCommitAndPushExport(projectRoot, repoExportPath);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            _logger.LogInformation("[WorkspaceDiagnosticsService] Exported analysis logs zip={ZipPath}, repoFolder={RepoFolder}", zipPath, repoExportPath);
            return new LogExportResult(
                zipPath,
                repoExportPath,
                summaryPath,
                gitResult.Succeeded,
                gitResult.CommitHash,
                gitResult.FailureMessage,
                projectRoot,
                rootResolution.ResolutionInfo,
                AppContext.BaseDirectory,
                Environment.CurrentDirectory);
        }

        private static bool CollectKnownLogs(string projectRoot, string stagingDir)
        {
            var copiedAny = false;
            var rootCandidates = new[]
            {
                "build-check.txt",
                "build-output.txt",
                "targeted_error.txt"
            };

            foreach (var fileName in rootCandidates)
            {
                var source = Path.Combine(projectRoot, fileName);
                if (!File.Exists(source))
                {
                    continue;
                }

                var destination = Path.Combine(stagingDir, fileName);
                File.Copy(source, destination, overwrite: true);
                copiedAny = true;
            }

            var runtimeLogDirectories = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TestCaseEditorApp", "logs"),
                Path.Combine(projectRoot, "logs")
            };

            foreach (var appLogDir in runtimeLogDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(appLogDir))
                {
                    continue;
                }

                var runtimeSnapshotPath = Path.Combine(appLogDir, "app-logs.txt");
                if (File.Exists(runtimeSnapshotPath))
                {
                    var runtimeSnapshotDestination = Path.Combine(stagingDir, $"{new DirectoryInfo(appLogDir).Name}-app-logs.txt");
                    File.Copy(runtimeSnapshotPath, runtimeSnapshotDestination, overwrite: true);
                    copiedAny = true;
                }

                foreach (var source in Directory.GetFiles(appLogDir, "*.log").Concat(Directory.GetFiles(appLogDir, "*.txt")))
                {
                    var fileName = Path.GetFileName(source);

                    if (fileName.StartsWith("app.old.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(source);
                    const long maxExportBytes = 80L * 1024L * 1024L;
                    if (fileInfo.Length > maxExportBytes)
                    {
                        continue;
                    }

                    var exportedFileName = fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                        ? $"{fileName}.txt"
                        : fileName;
                    var destination = Path.Combine(stagingDir, $"{new DirectoryInfo(appLogDir).Name}-{exportedFileName}");
                    File.Copy(source, destination, overwrite: true);
                    copiedAny = true;
                }
            }

            return copiedAny;
        }

        private static TraceabilityExportInfo CollectTraceabilityReports(string projectRoot, string stagingDir)
        {
            var traceabilityDirectories = new[]
            {
                Path.Combine(projectRoot, "exports", "traceability-reports"),
                Path.Combine(Environment.CurrentDirectory, "exports", "traceability-reports"),
                Path.Combine(AppContext.BaseDirectory, "exports", "traceability-reports")
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();

            if (traceabilityDirectories.Count == 0)
            {
                return new TraceabilityExportInfo(0, null);
            }

            var reportFiles = traceabilityDirectories
                .SelectMany(directory => Directory.GetFiles(directory, "derivation-trace-*.txt"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(20)
                .ToList();

            if (reportFiles.Count == 0)
            {
                return new TraceabilityExportInfo(0, null);
            }

            foreach (var report in reportFiles)
            {
                var destination = Path.Combine(stagingDir, $"traceability-{report.Name}");
                File.Copy(report.FullName, destination, overwrite: true);
            }

            var latestReportPath = reportFiles[0].FullName;
            var indexPath = Path.Combine(stagingDir, "traceability-index.txt");
            var indexBuilder = new StringBuilder();
            indexBuilder.AppendLine("Traceability Report Index");
            indexBuilder.AppendLine($"Generated: {DateTime.Now:O}");
            indexBuilder.AppendLine($"Count: {reportFiles.Count}");
            indexBuilder.AppendLine($"Latest: {latestReportPath}");
            indexBuilder.AppendLine();

            for (var i = 0; i < reportFiles.Count; i++)
            {
                indexBuilder.AppendLine($"[{i + 1}] {reportFiles[i].FullName}");
            }

            File.WriteAllText(indexPath, indexBuilder.ToString());
            return new TraceabilityExportInfo(reportFiles.Count, latestReportPath);
        }

        private GitAutomationResult TryCommitAndPushExport(string projectRoot, string repoExportPath)
        {
            try
            {
                if (!Directory.Exists(Path.Combine(projectRoot, ".git")))
                {
                    return new GitAutomationResult(false, null, "Not a git repository. Logs were exported locally only.");
                }

                var relativeExportPath = Path.GetRelativePath(projectRoot, repoExportPath).Replace('\\', '/');
                RunGitCommand(projectRoot, $"add \"{relativeExportPath}\"");

                var commitOutput = RunGitCommand(
                    projectRoot,
                    $"commit -m \"Add exported analysis logs {Path.GetFileName(repoExportPath)}\"");

                if (commitOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
                {
                    return new GitAutomationResult(false, null, "No repo changes to commit. Logs were exported locally only.");
                }

                var commitHash = RunGitCommand(projectRoot, "rev-parse --short HEAD").Trim();
                RunGitCommand(projectRoot, "push");

                return new GitAutomationResult(true, commitHash, null);
            }
            catch (Exception ex)
            {
                return new GitAutomationResult(false, null, $"Git automation error: {ex.Message}");
            }
        }

        private static string RunGitCommand(string workingDirectory, string arguments)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(60000))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException($"git {arguments} timed out");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {arguments} failed: {stdErr}".Trim());
            }

            return string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut;
        }

        private static ProjectRootResolution ResolveProjectRoot()
        {
            var candidates = new List<string>
            {
                AppContext.BaseDirectory,
                Environment.CurrentDirectory
            };

            try
            {
                var processPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    var processDir = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrWhiteSpace(processDir))
                    {
                        candidates.Add(processDir);
                    }
                }
            }
            catch
            {
                // Best effort only.
            }

            foreach (var candidate in candidates.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var root = TryFindAncestorContaining(candidate, ".git");
                if (root != null)
                {
                    return new ProjectRootResolution(root, $"Found .git from candidate '{candidate}'");
                }

                root = TryFindAncestorContaining(candidate, "TestCaseEditorApp.csproj");
                if (root != null)
                {
                    return new ProjectRootResolution(root, $"Found TestCaseEditorApp.csproj from candidate '{candidate}'");
                }
            }

            return new ProjectRootResolution(Environment.CurrentDirectory, "Fallback to Environment.CurrentDirectory");
        }

        private static string? TryFindAncestorContaining(string startPath, string fileOrDirectoryName)
        {
            var directory = new DirectoryInfo(startPath);
            while (directory != null)
            {
                var targetPath = Path.Combine(directory.FullName, fileOrDirectoryName);
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private sealed record LogExportResult(
            string ZipPath,
            string RepositoryExportPath,
            string SummaryPath,
            bool GitPushSucceeded,
            string? CommitHash,
            string? GitFailureMessage,
            string ProjectRoot,
            string RootResolutionInfo,
            string AppBaseDirectory,
            string CurrentDirectory);

        private sealed record GitAutomationResult(
            bool Succeeded,
            string? CommitHash,
            string? FailureMessage);

        private sealed record ProjectRootResolution(
            string RootPath,
            string ResolutionInfo);

        private sealed record TraceabilityExportInfo(
            int ReportCount,
            string? LatestReportPath);
    }
}