#!/usr/bin/env pwsh

# Direct Jama API Test - Examine Raw API Responses for Rich Content
# This script directly calls the Jama API to examine what's returned

Write-Host "=== Direct Jama API Rich Content Test ===" -ForegroundColor Cyan

# Get credentials from environment
$jamaUrl = $env:JAMA_BASE_URL
$jamaClientId = $env:JAMA_CLIENT_ID  
$jamaClientSecret = $env:JAMA_CLIENT_SECRET

if (-not $jamaUrl -or -not $jamaClientId -or -not $jamaClientSecret) {
    Write-Host "ERROR: Missing Jama credentials" -ForegroundColor Red
    Write-Host "Set these environment variables:" -ForegroundColor Yellow
    Write-Host "  JAMA_BASE_URL" -ForegroundColor Gray
    Write-Host "  JAMA_CLIENT_ID" -ForegroundColor Gray  
    Write-Host "  JAMA_CLIENT_SECRET" -ForegroundColor Gray
    exit 1
}

Write-Host "Found Jama credentials" -ForegroundColor Green
Write-Host "Base URL: $jamaUrl" -ForegroundColor Gray

# Function to get OAuth token
function Get-JamaOAuthToken($BaseUrl, $ClientId, $ClientSecret) {
    $tokenUrl = "$BaseUrl/rest/oauth/token"
    $authBytes = [System.Text.Encoding]::UTF8.GetBytes("${ClientId}:${ClientSecret}")
    $authHeader = [Convert]::ToBase64String($authBytes)
    
    $headers = @{
        'Authorization' = "Basic $authHeader"
        'Content-Type' = 'application/x-www-form-urlencoded'
    }
    
    $body = @{
        'grant_type' = 'client_credentials'
        'scope' = 'read'
    }
    
    $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body
    return $response.access_token
}

# Function to get projects
function Get-JamaProjects($BaseUrl, $AccessToken) {
    $headers = @{
        'Authorization' = "Bearer $AccessToken"
        'Accept' = 'application/json'
    }
    
    $response = Invoke-RestMethod -Uri "$BaseUrl/rest/v1/projects" -Headers $headers
    return $response.data
}

# Function to get items from a project
function Get-JamaItems($BaseUrl, $AccessToken, $ProjectId) {
    $headers = @{
        'Authorization' = "Bearer $AccessToken"
        'Accept' = 'application/json'
    }
    
    $response = Invoke-RestMethod -Uri "$BaseUrl/rest/v1/items?project=$ProjectId" -Headers $headers
    return $response
}

try {
    Write-Host "`nGetting OAuth token..." -ForegroundColor Yellow
    $token = Get-JamaOAuthToken $jamaUrl $jamaClientId $jamaClientSecret
    Write-Host "OAuth token obtained" -ForegroundColor Green
    
    Write-Host "`nGetting projects..." -ForegroundColor Yellow
    $projects = Get-JamaProjects $jamaUrl $token
    Write-Host "Found $($projects.Count) projects" -ForegroundColor Green
    
    if ($projects.Count -gt 0) {
        $project = $projects[0]
        Write-Host "Testing project: $($project.fields.name) (ID: $($project.id))" -ForegroundColor Cyan
        
        Write-Host "`nGetting items from project..." -ForegroundColor Yellow
        $items = Get-JamaItems $jamaUrl $token $project.id
        
        # Save raw API response to file
        $outputFile = "jama_direct_api_response_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
        $items | ConvertTo-Json -Depth 10 | Out-File -FilePath $outputFile -Encoding UTF8
        Write-Host "Raw API response saved to: $outputFile" -ForegroundColor Green
        
        Write-Host "`n=== RICH CONTENT ANALYSIS ===" -ForegroundColor Cyan
        
        $allItems = $items.data
        Write-Host "Total items: $($allItems.Count)" -ForegroundColor White
        
        $itemsWithHtml = 0
        $itemsWithTables = 0
        $itemsWithParagraphs = 0
        
        foreach ($item in $allItems | Select-Object -First 10) {
            $description = $item.description
            if ($description) {
                $hasHtml = $description -match '<[^>]+>'
                $hasTable = $description -match '<table'
                $hasParagraphs = $description -match '<p>'
                
                if ($hasHtml) { $itemsWithHtml++ }
                if ($hasTable) { $itemsWithTables++ }
                if ($hasParagraphs) { $itemsWithParagraphs++ }
                
                Write-Host "`nItem $($item.id):" -ForegroundColor Yellow
                Write-Host "  Name: $($item.name)" -ForegroundColor Gray
                Write-Host "  HTML: $hasHtml, Tables: $hasTable, Paragraphs: $hasParagraphs" -ForegroundColor $(if($hasHtml){"Green"}else{"Red"})
                
                if ($hasHtml -and $description.Length -lt 1000) {
                    Write-Host "  Description preview:" -ForegroundColor Gray
                    Write-Host "    $($description.Substring(0, [Math]::Min(200, $description.Length)))..." -ForegroundColor DarkGray
                }
            }
        }
        
        Write-Host "`n=== SUMMARY ===" -ForegroundColor Cyan
        Write-Host "Items with HTML: $itemsWithHtml" -ForegroundColor $(if($itemsWithHtml -gt 0){"Green"}else{"Red"})
        Write-Host "Items with Tables: $itemsWithTables" -ForegroundColor $(if($itemsWithTables -gt 0){"Green"}else{"Red"}) 
        Write-Host "Items with Paragraphs: $itemsWithParagraphs" -ForegroundColor $(if($itemsWithParagraphs -gt 0){"Green"}else{"Red"})
        
        Write-Host "`nCheck the saved file for complete API structure:" -ForegroundColor Yellow
        Write-Host "code `"$outputFile`"" -ForegroundColor Gray
        
    } else {
        Write-Host "No projects found" -ForegroundColor Red
    }
    
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Full error:" -ForegroundColor Yellow
    Write-Host $_.Exception.ToString() -ForegroundColor DarkRed
}