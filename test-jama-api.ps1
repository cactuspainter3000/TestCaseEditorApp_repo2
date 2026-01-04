#!/usr/bin/env pwsh
# Jama Connect API Test Script
# Tests basic connectivity and authentication with Jama Connect

Write-Host "=== Jama Connect API Test ===" -ForegroundColor Cyan
Write-Host "Testing Jama Connect integration capabilities" -ForegroundColor White

# Check for environment variables
Write-Host "`n1. Checking Configuration..." -ForegroundColor Yellow

$jamaUrl = $env:JAMA_BASE_URL
$jamaToken = $env:JAMA_API_TOKEN  
$jamaUser = $env:JAMA_USERNAME
$jamaPass = $env:JAMA_PASSWORD
$jamaClientId = $env:JAMA_CLIENT_ID
$jamaClientSecret = $env:JAMA_CLIENT_SECRET

Write-Host "   JAMA_BASE_URL: " -NoNewline -ForegroundColor Gray
if ($jamaUrl) {
    Write-Host "$jamaUrl" -ForegroundColor Green
} else {
    Write-Host "Not set" -ForegroundColor Red
}

Write-Host "   JAMA_API_TOKEN: " -NoNewline -ForegroundColor Gray
if ($jamaToken) {
    Write-Host "Configured (${jamaToken.Length} chars)" -ForegroundColor Green
} else {
    Write-Host "Not set" -ForegroundColor Red
}

Write-Host "   JAMA_CLIENT_ID: " -NoNewline -ForegroundColor Gray
if ($jamaClientId) {
    Write-Host "$jamaClientId" -ForegroundColor Green
} else {
    Write-Host "Not set" -ForegroundColor Red
}

Write-Host "   JAMA_CLIENT_SECRET: " -NoNewline -ForegroundColor Gray
if ($jamaClientSecret) {
    Write-Host "Configured (${jamaClientSecret.Length} chars)" -ForegroundColor Green
} else {
    Write-Host "Not set" -ForegroundColor Red
}

Write-Host "   JAMA_USERNAME: " -NoNewline -ForegroundColor Gray
if ($jamaUser) {
    Write-Host "$jamaUser" -ForegroundColor Green
} else {
    Write-Host "Not set" -ForegroundColor Red
}

Write-Host "   JAMA_PASSWORD: " -NoNewline -ForegroundColor Gray
if ($jamaPass) {
    Write-Host "Configured (${jamaPass.Length} chars)" -ForegroundColor Green
} else {
    Write-Host "Not set" -ForegroundColor Red
}

# Determine auth method
$hasTokenAuth = $jamaUrl -and $jamaToken
$hasOAuthAuth = $jamaUrl -and $jamaClientId -and $jamaClientSecret
$hasBasicAuth = $jamaUrl -and $jamaUser -and $jamaPass
$hasAnyAuth = $hasTokenAuth -or $hasOAuthAuth -or $hasBasicAuth

if (-not $hasAnyAuth) {
    Write-Host "`n❌ No valid authentication configured" -ForegroundColor Red
    Write-Host "`nTo test Jama Connect integration, set these environment variables:" -ForegroundColor Yellow
    Write-Host "   Option 1 - API Token (Recommended):" -ForegroundColor White
    Write-Host "     `$env:JAMA_BASE_URL = 'https://yourcompany.jamacloud.com'" -ForegroundColor Gray
    Write-Host "     `$env:JAMA_API_TOKEN = 'your-api-token-here'" -ForegroundColor Gray
    Write-Host "`n   Option 2 - OAuth Client Credentials:" -ForegroundColor White  
    Write-Host "     `$env:JAMA_BASE_URL = 'https://yourcompany.jamacloud.com'" -ForegroundColor Gray
    Write-Host "     `$env:JAMA_CLIENT_ID = 'your-client-id'" -ForegroundColor Gray
    Write-Host "     `$env:JAMA_CLIENT_SECRET = 'your-client-secret'" -ForegroundColor Gray
    Write-Host "`n   Option 3 - Username/Password:" -ForegroundColor White  
    Write-Host "     `$env:JAMA_BASE_URL = 'https://yourcompany.jamacloud.com'" -ForegroundColor Gray
    Write-Host "     `$env:JAMA_USERNAME = 'your-username'" -ForegroundColor Gray
    Write-Host "     `$env:JAMA_PASSWORD = 'your-password'" -ForegroundColor Gray
    Write-Host "`nThen run this script again to test the connection." -ForegroundColor White
    exit 1
}

$authMethod = if ($hasTokenAuth) { "API Token" } elseif ($hasOAuthAuth) { "OAuth Client Credentials" } else { "Username/Password" }
Write-Host "   Auth Method: $authMethod" -ForegroundColor Green

# Test basic connectivity
Write-Host "`n2. Testing Basic Connectivity..." -ForegroundColor Yellow

try {
    $testUrl = "$jamaUrl/rest/latest/ping"
    Write-Host "   Testing URL: $testUrl" -ForegroundColor Gray
    
    # Prepare headers
    $headers = @{
        "Accept" = "application/json"
    }
    
    if ($hasTokenAuth) {
        $headers["Authorization"] = "Bearer $jamaToken"
    } elseif ($hasOAuthAuth) {
        # Get OAuth token using Basic Auth with client ID/secret (per Jama docs)
        Write-Host "   Getting OAuth access token..." -ForegroundColor Gray
        $tokenUrl = "$jamaUrl/rest/oauth/token"
        
        # Use Basic Auth with client ID as username, client secret as password
        $credentials = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${jamaClientId}:${jamaClientSecret}"))
        $tokenHeaders = @{
            "Authorization" = "Basic $credentials"
            "Content-Type" = "application/x-www-form-urlencoded"
        }
        
        try {
            $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method POST -Headers $tokenHeaders -Body "grant_type=client_credentials" -TimeoutSec 15 -ErrorAction Stop
            $accessToken = $tokenResponse.access_token
            $headers["Authorization"] = "Bearer $accessToken"
            Write-Host "   ✅ OAuth token obtained successfully" -ForegroundColor Green
        } catch {
            Write-Host "   ❌ Failed to get OAuth token: $($_.Exception.Message)" -ForegroundColor Red
            if ($_.Exception.Response) {
                Write-Host "   Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
            }
            exit 1
        }
    } elseif ($hasBasicAuth) {
        $credentials = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${jamaUser}:${jamaPass}"))
        $headers["Authorization"] = "Basic $credentials"
    }
    
    $response = Invoke-RestMethod -Uri $testUrl -Headers $headers -Method GET -TimeoutSec 10 -ErrorAction Stop
    
    Write-Host "   ✅ Connection successful!" -ForegroundColor Green
    if ($response) {
        Write-Host "   Response: $($response | ConvertTo-Json -Compress)" -ForegroundColor Cyan
    }
    
} catch {
    Write-Host "   ❌ Connection failed: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        Write-Host "   Status Code: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
        Write-Host "   Reason: $($_.Exception.Response.ReasonPhrase)" -ForegroundColor Red
    }
    
    Write-Host "`nTroubleshooting Tips:" -ForegroundColor Yellow
    Write-Host "   • Verify your Jama URL is correct and accessible" -ForegroundColor White
    Write-Host "   • Check that your API token or credentials are valid" -ForegroundColor White
    Write-Host "   • Ensure your network allows access to the Jama instance" -ForegroundColor White
    Write-Host "   • Try accessing the URL in a web browser first" -ForegroundColor White
    exit 1
}

# Test projects endpoint
Write-Host "`n3. Testing Projects API..." -ForegroundColor Yellow

try {
    $projectsUrl = "$jamaUrl/rest/latest/projects"
    Write-Host "   Getting projects from: $projectsUrl" -ForegroundColor Gray
    
    $projectsResponse = Invoke-RestMethod -Uri $projectsUrl -Headers $headers -Method GET -TimeoutSec 15 -ErrorAction Stop
    
    if ($projectsResponse.data -and $projectsResponse.data.Count -gt 0) {
        Write-Host "   ✅ Found $($projectsResponse.data.Count) project(s):" -ForegroundColor Green
        
        $projectsResponse.data | ForEach-Object {
            Write-Host "      • $($_.name) (ID: $($_.id), Key: $($_.key))" -ForegroundColor White
        }
        
        # Save first project ID for requirements test
        $firstProjectId = $projectsResponse.data[0].id
        
    } else {
        Write-Host "   ⚠️  No projects found or empty response" -ForegroundColor Orange
        Write-Host "   This could mean:" -ForegroundColor Yellow
        Write-Host "     • Your user has no project access" -ForegroundColor White
        Write-Host "     • Projects exist but API format is different" -ForegroundColor White
        exit 1
    }
    
} catch {
    Write-Host "   ❌ Projects API failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test requirements endpoint (if we have a project)
if ($firstProjectId) {
    Write-Host "`n4. Testing Requirements API..." -ForegroundColor Yellow
    
    try {
        $reqUrl = "$jamaUrl/rest/latest/items?project=$firstProjectId"
        Write-Host "   Getting items from project $firstProjectId..." -ForegroundColor Gray
        
        $reqResponse = Invoke-RestMethod -Uri $reqUrl -Headers $headers -Method GET -TimeoutSec 20 -ErrorAction Stop
        
        if ($reqResponse.data) {
            $totalItems = $reqResponse.data.Count
            $requirements = $reqResponse.data | Where-Object { $_.itemType -eq 'requirement' -or $_.fields.name }
            
            Write-Host "   ✅ Found $totalItems total items, $($requirements.Count) look like requirements" -ForegroundColor Green
            
            if ($requirements.Count -gt 0) {
                Write-Host "   Sample requirements:" -ForegroundColor Cyan
                $requirements | Select-Object -First 3 | ForEach-Object {
                    $name = if ($_.fields.name) { $_.fields.name } elseif ($_.documentKey) { $_.documentKey } else { "ID-$($_.id)" }
                    Write-Host "      • $name" -ForegroundColor White
                }
            }
            
        } else {
            Write-Host "   ⚠️  No items found in project" -ForegroundColor Orange
        }
        
    } catch {
        Write-Host "   ❌ Requirements API failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   This might be normal if the project has no items or different item types" -ForegroundColor Yellow
    }
}

Write-Host "`n✅ Jama Connect API Test Complete!" -ForegroundColor Green
Write-Host "You can now integrate with Jama Connect using the JamaConnectService." -ForegroundColor White
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Update the ExportAllToJamaCommand to use JamaConnectService" -ForegroundColor White
Write-Host "2. Add direct requirement import from Jama (no file export needed)" -ForegroundColor White
Write-Host "3. Add test case push-back to Jama after generation" -ForegroundColor White