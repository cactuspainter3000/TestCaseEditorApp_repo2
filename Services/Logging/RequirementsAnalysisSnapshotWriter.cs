using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestCaseEditorApp.Services.Logging
{
    internal sealed class RequirementsAnalysisSnapshotOptions
    {
        public string LogDirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TestCaseEditorApp", "logs");
        public string SnapshotFileName { get; set; } = "app-logs.txt";
        public int MaxTraceWindows { get; set; } = 1;
        public int FallbackTailLineCount { get; set; } = 2000;
        public string[] AnalysisTags { get; set; } =
        {
            "[ANALYSIS_TRACE]",
            "[RequirementAnalysisVM]",
            "[RequirementsMediator]",
            "[ParserManager]",
            "[JsonParser]",
            "[DelimitedParser]",
            "[RequirementAnalysisService]",
            "[AnalysisEngine]",
            "[RAG RESPONSE DEBUG]",
            "[RAG]",
            "[PARSER_CANPARSE_CHECK]",
            "[PARSER_RESPONSE_PREVIEW]",
            "[RequirementAnalysis]"
        };
    }

    internal sealed class RequirementsAnalysisSnapshotWriter
    {
        private readonly RequirementsAnalysisSnapshotOptions _options;

        public RequirementsAnalysisSnapshotWriter(RequirementsAnalysisSnapshotOptions? options = null)
        {
            _options = options ?? new RequirementsAnalysisSnapshotOptions();
        }

        public void WriteSnapshot(AnalysisSnapshotContext? context = null)
        {
            var logDir = _options.LogDirectoryPath;
            if (!Directory.Exists(logDir))
                return;

            var logFile = new DirectoryInfo(logDir)
                .GetFiles("*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (logFile == null || !logFile.Exists)
                return;

            var allLines = File.ReadLines(logFile.FullName).ToList();
            var traceStartIndexes = FindTraceStartIndexes(allLines);
            var windowStartIndex = ResolveWindowStartIndex(traceStartIndexes, _options.MaxTraceWindows);

            var candidateLines = windowStartIndex >= 0
                ? allLines.Skip(windowStartIndex)
                : allLines.TakeLast(Math.Max(1, _options.FallbackTailLineCount));

            var filteredLines = candidateLines
                .Where(line => _options.AnalysisTags.Any(tag => line.Contains(tag)))
                .ToList();

            var outputLines = new List<string>
            {
                $"# SnapshotGeneratedUtc={DateTime.UtcNow:O}",
                $"# SourceLog={logFile.FullName}",
                $"# SourceLastWriteUtc={logFile.LastWriteTimeUtc:O}",
                $"# TotalSourceLines={allLines.Count}",
                $"# FilteredLines={filteredLines.Count}",
                $"# MaxTraceWindows={Math.Max(1, _options.MaxTraceWindows)}"
            };

            // Add context information if provided
            if (context != null)
            {
                outputLines.Add($"# Context.CreatedUtc={context.CreatedUtc:O}");
                if (!string.IsNullOrEmpty(context.MethodName))
                    outputLines.Add($"# Context.MethodName={context.MethodName}");
                if (!string.IsNullOrEmpty(context.TriggeredBy))
                    outputLines.Add($"# Context.TriggeredBy={context.TriggeredBy}");
                if (context.RetryAttempt.HasValue)
                    outputLines.Add($"# Context.RetryAttempt={context.RetryAttempt}");
                if (context.ElapsedMilliseconds.HasValue)
                    outputLines.Add($"# Context.ElapsedMilliseconds={context.ElapsedMilliseconds}");
                if (!string.IsNullOrEmpty(context.RequirementId))
                    outputLines.Add($"# Context.RequirementId={context.RequirementId}");
                if (!string.IsNullOrEmpty(context.Comments))
                    outputLines.Add($"# Context.Comments={context.Comments}");
            }

            if (windowStartIndex >= 0)
            {
                outputLines.Add($"# TraceWindowStartLine={windowStartIndex + 1}");
            }
            else
            {
                outputLines.Add("# TraceWindowStartLine=NOT_FOUND ([ANALYSIS_TRACE] START not found)");
            }

            outputLines.AddRange(filteredLines);

            var snapshotPath = Path.Combine(logDir, _options.SnapshotFileName);
            File.WriteAllLines(snapshotPath, outputLines);
        }

        private static List<int> FindTraceStartIndexes(List<string> allLines)
        {
            var indexes = new List<int>();
            for (var i = 0; i < allLines.Count; i++)
            {
                if (allLines[i].Contains("[ANALYSIS_TRACE] START"))
                {
                    indexes.Add(i);
                }
            }

            return indexes;
        }

        private static int ResolveWindowStartIndex(List<int> traceStartIndexes, int maxTraceWindows)
        {
            if (traceStartIndexes.Count == 0)
                return -1;

            var keepWindows = Math.Max(1, maxTraceWindows);
            var startSlot = Math.Max(0, traceStartIndexes.Count - keepWindows);
            return traceStartIndexes[startSlot];
        }
    }
}