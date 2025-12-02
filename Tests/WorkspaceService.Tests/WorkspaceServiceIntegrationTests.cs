using System;
using System.Diagnostics;
using System.IO;
using Xunit;

public class WorkspaceServiceIntegrationTests
{
    [Fact]
    public void RunnerExecutesAndProducesArtifacts()
    {
        // Run the self-contained runner project (built by CI or locally)
        // Locate repository root by walking up from the test assembly directory
        var dir = AppContext.BaseDirectory;
        string? repoRoot = null;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "TestCaseEditorApp.csproj"))) { repoRoot = dir; break; }
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        if (repoRoot == null) throw new InvalidOperationException("Repository root not found from test assembly location.");

        string[] candidates = new[] {
            Path.Combine(repoRoot, "Tests", "WorkspaceService.Runner", "bin", "Debug", "net8.0-windows", "WorkspaceService.Runner.dll"),
            Path.Combine(repoRoot, "Tests", "WorkspaceService.Runner", "bin", "Release", "net8.0-windows", "WorkspaceService.Runner.dll")
        };

        string? runnerDll = null;
        foreach (var c in candidates) if (File.Exists(c)) { runnerDll = Path.GetFullPath(c); break; }
        if (runnerDll == null) throw new InvalidOperationException("Runner DLL not found. Build the runner before running tests.");

        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments = "\"" + runnerDll + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start runner");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        // Provide output in assertion message to aid debugging when runner fails
        var combined = stdout + Environment.NewLine + stderr;
        if (p.ExitCode != 0)
            throw new Exception("Runner failed:\n" + combined);
        Assert.Contains("Runner: all checks passed", combined);
    }
}
