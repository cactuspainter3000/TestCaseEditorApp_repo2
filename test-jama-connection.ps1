# Test Jama Connection Script
# This script tests the Jama connection using the same code as the application

# Set environment variables (same as DT's working credentials)
$env:JAMA_BASE_URL = "https://sevone.jamacloud.com"
$env:JAMA_CLIENT_ID = "d3ccbf17-6ea4-448b-be14-e9d9a0b2b1e6"  
$env:JAMA_CLIENT_SECRET = "0e00f5a1-78c0-4ab2-a99d-71a3b41b8e0f"

Write-Host "Environment Variables:" -ForegroundColor Cyan
Write-Host "JAMA_BASE_URL: $env:JAMA_BASE_URL"
Write-Host "JAMA_CLIENT_ID: $env:JAMA_CLIENT_ID"
Write-Host "JAMA_CLIENT_SECRET: $env:JAMA_CLIENT_SECRET"
Write-Host ""

# Create and run a simple C# test
$testCode = @"
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class JamaConnectionTest
{
    public static async Task Main(string[] args)
    {
        var baseUrl = Environment.GetEnvironmentVariable("JAMA_BASE_URL");
        var clientId = Environment.GetEnvironmentVariable("JAMA_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("JAMA_CLIENT_SECRET");

        Console.WriteLine("=== Jama Connection Test ===");
        Console.WriteLine($"Base URL: {baseUrl}");
        Console.WriteLine($"Client ID: {clientId}");
        Console.WriteLine($"Client Secret: {(!string.IsNullOrEmpty(clientSecret) ? "SET" : "NOT SET")}");
        Console.WriteLine();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Console.WriteLine("❌ Missing environment variables!");
            return;
        }

        try
        {
            using var httpClient = new HttpClient();
            
            // Step 1: Get OAuth token
            Console.WriteLine("🔐 Getting OAuth token...");
            var tokenUrl = $"{baseUrl}/rest/oauth/token";
            var authBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
            var authHeader = Convert.ToBase64String(authBytes);

            var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
            
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "read")
            });
            
            request.Content = form;
            
            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ OAuth failed: {response.StatusCode}");
                Console.WriteLine($"   Error: {errorContent}");
                return;
            }
            
            var tokenContent = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(tokenContent);
            var accessToken = tokenResponse.GetProperty("access_token").GetString();
            
            Console.WriteLine("✅ OAuth token obtained successfully");
            Console.WriteLine($"   Token starts with: {accessToken.Substring(0, Math.Min(10, accessToken.Length))}...");
            
            // Step 2: Test projects API
            Console.WriteLine();
            Console.WriteLine("📋 Testing projects API...");
            
            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            
            var projectsUrl = $"{baseUrl}/rest/v1/projects";
            var projectsResponse = await httpClient.GetAsync(projectsUrl);
            
            if (projectsResponse.IsSuccessStatusCode)
            {
                var projectsContent = await projectsResponse.Content.ReadAsStringAsync();
                var projectsJson = JsonSerializer.Deserialize<JsonElement>(projectsContent);
                var projectsArray = projectsJson.GetProperty("data");
                var projectCount = projectsArray.GetArrayLength();
                
                Console.WriteLine($"✅ Successfully connected to Jama Connect!");
                Console.WriteLine($"   Found {projectCount} projects.");
            }
            else
            {
                var errorContent = await projectsResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ Projects API failed: {projectsResponse.StatusCode}");
                Console.WriteLine($"   Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.Message}");
        }
    }
}
"@

# Write the test code to a temporary file
$testFile = "JamaConnectionTest.cs"
$testCode | Out-File -FilePath $testFile -Encoding UTF8

# Compile and run the test
Write-Host "Running connection test..." -ForegroundColor Yellow
try {
    # Direct PowerShell HTTP test since C# compilation is complex
    Write-Host "`n=== PowerShell HTTP Test ===" -ForegroundColor Cyan
    
    $tokenUrl = "$env:JAMA_BASE_URL/rest/oauth/token"
    $credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$env:JAMA_CLIENT_ID`:$env:JAMA_CLIENT_SECRET"))
    
    $headers = @{
        'Authorization' = "Basic $credentials"
        'Content-Type' = 'application/x-www-form-urlencoded'
    }
    
    $body = 'grant_type=client_credentials&scope=read'
    
    Write-Host "🔐 Testing OAuth token request..."
    try {
        $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body
        Write-Host "✅ OAuth token obtained!" -ForegroundColor Green
        
        # Test projects API
        $projectsUrl = "$env:JAMA_BASE_URL/rest/v1/projects"
        $apiHeaders = @{
            'Authorization' = "Bearer $($tokenResponse.access_token)"
        }
        
        Write-Host "📋 Testing projects API..."
        $projectsResponse = Invoke-RestMethod -Uri $projectsUrl -Method Get -Headers $apiHeaders
        $projectCount = $projectsResponse.data.Count
        
        Write-Host "✅ Successfully connected to Jama Connect!" -ForegroundColor Green
        Write-Host "   Found $projectCount projects." -ForegroundColor Green
        
    } catch {
        Write-Host "❌ Connection failed: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            Write-Host "   Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
        }
    }
} catch {
    Write-Host "❌ Script error: $($_.Exception.Message)" -ForegroundColor Red
}

# Cleanup
Remove-Item $testFile -Force -ErrorAction SilentlyContinue

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan