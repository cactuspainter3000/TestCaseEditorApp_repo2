using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TestCaseEditorApp.Services.Logging;

namespace TestCaseEditorApp.Services;

/// <summary>
/// Manages the Ollama process lifecycle: starting, stopping, health checks, and stuck-state detection.
/// </summary>
public interface IOllamaProcessManager
{
    /// <summary>
    /// Ensures Ollama is running and healthy. Starts if not running, restarts if stuck.
    /// </summary>
    Task EnsureOllamaRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the Ollama process (kills existing and starts fresh).
    /// </summary>
    Task RestartOllamaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Ollama is responding to HTTP health checks.
    /// </summary>
    Task<bool> IsOllamaHealthyAsync(CancellationToken cancellationToken = default);
}

public class OllamaProcessManager : IOllamaProcessManager
{
    private const string OllamaProcessName = "ollama";
    private const string OllamaHealthCheckUrl = "http://localhost:11434/api/tags";
    private const int StartupTimeoutSeconds = 30;
    private const int HealthCheckTimeoutSeconds = 5;

    private readonly HttpClient _httpClient;
    private bool _environmentConfigured = false;

    public OllamaProcessManager()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(HealthCheckTimeoutSeconds)
        };
    }

    /// <summary>
    /// Configure Ollama environment variables for reliable operation.
    /// </summary>
    private void ConfigureEnvironmentVariables()
    {
        if (_environmentConfigured)
            return;

        // Set load timeout to 10 minutes (model loads in ~4s but allows buffer for slow systems)
        Environment.SetEnvironmentVariable("OLLAMA_LOAD_TIMEOUT", "10m");
        
        // Keep model in memory for 5 minutes to avoid cold-start delays between requests
        // With 32GB RAM, both models (3.2GB total) can coexist comfortably
        Environment.SetEnvironmentVariable("OLLAMA_KEEP_ALIVE", "5m");

        Log.Info("[OllamaProcessManager] Environment variables configured: LOAD_TIMEOUT=10m, KEEP_ALIVE=5m");
        _environmentConfigured = true;
    }

    /// <summary>
    /// Check if any Ollama process is currently running.
    /// </summary>
    private bool IsOllamaProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(OllamaProcessName);
            bool isRunning = processes.Length > 0;
            
            foreach (var proc in processes)
                proc.Dispose();

            return isRunning;
        }
        catch (Exception ex)
        {
            Log.Warn($"[OllamaProcessManager] Error checking Ollama process: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if Ollama responds to HTTP health check (GET /api/tags).
    /// </summary>
    public async Task<bool> IsOllamaHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(OllamaHealthCheckUrl, cancellationToken);
            bool isHealthy = response.IsSuccessStatusCode;
            
            if (isHealthy)
                Log.Debug("[OllamaProcessManager] Health check passed (HTTP 200)");
            else
                Log.Warn($"[OllamaProcessManager] Health check failed: HTTP {(int)response.StatusCode}");

            return isHealthy;
        }
        catch (HttpRequestException ex)
        {
            Log.Debug($"[OllamaProcessManager] Health check failed (connection refused): {ex.Message}");
            return false;
        }
        catch (TaskCanceledException)
        {
            Log.Debug("[OllamaProcessManager] Health check timed out");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"[OllamaProcessManager] Health check error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Start the Ollama process and wait for it to become healthy.
    /// </summary>
    private async Task StartOllamaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Info("[OllamaProcessManager] Starting Ollama service...");

            // Configure environment before starting
            ConfigureEnvironmentVariables();

            // Start Ollama in background
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Ollama process (Process.Start returned null)");
            }

            Log.Info($"[OllamaProcessManager] Ollama process started (PID: {process.Id})");

            // Wait for Ollama to become healthy
            var startTime = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(StartupTimeoutSeconds);

            while (DateTime.UtcNow - startTime < timeout)
            {
                if (await IsOllamaHealthyAsync(cancellationToken))
                {
                    var elapsedSeconds = (DateTime.UtcNow - startTime).TotalSeconds;
                    Log.Info($"[OllamaProcessManager] ✅ Ollama ready in {elapsedSeconds:F1} seconds");
                    return;
                }

                await Task.Delay(1000, cancellationToken); // Check every second
            }

            throw new TimeoutException($"Ollama did not become healthy within {StartupTimeoutSeconds} seconds");
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            Log.Error($"[OllamaProcessManager] Failed to start Ollama: {ex.Message}");
            throw new InvalidOperationException($"Cannot start Ollama: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Kill all Ollama processes and start fresh.
    /// </summary>
    public async Task RestartOllamaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Info("[OllamaProcessManager] Restarting Ollama (killing existing processes)...");

            // Kill all Ollama processes
            var processes = Process.GetProcessesByName(OllamaProcessName);
            foreach (var proc in processes)
            {
                try
                {
                    Log.Info($"[OllamaProcessManager] Killing Ollama process (PID: {proc.Id})");
                    proc.Kill();
                    proc.WaitForExit(5000); // Wait up to 5 seconds for graceful exit
                }
                catch (Exception ex)
                {
                    Log.Warn($"[OllamaProcessManager] Error killing process {proc.Id}: {ex.Message}");
                }
                finally
                {
                    proc.Dispose();
                }
            }

            // Wait a moment for ports to be released
            await Task.Delay(2000, cancellationToken);

            // Start fresh
            await StartOllamaAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error($"[OllamaProcessManager] Failed to restart Ollama: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Ensure Ollama is running and healthy. Start if not running, restart if stuck.
    /// </summary>
    public async Task EnsureOllamaRunningAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Configure environment first (even if Ollama already running, affects future starts)
            ConfigureEnvironmentVariables();

            // Check if process is running
            bool processRunning = IsOllamaProcessRunning();
            bool isHealthy = false;

            if (processRunning)
            {
                Log.Debug("[OllamaProcessManager] Ollama process detected, checking health...");
                isHealthy = await IsOllamaHealthyAsync(cancellationToken);
            }

            if (processRunning && isHealthy)
            {
                Log.Info("[OllamaProcessManager] ✅ Ollama is running and healthy");
                return;
            }

            if (processRunning && !isHealthy)
            {
                Log.Warn("[OllamaProcessManager] Ollama process running but not responding - restarting...");
                await RestartOllamaAsync(cancellationToken);
                return;
            }

            // Not running at all - start it
            Log.Info("[OllamaProcessManager] Ollama not running - starting...");
            await StartOllamaAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error($"[OllamaProcessManager] Failed to ensure Ollama running: {ex.Message}");
            throw;
        }
    }
}

