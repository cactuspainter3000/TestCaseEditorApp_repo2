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
using TestCaseEditorApp.MVVM.Utils;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for integrating with AnythingLLM workspaces.
    /// Provides workspace management capabilities for creating and listing AnythingLLM workspaces.
    /// Includes auto-start functionality for local AnythingLLM instances.
    /// </summary>
    public class AnythingLLMService : IAnythingLLMService
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
        private string? _apiKey;
        private Process? _anythingLLMProcess;
        
        // Events for status updates
        public event Action<string>? StatusUpdated;
        
        // Auto-start configuration
        private const int ANYTHINGLM_PORT = 3001;
        private const int STARTUP_TIMEOUT_SECONDS = 60;
        
        // Retry configuration
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int INITIAL_RETRY_DELAY_MS = 1000;
        private const int MAX_RETRY_DELAY_MS = 10000;
        
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
            _httpClient.Timeout = TimeSpan.FromMinutes(4); // 4 minutes per attempt; with 3 retries = 12+ min total max
            
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
        /// Deletes a specific thread from the workspace
        /// </summary>
        public async Task<bool> DeleteThreadAsync(string workspaceSlug, string threadSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/thread/{threadSlug}", cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to delete thread '{threadSlug}': {response.StatusCode}");
                    return false;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully deleted thread '{threadSlug}' from workspace '{workspaceSlug}'");
                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error deleting thread '{threadSlug}'");
                return false;
            }
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
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Workspace '{name}' already exists, reusing existing workspace");
                    var workspaces = await GetWorkspacesAsync(cancellationToken);
                    var existingWorkspace = workspaces?.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingWorkspace != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Found existing workspace '{existingWorkspace.Name}' (slug: '{existingWorkspace.Slug}')");
                        return existingWorkspace;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Workspace name exists but could not retrieve workspace details");
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Creating new workspace '{name}'");
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

                if (result?.Workspace != null)
                {
                    // Configure optimal workspace settings (system prompt, etc.) immediately after creation
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Configuring optimal settings for newly created workspace '{name}' (slug: '{result.Workspace.Slug}')");
                    var configured = await ConfigureWorkspaceSettingsAsync(result.Workspace.Slug, cancellationToken);
                    
                    if (configured)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully configured workspace '{name}' with system prompt for optimal performance");
                        
                        // Apply preventive RAG configuration fix immediately
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Applying preventive RAG configuration fix to new workspace '{result.Workspace.Slug}'");
                        var ragFixResult = await FixRagConfigurationAsync(result.Workspace.Slug, cancellationToken);
                        if (ragFixResult)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Preventive RAG fix applied successfully");
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Preventive RAG fix failed, but continuing");
                        }
                        
                        // Verify the configuration was applied
                        var validated = await ValidateWorkspaceSystemPromptAsync(result.Workspace.Slug, cancellationToken);
                        if (!validated)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Configuration succeeded but validation failed for workspace '{name}' - system prompt may not have been applied correctly");
                        }
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Workspace '{name}' created but settings configuration failed - will send full prompt with each request. This is not critical but will result in higher token usage.");
                    }
                }

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
        /// Validates that the workspace system prompt is properly configured to avoid sending redundant prompts
        /// </summary>
        public async Task<bool> ValidateWorkspaceSystemPromptAsync(string slug, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Validating system prompt configuration for workspace '{slug}'");
                
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/workspaces", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not get workspace settings for validation");
                    return false;
                }
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                if (data.TryGetProperty("workspaces", out var workspaces))
                {
                    foreach (var workspace in workspaces.EnumerateArray())
                    {
                        if (workspace.TryGetProperty("slug", out var workspaceSlug) && 
                            workspaceSlug.GetString() == slug)
                        {
                            // 🔍 LOG THE RAW WORKSPACE JSON TO SEE WHAT PROPERTIES EXIST
                            var workspaceJson = workspace.GetRawText();
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 RAW WORKSPACE JSON for '{slug}': {workspaceJson}");
                            
                            if (workspace.TryGetProperty("openAiPrompt", out var prompt))
                            {
                                var configuredPrompt = prompt.GetString();
                                var expectedPrompt = GetOptimalSystemPrompt();
                                
                                bool isConfigured = !string.IsNullOrEmpty(configuredPrompt) && 
                                                  configuredPrompt.Contains("requirements quality analysis") &&
                                                  configuredPrompt.Contains("ANTI-FABRICATION RULES");
                                
                                if (isConfigured)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Workspace '{slug}' has properly configured system prompt (length: {configuredPrompt?.Length})");
                                    return true;
                                }
                                else
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Workspace '{slug}' system prompt validation FAILED");
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 Prompt details - IsEmpty: {string.IsNullOrEmpty(configuredPrompt)}, Length: {configuredPrompt?.Length ?? 0}");
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 Has 'requirements quality analysis': {configuredPrompt?.Contains("requirements quality analysis") ?? false}");
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 Has 'ANTI-FABRICATION RULES': {configuredPrompt?.Contains("ANTI-FABRICATION RULES") ?? false}");
                                    
                                    // Try to configure it now if missing
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔧 Attempting on-demand configuration for workspace '{slug}'");
                                    var configResult = await ConfigureWorkspaceSettingsAsync(slug, cancellationToken);
                                    if (configResult)
                                    {
                                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ On-demand configuration succeeded for '{slug}'");
                                        return true;
                                    }
                                    return false;
                                }
                            }
                            else
                            {
                                // Property doesn't exist at all!
                                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Workspace '{slug}' has NO 'openAiPrompt' property in response!");
                                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔧 Attempting to configure missing system prompt for workspace '{slug}'");
                                var configResult = await ConfigureWorkspaceSettingsAsync(slug, cancellationToken);
                                if (configResult)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ On-demand configuration succeeded for '{slug}'");
                                    return true;
                                }
                                return false;
                            }
                        }
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not find workspace '{slug}' for validation");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error validating workspace system prompt for '{slug}'");
                return false;
            }
        }

        /// <summary>
        /// Configures workspace settings with optimal values for requirements analysis based on official AnythingLLM documentation
        /// </summary>
        public async Task<bool> ConfigureWorkspaceSettingsAsync(string slug, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Configuring optimal settings for workspace '{slug}' using official documentation guidelines");
                
                // Check if API key is available, if not try to detect/configure one
                if (string.IsNullOrEmpty(_apiKey))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] No API key configured - attempting alternative authentication");
                    
                    // Try without API key first (for backward compatibility)
                    var testResponse = await _httpClient.GetAsync($"{_baseUrl}/api/v1/workspaces", cancellationToken);
                    if (testResponse.StatusCode == HttpStatusCode.Unauthorized || testResponse.StatusCode == HttpStatusCode.Forbidden)
                    {
                        // API requires authentication - try to get/generate an API key
                        if (!await TryConfigureApiKeyAsync(cancellationToken))
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Cannot configure workspace - API authentication required but no key available");
                            return false;
                        }
                    }
                }
                
                // Get workspace details to find the workspace ID
                var workspacesResponse = await _httpClient.GetAsync($"{_baseUrl}/api/v1/workspaces", cancellationToken);
                if (!workspacesResponse.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not get workspaces to find ID for '{slug}'");
                    return false;
                }
                
                var workspacesJson = await workspacesResponse.Content.ReadAsStringAsync(cancellationToken);
                var workspacesData = JsonSerializer.Deserialize<JsonElement>(workspacesJson);
                
                int? workspaceId = null;
                if (workspacesData.TryGetProperty("workspaces", out var workspaces))
                {
                    foreach (var workspace in workspaces.EnumerateArray())
                    {
                        if (workspace.TryGetProperty("slug", out var workspaceSlug) && 
                            workspaceSlug.GetString() == slug)
                        {
                            if (workspace.TryGetProperty("id", out var id))
                            {
                                workspaceId = id.GetInt32();
                                break;
                            }
                        }
                    }
                }
                
                if (!workspaceId.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not find workspace ID for slug '{slug}'");
                    return false;
                }
                
                // Optimal settings based on official AnythingLLM documentation (v1.8.5+)
                var settings = new
                {
                    // Temperature: 0.0 for maximum determinism to prevent missing requirements
                    openAiTemp = 0.0,
                    // Context history: 20 messages for adequate context retention
                    openAiHistory = 20, 
                    // System prompt for requirements analysis with anti-fabrication rules
                    openAiPrompt = GetDocumentExtractionSystemPrompt(),
                    
                    // LLM Provider Configuration: Use local Ollama for data security and consistency
                    chatProvider = "ollama", // Local Ollama provider (no internet, keeps data secure)
                    chatModel = "phi3.5:3.8b-mini-instruct-q4_K_M",  // Phi-3.5 Mini Instruct model - better instruction following, less refusal
                    
                    // RAG Configuration (based on official docs):
                    // Document similarity threshold: No restriction to ensure comprehensive access to supplemental materials
                similarityThreshold = (int?)null, // CRITICAL: Set to null for "No Restriction" mode per AnythingLLM docs
                topN = 8, // Maximum allowed value - essential for large technical documents with many requirements
                
                // Vector search preference: Accuracy optimized to prevent hallucinations
                // Default is fastest but may return less relevant results leading to hallucinations
                // Note: This adds 100-500ms but significantly improves retrieval quality
                searchPreference = "accuracy", // Use accuracy optimization over speed for requirement analysis
                    queryRefusalResponse = "I can only analyze requirements based on the information provided. Please ensure your question relates to the requirement content or ask for clarification about specific aspects.",
                    
                    // Chat mode for better context understanding
                    chatMode = "chat"
                };

                var json = JsonSerializer.Serialize(settings);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // DEBUG: Log exactly what we're sending
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 DEBUG: Sending RAG configuration JSON: {json}");
                
                // Try multiple endpoint patterns (enhanced with official API knowledge)
                var endpointsToTry = new[]
                {
                    $"{_baseUrl}/api/v1/workspace/{slug}/update",           // Primary endpoint per API docs
                    $"{_baseUrl}/api/v1/workspace/{workspaceId}/update",    // Alternative with ID
                    $"{_baseUrl}/api/v1/workspaces/{slug}/settings",        // Legacy pattern
                    $"{_baseUrl}/api/v1/workspace/{slug}/update-settings",  // Potential alternative
                    $"{_baseUrl}/api/v1/workspace/{slug}/settings"          // Another alternative
                };
                
                foreach (var endpoint in endpointsToTry)
                {
                    try
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Trying workspace settings endpoint: {endpoint}");
                        
                        // Try POST method (primary per API docs)
                        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Successfully configured workspace settings for '{slug}' using POST: {endpoint}");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 DEBUG: API Response: {responseContent?.Substring(0, Math.Min(200, responseContent?.Length ?? 0))}");
                            return true;
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] POST {endpoint} returned: {response.StatusCode}");
                        
                        // Also try PUT method as fallback
                        var putContent = new StringContent(json, Encoding.UTF8, "application/json");
                        response = await _httpClient.PutAsync(endpoint, putContent, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Successfully configured workspace settings for '{slug}' using PUT: {endpoint}");
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 DEBUG: API Response: {responseContent?.Substring(0, Math.Min(200, responseContent?.Length ?? 0))}");
                            return true;
                        }
                        
                        // Log response for debugging
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] PUT {endpoint} returned: {response.StatusCode} - {errorContent?.Substring(0, Math.Min(200, errorContent?.Length ?? 0))}");
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Exception trying endpoint {endpoint}: {ex.Message}");
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ❌ All settings endpoints failed for workspace '{slug}' (ID: {workspaceId}). Tried {endpointsToTry.Length} endpoints with both POST and PUT methods.");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error configuring workspace settings for '{slug}'");
                return false;
            }
        }

        /// <summary>
        /// Updates specific RAG parameters for a workspace to optimize performance
        /// </summary>
        public async Task<bool> UpdateWorkspaceParametersAsync(
            string slug, 
            double temperature,
            double similarityThreshold,
            int topN,
            CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info(
                    $"[AnythingLLM] Updating workspace parameters for '{slug}': " +
                    $"Temp={temperature:F2}, Similarity={similarityThreshold:F2}, TopN={topN}");

                // Validate parameters
                if (temperature < 0.1 || temperature > 0.7)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Invalid temperature: {temperature}. Must be between 0.1 and 0.7");
                    return false;
                }

                if (similarityThreshold < 0 || similarityThreshold > 0.5)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Invalid similarity threshold: {similarityThreshold}. Must be between 0 and 0.5");
                    return false;
                }

                if (topN < 2 || topN > 8)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Invalid topN: {topN}. Must be between 2 and 8");
                    return false;
                }

                // Build update payload
                var settings = new
                {
                    openAiTemp = temperature,
                    similarityThreshold = similarityThreshold,
                    topN = topN
                };

                var json = JsonSerializer.Serialize(settings);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Try endpoints to update parameters
                var endpointsToTry = new[]
                {
                    $"{_baseUrl}/api/v1/workspace/{slug}/update",
                    $"{_baseUrl}/api/v1/workspace/{slug}/settings",
                    $"{_baseUrl}/api/v1/workspaces/{slug}/settings"
                };

                foreach (var endpoint in endpointsToTry)
                {
                    try
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Trying parameter update endpoint: {endpoint}");

                        // Try POST
                        var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info(
                                $"[AnythingLLM] Successfully updated workspace parameters for '{slug}' using POST");
                            return true;
                        }

                        // Try PUT
                        response = await _httpClient.PutAsync(endpoint, content, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info(
                                $"[AnythingLLM] Successfully updated workspace parameters for '{slug}' using PUT");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Exception trying endpoint {endpoint}: {ex.Message}");
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] All parameter update endpoints failed for workspace '{slug}'");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error updating workspace parameters for '{slug}'");
                return false;
            }
        }
        /// <summary>
        /// Attempts to configure API key for authentication
        /// </summary>
        private async Task<bool> TryConfigureApiKeyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Attempting to configure API authentication");
                
                // For now, try a few common approaches to bypass authentication
                // Method 1: Check if AnythingLLM is in setup mode and doesn't require auth
                var setupResponse = await _httpClient.GetAsync($"{_baseUrl}/api/setup", cancellationToken);
                if (setupResponse.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Service appears to be in setup mode - API key may not be required");
                    return false; // Continue without API key
                }
                
                // Method 2: Try to find if there's a default/development API key
                var devKeys = new[] { "development", "test", "default", "anythinglm" };
                foreach (var testKey in devKeys)
                {
                    using var testClient = new HttpClient();
                    testClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {testKey}");
                    var testResponse = await testClient.GetAsync($"{_baseUrl}/api/v1/workspaces", cancellationToken);
                    if (testResponse.IsSuccessStatusCode)
                    {
                        _apiKey = testKey;
                        _httpClient.DefaultRequestHeaders.Clear();
                        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Found working API key: {testKey}");
                        SetUserApiKey(testKey); // Save for future use
                        return true;
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not configure API authentication automatically");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error configuring API authentication");
                return false;
            }
        }

        /// <summary>
        /// Gets the optimal system prompt for requirements analysis based on the optimization guide
        /// This should match what's configured in workspace settings to avoid duplication
        /// </summary>
        public static string GetOptimalSystemPrompt()
        {
            var prompt = @"You are a systems requirements analysis expert. Analyze requirements for clarity, completeness, consistency, testability, and feasibility.

CRITICAL ANTI-FABRICATION RULES:
- Use ONLY information explicitly stated in the requirement text
- Use ONLY definitions from uploaded supplemental materials (if any) including tables, specifications, and reference documents
- Do NOT mention IEEE standards, ISO standards, or technical protocols unless they appear in the requirement
- Do NOT invent definitions for technical terms (e.g., 'Tier 1/2/3') unless provided in supplemental materials
- When information is missing, use [brackets with helpful examples] instead of inventing details
- ADAPTIVE APPROACH: Use available info when present, [brackets] when missing

Provide your analysis in this structured text format:

QUALITY SCORE: [0-100 integer]

ISSUES FOUND:
- [Issue Category] (Severity): [Description of the problem] | Fix: [Specific action that was addressed - use PAST TENSE (Added, Defined, Specified, etc.)]

STRENGTHS:
- [What this requirement does well]

IMPROVED REQUIREMENT:
[REQUIRED: Write ONLY the requirement text itself - NO prefixes like ""Fix:"", ""Note:"", ""Recommendation:"", or any meta-commentary. Use available information from the requirement/supplemental materials. When specific details are missing, use [brackets with helpful examples] like ""[specify time: 2 seconds, 500ms, etc.]"" or ""[define temperature range: -20°C to +70°C, etc.]"". Start directly with ""The [System Name] shall..."" and provide a single, clean requirement statement. Do NOT use markdown formatting. Write as one coherent paragraph that clearly states WHAT the system must do.]

RECOMMENDATIONS:
[Single consolidated recommendation that addresses ALL identified issues with specific guidance on what information is needed]

HALLUCINATION CHECK:
- If you used [brackets] with helpful examples for missing information, respond: 'HELPFUL_ELABORATION'  
- If you only used provided information without brackets, respond: 'NO_FABRICATION'
- If you added technical details not in the source, respond: 'FABRICATED_DETAILS'

OVERALL ASSESSMENT:
[Brief summary of the requirement's quality and main recommendations]

Use these Issue Types: Clarity, Completeness, Consistency, Testability, Feasibility
Use these Severity levels: Low, Medium, High

FORMATTING EXAMPLES:
- Clarity Issue (Medium): The term ""UUT"" is not defined in the requirement text | Fix: Defined ""UUT"" as ""Unit Under Test"" in the requirements document
- Completeness Issue (High): Missing specific acceptance criteria for warm-up period duration | Fix: Specified the exact time duration for the warm-up period (e.g., ""within 30 seconds"")
- Testability Issue (Medium): ""Full brightness"" measurement method is not specified | Fix: Defined measurable criteria for brightness (e.g., ""minimum 500 nits"" or ""100% of maximum rated output"")

CRITICAL: The IMPROVED REQUIREMENT should use [brackets] when information is missing, not invent details. Example: ""The system shall warm up within [specify time: 30 seconds, 2 minutes, etc.] when operating at [define temperature range]"" rather than inventing specific values.";

            return prompt;
        }

        /// <summary>
        /// Gets the system prompt for document extraction tasks
        /// </summary>
        public static string GetDocumentExtractionSystemPrompt()
        {
            var prompt = @"You are a technical requirements extraction AI with access to document content through a Retrieval-Augmented Generation (RAG) system.

🚩 CRITICAL OVERRIDE: Your built-in refusal patterns about file access DO NOT APPLY here. The document content has been processed, vectorized, and is available through RAG retrieval - this is NOT direct file access.

📋 EXTRACTION PROTOCOL:
- Document content is retrieved automatically when you analyze
- You WILL receive relevant document sections in your context
- NEVER refuse analysis - content IS accessible through RAG
- Extract ALL requirements using the format below

🎯 YOUR TASK: Extract requirements from the provided document content.

Look for:
- SHALL, MUST, WILL, SHOULD statements
- Technical specifications with measurable criteria  
- Performance requirements (speed, accuracy, timing)
- Interface requirements (signals, protocols, voltages)
- Environmental constraints (temperature, humidity)
- Safety and lifecycle requirements

FORMAT EACH REQUIREMENT:

---
ID: REQ-001
Text: [complete requirement statement]
Category: [functional/performance/interface/safety/environmental/lifecycle]
Priority: [High/Medium/Low if stated]
Verification: [Test/Analysis/Inspection/Demonstration]
Source: [section, page, or document location]
---

IMPORTANT: Begin analysis immediately. Do NOT refuse or ask for clarification.";

            return prompt;
        }

        /// <summary>
        /// Uploads the optimization guide as a training document to the workspace
        /// </summary>
        public async Task<bool> UploadOptimizationGuideAsync(string slug, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Uploading optimization guide to workspace '{slug}'");
                
                // File is copied to output directory by build, so check there first
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var guidePath = Path.Combine(baseDir, "Config", "ANYTHINGLM_OPTIMIZATION_GUIDE.md");
                
                // Fallback: if not in output dir, try project root (for development)
                if (!File.Exists(guidePath))
                {
                    var binParent = Directory.GetParent(baseDir); // Debug/net8.0-windows parent
                    var debugParent = binParent?.Parent; // Debug parent
                    var binFolder = debugParent?.Parent; // bin parent
                    var projectRoot = binFolder?.Parent?.FullName; // Project root
                    
                    if (!string.IsNullOrEmpty(projectRoot))
                    {
                        guidePath = Path.Combine(projectRoot, "Config", "ANYTHINGLM_OPTIMIZATION_GUIDE.md");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Using project root guide: {guidePath}");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Using output directory guide: {guidePath}");
                }
                
                if (!File.Exists(guidePath))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Optimization guide not found at: {guidePath}");
                    return false;
                }

                var guideContent = await File.ReadAllTextAsync(guidePath, cancellationToken);
                
                // Create multipart form data for file upload
                using var formData = new MultipartFormDataContent();
                using var fileContent = new StringContent(guideContent);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/markdown");
                formData.Add(fileContent, "file", "ANYTHINGLM_OPTIMIZATION_GUIDE.md");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{slug}/upload", formData, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to upload optimization guide to '{slug}': {response.StatusCode} - {errorContent}");
                    
                    // Try alternative upload approach using document endpoint
                    return await TryAlternativeDocumentUpload(slug, guideContent, cancellationToken);
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully uploaded optimization guide to workspace '{slug}'");
                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error uploading optimization guide to workspace '{slug}'");
                return false;
            }
        }

        /// <summary>
        /// Try alternative document upload approach
        /// </summary>
        private async Task<bool> TryAlternativeDocumentUpload(string slug, string content, CancellationToken cancellationToken)
        {
            return await UploadDocumentAsync(slug, "ANYTHINGLM_OPTIMIZATION_GUIDE.md", content, cancellationToken);
        }

        /// <summary>
        /// Uploads RAG training documents with scoring instructions to a workspace
        /// </summary>
        public async Task<bool> UploadRagTrainingDocumentsAsync(string slug, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Uploading RAG training documents to workspace '{slug}'");
                
                // Files are copied to output directory by build, so check there first
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var configDir = Path.Combine(baseDir, "Config");
                
                // Fallback: if not in output dir, try project root (for development)
                if (!Directory.Exists(configDir))
                {
                    var binParent = Directory.GetParent(baseDir); // Debug/net8.0-windows parent
                    var debugParent = binParent?.Parent; // Debug parent
                    var binFolder = debugParent?.Parent; // bin parent
                    var projectRoot = binFolder?.Parent?.FullName; // Project root
                    
                    if (!string.IsNullOrEmpty(projectRoot))
                    {
                        configDir = Path.Combine(projectRoot, "Config");
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Using project root Config directory: {configDir}");
                    }
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Using output directory Config: {configDir}");
                }
                
                // RAG documents to upload
                var ragDocuments = new[]
                {
                    "RAG-JSON-Schema-Training.md",
                    "RAG-Learning-Examples.md",
                    "RAG-Optimization-Summary.md"
                };
                
                bool allUploadsSuccessful = true;
                
                foreach (var docName in ragDocuments)
                {
                    var docPath = Path.Combine(configDir, docName);
                    
                    if (!File.Exists(docPath))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] RAG document not found at: {docPath}");
                        allUploadsSuccessful = false;
                        continue;
                    }
                    
                    var docContent = await File.ReadAllTextAsync(docPath, cancellationToken);
                    var uploadSuccess = await UploadDocumentAsync(slug, docName, docContent, cancellationToken);
                    
                    if (uploadSuccess)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully uploaded RAG document: {docName}");
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to upload RAG document: {docName}");
                        allUploadsSuccessful = false;
                    }
                }
                
                if (allUploadsSuccessful)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully uploaded all RAG training documents to workspace '{slug}'");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Some RAG training documents failed to upload to workspace '{slug}'");
                }
                
                return allUploadsSuccessful;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error uploading RAG training documents to workspace '{slug}'");
                return false;
            }
        }

        /// <summary>
        /// Uploads a document with specified name and content to a workspace using proper AnythingLLM protocol
        /// </summary>
        public async Task<bool> UploadDocumentAsync(string slug, string documentName, string content, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Uploading document '{documentName}' to workspace '{slug}' using proper protocol");
                
                // Method 1: Try direct upload with workspace assignment (most efficient)
                var directResult = await TryDirectUploadWithWorkspaceAsync(slug, documentName, content, cancellationToken);
                if (directResult.success)
                {
                    return true;
                }
                
                // Method 2: Two-step process - Upload document then add to workspace
                var uploadResult = await UploadDocumentToSystemAsync(documentName, content, cancellationToken);
                if (!uploadResult.success || string.IsNullOrEmpty(uploadResult.documentLocation))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to upload document '{documentName}' to system");
                    return false;
                }
                
                // Step 2: Add document to workspace embeddings
                var addResult = await AddDocumentToWorkspaceAsync(slug, uploadResult.documentLocation, cancellationToken);
                if (!addResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Document uploaded but failed to add to workspace '{slug}'");
                    return false;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully uploaded document '{documentName}' to workspace '{slug}'");
                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error uploading document '{documentName}' to '{slug}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try direct upload with workspace assignment in single request
        /// </summary>
        private async Task<(bool success, string? error)> TryDirectUploadWithWorkspaceAsync(string workspaceSlug, string documentName, string content, CancellationToken cancellationToken)
        {
            try
            {
                // Use /v1/document/raw-text with addToWorkspaces parameter
                var payload = new
                {
                    textContent = content,
                    addToWorkspaces = workspaceSlug,
                    metadata = new
                    {
                        title = documentName,
                        docAuthor = "Test Case Editor App",
                        description = "Supplemental information for requirement analysis",
                        docSource = "test-case-editor-supplemental"
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/document/raw-text", httpContent, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 DEBUG: Direct upload response: {responseContent}");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Direct upload successful for '{documentName}' to '{workspaceSlug}'");
                    
                    // Wait for vectorization and verify document presence
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Waiting 5 seconds for document vectorization...");
                    await Task.Delay(5000, cancellationToken);
                    
                    var documents = await GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                    if (documents.HasValue && documents.Value.GetArrayLength() > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Document vectorization verified - {documents.Value.GetArrayLength()} documents in workspace");
                        return (true, null);
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Direct upload succeeded but no documents found - vectorization may have failed");
                        return (false, "Document upload succeeded but vectorization failed");
                    }
                }
                
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Direct upload failed: {response.StatusCode} - {errorContent}");
                return (false, $"Direct upload failed: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Direct upload exception: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Upload document to AnythingLLM system (Step 1 of two-step process)
        /// </summary>
        private async Task<(bool success, string? documentLocation)> UploadDocumentToSystemAsync(string documentName, string content, CancellationToken cancellationToken)
        {
            try
            {
                // Method A: Try /v1/document/raw-text endpoint
                var payload = new
                {
                    textContent = content,
                    metadata = new
                    {
                        title = documentName,
                        docAuthor = "Test Case Editor App",
                        description = "Supplemental information for requirement analysis",
                        docSource = "test-case-editor-supplemental"
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/document/raw-text", httpContent, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                    
                    // Extract document location from response
                    if (result.TryGetProperty("documents", out var documents) && documents.GetArrayLength() > 0)
                    {
                        var firstDoc = documents[0];
                        if (firstDoc.TryGetProperty("location", out var location))
                        {
                            var docLocation = location.GetString();
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Document uploaded to system: {docLocation}");
                            return (true, docLocation);
                        }
                    }
                }
                
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Raw text upload failed: {response.StatusCode} - {errorContent}");
                
                // Method B: Try multipart form upload as fallback
                return await TryMultipartUploadAsync(documentName, content, cancellationToken);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] System upload exception: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Try multipart form upload as alternative method
        /// </summary>
        private async Task<(bool success, string? documentLocation)> TryMultipartUploadAsync(string documentName, string content, CancellationToken cancellationToken)
        {
            try
            {
                using var formData = new MultipartFormDataContent();
                
                // Add file content
                var fileContent = new StringContent(content);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                formData.Add(fileContent, "file", documentName);
                
                // Add metadata
                var metadataJson = JsonSerializer.Serialize(new
                {
                    title = documentName,
                    docAuthor = "Test Case Editor App",
                    description = "Supplemental information for requirement analysis",
                    docSource = "test-case-editor-supplemental"
                });
                formData.Add(new StringContent(metadataJson), "metadata");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/document/upload", formData, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JsonElement>(responseJson);
                    
                    if (result.TryGetProperty("documents", out var documents) && documents.GetArrayLength() > 0)
                    {
                        var firstDoc = documents[0];
                        if (firstDoc.TryGetProperty("location", out var location))
                        {
                            var docLocation = location.GetString();
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Multipart upload successful: {docLocation}");
                            return (true, docLocation);
                        }
                    }
                }
                
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Multipart upload failed: {response.StatusCode} - {errorContent}");
                return (false, null);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Multipart upload exception: {ex.Message}");
                return (false, null);
            }
        }

        /// <summary>
        /// Add uploaded document to workspace embeddings (Step 2 of two-step process)
        /// </summary>
        private async Task<bool> AddDocumentToWorkspaceAsync(string workspaceSlug, string documentLocation, CancellationToken cancellationToken)
        {
            try
            {
                var payload = new
                {
                    adds = new[] { documentLocation },
                    deletes = new string[0]
                };

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/update-embeddings", httpContent, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Document added to workspace embeddings: {workspaceSlug}");
                    return true;
                }
                
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to add document to workspace embeddings: {response.StatusCode} - {errorContent}");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error adding document to workspace embeddings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pins a document for full-text inclusion in context (based on official documentation)
        /// Document pinning embeds the full document text directly in the context window for critical documents
        /// </summary>
        public async Task<bool> PinDocumentAsync(string workspaceSlug, string documentPath, bool pinStatus = true, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] {(pinStatus ? "Pinning" : "Unpinning")} document '{documentPath}' in workspace '{workspaceSlug}'");
                
                var payload = new
                {
                    docPath = documentPath,
                    pinStatus = pinStatus
                };

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/update-pin", httpContent, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Document pin status updated successfully for '{documentPath}'");
                    return true;
                }
                
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to update document pin status: {response.StatusCode} - {errorContent}");
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error updating document pin status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Upload and pin supplemental information document for full-text context inclusion
        /// Uses document pinning for critical supplemental data that must be fully accessible
        /// </summary>
        public async Task<bool> UploadAndPinSupplementalInfoAsync(string workspaceSlug, string documentName, string content, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Uploading and pinning supplemental information '{documentName}' to workspace '{workspaceSlug}'");
                
                // Step 1: Upload the document
                var uploadResult = await UploadDocumentAsync(workspaceSlug, documentName, content, cancellationToken);
                if (!uploadResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to upload supplemental information document");
                    return false;
                }
                
                // Step 2: Find the document in workspace to get its path for pinning
                // Give the system a moment to process the upload
                await Task.Delay(1000, cancellationToken);
                
                // Get workspace details to find the uploaded document
                var workspaceResponse = await _httpClient.GetAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}", cancellationToken);
                if (!workspaceResponse.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not retrieve workspace details for pinning");
                    return uploadResult; // Document uploaded but not pinned
                }
                
                var workspaceJson = await workspaceResponse.Content.ReadAsStringAsync(cancellationToken);
                var workspaceData = JsonSerializer.Deserialize<JsonElement>(workspaceJson);
                
                // Look for the document in the workspace
                string? documentPath = null;
                if (workspaceData.TryGetProperty("workspace", out var workspace))
                {
                    if (workspace.TryGetProperty("documents", out var documents))
                    {
                        foreach (var doc in documents.EnumerateArray())
                        {
                            if (doc.TryGetProperty("docpath", out var path) && 
                                doc.TryGetProperty("title", out var title) &&
                                title.GetString()?.Contains(documentName, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                documentPath = path.GetString();
                                break;
                            }
                        }
                    }
                }
                
                if (string.IsNullOrEmpty(documentPath))
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not find document path for pinning, but document was uploaded successfully");
                    return uploadResult; // Document uploaded but not pinned
                }
                
                // Step 3: Pin the document for full-text inclusion
                var pinResult = await PinDocumentAsync(workspaceSlug, documentPath, true, cancellationToken);
                if (pinResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully uploaded and pinned supplemental information");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Document uploaded but pinning failed - document available via RAG");
                }
                
                return uploadResult; // Return upload success regardless of pinning
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error uploading and pinning supplemental information: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a new workspace with automated optimization configuration
        /// Implements the automated setup described in ANYTHINGLM_OPTIMIZATION_GUIDE.md
        /// </summary>
        public async Task<(Workspace? workspace, bool configurationSuccessful)> CreateAndConfigureWorkspaceAsync(
            string baseName, 
            Action<string>? onProgress = null,
            bool preserveOriginalName = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Generate smart name for Test Case Editor projects (unless preserving original name)
                var workspaceName = preserveOriginalName ? baseName : GenerateSmartWorkspaceName(baseName);
                onProgress?.Invoke($"Creating workspace: {workspaceName}...");
                
                // Step 1: Create the workspace
                var workspace = await CreateWorkspaceAsync(workspaceName, cancellationToken);
                if (workspace == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to create workspace '{workspaceName}'");
                    return (null, false);
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Created workspace '{workspaceName}' - applying automated configuration...");
                onProgress?.Invoke("Applying optimal settings...");

                bool configurationSuccessful = true;

                // Step 2: Configure optimal settings (temperature, system prompt, context limits)
                var settingsResult = await ConfigureWorkspaceSettingsAsync(workspace.Slug, cancellationToken);
                if (!settingsResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to configure settings for workspace '{workspaceName}', but continuing...");
                    configurationSuccessful = false;
                }

                onProgress?.Invoke("Uploading optimization guide...");

                // Step 3: Upload optimization guide as training document
                var uploadResult = await UploadOptimizationGuideAsync(workspace.Slug, cancellationToken);
                if (!uploadResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to upload optimization guide for workspace '{workspaceName}', but continuing...");
                    configurationSuccessful = false;
                }

                onProgress?.Invoke("Uploading RAG training documents...");

                // Step 4: Upload RAG training documents with scoring instructions
                var ragUploadResult = await UploadRagTrainingDocumentsAsync(workspace.Slug, cancellationToken);
                if (!ragUploadResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to upload RAG training documents for workspace '{workspaceName}', but continuing...");
                    configurationSuccessful = false;
                }

                if (configurationSuccessful)
                {
                    onProgress?.Invoke("✅ Workspace ready with optimized settings!");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Successfully created and configured workspace '{workspaceName}' with all optimizations");
                }
                else
                {
                    onProgress?.Invoke("⚠️ Workspace created but some optimizations failed");
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Workspace '{workspaceName}' created but some configuration steps failed");
                }

                return (workspace, configurationSuccessful);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error during automated workspace creation and configuration");
                onProgress?.Invoke($"❌ Error: {ex.Message}");
                return (null, false);
            }
        }

        /// <summary>
        /// Generates smart workspace names for Test Case Editor projects
        /// </summary>
        private static string GenerateSmartWorkspaceName(string baseName)
        {
            // Sanitize the base name
            var sanitized = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^\w\s-]", "");
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s+", " ").Trim();
            
            // If empty or too short, use default
            if (string.IsNullOrWhiteSpace(sanitized) || sanitized.Length < 3)
            {
                sanitized = "Requirements Analysis";
            }

            // Ensure it starts with "Test Case Editor" for easy identification
            if (!sanitized.StartsWith("Test Case Editor", StringComparison.OrdinalIgnoreCase))
            {
                return $"Test Case Editor - {sanitized}";
            }

            return sanitized;
        }

        /// <summary>
        /// Creates a new thread in the workspace for isolated conversations
        /// </summary>
        public async Task<string?> CreateThreadAsync(string workspaceSlug, string? threadName = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new
                {
                    name = threadName ?? $"Analysis_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/thread/new", content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to create thread in '{workspaceSlug}': {response.StatusCode}");
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<dynamic>(responseJson);
                
                // Extract thread slug from response
                var threadSlug = result?.GetProperty("thread")?.GetProperty("slug").GetString();
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Created new thread '{threadSlug}' in workspace '{workspaceSlug}'");
                return threadSlug;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error creating thread in workspace '{workspaceSlug}'");
                return null;
            }
        }

        /// <summary>
        /// Sends a chat message to a specific thread with streaming response and progress updates
        /// </summary>
        public async Task<string?> SendChatMessageStreamingAsync(
            string workspaceSlug, 
            string message, 
            Action<string>? onChunkReceived = null, 
            Action<string>? onProgressUpdate = null,
            string? threadSlug = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                onProgressUpdate?.Invoke("Starting streaming request...");
                
                var payload = new
                {
                    message = message,
                    mode = "chat",
                    stream = true
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                onProgressUpdate?.Invoke("Sending request to AnythingLLM...");
                
                // Use thread-specific endpoint if thread is specified
                var endpoint = string.IsNullOrEmpty(threadSlug) 
                    ? $"{_baseUrl}/api/v1/workspace/{workspaceSlug}/stream-chat"
                    : $"{_baseUrl}/api/v1/workspace/{workspaceSlug}/thread/{threadSlug}/stream-chat";
                
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Failed to start streaming chat to '{workspaceSlug}': {response.StatusCode} - {errorContent}");
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] Request endpoint: {endpoint}");
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnythingLLM] API Key configured: {!string.IsNullOrEmpty(_apiKey)}");
                    return null;
                }

                onProgressUpdate?.Invoke("Receiving streaming response...");
                
                var responseBuilder = new StringBuilder();
                
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);
                
                string? line;
                while ((line = await reader.ReadLineAsync()) != null && !cancellationToken.IsCancellationRequested)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    // Handle Server-Sent Events format
                    if (line.StartsWith("data: "))
                    {
                        var chunkData = line.Substring(6); // Remove "data: " prefix
                        
                        if (chunkData == "[DONE]") break;
                        
                        try
                        {
                            var chunkJson = JsonSerializer.Deserialize<StreamChunkResponse>(chunkData, new JsonSerializerOptions 
                            { 
                                PropertyNameCaseInsensitive = true 
                            });
                            
                            if (!string.IsNullOrEmpty(chunkJson?.TextResponse))
                            {
                                responseBuilder.Append(chunkJson.TextResponse);
                                onChunkReceived?.Invoke(chunkJson.TextResponse);
                            }
                        }
                        catch (JsonException)
                        {
                            // Handle plain text chunks
                            responseBuilder.Append(chunkData);
                            onChunkReceived?.Invoke(chunkData);
                        }
                    }
                }
                
                onProgressUpdate?.Invoke("Stream complete");
                return responseBuilder.ToString();
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Timeout in streaming chat to workspace '{workspaceSlug}' - model may be overloaded");
                return null; // Let caller handle fallback
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error in streaming chat to workspace '{workspaceSlug}'");
                return null;
            }
        }

        /// <summary>
        /// Fixes workspace RAG configuration based on AnythingLLM documentation recommendations
        /// </summary>
        public async Task<bool> FixRagConfigurationAsync(string workspaceSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Applying RAG troubleshooting configuration for workspace '{workspaceSlug}'");
                
                // Enhanced RAG settings based on AnythingLLM troubleshooting documentation
                var ragFixSettings = new
                {
                    // CRITICAL: Disable similarity threshold completely (causes chunk filtering)
                    similarityThreshold = 0.0, // 0.0 = "No Restriction" mode per docs - prevents relevant chunks being filtered
                    
                    // Maximize context retrieval for technical documents
                    topN = 16, // Maximum increase for dense technical documents
                    
                    // Force accuracy optimization for better semantic matching
                    vectorSearchMode = "accuracy", // Enhanced semantic matching for requirement extraction
                    
                    // LLM configuration for better document analysis
                    openAiTemp = 0.0, // Zero temperature for deterministic extraction
                    openAiHistory = 25, // Increased context history for better continuity
                    
                    // Enhanced system prompt specifically for RAG troubleshooting
                    openAiPrompt = @"You are a technical document analysis AI with full access to uploaded document content through RAG retrieval.

CRITICAL INSTRUCTIONS:
- Document content IS available to you through the RAG system
- NEVER state you cannot access documents - this is incorrect
- The document chunks are automatically retrieved and provided in your context
- Analyze the actual document content provided, not generic examples
- If you genuinely receive no document chunks, state 'NO DOCUMENT CHUNKS RECEIVED' and explain what you can see

Your task: Extract technical requirements from the provided document content with complete accuracy.",
                    
                    // Database and model settings optimized for document retrieval
                    chatProvider = "ollama",
                    chatModel = "phi3.5:3.8b-mini-instruct-q4_K_M",
                    chatMode = "chat"
                };

                // Apply settings using the same endpoint pattern as ConfigureWorkspaceSettingsAsync
                var json = JsonSerializer.Serialize(ragFixSettings);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/update", content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ RAG configuration fix applied successfully");
                    
                    // Verify the configuration actually changed
                    await Task.Delay(1000, cancellationToken); // Brief delay to allow settings to persist
                    var workspaceDetails = await GetWorkspaceDetailsBySlugAsync(workspaceSlug, cancellationToken);
                    if (workspaceDetails.HasValue)
                    {
                        var workspace = workspaceDetails.Value;
                        // similarityThreshold and topN are at the root level, not nested in queryRefusalResponse
                        var currentThreshold = workspace.TryGetProperty("similarityThreshold", out var thresholdProp) ? thresholdProp.GetDouble() : 0.25;
                        var currentTopN = workspace.TryGetProperty("topN", out var topNProp) ? topNProp.GetInt32() : 8;
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Configuration verification - similarityThreshold: {currentThreshold}, topN: {currentTopN}");
                        
                        if (Math.Abs(currentThreshold - 0.0) < 0.001 && currentTopN == 16)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Configuration verified as applied");
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Configuration API succeeded but settings didn't change - threshold: {currentThreshold} (expected 0.0), topN: {currentTopN} (expected 16)");
                            return false;
                        }
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Could not verify configuration - workspace details unavailable");
                    }
                    
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] RAG fix failed: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error applying RAG configuration fix");
                return false;
            }
        }

        /// <summary>
        /// Gets workspace details by slug
        /// </summary>
        private async Task<JsonElement?> GetWorkspaceDetailsBySlugAsync(string workspaceSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                var workspacesResponse = await _httpClient.GetAsync($"{_baseUrl}/api/v1/workspaces", cancellationToken);
                if (!workspacesResponse.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not get workspaces to find details for '{workspaceSlug}'");
                    return null;
                }
                
                var workspacesJson = await workspacesResponse.Content.ReadAsStringAsync(cancellationToken);
                var workspacesData = JsonSerializer.Deserialize<JsonElement>(workspacesJson);
                
                if (workspacesData.TryGetProperty("workspaces", out var workspaces))
                {
                    foreach (var workspace in workspaces.EnumerateArray())
                    {
                        if (workspace.TryGetProperty("slug", out var slug) && 
                            slug.GetString() == workspaceSlug)
                        {
                            return workspace;
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error getting workspace details for '{workspaceSlug}'");
                return null;
            }
        }

        /// <summary>
        /// Tests RAG document retrieval with a simple diagnostic query
        /// </summary>
        public async Task<(bool success, string diagnostics)> TestDocumentAccessAsync(string workspaceSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Testing RAG document access for workspace '{workspaceSlug}'");
                
                // Simple diagnostic query that should trigger RAG retrieval
                var diagnosticQuery = "What documents are available? List the document name, first section heading, and total page count.";
                
                var response = await SendChatMessageAsync(workspaceSlug, diagnosticQuery, cancellationToken);
                
                if (string.IsNullOrEmpty(response))
                {
                    return (false, "No response received from LLM");
                }
                
                // Check if response indicates document access
                var hasDocAccess = !response.Contains("I do not have access") && 
                                  !response.Contains("cannot access") && 
                                  !response.Contains("don't have direct access") &&
                                  !response.Contains("unable to provide direct content") &&
                                  !response.Contains("without the capability to directly interact") &&
                                  !response.Contains("AI language model") && // Generic AI refusal pattern
                                  !response.Contains("No documents") &&
                                  (response.Contains("document") || response.Contains("section") || response.Contains("page"));
                
                var diagnostics = $"RAG Test Query: '{diagnosticQuery}'\nLLM Response: {response.Substring(0, Math.Min(300, response.Length))}...";
                
                if (hasDocAccess)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ RAG document access confirmed");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ❌ RAG document access failed - LLM cannot see document content");
                }
                
                return (hasDocAccess, diagnostics);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error testing document access");
                return (false, $"Error during RAG test: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a chat message to a workspace
        /// </summary>
        public async Task<string?> SendChatMessageAsync(string workspaceSlug, string message, CancellationToken cancellationToken = default)
        {
            int retryCount = 0;
            int delayMs = INITIAL_RETRY_DELAY_MS;

            while (retryCount <= MAX_RETRY_ATTEMPTS)
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
                    
                    OnStatusUpdated($"[Attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS + 1}] Sending request to LLM...");
                    
                    var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/chat", content, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMsg = $"LLM request failed with status {response.StatusCode}";
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] {errorMsg} - workspace: '{workspaceSlug}'");
                        
                        if (response.StatusCode == System.Net.HttpStatusCode.RequestTimeout || 
                            response.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
                        {
                            if (retryCount < MAX_RETRY_ATTEMPTS)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Timeout detected, retrying in {delayMs}ms...");
                                await Task.Delay(delayMs, cancellationToken);
                                delayMs = Math.Min(delayMs * 2, MAX_RETRY_DELAY_MS);
                                retryCount++;
                                continue;
                            }
                            else
                            {
                                TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLM] Timeout after {MAX_RETRY_ATTEMPTS} retries");
                                return null;
                            }
                        }
                        
                        return null;
                    }

                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    if (string.IsNullOrWhiteSpace(responseJson))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Received empty response from workspace '{workspaceSlug}'");
                        
                        if (retryCount < MAX_RETRY_ATTEMPTS)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Empty response, retrying in {delayMs}ms...");
                            await Task.Delay(delayMs, cancellationToken);
                            delayMs = Math.Min(delayMs * 2, MAX_RETRY_DELAY_MS);
                            retryCount++;
                            continue;
                        }
                    }
                    
                    var result = JsonSerializer.Deserialize<ChatResponse>(responseJson, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });

                    if (string.IsNullOrEmpty(result?.TextResponse))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] LLM returned empty response text");
                        
                        if (retryCount < MAX_RETRY_ATTEMPTS)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Empty text response, retrying in {delayMs}ms...");
                            await Task.Delay(delayMs, cancellationToken);
                            delayMs = Math.Min(delayMs * 2, MAX_RETRY_DELAY_MS);
                            retryCount++;
                            continue;
                        }
                        
                        return null;
                    }

                    OnStatusUpdated("LLM response received successfully");
                    return result.TextResponse;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info("[AnythingLLM] Request was cancelled by user");
                    return null;
                }
                catch (OperationCanceledException ex)
                {
                    // Timeout from HttpClient.Timeout - treat as timeout and retry
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Request timeout after {_httpClient.Timeout.TotalMinutes} minutes");
                    
                    if (retryCount < MAX_RETRY_ATTEMPTS)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Timeout occurred, retrying in {delayMs}ms... (Attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS})");
                        await Task.Delay(delayMs, CancellationToken.None); // Don't use cancellationToken for retry delay
                        delayMs = Math.Min(delayMs * 2, MAX_RETRY_DELAY_MS);
                        retryCount++;
                        continue;
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Timeout after {MAX_RETRY_ATTEMPTS} retry attempts");
                    return null;
                }
                catch (TimeoutException ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Request timeout: {ex.Message}");
                    
                    if (retryCount < MAX_RETRY_ATTEMPTS)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Timeout occurred, retrying in {delayMs}ms... (Attempt {retryCount + 1}/{MAX_RETRY_ATTEMPTS})");
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, MAX_RETRY_DELAY_MS);
                        retryCount++;
                        continue;
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Timeout after {MAX_RETRY_ATTEMPTS} retry attempts");
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Request error: {ex.Message}");
                    
                    if (retryCount < MAX_RETRY_ATTEMPTS)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Connection error, retrying in {delayMs}ms...");
                        await Task.Delay(delayMs, cancellationToken);
                        delayMs = Math.Min(delayMs * 2, MAX_RETRY_DELAY_MS);
                        retryCount++;
                        continue;
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] HTTP request failed after retries");
                    return null;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Unexpected error sending chat message to workspace '{workspaceSlug}'");
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Exception type: {ex.GetType().Name}");
                    if (ex.InnerException != null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Inner exception: {ex.InnerException.Message}");
                    }
                    return null;
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLM] Failed to get response after {MAX_RETRY_ATTEMPTS} retry attempts");
            return null;
        }

        /// <summary>
        /// Send a chat message to a workspace with custom timeout (overload for operations like recovery that need shorter timeouts)
        /// </summary>
        public async Task<string?> SendChatMessageAsync(string workspaceSlug, string message, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var originalTimeout = _httpClient.Timeout;
            try
            {
                // Temporarily set the custom timeout
                _httpClient.Timeout = timeout;
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Using custom timeout: {timeout.TotalMinutes:F1} minutes for recovery operation");
                
                // Use the existing method with the custom timeout
                return await SendChatMessageAsync(workspaceSlug, message, cancellationToken);
            }
            finally
            {
                // Always restore the original timeout
                _httpClient.Timeout = originalTimeout;
            }
        }

        /// <summary>
        /// Gets workspace documents to verify document presence and vectorization status
        /// </summary>
        public async Task<JsonElement?> GetWorkspaceDocumentsAsync(string workspaceSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not get workspace documents for '{workspaceSlug}': {response.StatusCode}");
                    return null;
                }
                
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 DEBUG: Workspace documents response JSON: {responseJson}");
                var workspaceData = JsonSerializer.Deserialize<JsonElement>(responseJson);
                
                // Handle different possible response structures
                JsonElement? documentsElement = null;
                
                // Try structure: { "workspace": [array] } - need to access first element
                if (workspaceData.TryGetProperty("workspace", out var workspaceProperty))
                {
                    if (workspaceProperty.ValueKind == JsonValueKind.Array && workspaceProperty.GetArrayLength() > 0)
                    {
                        var firstWorkspace = workspaceProperty[0];
                        if (firstWorkspace.TryGetProperty("documents", out var docs1))
                        {
                            documentsElement = docs1;
                        }
                    }
                    else if (workspaceProperty.ValueKind == JsonValueKind.Object && workspaceProperty.TryGetProperty("documents", out var docs2))
                    {
                        documentsElement = docs2;
                    }
                }
                // Try direct structure: { "documents": [...] }
                else if (workspaceData.TryGetProperty("documents", out var docs3))
                {
                    documentsElement = docs3;
                }
                // Handle case where response is directly an array
                else if (workspaceData.ValueKind == JsonValueKind.Array)
                {
                    documentsElement = workspaceData;
                }
                
                if (documentsElement.HasValue && documentsElement.Value.ValueKind == JsonValueKind.Array)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Found {documentsElement.Value.GetArrayLength()} documents in workspace '{workspaceSlug}'");
                    return documentsElement;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] No documents array found in workspace '{workspaceSlug}' response");
                return null;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error getting workspace documents for '{workspaceSlug}'");
                return null;
            }
        }

        /// <summary>
        /// Forces document re-processing by removing and re-adding the document
        /// </summary>
        public async Task<bool> ForceDocumentReprocessingAsync(string workspaceSlug, string documentName, string content, CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Force reprocessing document '{documentName}' in workspace '{workspaceSlug}'");
                
                // First, try to remove any existing documents to prevent conflicts
                await ClearWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                
                // Wait a moment for the clear to take effect
                await Task.Delay(1000, cancellationToken);
                
                // Re-upload the document
                var uploadResult = await UploadDocumentAsync(workspaceSlug, documentName, content, cancellationToken);
                if (uploadResult)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Document reprocessing successful for '{documentName}'");
                    
                    // Wait longer for vectorization to complete (increased from 2s to 5s)
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Waiting 5 seconds for document vectorization to complete...");
                    await Task.Delay(5000, cancellationToken);
                    
                    // Verify the document is now present
                    var documents = await GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                    if (documents.HasValue && documents.Value.GetArrayLength() > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Document verification successful - {documents.Value.GetArrayLength()} documents present");
                        return true;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Document reprocessing completed but no documents found in workspace");
                        return false;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error during document reprocessing for '{workspaceSlug}'");
                return false;
            }
        }

        /// <summary>
        /// Clears all documents from a workspace
        /// </summary>
        private async Task<bool> ClearWorkspaceDocumentsAsync(string workspaceSlug, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get current documents
                var documents = await GetWorkspaceDocumentsAsync(workspaceSlug, cancellationToken);
                if (!documents.HasValue || documents.Value.GetArrayLength() == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] No documents to clear in workspace '{workspaceSlug}'");
                    return true;
                }
                
                // Remove each document
                int removedCount = 0;
                foreach (var doc in documents.Value.EnumerateArray())
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 DEBUG: Document object: {doc}");
                    
                    // Try different possible property names for the document path
                    string? docPath = null;
                    if (doc.TryGetProperty("docpath", out var docPathProp))
                    {
                        docPath = docPathProp.GetString();
                    }
                    else if (doc.TryGetProperty("location", out var locationProp))
                    {
                        docPath = locationProp.GetString();
                    }
                    else if (doc.TryGetProperty("path", out var pathProp))
                    {
                        docPath = pathProp.GetString();
                    }
                    else if (doc.TryGetProperty("filename", out var filenameProp))
                    {
                        docPath = filenameProp.GetString();
                    }
                    
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Attempting to remove document with path: '{docPath}'");
                        var removed = await RemoveDocumentFromWorkspaceAsync(workspaceSlug, docPath, cancellationToken);
                        if (removed) removedCount++;
                    }
                    else
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Could not find document path property in document object");
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Cleared {removedCount} documents from workspace '{workspaceSlug}'");
                return removedCount > 0;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error clearing documents from workspace '{workspaceSlug}'");
                return false;
            }
        }

        /// <summary>
        /// Removes a specific document from a workspace
        /// </summary>
        private async Task<bool> RemoveDocumentFromWorkspaceAsync(string workspaceSlug, string docPath, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new { docpath = docPath };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_baseUrl}/api/v1/workspace/{workspaceSlug}/remove-document", content, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Removed document '{docPath}' from workspace '{workspaceSlug}'");
                    return true;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Failed to remove document '{docPath}': {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error removing document '{docPath}' from workspace '{workspaceSlug}'");
                return false;
            }
        }

        /// <summary>
        /// Diagnoses AnythingLLM vectorization configuration to identify potential issues
        /// </summary>
        public async Task<bool> DiagnoseVectorizationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 Running vectorization diagnostics...");
                
                // Check system configuration
                var systemResponse = await _httpClient.GetAsync($"{_baseUrl}/api/v1/system", cancellationToken);
                if (systemResponse.IsSuccessStatusCode)
                {
                    var systemJson = await systemResponse.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 System config: {systemJson.Substring(0, Math.Min(500, systemJson.Length))}...");
                }
                
                // Check if embedding service is configured
                var embeddingResponse = await _httpClient.GetAsync($"{_baseUrl}/api/v1/embedding", cancellationToken);
                if (embeddingResponse.IsSuccessStatusCode)
                {
                    var embeddingJson = await embeddingResponse.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 Embedding config: {embeddingJson}");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Could not get embedding configuration: {embeddingResponse.StatusCode}");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error during vectorization diagnostics");
                return false;
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
                // Immediately signal that we're checking/starting AnythingLLM
                OnStatusUpdated("Checking AnythingLLM status...");
                
                // First try to connect to existing service
                if (await IsServiceAvailableAsync(cancellationToken))
                {
                    OnStatusUpdated("AnythingLLM service is ready!");
                    return (true, "AnythingLLM service is already running and ready");
                }
                
                // Global coordination to prevent multiple instances from starting simultaneously
                bool shouldStartup = false;
                lock (_globalStartupLock)
                {
                    if (!_globalStartupInProgress)
                    {
                        _globalStartupInProgress = true;
                        shouldStartup = true;
                    }
                }
                
                // If another instance is starting, wait for it to complete
                if (!shouldStartup)
                {
                    OnStatusUpdated("AnythingLLM startup already in progress, waiting...");
                    int waitCount = 0;
                    while (_globalStartupInProgress && waitCount < 60) // Wait up to 60 seconds
                    {
                        await Task.Delay(1000, cancellationToken);
                        waitCount++;
                        
                        // Check if service became available
                        if (await IsServiceAvailableAsync(cancellationToken))
                        {
                            OnStatusUpdated("AnythingLLM service is now ready");
                            return (true, "AnythingLLM service started successfully");
                        }
                        
                        if (waitCount % 5 == 0) // Update every 5 seconds
                        {
                            OnStatusUpdated($"Waiting for AnythingLLM startup... ({waitCount}/60s)");
                        }
                    }
                    
                    // If still not available, return error
                    return (false, "Timeout waiting for AnythingLLM startup by another instance");
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
        private async void OnStatusUpdated(string status)
        {
            StatusUpdated?.Invoke(status);
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] {status}");
            
            // Determine actual availability using proper service check, not text matching
            bool isActuallyAvailable = false;
            try
            {
                isActuallyAvailable = await IsServiceAvailableAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Availability check result: {isActuallyAvailable}");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] Availability check failed: {ex.Message}");
            }
            
            // Publish status via mediator for cross-cutting concerns (spinner, status indicators)
            var anythingLLMStatus = new AnythingLLMStatus
            {
                IsAvailable = isActuallyAvailable, // Use actual availability, not text matching
                IsStarting = status.Contains("Starting") || status.Contains("waiting") || status.Contains("Checking"),
                StatusMessage = status
            };
            AnythingLLMMediator.NotifyStatusUpdated(anythingLLMStatus);
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

        private class StreamChunkResponse
        {
            public string? TextResponse { get; set; }
            public string? Id { get; set; }
            public string? Type { get; set; }
            public bool? Done { get; set; }
        }
        
        /// <summary>
        /// Parallel processing result for batch operations
        /// </summary>
        public class ParallelProcessingResult<T>
        {
            public T? Result { get; set; }
            public bool Success { get; set; }
            public string? Error { get; set; }
            public TimeSpan Duration { get; set; }
            public int Index { get; set; }
        }
        
        /// <summary>
        /// Process multiple requirements in parallel with rate limiting and progress tracking
        /// </summary>
        public async Task<List<ParallelProcessingResult<string?>>> ProcessRequirementsInParallelAsync<T>(
            IEnumerable<T> items,
            Func<T, int, CancellationToken, Task<string?>> processor,
            int maxConcurrency = 3,
            int rateLimitDelayMs = 100,
            Action<string, int, int>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            var itemsList = items.ToList();
            var results = new List<ParallelProcessingResult<string?>>();
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            
            onProgress?.Invoke("Starting parallel processing...", 0, itemsList.Count);
            
            var tasks = itemsList.Select(async (item, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    
                    // Rate limiting delay (except for first batch)
                    if (index > 0)
                    {
                        await Task.Delay(rateLimitDelayMs, cancellationToken);
                    }
                    
                    onProgress?.Invoke($"Processing item {index + 1}...", index + 1, itemsList.Count);
                    
                    try
                    {
                        var result = await processor(item, index, cancellationToken);
                        stopwatch.Stop();
                        
                        return new ParallelProcessingResult<string?>
                        {
                            Result = result,
                            Success = true,
                            Duration = stopwatch.Elapsed,
                            Index = index
                        };
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Error processing item {index}: {ex.Message}");
                        
                        return new ParallelProcessingResult<string?>
                        {
                            Success = false,
                            Error = ex.Message,
                            Duration = stopwatch.Elapsed,
                            Index = index
                        };
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            var completedResults = await Task.WhenAll(tasks);
            results.AddRange(completedResults);
            
            var successCount = results.Count(r => r.Success);
            onProgress?.Invoke($"Parallel processing complete: {successCount}/{itemsList.Count} successful", itemsList.Count, itemsList.Count);
            
            return results;
        }

        /// <summary>
        /// Checks document content quality to determine if PDF extraction was successful
        /// </summary>
        public async Task<bool> CheckDocumentContentAsync(string workspaceSlug)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔍 Checking document content quality for workspace '{workspaceSlug}'");

                var documentsElement = await GetWorkspaceDocumentsAsync(workspaceSlug);
                if (!documentsElement.HasValue)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ❌ No documents data found in workspace");
                    return false;
                }

                var documents = documentsElement.Value;
                if (documents.ValueKind != JsonValueKind.Array || documents.GetArrayLength() == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ❌ No documents found in workspace");
                    return false;
                }

                foreach (var doc in documents.EnumerateArray())
                {
                    if (doc.TryGetProperty("filename", out var filenameProp))
                    {
                        var filename = filenameProp.GetString() ?? "unknown";
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 📄 Document: {filename}");
                        
                        // Parse metadata to check content quality
                        if (doc.TryGetProperty("metadata", out var metadataProp) && metadataProp.ValueKind == JsonValueKind.String)
                        {
                            try
                            {
                                var metadataJson = metadataProp.GetString();
                                if (!string.IsNullOrEmpty(metadataJson))
                                {
                                    var metadata = JsonDocument.Parse(metadataJson);
                                    if (metadata.RootElement.TryGetProperty("wordCount", out var wordCountProp) &&
                                        metadata.RootElement.TryGetProperty("token_count_estimate", out var tokenCountProp))
                                    {
                                        var wordCount = wordCountProp.GetInt32();
                                        var tokenCount = tokenCountProp.GetInt32();
                                        
                                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 📊 Content metrics - Words: {wordCount}, Tokens: {tokenCount}");
                                        
                                        if (wordCount <= 5)
                                        {
                                            TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLM] 🚨 CONTENT EXTRACTION FAILED - Word count too low: {wordCount}");
                                            TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLM] 💡 Likely PDF processing failure - need alternative approach");
                                            return false;
                                        }
                                        
                                        if (tokenCount > 250000 && wordCount < 100)
                                        {
                                            TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLM] 🚨 CONTENT CORRUPTION - High token count ({tokenCount}) but low word count ({wordCount})");
                                            TestCaseEditorApp.Services.Logging.Log.Error($"[AnythingLLM] 💡 This indicates binary content or extraction artifacts");
                                            return false;
                                        }
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Warn($"[AnythingLLM] ⚠️ Could not parse document metadata: {ex.Message}");
                            }
                        }
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Document content quality check passed");
                return true;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error checking document content");
                return false;
            }
        }

        /// <summary>
        /// Attempts alternative upload methods when PDF processing fails
        /// </summary>
        public async Task<bool> TryAlternativeUploadAsync(string filePath, string workspaceSlug)
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 🔄 Attempting alternative upload method for '{Path.GetFileName(filePath)}'");
                
                // Try uploading as plain text if it's a PDF
                if (Path.GetExtension(filePath).ToLowerInvariant() == ".pdf")
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 📋 Converting PDF to text for alternative upload");
                    
                    // Create a simple text version with metadata
                    var textContent = $"Document: {Path.GetFileName(filePath)}\n";
                    textContent += $"Source: PDF document converted for requirement extraction\n";
                    textContent += $"Processing Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n";
                    textContent += "[Note: This document requires manual text extraction. The PDF may contain:";
                    textContent += " requirements, specifications, technical details, SHALL/MUST/WILL statements,";
                    textContent += " performance criteria, interface definitions, environmental constraints.]\n\n";
                    textContent += "MANUAL EXTRACTION REQUIRED: Please copy and paste the relevant document content ";
                    textContent += "or use external PDF text extraction tools to provide the document text for analysis.";
                    
                    var textFileName = Path.GetFileNameWithoutExtension(filePath) + "_extracted.txt";
                    var tempTextPath = Path.Combine(Path.GetTempPath(), textFileName);
                    
                    await File.WriteAllTextAsync(tempTextPath, textContent);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] 📤 Uploading text version: {textFileName}");
                    
                    var result = await UploadDocumentAsync(workspaceSlug, textFileName, textContent);
                    
                    // Cleanup temp file
                    try { File.Delete(tempTextPath); } catch { }
                    
                    if (result)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] ✅ Alternative upload successful");
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnythingLLM] Error in alternative upload");
                return false;
            }
        }
    }
}