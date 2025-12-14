using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for integrating with AnythingLLM workspaces.
    /// Provides workspace management capabilities for creating and listing AnythingLLM workspaces.
    /// Includes auto-start functionality for local AnythingLLM instances.
    /// </summary>
    public class AnythingLLMService
    {
        // Windows API imports for window management
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        private const int SW_MINIMIZE = 6;
        private const int SW_HIDE = 0;
        private const int SW_SHOWMINIMIZED = 2;
        
        private readonly HttpClient _httpClient;
        private string _baseUrl;
        private readonly string? _apiKey;
        private Process? _anythingLLMProcess;
        
        // Events for status updates
        public event Action<string>? StatusUpdated;
        
        // Auto-start configuration
        private const int ANYTHINGLM_PORT = 3001;
        private const int STARTUP_TIMEOUT_SECONDS = 60;
        
        // Global startup coordination to prevent multiple instances from starting simultaneously
        private static bool _globalStartupInProgress = false;
        private static readonly object _globalStartupLock = new object();
        
        // Rate limiting for service availability checks to reduce log spam
        private static DateTime _lastAvailabilityCheck = DateTime.MinValue;
        private static bool _lastAvailabilityResult = false;
        private static readonly TimeSpan _availabilityCheckCooldown = TimeSpan.FromSeconds(2);
        
        // Installation detection cache
        private static string? _cachedInstallPath;
        private static string? _cachedShortcutPath;
        private static bool? _cachedInstallationStatus;

        public AnythingLLMService(string? baseUrl = null, string? apiKey = null)
        {
            // Try to get API key from user configuration, parameter, or environment
            _apiKey = apiKey ?? GetUserApiKey();
            
            // Force localhost for local AnythingLLM instance
            _baseUrl = (baseUrl ?? "http://localhost:3001").TrimEnd('/');
            
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }
            
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Gets or sets the user's API key in local configuration
        /// </summary>
        public static string? GetUserApiKey()
        {
            try
            {
                // Try environment variable first
                var envKey = Environment.GetEnvironmentVariable("ANYTHINGLM_API_KEY");
                if (!string.IsNullOrEmpty(envKey))
                    return envKey;
                
                // Try user-specific registry location (Windows only)
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\TestCaseEditorApp\AnythingLLM");
                    return key?.GetValue("ApiKey") as string;
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Saves the user's API key to local configuration
        /// </summary>
        public static bool SetUserApiKey(string apiKey)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\TestCaseEditorApp\AnythingLLM");
                    key.SetValue("ApiKey", apiKey);
                    return true;
                }
                
                // For non-Windows platforms, could implement file-based storage here
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Detects if AnythingLLM is installed on the system
        /// </summary>
        public static (bool IsInstalled, string? InstallPath, string? ShortcutPath, string Message) DetectInstallation()
        {
            if (_cachedInstallationStatus.HasValue)
            {
                return (_cachedInstallationStatus.Value, _cachedInstallPath, _cachedShortcutPath, 
                       _cachedInstallationStatus.Value ? "AnythingLLM installation detected" : "AnythingLLM not found");
            }
            
            try
            {
                // Method 1: Check common installation directories
                var commonPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AnythingLLM"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AnythingLLM"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "AnythingLLM")
                };
                
                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var exePath = Directory.GetFiles(path, "AnythingLLM.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (exePath != null)
                        {
                            _cachedInstallPath = exePath;
                            break;
                        }
                    }
                }
                
                // Method 2: Check Start Menu shortcuts
                var startMenuPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
                };
                
                foreach (var startMenuPath in startMenuPaths)
                {
                    if (Directory.Exists(startMenuPath))
                    {
                        var shortcut = Directory.GetFiles(startMenuPath, "AnythingLLM.lnk", SearchOption.AllDirectories).FirstOrDefault();
                        if (shortcut != null)
                        {
                            _cachedShortcutPath = shortcut;
                            break;
                        }
                    }
                }
                
                // Method 3: Check Windows Registry for uninstall entries
                if (_cachedInstallPath == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var registryPaths = new[]
                    {
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                    };
                    
                    foreach (var regPath in registryPaths)
                    {
                        try
                        {
                            using var uninstallKey = Registry.LocalMachine.OpenSubKey(regPath);
                            if (uninstallKey != null)
                            {
                                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                                {
                                    using var subKey = uninstallKey.OpenSubKey(subKeyName);
                                    var displayName = subKey?.GetValue("DisplayName") as string;
                                    if (displayName?.Contains("AnythingLLM", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        var installLocation = subKey?.GetValue("InstallLocation") as string;
                                        if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                                        {
                                            var exePath = Directory.GetFiles(installLocation, "AnythingLLM.exe", SearchOption.AllDirectories).FirstOrDefault();
                                            if (exePath != null)
                                            {
                                                _cachedInstallPath = exePath;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { /* Continue searching */ }
                        
                        if (_cachedInstallPath != null) break;
                    }
                }
                
                var isInstalled = _cachedInstallPath != null || _cachedShortcutPath != null;
                _cachedInstallationStatus = isInstalled;
                
                if (isInstalled)
                {
                    return (true, _cachedInstallPath, _cachedShortcutPath, "AnythingLLM installation detected");
                }
                else
                {
                    return (false, null, null, GetInstallationInstructions());
                }
            }
            catch (Exception ex)
            {
                return (false, null, null, $"Error detecting installation: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets installation instructions for AnythingLLM
        /// </summary>
        private static string GetInstallationInstructions()
        {
            return "AnythingLLM is not installed on this system.\n\n" +
                   "To use AI features, please install AnythingLLM from:\n" +
                   "• Official website: https://anythinglm.com\n" +
                   "• GitHub releases: https://github.com/Mintplex-Labs/anything-llm/releases\n\n" +
                   "After installation, restart this application to enable AI features.";
        }
        
        /// <summary>
        /// Represents a workspace in AnythingLLM
        /// </summary>
        public class Workspace
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Slug { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
            public double? OpenAiTemp { get; set; } // Changed from string to double
            public int? OpenAiHistory { get; set; } // Changed from string to int
            public string? LastUpdatedBy { get; set; }
            public string? VectorTag { get; set; } // Added missing field
            
            // Local file management
            public bool HasLocalFile { get; set; }
            public string? LocalFilePath { get; set; }
        }

        /// <summary>
        /// Checks if a local .tcex.json file exists for the given workspace
        /// </summary>
        private static (bool exists, string? path) CheckForLocalWorkspaceFile(string workspaceName, string workspaceSlug)
        {
            // Common workspace storage locations
            var searchLocations = new[]
            {
                @"C:\Users\e10653214\Desktop\testing import", // Known test folder
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestCaseEditor"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TestCaseEditor")
            };
            
            // Possible filename patterns
            var filenamePatterns = new[]
            {
                $"{workspaceName}.tcex.json",
                $"{workspaceSlug}.tcex.json",
                $"QuickImport_Decagon_{workspaceName}.tcex.json"
            };
            
            foreach (var location in searchLocations)
            {
                if (!Directory.Exists(location)) continue;
                
                foreach (var pattern in filenamePatterns)
                {
                    var fullPath = Path.Combine(location, pattern);
                    if (File.Exists(fullPath))
                    {
                        return (true, fullPath);
                    }
                }
                
                // Also search for files containing the workspace name
                try
                {
                    var matchingFiles = Directory.GetFiles(location, "*.tcex.json")
                        .Where(f => Path.GetFileNameWithoutExtension(f).Contains(workspaceName, StringComparison.OrdinalIgnoreCase)
                                 || Path.GetFileNameWithoutExtension(f).Contains(workspaceSlug, StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();
                    
                    if (matchingFiles != null)
                    {
                        return (true, matchingFiles);
                    }
                }
                catch (Exception)
                {
                    // Ignore directory access errors
                }
            }
            
            return (false, null);
        }

        /// <summary>
        /// Checks if the AnythingLLM service is properly configured with an API key
        /// </summary>
        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

        /// <summary>
        /// Gets the configuration status message
        /// </summary>
        public string GetConfigurationStatus()
        {
            var (isInstalled, installPath, shortcutPath, message) = DetectInstallation();
            
            if (!isInstalled)
            {
                return $"❌ AnythingLLM not installed. {message}";
            }
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                return $"⚠️ AnythingLLM installed but API key not configured. Please set up your API key.";
            }
            
            return $"✅ AnythingLLM ready at {_baseUrl} (Install: {installPath ?? shortcutPath ?? "detected"})";
        }

        /// <summary>
        /// Tests connectivity to AnythingLLM API
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectivityAsync()
        {
            try
            {
                var workspaces = await GetWorkspacesAsync();
                return (true, $"✅ Connected successfully. Found {workspaces.Count} workspace(s).");
            }
            catch (Exception ex)
            {
                return (false, $"❌ Connection failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a list of all available AnythingLLM workspaces
        /// </summary>
        public async Task<List<Workspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                }
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                // For cloud APIs, try multiple base URLs and endpoints
                List<string> possibleUrls = new List<string>();
                
                if (_baseUrl.Contains("localhost"))
                {
                    possibleUrls.Add($"{_baseUrl}/api/v1/workspaces");
                }
                else
                {
                    // Only try the configured base URL for cloud instances
                    possibleUrls.Add($"{_baseUrl}/v1/workspaces");
                }
                
                Exception? lastException = null;
                foreach (var endpoint in possibleUrls)
                {
                    try
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Trying GET endpoint: {endpoint}");
                        var response = await client.GetAsync(endpoint, cancellationToken);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync(cancellationToken);
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Success! Working endpoint: {endpoint}");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Raw response: {json}");
                            
                            var result = JsonSerializer.Deserialize<WorkspaceListResponse>(json, new JsonSerializerOptions 
                            { 
                                PropertyNameCaseInsensitive = true 
                            });

                            var workspaces = result?.Workspaces ?? new List<Workspace>();
                            
                            // Check for local files for each workspace
                            foreach (var workspace in workspaces)
                            {
                                var (hasFile, filePath) = CheckForLocalWorkspaceFile(workspace.Name, workspace.Slug);
                                workspace.HasLocalFile = hasFile;
                                workspace.LocalFilePath = filePath;
                            }

                            return workspaces;
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Failed endpoint {endpoint}: {response.StatusCode} - {errorContent}");
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Exception with endpoint {endpoint}: {ex.Message}");
                        lastException = ex;
                    }
                }
                
                // If all endpoints failed, throw the last exception
                throw lastException ?? new Exception("All API endpoints failed");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error getting workspaces");
                throw;
            }
        }

        /// <summary>
        /// Checks if a workspace name already exists
        /// </summary>
        public async Task<bool> WorkspaceNameExistsAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                var workspaces = await GetWorkspacesAsync(cancellationToken);
                return workspaces?.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) ?? false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error checking workspace name existence for '{name}'");
                // If we can't check, assume it doesn't exist to allow creation attempt
                return false;
            }
        }

        /// <summary>
        /// Creates a new workspace in AnythingLLM
        /// </summary>
        public async Task<Workspace?> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Attempting to create workspace '{name}'");
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] API Key configured: {(!string.IsNullOrEmpty(_apiKey) ? "Yes" : "No")}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Base URL: {_baseUrl}");
                
                // Check if workspace name already exists
                if (await WorkspaceNameExistsAsync(name, cancellationToken))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Cannot create workspace '{name}' - name already exists");
                    throw new InvalidOperationException($"A workspace with the name '{name}' already exists. Please choose a different name.");
                }

                var payload = new
                {
                    name = name
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Sending payload: {json}");
                
                // Use the correct API endpoint based on AnythingLLM documentation
                string createEndpoint = $"{_baseUrl}/api/v1/workspace/new";
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] POST endpoint: {createEndpoint}");
                
                var response = await _httpClient.PostAsync(createEndpoint, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Create workspace failed with {response.StatusCode}: {errorText}");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[DEBUG] Success response: {responseJson}");
                
                var result = JsonSerializer.Deserialize<WorkspaceCreateResponse>(responseJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                return result?.Workspace;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error creating workspace '{name}'");
                return null;
            }
        }

        /// <summary>
        /// Deletes a workspace from AnythingLLM
        /// </summary>
        public async Task<bool> DeleteWorkspaceAsync(string slug, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/workspace/{slug}", cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Failed to delete workspace '{slug}': {response.StatusCode}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error deleting workspace '{slug}'");
                return false;
            }
        }

        /// <summary>
        /// Sends a chat message to a workspace
        /// </summary>
        public async Task<string?> SendChatMessageAsync(string workspaceSlug, string message, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new
                {
                    message = message,
                    mode = "chat"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/chat", content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Failed to send chat message to '{workspaceSlug}': {response.StatusCode}");
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<ChatResponse>(responseJson, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                return result?.TextResponse;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error sending chat message to workspace '{workspaceSlug}'");
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Inner exception: {ex.InnerException.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// Checks if a port is in use (indicating a service is running)
        /// </summary>
        private bool IsPortInUse(int port)
        {
            try
            {
                var tcpListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
                tcpListener.Start();
                tcpListener.Stop();
                return false; // Port is available
            }
            catch
            {
                return true; // Port is in use
            }
        }
        
        /// <summary>
        /// Checks if AnythingLLM process is already running
        /// </summary>
        private bool IsAnythingLLMRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("AnythingLLM");
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Starts AnythingLLM application and waits for it to be ready
        /// </summary>
        public async Task<(bool Success, string Message)> StartAnythingLLMAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // First check if already running
                if (IsPortInUse(ANYTHINGLM_PORT))
                {
                    OnStatusUpdated("AnythingLLM is already running");
                    return (true, "AnythingLLM is already running on port 3001");
                }
                
                if (IsAnythingLLMRunning())
                {
                    OnStatusUpdated("AnythingLLM process found, waiting for service...");
                }
                else
                {
                    // Detect AnythingLLM installation
                    var (isInstalled, installPath, shortcutPath, installMessage) = DetectInstallation();
                    
                    if (!isInstalled)
                    {
                        return (false, installMessage);
                    }
                    
                    OnStatusUpdated("Starting AnythingLLM application...");
                    
                    // Try to start using available methods
                    ProcessStartInfo? startInfo = null;
                    
                    if (!string.IsNullOrEmpty(shortcutPath) && File.Exists(shortcutPath))
                    {
                        // Use shortcut if available
                        startInfo = new ProcessStartInfo
                        {
                            FileName = shortcutPath,
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Minimized
                        };
                    }
                    else if (!string.IsNullOrEmpty(installPath) && File.Exists(installPath))
                    {
                        // Use direct executable path
                        startInfo = new ProcessStartInfo
                        {
                            FileName = installPath,
                            UseShellExecute = true,
                            WindowStyle = ProcessWindowStyle.Minimized
                        };
                    }
                    
                    if (startInfo == null)
                    {
                        return (false, "AnythingLLM detected but no valid launch method found");
                    }
                    
                    _anythingLLMProcess = Process.Start(startInfo);
                    
                    if (_anythingLLMProcess == null)
                    {
                        return (false, "Failed to start AnythingLLM process");
                    }
                    
                    OnStatusUpdated("AnythingLLM started, waiting for service initialization...");
                }
                
                // Wait for service to become available
                var startTime = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(STARTUP_TIMEOUT_SECONDS);
                int pollInterval = 1000; // Start with 1 second
                int pollIncrement = 500;  // Increase by 500ms each time
                int maxPollInterval = 3000; // Cap at 3 seconds
                
                while (DateTime.Now - startTime < timeout)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return (false, "Startup cancelled by user");
                    }
                    
                    if (await IsServiceAvailableAsync(cancellationToken))
                    {
                        // Try to minimize the AnythingLLM window after it's started
                        _ = Task.Run(() => MinimizeAnythingLLMWindow());
                        
                        OnStatusUpdated("AnythingLLM service is ready!");
                        return (true, "AnythingLLM started successfully and is ready for use");
                    }
                    
                    var elapsed = (int)(DateTime.Now - startTime).TotalSeconds;
                    if (elapsed <= 10) 
                    {
                        // More frequent updates in first 10 seconds
                        OnStatusUpdated($"Waiting for AnythingLLM service... ({elapsed}/{STARTUP_TIMEOUT_SECONDS}s)");
                    }
                    else if (elapsed % 5 == 0) 
                    {
                        // Less frequent updates after 10 seconds
                        OnStatusUpdated($"Waiting for AnythingLLM service... ({elapsed}/{STARTUP_TIMEOUT_SECONDS}s)");
                    }
                    
                    await Task.Delay(pollInterval, cancellationToken);
                    
                    // Gradually increase polling interval for efficiency
                    if (pollInterval < maxPollInterval)
                    {
                        pollInterval = Math.Min(pollInterval + pollIncrement, maxPollInterval);
                    }
                }
                
                return (false, $"AnythingLLM failed to start within {STARTUP_TIMEOUT_SECONDS} seconds");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error starting AnythingLLM");
                return (false, $"Error starting AnythingLLM: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Attempts to minimize or hide the AnythingLLM window
        /// </summary>
        private void MinimizeAnythingLLMWindow()
        {
            try
            {
                // Give the application time to fully load
                Thread.Sleep(5000);
                
                var processes = Process.GetProcessesByName("AnythingLLM");
                foreach (var process in processes)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_MINIMIZE);
                        TestCaseEditorApp.Services.Logging.Log.Info("[AnythingLLM] Window minimized");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error minimizing window");
            }
        }
        
        /// <summary>
        /// Ensures AnythingLLM is running and ready, starting it if necessary
        /// </summary>
        public async Task<(bool Success, string Message)> EnsureServiceRunningAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // First try to connect to existing service
                if (await IsServiceAvailableAsync(cancellationToken))
                {
                    return (true, "AnythingLLM service is already running and ready");
                }
                
                // Global coordination to prevent multiple instances from starting simultaneously
                lock (_globalStartupLock)
                {
                    if (_globalStartupInProgress)
                    {
                        OnStatusUpdated("AnythingLLM startup already in progress by another instance");
                        // Return success assuming the other instance will start it
                        return (true, "AnythingLLM startup delegated to another instance");
                    }
                    _globalStartupInProgress = true;
                }
                
                try
                {
                    // If not available, try to start it
                    OnStatusUpdated("AnythingLLM not detected, attempting to start...");
                    return await StartAnythingLLMAsync(cancellationToken);
                }
                finally
                {
                    lock (_globalStartupLock)
                    {
                        _globalStartupInProgress = false;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_globalStartupLock)
                {
                    _globalStartupInProgress = false;
                }
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error ensuring service is running");
                return (false, $"Error ensuring AnythingLLM service: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Raises the StatusUpdated event
        /// </summary>
        private void OnStatusUpdated(string status)
        {
            StatusUpdated?.Invoke(status);
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] {status}");
        }
        
        /// <summary>
        /// Checks if AnythingLLM service is available and responding
        /// </summary>
        public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Simple rate limiting to prevent excessive checks from multiple instances
                var now = DateTime.Now;
                if (now - _lastAvailabilityCheck < _availabilityCheckCooldown)
                {
                    return _lastAvailabilityResult;
                }
                
                // Prioritize endpoints likely to be faster/more reliable
                var endpoints = new[]
                {
                    "http://localhost:3001/api/v1/workspaces", // Most likely endpoint for desktop AnythingLLM
                    $"{_baseUrl}/api/v1/workspaces",          // Use existing base URL first
                    "http://localhost:3001/api/workspaces",   // Alternative desktop endpoint
                    "http://localhost:3000/api/v1/workspaces" // Alternative port
                };

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(3); // Shorter timeout for faster checks
                
                // Always try API key first if we have one
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                }

                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var response = await client.GetAsync(endpoint, cancellationToken);
                        
                        // Accept successful responses
                        if (response.IsSuccessStatusCode)
                        {
                            // Update base URL based on working endpoint
                            if (endpoint.Contains("localhost:3001"))
                            {
                                _baseUrl = "http://localhost:3001";
                            }
                            else if (endpoint.Contains("localhost:3000"))
                            {
                                _baseUrl = "http://localhost:3000";
                            }
                            
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Service available via {endpoint} (status: {response.StatusCode})");
                            
                            // Cache result
                            _lastAvailabilityCheck = now;
                            _lastAvailabilityResult = true;
                            return true;
                        }
                        // Accept auth errors as "service available"
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Service available but needs auth via {endpoint}");
                            
                            // Cache result
                            _lastAvailabilityCheck = now;
                            _lastAvailabilityResult = true;
                            return true;
                        }
                    }
                    catch (HttpRequestException)
                    {
                        // Try next endpoint
                        continue;
                    }
                    catch (TaskCanceledException)
                    {
                        // Timeout, try next endpoint  
                        continue;
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info("[AnythingLLM] Service not available - no endpoints responded");
                
                // Cache result
                _lastAvailabilityCheck = now;
                _lastAvailabilityResult = false;
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error checking service availability");
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                _httpClient?.Dispose();
                
                // Don't kill the AnythingLLM process on dispose as user might want to keep it running
                // Just clean up our reference
                _anythingLLMProcess = null;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error during disposal");
            }
        }

        // Response DTOs for JSON deserialization
        private class WorkspaceListResponse
        {
            public List<Workspace>? Workspaces { get; set; }
        }

        private class WorkspaceCreateResponse
        {
            public Workspace? Workspace { get; set; }
            public string? Message { get; set; }
        }

        private class ChatResponse
        {
            public string? TextResponse { get; set; }
            public string? Id { get; set; }
            public string? Type { get; set; }
        }
    }
}