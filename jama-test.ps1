# Simple Jama Connection Test Script

$env:JAMA_BASE_URL = "https://sevone.jamacloud.com"
$env:JAMA_CLIENT_ID = "d3ccbf17-6ea4-448b-be14-e9d9a0b2b1e6"  
$env:JAMA_CLIENT_SECRET = "0e00f5a1-78c0-4ab2-a99d-71a3b41b8e0f"

Write-Host "=== Jama Connection Test ===" -ForegroundColor Cyan
Write-Host "JAMA_BASE_URL: $env:JAMA_BASE_URL"
Write-Host "JAMA_CLIENT_ID: $env:JAMA_CLIENT_ID"
Write-Host "JAMA_CLIENT_SECRET: $env:JAMA_CLIENT_SECRET"
Write-Host ""

$tokenUrl = "$env:JAMA_BASE_URL/rest/oauth/token"
$credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$env:JAMA_CLIENT_ID`:$env:JAMA_CLIENT_SECRET"))

$headers = @{
    'Authorization' = "Basic $credentials"
    'Content-Type' = 'application/x-www-form-urlencoded'
}

$body = 'grant_type=client_credentials&scope=read'

Write-Host "🔐 Testing OAuth token request..."
Write-Host "URL: $tokenUrl"

try {
    $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body
    Write-Host "✅ OAuth token obtained!" -ForegroundColor Green
    Write-Host "Token: $($tokenResponse.access_token.Substring(0, 20))..." -ForegroundColor Green
    
    $projectsUrl = "$env:JAMA_BASE_URL/rest/v1/projects"
    $apiHeaders = @{
        'Authorization' = "Bearer $($tokenResponse.access_token)"
    }
    
    Write-Host ""
    Write-Host "📋 Testing projects API..."
    Write-Host "URL: $projectsUrl"
    
    $projectsResponse = Invoke-RestMethod -Uri $projectsUrl -Method Get -Headers $apiHeaders
    $projectCount = if ($projectsResponse.data) { $projectsResponse.data.Count } else { 0 }
    
    Write-Host "✅ SUCCESS! Connected to Jama Connect!" -ForegroundColor Green
    Write-Host "Found $projectCount projects." -ForegroundColor Green
    
} catch {
    Write-Host "❌ FAILED!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan