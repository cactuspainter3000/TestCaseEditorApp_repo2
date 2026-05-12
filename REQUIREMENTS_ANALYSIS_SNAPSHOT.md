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

## Controlling Snapshot Execution: EnableRequirementsAnalysisSnapshot

Starting with this version, all snapshot capture calls are **gated by a binary toggle flag**. This allows you to keep instrumentation in code permanently while controlling whether snapshots are actually written.

### Setting the Flag

The flag is stored in `AppUserSettings`:

```csharp
public bool EnableRequirementsAnalysisSnapshot { get; set; } = false;
```

**Default is `false`** (disabled for production).

To enable snapshots:

1. **In code (development/debugging)**:
   ```csharp
   var settings = App.ServiceProvider?.GetService<IUserSettingsService>();
   if (settings != null)
   {
       var appSettings = settings.LoadSettings();
       appSettings.EnableRequirementsAnalysisSnapshot = true;
       settings.SaveSettings(appSettings);
   }
   ```

2. **Via settings file** (location: `%USERPROFILE%\TestCaseEditorApp\app-settings.json`):
   ```json
   {
     "EnableRequirementsAnalysisSnapshot": true
   }
   ```

### Future Re-enablement

Once you've finished debugging and want to disable snapshots again:
- Simply set the flag back to `false`
- **No code changes needed** — all capture points remain in place
- Next time snapshot logging is needed, flip the flag to `true` and run again

## Rich Snapshot Context: AnalysisSnapshotContext

The snapshot now captures detailed metadata about each analysis run through the `AnalysisSnapshotContext` class. This helps future debugging by providing method names, triggers, retry info, timing, and custom comments.

### Context Properties

| Property | Type | Purpose |
|----------|------|---------|
| `MethodName` | `string?` | Name of the method that triggered the snapshot (e.g., `AnalyzeRequirementAsync`) |
| `TriggeredBy` | `string?` | What initiated analysis: `"UserButton"`, `"AutoRetry"`, `"ImportFlow"`, `"BackgroundValidation"` |
| `RetryAttempt` | `int?` | Retry attempt number (1 for first, 2+ for retries) |
| `ElapsedMilliseconds` | `long?` | Total duration of the analysis in milliseconds |
| `RequirementId` | `string?` | GlobalId of the requirement being analyzed |
| `Comments` | `string?` | Free-form notes explaining context (e.g., "Testing JSON parser", "User reported timeout") |
| `CustomData` | `Dictionary<string, object>?` | Extensibility for additional metadata |

### How Context Appears in Snapshot Output

Context metadata is included in the snapshot header as comments:

```
# SnapshotGeneratedUtc=2026-05-12T14:23:45.123456Z
# SourceLog=C:\Users\nicol\TestCaseEditorApp\logs\2026-05-12.log
# Context.MethodName=AnalyzeRequirementAsync
# Context.TriggeredBy=UserButton
# Context.ElapsedMilliseconds=12345
# Context.RequirementId=CSDC-REQ-001
# Context.Comments=Score=5, Issues=2, Recommendations=2
```

### Example: Passing Context from a Call Site

```csharp
private void OnAnalysisComplete()
{
    try
    {
        // Calculate elapsed time
        var elapsedMs = _analysisStartTime != default
            ? (long)(DateTime.Now - _analysisStartTime).TotalMilliseconds
            : (long?)null;

        // Create context with rich metadata
        var context = new TestCaseEditorApp.Services.Logging.AnalysisSnapshotContext
        {
            MethodName = nameof(OnAnalysisComplete),
            TriggeredBy = "UserButton",
            ElapsedMilliseconds = elapsedMs,
            RequirementId = CurrentRequirement?.GlobalId,
            Comments = $"Score={QualityScore}, Issues={Issues?.Count ?? 0}"
        };

        // Pass context to snapshot writer
        Log.WriteRequirementsAnalysisLogSnapshot(context: context);
    }
    catch (Exception ex)
    {
        SafeLogError(ex, "[AnalysisVM] Failed to write snapshot");
    }
}
```

### Adding Comments for Future Debugging

Use the `Comments` field to leave notes for your future self or teammates:

```csharp
var context = new AnalysisSnapshotContext
{
    MethodName = nameof(AnalyzeAsync),
    Comments = "Tracking issue #456: JSON parser hangs on large requirements. " +
               "Enabled SSE streaming fix in commit a7c3def."
};

Log.WriteRequirementsAnalysisLogSnapshot(context: context);
```

## Instrumentation Best Practices

### 1. Keep Capture Calls in Code

Never delete `Log.WriteRequirementsAnalysisLogSnapshot()` calls after debugging. Instead, toggle the binary flag.

**Good** (future debugging = just flip flag):
```csharp
// Remains in code even after initial debugging
Log.WriteRequirementsAnalysisLogSnapshot(context);
```

**Bad** (future debugging = code changes):
```csharp
// Deleted after issue resolved — hard to re-enable later
// Log.WriteRequirementsAnalysisLogSnapshot(context);
```

### 2. Add Meaningful Context

Use `Comments` to explain *why* you're capturing:

```csharp
// ✅ Good
context.Comments = "Testing JSON parser after merge from feature-xyz";

// ❌ Poor
context.Comments = "test";
```

### 3. Extend Filter Tags if Needed

If you add a new analysis subsystem with its own trace markers (e.g., `[NewAnalysisEngine]`), add it to the filter list:

```csharp
// In RequirementsAnalysisSnapshotOptions.cs
public string[] AnalysisTags { get; set; } =
{
    "[ANALYSIS_TRACE]",
    "[RequirementAnalysisVM]",
    "[NewAnalysisEngine]",  // ← Add new subsystem tags here
    // ... other tags
};
```

### 4. Use Retry Attempt Tracking

If your analysis logic has retry logic, pass the attempt number:

```csharp
for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        // Perform analysis
    }
    catch
    {
        var context = new AnalysisSnapshotContext
        {
            RetryAttempt = attempt,
            TriggeredBy = attempt > 1 ? "AutoRetry" : "UserButton"
        };
        
        if (attempt == maxRetries)
        {
            Log.WriteRequirementsAnalysisLogSnapshot(context: context);
        }
    }
}
```

## Troubleshooting

**Q: I enabled the flag but snapshots still aren't being written**  
A: Verify the `IUserSettingsService` is registered in DI and that `LoadSettings()` is returning the updated settings with `EnableRequirementsAnalysisSnapshot = true`.

**Q: The snapshot file is huge/empty**  
A: Check that at least one of the filter tags appears in your analysis run. Add more tags to `RequirementsAnalysisSnapshotOptions.AnalysisTags` if entire subsystems are missing.

**Q: I want to exclude certain logs from snapshots**  
A: Edit the filter tag list or use `maxTraceWindows` to reduce output. The writer always includes metadata headers regardless of content.

- easier testability and safer iteration
