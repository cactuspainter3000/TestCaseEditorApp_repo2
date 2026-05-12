# Requirements Analysis Snapshot

This document describes the lightweight diagnostics snapshot used during requirements-analysis troubleshooting.

## Purpose

The snapshot writer produces a filtered, shareable text file from the main runtime log. It keeps analysis-critical events and removes unrelated noise.

Default output:
- `%USERPROFILE%/TestCaseEditorApp/logs/app-logs.txt`

## Current Implementation

Core classes:
- `Services/Logging/RequirementsAnalysisSnapshotWriter.cs`
- `Services/Logging/Log.cs`

Entry point used by application flow:
- `Log.WriteRequirementsAnalysisLogSnapshot()`

Current call site:
- `MVVM/Domains/Requirements/ViewModels/RequirementAnalysisViewModel.cs`

## What Gets Captured

The writer filters for tags used across the analysis pipeline, including:
- `[ANALYSIS_TRACE]`
- `[RequirementAnalysisVM]`
- `[RequirementsMediator]`
- `[ParserManager]`
- `[JsonParser]`
- `[DelimitedParser]`
- `[RequirementAnalysisService]`
- `[AnalysisEngine]`
- `[RAG]`

Snapshot header metadata includes:
- generation timestamp
- source log path and source log write time
- total source line count
- filtered line count
- configured trace-window depth
- selected trace window start line

## Usage

### Default behavior (latest run only)

```csharp
TestCaseEditorApp.Services.Logging.Log.WriteRequirementsAnalysisLogSnapshot();
```

### Keep multiple recent runs in one snapshot

```csharp
TestCaseEditorApp.Services.Logging.Log.WriteRequirementsAnalysisLogSnapshot(maxTraceWindows: 5);
```

### Write to a custom snapshot file name

```csharp
TestCaseEditorApp.Services.Logging.Log.WriteRequirementsAnalysisLogSnapshot(
    maxTraceWindows: 5,
    snapshotFileName: "app-logs-last-5-runs.txt");
```

## Operational Guidance

- Use `maxTraceWindows: 1` for day-to-day triage.
- Use `maxTraceWindows: 5` to `10` during soak testing.
- Keep the filter tag list narrow to avoid huge snapshots.
- If a new subsystem is added to the analysis path, add its trace tag to `RequirementsAnalysisSnapshotOptions.AnalysisTags`.

## Why This Refactor Matters

The snapshot feature is now isolated behind a dedicated writer and options object.

Benefits:
- easier reuse in future incidents
- easier adaptation without touching generic log helpers
- easier testability and safer iteration
