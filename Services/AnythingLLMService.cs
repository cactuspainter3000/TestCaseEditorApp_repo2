using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for integrating with AnythingLLM workspaces.
    /// Provides workspace management capabilities for creating and listing AnythingLLM workspaces.
    /// </summary>
    public class AnythingLLMService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl;
        private readonly string? _apiKey;

        public AnythingLLMService(string? baseUrl = null, string? apiKey = null)
        {
            _apiKey = apiKey ?? "222C5V1-KK3MFY3-G1FFH7D-HN69H6G"; // Hardcode for testing
            
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
            if (string.IsNullOrEmpty(_apiKey))
            {
                return $"⚠️ AnythingLLM API key not configured. Using local instance at {_baseUrl}";
            }
            return $"✅ AnythingLLM API ready for cloud service at {_baseUrl}";
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

                            return result?.Workspaces ?? new List<Workspace>();
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
        /// Creates a new workspace in AnythingLLM
        /// </summary>
        public async Task<Workspace?> CreateWorkspaceAsync(string name, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new
                {
                    name = name
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                }
                
                // Use the correct API endpoint based on AnythingLLM documentation
                string createEndpoint;
                if (_baseUrl.Contains("localhost"))
                {
                    createEndpoint = $"{_baseUrl}/api/v1/workspace/new"; // Local API v1 endpoint
                }
                else
                {
                    createEndpoint = $"{_baseUrl}/api/v1/workspace/new"; // Cloud API v1 endpoint
                }
                
                Console.WriteLine($"[DEBUG] POST endpoint: {createEndpoint}");
                
                var response = await client.PostAsync(createEndpoint, content, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"[DEBUG] Error response: {errorText}");
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
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[AnythingLLM] Error sending chat message to '{workspaceSlug}'");
                return null;
            }
        }

        /// <summary>
        /// Checks if AnythingLLM service is available and responding
        /// </summary>
        public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Try multiple endpoints to check if AnythingLLM is available
                var endpoints = new[]
                {
                    $"{_baseUrl}/api/v1/workspaces",        // API v1 endpoint (prioritize if we have API key)
                    $"{_baseUrl}/v1/workspaces",            // Developer API v1 endpoint  
                    "http://localhost:3001/api/v1/workspaces", // Local API v1 endpoint
                    "http://localhost:3001/api/workspaces",  // Desktop AnythingLLM endpoint
                    "http://localhost:3000/api/workspaces",  // Alternative port
                    $"{_baseUrl}/api/workspaces"            // Simplified API endpoint
                };

                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(5); // Short timeout
                        
                        // Always try API key first if we have one, regardless of endpoint
                        if (!string.IsNullOrEmpty(_apiKey))
                        {
                            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                        }

                        var response = await client.GetAsync(endpoint, cancellationToken);
                        
                        // Accept successful responses and set the correct base URL
                        if (response.IsSuccessStatusCode)
                        {
                            // Update base URL based on working endpoint, but prefer API versions
                            if (endpoint.Contains("/v1/workspaces"))
                            {
                                if (endpoint.Contains("localhost:3001"))
                                {
                                    _baseUrl = "http://localhost:3001";
                                }
                                else if (endpoint.Contains("localhost:3000"))
                                {
                                    _baseUrl = "http://localhost:3000";
                                }
                                // Keep existing _baseUrl if it's already set to something else
                            }
                            else if (endpoint.Contains("localhost:3001"))
                            {
                                _baseUrl = "http://localhost:3001";
                            }
                            else if (endpoint.Contains("localhost:3000"))
                            {
                                _baseUrl = "http://localhost:3000";
                            }
                            
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Service available via {endpoint} (status: {response.StatusCode})");
                            return true;
                        }
                        // Accept auth errors as "service available"
                        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            TestCaseEditorApp.Services.Logging.Log.Info($"[AnythingLLM] Service available but needs auth via {endpoint}");
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
            _httpClient?.Dispose();
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