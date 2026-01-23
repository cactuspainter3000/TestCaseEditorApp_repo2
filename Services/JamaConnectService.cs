using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Service for integrating with Jama Connect REST API
    /// Provides direct access to requirements and test case management
    /// </summary>
    public class JamaConnectService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _apiToken;
        private readonly string? _clientId;
        private readonly string? _clientSecret;
        private string? _accessToken;
        private JsonDocument? _lastApiResponseJson;
        private static readonly SemaphoreSlim _rateLimitSemaphore = new(10, 10); // 10 requests per second
        private static DateTime _lastRequest = DateTime.MinValue;
        private const int MinRequestInterval = 100; // milliseconds
        
        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl) && 
            (!string.IsNullOrEmpty(_apiToken) || 
             (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password)) ||
             (!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret)));

        /// <summary>
        /// Initialize with API token (recommended)
        /// </summary>
        public JamaConnectService(string baseUrl, string apiToken)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiToken = apiToken;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Initialize with username/password
        /// </summary>
        public JamaConnectService(string baseUrl, string username, string password)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Initialize with OAuth client credentials
        /// </summary>
        public JamaConnectService(string baseUrl, string clientId, string clientSecret, bool isOAuth)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = new HttpClient();
            
            // Debug logging
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth constructor - Base URL: {_baseUrl}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth constructor - Client ID: {_clientId}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth constructor - Client Secret: {(!string.IsNullOrEmpty(_clientSecret) ? "SET" : "NOT SET")}");
        }

        /// <summary>
        /// Initialize from configuration (will read from environment or config)
        /// </summary>
        public static JamaConnectService FromConfiguration()
        {
            var baseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL") ?? "";
            var apiToken = Environment.GetEnvironmentVariable("JAMA_API_TOKEN");
            var username = Environment.GetEnvironmentVariable("JAMA_USERNAME");
            var password = Environment.GetEnvironmentVariable("JAMA_PASSWORD");
            var clientId = Environment.GetEnvironmentVariable("JAMA_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET");

            // Debug logging
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] FromConfiguration called");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Base URL: {baseUrl}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] API Token: {(!string.IsNullOrEmpty(apiToken) ? "SET" : "NOT SET")}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Client ID: {clientId}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Client Secret: {(!string.IsNullOrEmpty(clientSecret) ? "SET" : "NOT SET")}");

            // Handle common Jama path variations
            if (!string.IsNullOrEmpty(baseUrl) && !baseUrl.EndsWith("/rest"))
            {
                // Check if we need to add /contour for certain Jama instances
                if (baseUrl.Contains("rockwellcollins.com") && !baseUrl.Contains("/contour"))
                {
                    baseUrl = baseUrl.TrimEnd('/') + "/contour";
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Added /contour to URL: {baseUrl}");
                }
            }

            if (!string.IsNullOrEmpty(apiToken))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating service with API Token");
                return new JamaConnectService(baseUrl, apiToken);
            }
            else if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating service with OAuth");
                return new JamaConnectService(baseUrl, clientId, clientSecret, true);
            }
            else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Creating service with username/password");
                return new JamaConnectService(baseUrl, username, password);
            }
            else
            {
                throw new InvalidOperationException(
                    "Jama Connect not configured. Set environment variables: " +
                    "JAMA_BASE_URL and either JAMA_API_TOKEN, (JAMA_CLIENT_ID + JAMA_CLIENT_SECRET), or (JAMA_USERNAME + JAMA_PASSWORD)");
            }
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Set authentication for non-OAuth methods
            if (!string.IsNullOrEmpty(_apiToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
            }
            else if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_username}:{_password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
            // OAuth will set Authorization header dynamically after getting access token

            return client;
        }

        /// <summary>
        /// Ensure we have a valid access token
        /// 🚨 CRITICAL OAUTH METHOD - DO NOT MODIFY WITHOUT EXPLICIT CONFIRMATION 🚨
        /// </summary>
        private async Task<bool> EnsureAccessTokenAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
                return await GetOAuthTokenAsync();
                
            return true;
        }

        /// <summary>
        /// Get OAuth access token
        /// 🚨 CRITICAL OAUTH METHOD - DO NOT MODIFY WITHOUT EXPLICIT CONFIRMATION 🚨
        /// This method has been fixed multiple times due to token acquisition failures.
        /// The JSON deserialization and error handling are critical for project loading.
        /// </summary>
        private async Task<bool> GetOAuthTokenAsync()
        {
            var tokenUrl = $"{_baseUrl}/rest/oauth/token";
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting OAuth token from: {tokenUrl}");
            
            var authBytes = Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}");
            var authHeader = Convert.ToBase64String(authBytes);

            var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
            
            var form = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "token_information")
            ]);
            
            request.Content = form;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Sending OAuth request...");
            var response = await _httpClient.SendAsync(request);
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth response: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth content length: {content.Length}");
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth response content: {content}");
                
                try
                {
                    var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(content);
                    
                    // 🚨 STEEL TRAP: Validate token response structure
                    if (tokenResponse == null)
                    {
                        TestCaseEditorApp.Services.Logging.Log.ValidationFailure("OAuth token deserialization", "Returned null - check OAuthTokenResponse JsonPropertyName attributes");
                        return false;
                    }
                    
                    if (!tokenResponse.IsValid)
                    {
                        TestCaseEditorApp.Services.Logging.Log.ValidationFailure("OAuth token response", $"AccessToken: '{tokenResponse.AccessToken ?? "NULL"}', Raw response: {content}");
                        return false;
                    }
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Deserialized token response - AccessToken is null: {tokenResponse?.AccessToken == null}");
                    
                    _accessToken = tokenResponse?.AccessToken;
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    var hasToken = !string.IsNullOrEmpty(_accessToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token obtained: {hasToken}");
                    if (hasToken)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token preview: {_accessToken?.Substring(0, Math.Min(20, _accessToken.Length))}...");
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] ✅ OAuth steel trap validation passed!");
                    }
                    return hasToken;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Exception(ex, "[JamaConnect] Failed to deserialize OAuth response");
                    TestCaseEditorApp.Services.Logging.Log.Error($"[JamaConnect] 🚨 STEEL TRAP: Check OAuthTokenResponse class structure!");
                    return false;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] OAuth failed: {errorContent}");
            }
            
            return false;
        }

        /// <summary>
        /// Test connection to Jama Connect API
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] TestConnection called - IsConfigured: {IsConfigured}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Base URL: {_baseUrl}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Client ID: {_clientId}");
            
            if (!IsConfigured)
            {
                var msg = "Service not configured. Missing base URL or credentials.";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {msg}");
                return (false, msg);
            }

            // Get OAuth token first
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting OAuth token...");
            var tokenSuccess = await EnsureAccessTokenAsync();
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Token success: {tokenSuccess}");
            
            if (!tokenSuccess)
            {
                var msg = "Failed to authenticate with OAuth credentials";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {msg}");
                return (false, msg);
            }
            
            // Test projects API
            var testUrl = $"{_baseUrl}/rest/v1/projects";
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Testing projects API: {testUrl}");
            
            var response = await _httpClient.GetAsync(testUrl);
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Response content length: {content.Length}");
                
                var projects = JsonSerializer.Deserialize<JamaProjectsResponse>(content, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                var projectCount = projects?.Data?.Count ?? 0;
                var successMsg = $"Successfully connected to Jama Connect. Found {projectCount} projects.";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {successMsg}");
                return (true, successMsg);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Error response: {errorContent}");
                
                if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError &&
                    (errorContent.Contains("Index") && errorContent.Contains("out of bounds")))
                {
                    var msg = $"OAuth scope issue: Client '{_clientId}' has 'token_information' scope but needs 'read' scope for data access.";
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {msg}");
                    return (false, msg);
                }
                
                var failMsg = $"Connection failed: {response.StatusCode} - {errorContent}";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] {failMsg}");
                return (false, failMsg);
            }
        }

        /// <summary>
        /// Get list of projects accessible to the current user
        /// </summary>
        public async Task<List<JamaProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure we have a valid access token for OAuth
                await EnsureAccessTokenAsync();
                
                var response = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/projects", cancellationToken);
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Projects API response: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Raw JSON response (first 1000 chars): {json.Substring(0, Math.Min(json.Length, 1000))}");
                    
                    var result = JsonSerializer.Deserialize<JamaProjectsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    // Log the first few projects to see what data is actually being mapped
                    if (result?.Data?.Any() == true)
                    {
                        for (int i = 0; i < Math.Min(3, result.Data.Count); i++)
                        {
                            var p = result.Data[i];
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project {i}: Id={p.Id}, Name='{p.Name}', Key='{p.Key}', Desc='{p.Description}'");
                        }
                    }
                    
                    return result?.Data ?? new List<JamaProject>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Projects API failed: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"Failed to get projects: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get Jama projects: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Enhanced requirements retrieval using cookbook patterns
        /// Combines search, include parameters, and retry logic for optimal performance
        /// Falls back to standard API if enhanced features are not available
        /// </summary>
        public async Task<List<Requirement>> GetRequirementsEnhancedAsync(int projectId, CancellationToken cancellationToken = default)
        {
            return await WithRetryAsync(async () =>
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Getting requirements (enhanced) from project {projectId}");

                await EnsureAccessTokenAsync();
                
                try
                {
                    // Use enhanced search with include parameters for comprehensive data fetching
                    var includeParams = new List<string> 
                    {
                        "data.createdBy",
                        "data.modifiedBy",
                        "data.project",
                        "data.itemType"
                    };
                    
                    // Use abstract items for better search capabilities
                    var jamaItems = await SearchAbstractItemsAsync(
                        projectId: projectId,
                        maxResults: 1000,  // Increased batch size per cookbook
                        includeParams: includeParams
                    );
                    
                    // Convert to requirements
                    var requirements = await ConvertToRequirementsAsync(jamaItems);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {requirements.Count} requirements using enhanced patterns");
                    return requirements;
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Enhanced requirements failed, falling back to standard API: {ex.Message}");
                    
                    // Fallback to standard requirements API
                    var jamaItems = await GetRequirementsAsync(projectId, cancellationToken);
                    var requirements = await ConvertToRequirementsAsync(jamaItems);
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {requirements.Count} requirements using fallback method");
                    return requirements;
                }
            });
        }

        /// <summary>
        /// Get requirements from a specific project
        /// Enhanced with fallback mechanisms for better reliability
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Ensure we have a valid access token for OAuth
                await EnsureAccessTokenAsync();
                
                // Try enhanced URL with include parameters first
                var url1 = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50&include=createdBy,modifiedBy,createdDate,modifiedDate";
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Trying enhanced format: {url1}");
                
                var response = await _httpClient.GetAsync(url1, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] API Response Status: {response.StatusCode}");
                
                // If enhanced format fails with server error, try basic format
                if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Enhanced format failed with 500: {errorContent}");
                    TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Trying basic format without include parameters");
                    
                    var basicUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50";
                    response = await _httpClient.GetAsync(basicUrl, cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Basic format response: {response.StatusCode}");
                }
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Received API response: {json.Length} characters");
                    
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    // Parse JSON for rich content extraction from dynamic fields
                    _lastApiResponseJson?.Dispose();
                    _lastApiResponseJson = null;
                    try
                    {
                        _lastApiResponseJson = JsonDocument.Parse(json);
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Could not parse JSON for rich content extraction: {ex.Message}");
                    }
                    
                    var allItems = result?.Data ?? new List<JamaItem>();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {allItems.Count} items from first page");
                    
                    // Check if there are more items to fetch (pagination)
                    if (allItems.Count == 50) // Full page means there might be more
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] First page full, fetching additional pages...");
                        
                        int pageNumber = 2;
                        int startIndex = 50;
                        bool hasMorePages = true;
                        
                        while (hasMorePages && pageNumber <= 10) // Safety limit of 10 pages (500 items)
                        {
                            var nextPageUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50&startAt={startIndex}&include=createdBy,modifiedBy,createdDate,modifiedDate";
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fetching page {pageNumber} with user metadata: startAt={startIndex}");
                            
                            var nextPageResponse = await _httpClient.GetAsync(nextPageUrl, cancellationToken);
                            
                            // Handle pagination errors gracefully
                            if (!nextPageResponse.IsSuccessStatusCode && nextPageResponse.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                            {
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Page {pageNumber} failed with 500 error, trying basic format");
                                var basicPageUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50&startAt={startIndex}";
                                nextPageResponse = await _httpClient.GetAsync(basicPageUrl, cancellationToken);
                            }
                            
                            if (nextPageResponse.IsSuccessStatusCode)
                            {
                                var nextPageJson = await nextPageResponse.Content.ReadAsStringAsync(cancellationToken);
                                var nextPageResult = JsonSerializer.Deserialize<JamaItemsResponse>(nextPageJson, new JsonSerializerOptions 
                                { 
                                    PropertyNameCaseInsensitive = true 
                                });
                                
                                var pageItems = nextPageResult?.Data ?? new List<JamaItem>();
                                allItems.AddRange(pageItems);
                                
                                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Page {pageNumber}: Retrieved {pageItems.Count} items (total: {allItems.Count})");
                                
                                // Continue if we got a full page
                                hasMorePages = pageItems.Count == 50;
                                startIndex += 50;
                                pageNumber++;
                            }
                            else
                            {
                                var errorContent = await nextPageResponse.Content.ReadAsStringAsync(cancellationToken);
                                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Failed to fetch page {pageNumber}: {nextPageResponse.StatusCode} - {errorContent}");
                                
                                // For server errors, stop pagination but continue with what we have
                                if (nextPageResponse.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Server error during pagination - continuing with partial results");
                                    break;
                                }
                                
                                // For other errors, stop pagination
                                break;
                            }
                        }
                        
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Pagination complete: {allItems.Count} total items from {pageNumber - 1} pages");
                    }
                    
                    // Filter for only requirements (itemType 193) and log summary
                    var requirements = allItems.Where(item => item.ItemType == 193).ToList();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {requirements.Count} requirements from {allItems.Count} total items");
                    
                    return requirements;
                }
                else
                {
                    // If all attempts fail, log detailed error but try to return empty list for graceful degradation
                    var finalErrorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] All API attempts failed. Status: {response.StatusCode}, Content: {finalErrorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Server error encountered - returning empty list for graceful degradation");
                        return new List<JamaItem>(); // Return empty list instead of throwing
                    }
                    
                    throw new HttpRequestException($"Failed to get requirements: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get Jama requirements: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get the count of items (requirements/test cases) in a project
        /// </summary>
        public async Task<int> GetProjectRequirementCountAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                // Get all items from the project to count them
                // This is simple and reliable, though potentially slower for very large projects
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    var count = result?.Data?.Count ?? 0;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Project {projectId} has {count} items");
                    return count;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"Failed to get requirement count for project {projectId}: {response.StatusCode}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Error getting requirement count for project {projectId}");
                return 0;
            }
        }

        /// <summary>
        /// Get user information by user ID
        /// </summary>
        public async Task<JamaUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/users/{userId}";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Getting user info: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaUserResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    return result?.Data;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Failed to get user {userId}: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get user {userId}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Get individual item with user metadata resolved
        /// </summary>
        public async Task<JamaItem?> GetItemWithUserMetadataAsync(int itemId, CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/abstractitems/{itemId}";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Getting item with user metadata: {url}");
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var result = JsonSerializer.Deserialize<JamaItemResponse>(json, new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    
                    if (result?.Data != null)
                    {
                        // Resolve CreatedBy user ID to name
                        if (result.Data.CreatedBy > 0)
                        {
                            var user = await GetUserAsync(result.Data.CreatedBy, cancellationToken);
                            if (user != null)
                            {
                                result.Data.CreatedByName = $"{user.FirstName} {user.LastName}".Trim();
                            }
                        }
                        
                        // Resolve ModifiedBy user ID to name
                        if (result.Data.ModifiedBy > 0)
                        {
                            var user = await GetUserAsync(result.Data.ModifiedBy, cancellationToken);
                            if (user != null)
                            {
                                result.Data.ModifiedByName = $"{user.FirstName} {user.LastName}".Trim();
                            }
                        }
                    }
                    
                    return result?.Data;
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Failed to get item {itemId}: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get item {itemId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get requirements with user metadata resolved (enhanced version)
        /// Uses individual item calls to get complete user information
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsWithUserMetadataAsync(int projectId, CancellationToken cancellationToken = default)
        {
            try
            {
                // First get the bulk list of requirements
                var bulkRequirements = await GetRequirementsAsync(projectId, cancellationToken);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Retrieved {bulkRequirements.Count} requirements, enhancing with user metadata...");
                
                var enhancedRequirements = new List<JamaItem>();
                
                // Process in batches to avoid overwhelming the API
                const int batchSize = 10;
                for (int i = 0; i < bulkRequirements.Count; i += batchSize)
                {
                    var batch = bulkRequirements.Skip(i).Take(batchSize);
                    var enhancedBatch = new List<JamaItem>();
                    
                    // Process batch items in parallel for efficiency
                    var tasks = batch.Select(async item =>
                    {
                        var enhancedItem = await GetItemWithUserMetadataAsync(item.Id, cancellationToken);
                        if (enhancedItem != null)
                        {
                            // Preserve the bulk data fields that aren't in individual calls
                            enhancedItem.Fields = item.Fields;
                            enhancedItem.Name = item.Name;
                            enhancedItem.Description = item.Description;
                            enhancedItem.Status = item.Status;
                            enhancedItem.Item = item.Item;
                            enhancedItem.Project = item.Project;
                            enhancedItem.ItemType = item.ItemType;
                        }
                        return enhancedItem;
                    });
                    
                    var batchResults = await Task.WhenAll(tasks);
                    enhancedBatch.AddRange(batchResults.Where(r => r != null)!);
                    
                    enhancedRequirements.AddRange(enhancedBatch);
                    
                    // Small delay between batches to be respectful to the API
                    if (i + batchSize < bulkRequirements.Count)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Enhanced {enhancedRequirements.Count} requirements with user metadata");
                return enhancedRequirements;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"Failed to get requirements with user metadata for project {projectId}: {ex.Message}");
                // Fallback to bulk requirements if enhanced version fails
                return await GetRequirementsAsync(projectId, cancellationToken);
            }
        }
        
        /// <summary>
        /// Parse all fields in a Jama item for rich content (HTML tables, paragraphs, etc.)
        /// This method works with the raw JSON API response to access dynamic field names
        /// </summary>
        private async Task<RequirementLooseContent> ParseAllFieldsForRichContentFromJsonAsync(JsonElement itemElement, string description, int itemId)
        {
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
            };

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Scanning all fields for rich content");

            var fieldsToScan = new List<(string name, string content)>();

            // Add description field
            if (!string.IsNullOrWhiteSpace(description))
            {
                fieldsToScan.Add(("description", description));
            }

            // Scan all fields from the raw JSON
            if (itemElement.TryGetProperty("fields", out JsonElement fieldsElement))
            {
                foreach (var field in fieldsElement.EnumerateObject())
                {
                    try
                    {
                        if (field.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = field.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                fieldsToScan.Add((field.Name, value));
                                
                                // Log interesting field names for debugging
                                if (field.Name.Contains("jurisdiction") || field.Name.Contains("table") || 
                                    field.Name.Contains("classification") || value.Contains("<table"))
                                {
                                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Found potential table field '{field.Name}' with HTML content");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Failed to read field {field.Name}: {ex.Message}");
                    }
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Found {fieldsToScan.Count} fields to scan for HTML content");

            // Process each field for HTML content
            int totalTablesFound = 0;
            int totalParagraphsFound = 0;
            int htmlFieldsFound = 0;

            foreach (var (fieldName, content) in fieldsToScan)
            {
                if (content.Contains("<") && content.Contains(">"))
                {
                    htmlFieldsFound++;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Processing HTML content in field '{fieldName}' ({content.Length} chars)");

                    var fieldContent = ParseHtmlContent(content, itemId);
                    
                    // Merge tables (with deduplication)
                    foreach (var table in fieldContent.Tables)
                    {
                        // Check for duplicate tables based on content
                        bool isDuplicate = looseContent.Tables.Any(existingTable =>
                            existingTable.ColumnHeaders.Count == table.ColumnHeaders.Count &&
                            existingTable.Rows.Count == table.Rows.Count &&
                            existingTable.ColumnHeaders.SequenceEqual(table.ColumnHeaders) &&
                            existingTable.Rows.SelectMany(r => r).SequenceEqual(table.Rows.SelectMany(r => r)));
                        
                        if (!isDuplicate)
                        {
                            // Set a descriptive title indicating which field this came from
                            if (string.IsNullOrWhiteSpace(table.EditableTitle) || table.EditableTitle.StartsWith("Table from Jama Item"))
                            {
                                table.EditableTitle = $"Table from {fieldName} (Item {itemId})";
                            }
                            looseContent.Tables.Add(table);
                            totalTablesFound++;
                            
                            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted table from field '{fieldName}' with {table.Rows.Count} rows");
                        }
                        else
                        {
                            TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Skipped duplicate table from field '{fieldName}'");
                        }
                    }

                    // Merge paragraphs
                    foreach (var paragraph in fieldContent.Paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(paragraph))
                        {
                            // Optionally prefix with field name for context
                            var contextualParagraph = fieldName != "description" 
                                ? $"[{fieldName}] {paragraph}" 
                                : paragraph;
                            looseContent.Paragraphs.Add(contextualParagraph);
                            totalParagraphsFound++;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    // Plain text content - add as paragraph with context
                    var contextualParagraph = fieldName != "description" 
                        ? $"[{fieldName}] {content.Trim()}" 
                        : content.Trim();
                    looseContent.Paragraphs.Add(contextualParagraph);
                    totalParagraphsFound++;
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Rich content parsing complete - " +
                $"{htmlFieldsFound} HTML fields, {totalTablesFound} tables, {totalParagraphsFound} paragraphs");

            return looseContent;
        }

        /// <summary>
        /// Get rich content for an item, using JSON data if available, fallback to object data
        /// </summary>
        private async Task<RequirementLooseContent> GetRichContentForItemAsync(int itemId, Dictionary<int, JsonElement> jsonLookup, string description)
        {
            // Try to use JSON data for better field access
            if (jsonLookup.TryGetValue(itemId, out JsonElement jsonElement))
            {
                return await ParseAllFieldsForRichContentFromJsonAsync(jsonElement, description, itemId);
            }
            else
            {
                // Fallback to basic content parsing from description only
                TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Item {itemId}: No JSON data available, using basic description parsing");
                return ParseHtmlContent(description, itemId);
            }
        }

        /// <summary>
        /// Parse all fields in a Jama item for rich content (HTML tables, paragraphs, etc.)
        /// </summary>
        private RequirementLooseContent ParseAllFieldsForRichContent(JamaItem item, string description)
        {
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
            };

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Scanning all fields for rich content");

            var fieldsToScan = new List<(string name, string content)>();

            // Add description field
            if (!string.IsNullOrWhiteSpace(description))
            {
                fieldsToScan.Add(("description", description));
            }

            // Scan all custom fields in the Fields object using reflection
            if (item.Fields != null)
            {
                var fieldsType = item.Fields.GetType();
                var properties = fieldsType.GetProperties();

                foreach (var property in properties)
                {
                    try
                    {
                        var value = property.GetValue(item.Fields);
                        if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            fieldsToScan.Add((property.Name, stringValue));
                        }
                    }
                    catch (Exception ex)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {item.Id}: Failed to read property {property.Name}: {ex.Message}");
                    }
                }
            }

            // Also scan the raw fields collection if it exists as a dictionary
            try
            {
                // Check if the JamaItem has additional fields as a dictionary
                var itemType = item.GetType();
                var fieldsProperty = itemType.GetProperty("Fields");
                if (fieldsProperty?.GetValue(item) is IDictionary<string, object> fieldsDictionary)
                {
                    foreach (var kvp in fieldsDictionary)
                    {
                        if (kvp.Value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
                        {
                            fieldsToScan.Add((kvp.Key, stringValue));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {item.Id}: Failed to scan fields dictionary: {ex.Message}");
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Found {fieldsToScan.Count} fields to scan for HTML content");

            // Process each field for HTML content
            int totalTablesFound = 0;
            int totalParagraphsFound = 0;
            int htmlFieldsFound = 0;

            foreach (var (fieldName, content) in fieldsToScan)
            {
                if (content.Contains("<") && content.Contains(">"))
                {
                    htmlFieldsFound++;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Processing HTML content in field '{fieldName}' ({content.Length} chars)");

                    var fieldContent = ParseHtmlContent(content, item.Id);
                    
                    // Merge tables
                    foreach (var table in fieldContent.Tables)
                    {
                        // Set a descriptive title indicating which field this came from
                        if (string.IsNullOrWhiteSpace(table.EditableTitle) || table.EditableTitle.StartsWith("Table from Jama Item"))
                        {
                            table.EditableTitle = $"Table from {fieldName} (Item {item.Id})";
                        }
                        looseContent.Tables.Add(table);
                        totalTablesFound++;
                    }

                    // Merge paragraphs
                    foreach (var paragraph in fieldContent.Paragraphs)
                    {
                        if (!string.IsNullOrWhiteSpace(paragraph))
                        {
                            // Optionally prefix with field name for context
                            var contextualParagraph = fieldName != "description" 
                                ? $"[{fieldName}] {paragraph}" 
                                : paragraph;
                            looseContent.Paragraphs.Add(contextualParagraph);
                            totalParagraphsFound++;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    // Plain text content - add as paragraph with context
                    var contextualParagraph = fieldName != "description" 
                        ? $"[{fieldName}] {content.Trim()}" 
                        : content.Trim();
                    looseContent.Paragraphs.Add(contextualParagraph);
                    totalParagraphsFound++;
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {item.Id}: Rich content parsing complete - " +
                $"{htmlFieldsFound} HTML fields, {totalTablesFound} tables, {totalParagraphsFound} paragraphs");

            return looseContent;
        }

        /// <summary>
        /// Parse HTML content to extract rich content like tables and paragraphs
        /// </summary>
        private RequirementLooseContent ParseHtmlContent(string htmlContent, int itemId)
        {
            var looseContent = new RequirementLooseContent()
            {
                Paragraphs = new List<string>(),
                Tables = new List<LooseTable>()
            };

            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                return looseContent;
            }

            try
            {
                // Check if content contains HTML tags
                if (!htmlContent.Contains("<") || !htmlContent.Contains(">"))
                {
                    // Plain text content - add as single paragraph with HTML entity cleaning
                    var cleanedText = CleanHtmlText(htmlContent);
                    if (!string.IsNullOrWhiteSpace(cleanedText))
                    {
                        looseContent.Paragraphs.Add(cleanedText);
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Plain text content, added as single paragraph after cleaning");
                    return looseContent;
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Parsing HTML content for rich elements");

                // Load HTML content
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // Extract tables
                var tableNodes = doc.DocumentNode.SelectNodes("//table");
                if (tableNodes != null)
                {
                    foreach (var tableNode in tableNodes)
                    {
                        var table = ExtractTable(tableNode, itemId);
                        if (table != null)
                        {
                            looseContent.Tables.Add(table);
                        }
                    }
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted {looseContent.Tables.Count} tables from HTML");
                }

                // Extract paragraphs (exclude content within tables)
                ExtractParagraphs(doc.DocumentNode, looseContent.Paragraphs, itemId);

                // If no paragraphs were extracted but we have content, fall back to the full HTML as text
                if (looseContent.Paragraphs.Count == 0 && !string.IsNullOrWhiteSpace(doc.DocumentNode.InnerText))
                {
                    var plainText = CleanHtmlText(doc.DocumentNode.InnerText);
                    if (!string.IsNullOrWhiteSpace(plainText))
                    {
                        looseContent.Paragraphs.Add(plainText);
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Fallback to plain text extraction");
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: HTML parsing complete - {looseContent.Tables.Count} tables, {looseContent.Paragraphs.Count} paragraphs");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to parse HTML content, falling back to plain text");
                // Fallback: treat as plain text
                looseContent.Paragraphs.Add(htmlContent.Trim());
            }

            return looseContent;
        }

        /// <summary>
        /// Extract table structure from HTML table node
        /// </summary>
        private LooseTable? ExtractTable(HtmlNode tableNode, int itemId)
        {
            try
            {
                var table = new LooseTable();

                // Extract table caption if exists
                var captionNode = tableNode.SelectSingleNode(".//caption");
                if (captionNode != null)
                {
                    table.EditableTitle = captionNode.InnerText.Trim();
                }

                // Extract rows
                var rowNodes = tableNode.SelectNodes(".//tr");
                if (rowNodes == null || rowNodes.Count == 0)
                {
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Table found but no rows, skipping");
                    return null;
                }

                var isFirstRowHeaders = false;
                var headerRow = rowNodes[0];
                
                // Check if first row contains th elements (headers)
                var headerCells = headerRow.SelectNodes(".//th");
                if (headerCells != null && headerCells.Count > 0)
                {
                    isFirstRowHeaders = true;
                    // Extract headers
                    foreach (var cell in headerCells)
                    {
                        // Apply CleanHtmlText to decode HTML entities in headers
                        var headerText = CleanHtmlText(cell.InnerText);
                        table.ColumnHeaders.Add(headerText);
                    }
                }
                else
                {
                    // Check if first row should be treated as headers based on td content
                    var firstRowCells = headerRow.SelectNodes(".//td");
                    if (firstRowCells != null && firstRowCells.Count > 0)
                    {
                        // Add headers from first row if it looks like headers
                        foreach (var cell in firstRowCells)
                        {
                            // Apply CleanHtmlText to decode HTML entities in headers
                            var cellText = CleanHtmlText(cell.InnerText);
                            table.ColumnHeaders.Add(cellText);
                        }
                        isFirstRowHeaders = true; // Treat first row as headers
                    }
                }

                // Generate column keys for headers
                for (int i = 0; i < table.ColumnHeaders.Count; i++)
                {
                    table.ColumnKeys.Add($"c{i}");
                }

                // Extract data rows (skip first row if it was headers)
                int startIndex = isFirstRowHeaders ? 1 : 0;
                for (int i = startIndex; i < rowNodes.Count; i++)
                {
                    var rowNode = rowNodes[i];
                    var cellNodes = rowNode.SelectNodes(".//td | .//th");
                    
                    if (cellNodes != null && cellNodes.Count > 0)
                    {
                        var row = new List<string>();
                        foreach (var cell in cellNodes)
                        {
                            // Apply CleanHtmlText to decode HTML entities in table cells
                            var cellText = CleanHtmlText(cell.InnerText);
                            row.Add(cellText);
                        }
                        table.Rows.Add(row);
                    }
                }

                // Set a default title if none was found
                if (string.IsNullOrWhiteSpace(table.EditableTitle))
                {
                    table.EditableTitle = $"Table from Jama Item {itemId}";
                }

                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Item {itemId}: Extracted table '{table.EditableTitle}' with {table.ColumnHeaders.Count} headers and {table.Rows.Count} rows");
                return table;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to extract table");
                return null;
            }
        }

        /// <summary>
        /// Check if a field is metadata that should not appear in supplemental information
        /// </summary>
        private bool IsMetadataField(string fieldName)
        {
            var metadataFields = new[] {
                "documentkey", "globalid", "id", "itemtype", "project", 
                "status", "createddate", "modifieddate", "createdby", "modifiedby",
                "version", "locked", "lockedby", "lastlocked", "sortorder"
            };
            
            return metadataFields.Contains(fieldName.ToLowerInvariant());
        }

        /// <summary>
        /// Convert Jama itemType numbers to human-readable text
        /// </summary>
        private string GetHumanReadableItemType(int? itemType)
        {
            return itemType switch
            {
                193 => "Requirement",
                194 => "Test Case", 
                195 => "Test Step",
                196 => "Defect",
                197 => "Epic",
                198 => "User Story",
                199 => "Task",
                _ => itemType?.ToString() ?? "Unknown"
            };
        }

        /// <summary>
        /// Clean and decode HTML text content, converting entities to proper text and stripping HTML tags
        /// </summary>
        private string CleanHtmlText(string htmlText)
        {
            if (string.IsNullOrWhiteSpace(htmlText))
                return string.Empty;

            // First, decode HTML entities using System.Net.WebUtility
            var decodedText = System.Net.WebUtility.HtmlDecode(htmlText);
            
            // Then use HtmlAgilityPack to strip any remaining HTML tags
            var doc = new HtmlDocument();
            doc.LoadHtml(decodedText);
            
            // Extract plain text
            var plainText = doc.DocumentNode.InnerText;
            
            // Clean up extra whitespace
            plainText = System.Text.RegularExpressions.Regex.Replace(plainText, @"\s+", " ");
            
            return plainText.Trim();
        }

        /// <summary>
        /// Extract paragraphs from HTML, excluding table content
        /// </summary>
        private void ExtractParagraphs(HtmlNode node, List<string> paragraphs, int itemId)
        {
            try
            {
                // Remove table nodes to avoid duplicate content
                var tableNodes = node.SelectNodes(".//table")?.ToList();
                if (tableNodes != null)
                {
                    foreach (var tableNode in tableNodes)
                    {
                        tableNode.Remove();
                    }
                }

                // Extract paragraph nodes
                var paragraphNodes = node.SelectNodes(".//p");
                if (paragraphNodes != null)
                {
                    foreach (var pNode in paragraphNodes)
                    {
                        var text = CleanHtmlText(pNode.InnerText);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            paragraphs.Add(text);
                        }
                    }
                }

                // If no paragraph nodes found, extract text from other common elements
                if (paragraphs.Count == 0)
                {
                    // Try div elements
                    var divNodes = node.SelectNodes(".//div");
                    if (divNodes != null)
                    {
                        foreach (var divNode in divNodes)
                        {
                            var text = CleanHtmlText(divNode.InnerText);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                paragraphs.Add(text);
                            }
                        }
                    }
                }

                // If still no content, extract from lists
                if (paragraphs.Count == 0)
                {
                    var listItems = node.SelectNodes(".//li");
                    if (listItems != null)
                    {
                        var listContent = new List<string>();
                        foreach (var li in listItems)
                        {
                            var text = CleanHtmlText(li.InnerText);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                listContent.Add($"• {text}");
                            }
                        }
                        if (listContent.Count > 0)
                        {
                            paragraphs.Add(string.Join("\n", listContent));
                        }
                    }
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Item {itemId}: Extracted {paragraphs.Count} paragraphs");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[JamaConnect] Item {itemId}: Failed to extract paragraphs");
            }
        }

        /// <summary>
        /// Convert Jama items to our Requirement model
        /// </summary>
        public async Task<List<Requirement>> ConvertToRequirementsAsync(List<JamaItem> jamaItems)
        {
            var requirements = new List<Requirement>();
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Converting {jamaItems.Count} Jama items to requirements");
            
            // Create a lookup for JSON elements by item ID for rich content parsing
            Dictionary<int, JsonElement> jsonItemLookup = new Dictionary<int, JsonElement>();
            if (_lastApiResponseJson != null)
            {
                try
                {
                    if (_lastApiResponseJson.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var jsonItem in dataElement.EnumerateArray())
                        {
                            if (jsonItem.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number)
                            {
                                var id = idElement.GetInt32();
                                jsonItemLookup[id] = jsonItem;
                            }
                        }
                    }
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[JamaConnect] Created JSON lookup for rich content extraction: {jsonItemLookup.Count} items");
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[JamaConnect] Failed to create JSON lookup: {ex.Message}");
                }
            }
            
            foreach (var item in jamaItems)
            {
                // Enhanced field mapping with better fallbacks
                var itemId = item.Item ?? item.DocumentKey ?? item.GlobalId ?? $"JAMA-{item.Id}";
                
                // Access Name and Description directly from the item, not from Fields
                var name = item.Name;
                var description = item.Description;
                
                // Try multiple field sources for name
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = item.Fields?.Name;  // Fallback to Fields.Name
                }
                
                // Try multiple field sources for description  
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = item.Fields?.Description;  // Fallback to Fields.Description
                }
                
                // Use the actual name or empty string - NO fake name generation

                var requirement = new Requirement
                {
                    Item = itemId,
                    Name = CleanHtmlText(name ?? ""),  // Clean HTML entities from name
                    Description = CleanHtmlText(description ?? ""),
                    GlobalId = item.GlobalId ?? "",
                    Status = item.Status ?? item.Fields?.Status ?? "",
                    RequirementType = GetHumanReadableItemType(item.ItemType),
                    Project = item.Project?.ToString() ?? "",
                    
                    // Enhanced field mapping from Jama with user metadata resolution
                    Version = item.Fields?.Version ?? "",
                    CreatedBy = GetCreatedByField(item),  // Enhanced user resolution
                    ModifiedBy = GetModifiedByField(item), // Enhanced user resolution  
                    CreatedDateRaw = item.Fields?.CreatedDate ?? "",
                    ModifiedDateRaw = item.Fields?.ModifiedDate ?? "",
                    KeyCharacteristics = CleanHtmlText(item.Fields?.KeyCharacteristics ?? ""), // Clean HTML entities
                    Fdal = item.Fields?.FDAL ?? "",
                    DerivedRequirement = item.Fields?.Derived ?? "",
                    ExportControlled = item.Fields?.ExportControlled ?? "",
                    SetName = item.Fields?.Set ?? "",
                    Heading = CleanHtmlText(item.Fields?.Heading ?? ""), // Clean HTML entities
                    ChangeDriver = CleanHtmlText(item.Fields?.ChangeDriver ?? ""), // Clean HTML entities
                    LastLockedBy = item.Fields?.LockedBy ?? "",
                    
                    // Set dates if available from enhanced metadata
                    CreatedDate = TryParseDate(item.CreatedDate),
                    ModifiedDate = TryParseDate(item.ModifiedDate),
                    
                    // Initialize LooseContent with HTML parsing for rich content from ALL fields
                    LooseContent = await GetRichContentForItemAsync(item.Id, jsonItemLookup, description ?? "")
                };
                
                requirements.Add(requirement);
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Successfully converted {requirements.Count} Jama items to requirements");
            
            return requirements;
        }
        
        /// <summary>
        /// Get CreatedBy field with enhanced user metadata resolution
        /// </summary>
        private string GetCreatedByField(JamaItem item)
        {
            // Priority 1: Enhanced user metadata (from individual item calls)
            if (!string.IsNullOrEmpty(item.CreatedByName))
            {
                return item.CreatedByName;
            }
            
            // Priority 2: Fallback to bulk field data
            if (!string.IsNullOrEmpty(item.Fields?.CreatedBy))
            {
                return item.Fields.CreatedBy;
            }
            
            // Priority 3: Empty string for missing data
            return "";
        }
        
        /// <summary>
        /// Get ModifiedBy field with enhanced user metadata resolution
        /// </summary>
        private string GetModifiedByField(JamaItem item)
        {
            // Priority 1: Enhanced user metadata (from individual item calls)
            if (!string.IsNullOrEmpty(item.ModifiedByName))
            {
                return item.ModifiedByName;
            }
            
            // Priority 2: Fallback to bulk field data  
            if (!string.IsNullOrEmpty(item.Fields?.ModifiedBy))
            {
                return item.Fields.ModifiedBy;
            }
            
            // Priority 3: Empty string for missing data
            return "";
        }
        
        /// <summary>
        /// Try to parse date string into nullable DateTime
        /// </summary>
        private DateTime? TryParseDate(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
                return null;
                
            if (DateTime.TryParse(dateString, out var result))
                return result;
                
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // ENHANCED API METHODS - Based on Jama Developer Cookbook patterns
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Download file from rich text field attachment using Files endpoint
        /// Based on cookbook pattern for downloading attachments from rich text fields
        /// </summary>
        public async Task<byte[]?> DownloadFileAsync(string jamaFileUrl)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                // Extract file URL parameter if it's a full Jama URL
                var fileUrl = jamaFileUrl;
                if (jamaFileUrl.Contains("?url="))
                {
                    var urlParam = jamaFileUrl.Split("?url=")[1];
                    fileUrl = Uri.UnescapeDataString(urlParam);
                }
                
                // Use Files endpoint: GET /rest/v1/files?url=<file-url>
                var filesEndpoint = $"{_baseUrl}/rest/v1/files?url={Uri.EscapeDataString(fileUrl)}";
                
                var response = await _httpClient.GetAsync(filesEndpoint);
                response.EnsureSuccessStatusCode();
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Downloaded file from {fileUrl}");
                return await response.Content.ReadAsByteArrayAsync();
            });
        }

        /// <summary>
        /// Search items using Abstract Items endpoint for advanced filtering
        /// Based on cookbook pattern for searching with Lucene syntax
        /// </summary>
        public async Task<List<JamaItem>> SearchAbstractItemsAsync(
            int projectId,
            string? contains = null,
            int maxResults = 20,
            List<string>? includeParams = null)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var query = new StringBuilder($"{_baseUrl}/rest/v1/abstractitems");
                query.Append($"?project={projectId}");
                
                if (!string.IsNullOrEmpty(contains))
                {
                    query.Append($"&contains={Uri.EscapeDataString(contains)}");
                }
                
                query.Append($"&maxResults={maxResults}");
                
                if (includeParams?.Count > 0)
                {
                    query.Append($"&include={string.Join(",", includeParams)}");
                }
                
                var response = await _httpClient.GetAsync(query.ToString());
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Abstract items endpoint failed with {response.StatusCode}: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Falling back to standard items endpoint");
                        // Fallback to standard items endpoint
                        var fallbackUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults={maxResults}";
                        var fallbackResponse = await _httpClient.GetAsync(fallbackUrl);
                        fallbackResponse.EnsureSuccessStatusCode();
                        
                        var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync();
                        var fallbackResult = JsonSerializer.Deserialize<JamaItemsResponse>(fallbackJson, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fallback search found {fallbackResult?.Data?.Count ?? 0} items");
                        return fallbackResult?.Data ?? new List<JamaItem>();
                    }
                    
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Abstract search found {result?.Data?.Count ?? 0} items");
                return result?.Data ?? new List<JamaItem>();
            });
        }

        /// <summary>
        /// Get activities for tracking changes - based on cookbook change detection pattern
        /// </summary>
        public async Task<List<JamaActivity>> GetActivitiesAsync(int projectId, int maxResults = 20)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var url = $"{_baseUrl}/rest/v1/activities?project={projectId}&maxResults={maxResults}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Activities endpoint failed with {response.StatusCode}: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Activities endpoint not supported - returning empty list");
                        return new List<JamaActivity>();
                    }
                    
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JamaActivityListResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {result?.Data?.Count ?? 0} activities");
                return result?.Data ?? new List<JamaActivity>();
            });
        }

        /// <summary>
        /// Get requirements with include parameters for efficient API usage
        /// Based on cookbook pattern for reducing API calls
        /// </summary>
        public async Task<List<JamaItem>> GetRequirementsWithIncludesAsync(
            int projectId,
            List<string> includeParams,
            CancellationToken cancellationToken = default)
        {
            return await WithRetryAsync(async () =>
            {
                await EnsureAccessTokenAsync();
                
                var includes = string.Join(",", includeParams);
                var url = $"{_baseUrl}/rest/v1/items?project={projectId}&include={includes}&maxResults=50";
                
                var response = await _httpClient.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Enhanced requirements endpoint failed with {response.StatusCode}: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Info("[JamaConnect] Falling back to basic items endpoint");
                        // Fallback to basic endpoint without include parameters
                        var fallbackUrl = $"{_baseUrl}/rest/v1/items?project={projectId}&maxResults=50";
                        var fallbackResponse = await _httpClient.GetAsync(fallbackUrl, cancellationToken);
                        fallbackResponse.EnsureSuccessStatusCode();
                        
                        var fallbackJson = await fallbackResponse.Content.ReadAsStringAsync(cancellationToken);
                        var fallbackResult = JsonSerializer.Deserialize<JamaItemsResponse>(fallbackJson, new JsonSerializerOptions 
                        { 
                            PropertyNameCaseInsensitive = true 
                        });
                        
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Fallback found {fallbackResult?.Data?.Count ?? 0} items");
                        return fallbackResult?.Data ?? new List<JamaItem>();
                    }
                    
                    response.EnsureSuccessStatusCode();
                }
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<JamaItemsResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Retrieved {result?.Data?.Count ?? 0} items with includes: {includes}");
                return result?.Data ?? new List<JamaItem>();
            });
        }



        /// <summary>
        /// Retry wrapper with rate limiting - based on cookbook throttling pattern
        /// </summary>
        private async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
        {
            await _rateLimitSemaphore.WaitAsync();
            
            try
            {
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        return await operation();
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("429") && attempt < maxRetries - 1)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                        TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Rate limited, retrying in {delay.TotalSeconds}s (attempt {attempt + 1}/{maxRetries})");
                        await Task.Delay(delay);
                    }
                }
                
                // Final attempt without catch
                return await operation();
            }
            finally
            {
                // Release rate limit after a short delay
                _ = Task.Delay(100).ContinueWith(_ => _rateLimitSemaphore.Release());
            }
        }

        /// <summary>
        /// Extract attachment URLs from rich text fields - based on cookbook file download pattern
        /// </summary>
        public List<string> ExtractAttachmentUrls(string richTextContent)
        {
            var urls = new List<string>();
            
            if (string.IsNullOrEmpty(richTextContent))
                return urls;
            
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(richTextContent);
                
                // Find images with src attributes
                var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src) && src.Contains("attachment"))
                        {
                            urls.Add(src);
                        }
                    }
                }
                
                // Find links with href attributes pointing to attachments
                var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
                if (linkNodes != null)
                {
                    foreach (var link in linkNodes)
                    {
                        var href = link.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(href) && href.Contains("attachment"))
                        {
                            urls.Add(href);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[JamaConnect] Error parsing rich text for attachments");
            }
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[JamaConnect] Extracted {urls.Count} attachment URLs from rich text");
            return urls;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // DTOs for Jama Connect API responses
    public class JamaProjectsResponse
    {
        public List<JamaProject> Data { get; set; } = new();
    }

    public class JamaProject
    {
        public int Id { get; set; }
        public string ProjectKey { get; set; } = "";
        public bool IsFolder { get; set; }
        public string CreatedDate { get; set; } = "";
        public string ModifiedDate { get; set; } = "";
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public JamaProjectFields? Fields { get; set; }
        public string Type { get; set; } = "";
        
        // Convenience properties for UI binding
        public string Name => Fields?.Name ?? "";
        public string Key => ProjectKey;
        public string Description => Fields?.Description ?? "";
    }
    
    public class JamaProjectFields
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ProjectKey { get; set; } = "";
        public string Text1 { get; set; } = "";
    }
    
    /// <summary>
    /// Custom JSON converter that handles status fields that can be either string or number
    /// Different Jama instances/configurations may return status as string ("Active") or number (1)
    /// </summary>
    public class FlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    return reader.GetInt32().ToString();
                case JsonTokenType.Null:
                    return null;
                default:
                    throw new JsonException($"Unable to convert token type '{reader.TokenType}' to string.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public class JamaItemsResponse
    {
        public List<JamaItem> Data { get; set; } = new();
    }

    public class JamaItem
    {
        public int Id { get; set; }
        public string? DocumentKey { get; set; }
        public string? GlobalId { get; set; }
        public int? Project { get; set; }
        public int? ItemType { get; set; }
        public JamaItemFields? Fields { get; set; }
        
        // Direct properties that exist in actual Jama response
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public string? Item { get; set; }  // The Item ID like "DECAGON-CMP-7"
        
        // User metadata properties (for individual item calls)
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public string CreatedDate { get; set; } = "";
        public string ModifiedDate { get; set; } = "";
        public int Version { get; set; }
        
        // Resolved user names (populated after user lookup)
        public string CreatedByName { get; set; } = "";
        public string ModifiedByName { get; set; } = "";
    }

    public class JamaItemFields
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Status { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Priority { get; set; }
        
        // Enhanced field mapping for common Jama fields
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Version { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? CreatedBy { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ModifiedBy { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? CreatedDate { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ModifiedDate { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? KeyCharacteristics { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Safety { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Security { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Derived { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ExportControlled { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? FDAL { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Set { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Heading { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? ChangeDriver { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? LockedBy { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Comments { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Attachments { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? DownstreamLinks { get; set; }
        
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? UpstreamLinks { get; set; }
        
        // Add more fields as needed based on your Jama configuration
    }

    /// <summary>
    /// User information from Jama
    /// </summary>
    public class JamaUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool Active { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 🚨 CRITICAL OAUTH CODE - DO NOT MODIFY WITHOUT EXPLICIT CONFIRMATION 🚨
    // ═══════════════════════════════════════════════════════════════════════════════
    // This OAuth implementation has been fixed multiple times due to JSON serialization
    // issues. The JsonPropertyName attributes are CRITICAL and must match the exact
    // field names returned by Jama's OAuth API (snake_case).
    // 
    // CHANGE HISTORY:
    // - 2026-01-23: Fixed missing JsonPropertyName attributes causing auth failures
    // - Previous issues: OAuth token deserialization returning null AccessToken
    // 
    // ⚠️  BEFORE MAKING ANY CHANGES:
    // 1. Test OAuth authentication thoroughly 
    // 2. Verify token response JSON structure matches these property names
    // 3. Confirm with maintainer before modifying
    // ═══════════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// OAuth token response from Jama
    /// </summary>
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]  // 🚨 CRITICAL: Must match Jama API response exactly
        public string? AccessToken { get; set; }
        
        [JsonPropertyName("token_type")]   // 🚨 CRITICAL: Must match Jama API response exactly
        public string? TokenType { get; set; }
        
        [JsonPropertyName("expires_in")]   // 🚨 CRITICAL: Must match Jama API response exactly
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("refresh_token")] // 🚨 CRITICAL: Must match Jama API response exactly
        public string? RefreshToken { get; set; }
        
        /// <summary>
        /// Validate that the OAuth response has the required token
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(AccessToken);
    }

    /// <summary>
    /// User response wrapper from Jama API
    /// </summary>
    public class JamaUserResponse
    {
        public JamaUser? Data { get; set; }
        public JamaMeta? Meta { get; set; }
        public List<object>? Links { get; set; }
    }

    /// <summary>
    /// Item response wrapper from Jama API
    /// </summary>
    public class JamaItemResponse
    {
        public JamaItem? Data { get; set; }
        public JamaMeta? Meta { get; set; }
        public List<object>? Links { get; set; }
    }

    /// <summary>
    /// Activity information from Jama for tracking changes
    /// </summary>
    public class JamaActivity
    {
        public int Id { get; set; }
        public string Action { get; set; } = "";
        public DateTime Date { get; set; }
        public string UserName { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int ItemId { get; set; }
    }

    /// <summary>
    /// Response containing list of activities
    /// </summary>
    public class JamaActivityListResponse
    {
        public List<JamaActivity> Data { get; set; } = new();
        public JamaMeta? Meta { get; set; }
    }

    /// <summary>
    /// Metadata for Jama API responses
    /// </summary>
    public class JamaMeta
    {
        public JamaPageInfo? PageInfo { get; set; }
    }

    /// <summary>
    /// Pagination information from Jama API responses
    /// </summary>
    public class JamaPageInfo
    {
        public int StartIndex { get; set; }
        public int ResultCount { get; set; }
        public int TotalResults { get; set; }
    }
}
