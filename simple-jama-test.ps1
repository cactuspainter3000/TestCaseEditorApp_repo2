# Simple Jama Connection Test Script

# Set environment variables (same as DT's working credentials)
$env:JAMA_BASE_URL = "https://sevone.jamacloud.com"
$env:JAMA_CLIENT_ID = "d3ccbf17-6ea4-448b-be14-e9d9a0b2b1e6"  
$env:JAMA_CLIENT_SECRET = "0e00f5a1-78c0-4ab2-a99d-71a3b41b8e0f"

Write-Host "=== Jama Connection Test ===" -ForegroundColor Cyan
Write-Host "Environment Variables:" -ForegroundColor Yellow
Write-Host "JAMA_BASE_URL: $env:JAMA_BASE_URL"
Write-Host "JAMA_CLIENT_ID: $env:JAMA_CLIENT_ID"
Write-Host "JAMA_CLIENT_SECRET: $env:JAMA_CLIENT_SECRET"
Write-Host ""

# Test OAuth token request
$tokenUrl = "$env:JAMA_BASE_URL/rest/oauth/token"
$credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$env:JAMA_CLIENT_ID`:$env:JAMA_CLIENT_SECRET"))

$headers = @{
    'Authorization' = "Basic $credentials"
    'Content-Type' = 'application/x-www-form-urlencoded'
}

$body = 'grant_type=client_credentials&scope=read'

Write-Host "🔐 Testing OAuth token request..." -ForegroundColor Yellow
Write-Host "   URL: $tokenUrl"
Write-Host "   Headers: Authorization=Basic [ENCODED]"
Write-Host "   Body: $body"
Write-Host ""

try {
    $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body
    Write-Host "✅ OAuth token obtained successfully!" -ForegroundColor Green
    Write-Host "   Access Token: $($tokenResponse.access_token.Substring(0, 20))..." -ForegroundColor Green
    Write-Host ""
    
    # Test projects API
    $projectsUrl = "$env:JAMA_BASE_URL/rest/v1/projects"
    $apiHeaders = @{
        'Authorization' = "Bearer $($tokenResponse.access_token)"
    }
    
    Write-Host "📋 Testing projects API..." -ForegroundColor Yellow
    Write-Host "   URL: $projectsUrl"
    Write-Host "   Headers: Authorization=Bearer [TOKEN]"
    Write-Host ""
    
    $projectsResponse = Invoke-RestMethod -Uri $projectsUrl -Method Get -Headers $apiHeaders
    $projectCount = if ($projectsResponse.data) { $projectsResponse.data.Count } else { 0 }
    
    Write-Host "✅ Successfully connected to Jama Connect!" -ForegroundColor Green
    Write-Host "   Found $projectCount projects." -ForegroundColor Green
    
} catch {
    Write-Host "❌ Connection failed!" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode
        Write-Host "   HTTP Status: $statusCode" -ForegroundColor Red
        
        if ($statusCode -eq 400) {
            Write-Host "   💡 This might be an OAuth scope issue - check that the client has 'read' scope" -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan