#!/usr/bin/env pwsh

# Direct Jama API Investigation - Get Raw Data to Solve Rich Content Issue
# Handles authentication edge cases and diagnoses 401 errors properly

Write-Host "=== Jama Rich Content Investigation ===" -ForegroundColor Cyan
Write-Host "Getting raw API data to solve the missing tables/supplemental info issue" -ForegroundColor White

# Check environment variables with validation
$jamaUrl = $env:JAMA_BASE_URL
$jamaClientId = $env:JAMA_CLIENT_ID  
$jamaClientSecret = $env:JAMA_CLIENT_SECRET

if ([string]::IsNullOrWhiteSpace($jamaUrl) -or
    [string]::IsNullOrWhiteSpace($jamaClientId) -or
    [string]::IsNullOrWhiteSpace($jamaClientSecret)) {
    Write-Host "ERROR: Missing required environment variables:" -ForegroundColor Red
    Write-Host "  JAMA_BASE_URL" -ForegroundColor Yellow
    Write-Host "  JAMA_CLIENT_ID" -ForegroundColor Yellow  
    Write-Host "  JAMA_CLIENT_SECRET" -ForegroundColor Yellow
    exit 1
}

# Normalize base URL
$jamaUrl = $jamaUrl.TrimEnd('/')

Write-Host "`nUsing configuration:" -ForegroundColor Yellow
Write-Host "Base URL: $jamaUrl" -ForegroundColor Gray
Write-Host "Client ID: $($jamaClientId.Substring(0,8))..." -ForegroundColor Gray
Write-Host "Token URL: $jamaUrl/rest/oauth/token" -ForegroundColor Gray
Write-Host "Projects URL: $jamaUrl/rest/v1/projects" -ForegroundColor Gray

# Enhanced error handling
function Show-WebError($_err) {
    try {
        $resp = $_err.Exception.Response
        if ($resp -and $resp.GetResponseStream()) {
            $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $text = $reader.ReadToEnd()
            Write-Host "HTTP Status: $([int]$resp.StatusCode) $($resp.StatusDescription)" -ForegroundColor Red
            Write-Host "Response Body: $text" -ForegroundColor DarkRed
        } else {
            Write-Host "Error: $($_err.Exception.Message)" -ForegroundColor Red
        }
    } catch {
        Write-Host "Error: $($_err.Exception.Message)" -ForegroundColor Red
    }
}

# Enhanced OAuth token function with proper error handling
function Get-JamaOAuthToken($BaseUrl, $ClientId, $ClientSecret) {
    $BaseUrl = $BaseUrl.TrimEnd('/')
    $tokenUrl = "$BaseUrl/rest/oauth/token"

    $authBytes = [System.Text.Encoding]::UTF8.GetBytes("$ClientId`:$ClientSecret")
    $authHeader = [Convert]::ToBase64String($authBytes)

    $headers = @{
        Authorization = "Basic $authHeader"
        "Content-Type" = "application/x-www-form-urlencoded"
        Accept = "application/json"
    }

    # Send body as string, try without scope first
    $body = "grant_type=client_credentials"

    try {
        Write-Host "  Trying OAuth client_credentials flow..." -ForegroundColor DarkGray
        $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body
        return $response.access_token
    } catch {
        Write-Host "  OAuth client_credentials failed:" -ForegroundColor Red
        Show-WebError $_
        
        # Try with scope
        Write-Host "  Trying with scope=read..." -ForegroundColor DarkGray
        try {
            $bodyWithScope = "grant_type=client_credentials&scope=read"
            $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $bodyWithScope
            return $response.access_token
        } catch {
            Write-Host "  OAuth with scope also failed:" -ForegroundColor Red
            Show-WebError $_
            throw "OAuth authentication not supported or credentials invalid"
        }
    }
}

# Alternative: Try Basic Auth directly (fallback if OAuth fails)
function Test-BasicAuth($BaseUrl, $ClientId, $ClientSecret) {
    $BaseUrl = $BaseUrl.TrimEnd('/')
    $authBytes = [System.Text.Encoding]::UTF8.GetBytes("$ClientId`:$ClientSecret")
    $authHeader = [Convert]::ToBase64String($authBytes)

    $headers = @{
        Authorization = "Basic $authHeader"
        Accept = "application/json"
    }

    try {
        Write-Host "  Trying Basic Auth directly..." -ForegroundColor DarkGray
        $response = Invoke-RestMethod -Uri "$BaseUrl/rest/v1/projects" -Headers $headers
        Write-Host "  Basic Auth successful!" -ForegroundColor Green
        return $headers
    } catch {
        Write-Host "  Basic Auth failed:" -ForegroundColor Red
        Show-WebError $_
        return $null
    }
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

# Function to get items with detailed analysis
function Get-JamaItemsWithAnalysis($BaseUrl, $AccessToken, $ProjectId) {
    $headers = @{
        'Authorization' = "Bearer $AccessToken"
        'Accept' = 'application/json'
    }
    
    Write-Host "Getting items from project $ProjectId..." -ForegroundColor Yellow
    $response = Invoke-RestMethod -Uri "$BaseUrl/rest/v1/items?project=$ProjectId" -Headers $headers
    return $response
}

# Function to get single item with full details
function Get-JamaItemDetails($BaseUrl, $AccessToken, $ItemId) {
    $headers = @{
        'Authorization' = "Bearer $AccessToken"
        'Accept' = 'application/json'
    }
    
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/rest/v1/items/$ItemId" -Headers $headers
        return $response
    }
    catch {
        Write-Host "Failed to get item $ItemId details: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

try {
    Write-Host "`nStep 1: Getting OAuth token..." -ForegroundColor Yellow
    $token = Get-JamaOAuthToken $jamaUrl $jamaClientId $jamaClientSecret
    Write-Host "Success - token obtained" -ForegroundColor Green
    
    Write-Host "`nStep 2: Getting projects..." -ForegroundColor Yellow
    $projects = Get-JamaProjects $jamaUrl $token
    Write-Host "Success - found $($projects.Count) projects" -ForegroundColor Green
    
    if ($projects.Count -eq 0) {
        Write-Host "No projects found" -ForegroundColor Red
        exit 1
    }
    
    # Find the project that was imported (or use first one)
    $targetProject = $projects[0]
    Write-Host "`nStep 3: Analyzing project: $($targetProject.fields.name) (ID: $($targetProject.id))" -ForegroundColor Cyan
    
    Write-Host "`nStep 4: Getting requirements from project..." -ForegroundColor Yellow
    $itemsResponse = Get-JamaItemsWithAnalysis $jamaUrl $token $targetProject.id
    
    # Save the complete response for analysis
    $responseFile = "jama_investigation_response_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
    $itemsResponse | ConvertTo-Json -Depth 10 | Out-File -FilePath $responseFile -Encoding UTF8
    Write-Host "Complete API response saved to: $responseFile" -ForegroundColor Green
    
    $items = $itemsResponse.data
    Write-Host "Found $($items.Count) total items" -ForegroundColor White
    
    Write-Host "`nStep 5: Analyzing descriptions for rich content..." -ForegroundColor Yellow
    
    $richContentAnalysis = @()
    $itemsWithHtml = 0
    $itemsWithTables = 0
    $itemsWithParagraphs = 0
    
    foreach ($item in $items | Select-Object -First 5) {
        $description = $item.description
        if ($description) {
            $hasHtml = $description -match '<[^>]+>'
            $hasTable = $description -match '<table'
            $hasParagraphs = $description -match '<p>'
            
            if ($hasHtml) { $itemsWithHtml++ }
            if ($hasTable) { $itemsWithTables++ }
            if ($hasParagraphs) { $itemsWithParagraphs++ }
            
            $analysis = [PSCustomObject]@{
                ItemId = $item.id
                Name = $item.name
                HasHTML = $hasHtml
                HasTables = $hasTable
                HasParagraphs = $hasParagraphs
                DescriptionLength = $description.Length
                DescriptionPreview = if ($description.Length -gt 200) { $description.Substring(0, 200) + "..." } else { $description }
            }
            $richContentAnalysis += $analysis
            
            Write-Host "`nItem $($item.id): $($item.name)" -ForegroundColor White
            Write-Host "  HTML: $hasHtml | Tables: $hasTable | Paragraphs: $hasParagraphs | Length: $($description.Length)" -ForegroundColor Gray
            
            if ($hasHtml) {
                Write-Host "  Preview: $($analysis.DescriptionPreview)" -ForegroundColor DarkCyan
            }
        }
        
        # Also try to get detailed item info
        Write-Host "  Getting detailed item info..." -ForegroundColor DarkGray
        $itemDetails = Get-JamaItemDetails $jamaUrl $token $item.id
        if ($itemDetails) {
            $detailsFile = "jama_item_details_$($item.id).json"
            $itemDetails | ConvertTo-Json -Depth 10 | Out-File -FilePath $detailsFile -Encoding UTF8
            Write-Host "  Item details saved to: $detailsFile" -ForegroundColor DarkGreen
        }
    }
    
    # Save analysis summary
    $analysisFile = "jama_rich_content_analysis_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
    $richContentAnalysis | ConvertTo-Json -Depth 5 | Out-File -FilePath $analysisFile -Encoding UTF8
    
    Write-Host "`n=== RICH CONTENT ANALYSIS RESULTS ===" -ForegroundColor Cyan
    Write-Host "Items with HTML: $itemsWithHtml / $($items.Count)" -ForegroundColor $(if($itemsWithHtml -gt 0){"Green"}else{"Red"})
    Write-Host "Items with Tables: $itemsWithTables / $($items.Count)" -ForegroundColor $(if($itemsWithTables -gt 0){"Green"}else{"Red"})
    Write-Host "Items with Paragraphs: $itemsWithParagraphs / $($items.Count)" -ForegroundColor $(if($itemsWithParagraphs -gt 0){"Green"}else{"Red"})
    
    Write-Host "`nFiles created for analysis:" -ForegroundColor Yellow
    Write-Host "  $responseFile - Complete API response" -ForegroundColor Gray
    Write-Host "  $analysisFile - Rich content analysis" -ForegroundColor Gray
    Get-ChildItem -Filter "jama_item_details_*.json" | ForEach-Object {
        Write-Host "  $($_.Name) - Individual item details" -ForegroundColor Gray
    }
    
    Write-Host "`n=== NEXT STEPS ===" -ForegroundColor Yellow
    if ($itemsWithHtml -gt 0) {
        Write-Host "SUCCESS: Found HTML content in descriptions!" -ForegroundColor Green
        Write-Host "Solution: Enhance JamaConnectService.ConvertToRequirements() to parse HTML" -ForegroundColor White
        Write-Host "- Extract <table> elements into LooseContent.Tables" -ForegroundColor White
        Write-Host "- Extract <p> elements into LooseContent.Paragraphs" -ForegroundColor White
    } else {
        Write-Host "No HTML found in descriptions - check if rich content is in other fields" -ForegroundColor Yellow
        Write-Host "Next: Examine individual item detail files for additional fields" -ForegroundColor White
    }
    
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Full error:" -ForegroundColor Yellow
    Write-Host $_.Exception.ToString() -ForegroundColor DarkRed
}