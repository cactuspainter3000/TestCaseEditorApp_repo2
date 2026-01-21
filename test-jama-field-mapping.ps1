#!/usr/bin/env pwsh

# Test Jama Connect field mapping directly
# This script simulates what the JamaConnectService does to see raw API responses

Write-Host "Testing Jama Connect Field Mapping" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan

# Get Jama credentials from environment variables
$jamaBaseUrl = $env:JAMA_BASE_URL
$jamaClientId = $env:JAMA_CLIENT_ID  
$jamaClientSecret = $env:JAMA_CLIENT_SECRET

if (-not $jamaBaseUrl -or -not $jamaClientId -or -not $jamaClientSecret) {
    Write-Host "ERROR: Missing Jama credentials. Please set environment variables:" -ForegroundColor Red
    Write-Host "  JAMA_BASE_URL" -ForegroundColor Yellow
    Write-Host "  JAMA_CLIENT_ID" -ForegroundColor Yellow
    Write-Host "  JAMA_CLIENT_SECRET" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Current values:" -ForegroundColor Yellow
    Write-Host "  JAMA_BASE_URL: $jamaBaseUrl" -ForegroundColor Gray
    Write-Host "  JAMA_CLIENT_ID: $jamaClientId" -ForegroundColor Gray
    Write-Host "  JAMA_CLIENT_SECRET: $(if($jamaClientSecret) { '***' } else { '(not set)' })" -ForegroundColor Gray
    exit 1
}

Write-Host "Found Jama credentials" -ForegroundColor Green
Write-Host "Base URL: $jamaBaseUrl" -ForegroundColor Gray

# Function to get OAuth token
function Get-JamaOAuthToken {
    param($BaseUrl, $ClientId, $ClientSecret)
    
    $tokenUrl = "$BaseUrl/rest/oauth/token"
    $authBytes = [System.Text.Encoding]::UTF8.GetBytes("${ClientId}:${ClientSecret}")
    $authHeader = [Convert]::ToBase64String($authBytes)
    
    $headers = @{
        'Authorization' = "Basic $authHeader"
        'Content-Type' = 'application/x-www-form-urlencoded'
    }
    
    $body = @{
        'grant_type' = 'client_credentials'
        'scope' = 'token_information'
    }
    
    try {
        Write-Host "Getting OAuth token..." -ForegroundColor Yellow
        $response = Invoke-RestMethod -Uri $tokenUrl -Method POST -Headers $headers -Body $body
        Write-Host "OAuth token obtained successfully" -ForegroundColor Green
        return $response.access_token
    }
    catch {
        Write-Host "ERROR: Failed to get OAuth token: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Function to get projects
function Get-JamaProjects {
    param($BaseUrl, $AccessToken)
    
    $projectsUrl = "$BaseUrl/rest/v1/projects"
    $headers = @{
        'Authorization' = "Bearer $AccessToken"
        'Content-Type' = 'application/json'
    }
    
    try {
        Write-Host "Getting projects..." -ForegroundColor Yellow
        $response = Invoke-RestMethod -Uri $projectsUrl -Method GET -Headers $headers
        Write-Host "Found $($response.data.Count) projects" -ForegroundColor Green
        return $response.data
    }
    catch {
        Write-Host "ERROR: Failed to get projects: $($_.Exception.Message)" -ForegroundColor Red
        return @()
    }
}

# Function to get items from project
function Get-JamaItems {
    param($BaseUrl, $AccessToken, $ProjectId)
    
    $itemsUrl = "$BaseUrl/rest/v1/items?project=$ProjectId"
    $headers = @{
        'Authorization' = "Bearer $AccessToken"
        'Content-Type' = 'application/json'
    }
    
    try {
        Write-Host "Getting items from project $ProjectId..." -ForegroundColor Yellow
        $response = Invoke-RestMethod -Uri $itemsUrl -Method GET -Headers $headers
        Write-Host "Found $($response.data.Count) items" -ForegroundColor Green
        return $response.data
    }
    catch {
        Write-Host "ERROR: Failed to get items: $($_.Exception.Message)" -ForegroundColor Red
        return @()
    }
}

# Main test execution
try {
    # Get OAuth token
    $accessToken = Get-JamaOAuthToken -BaseUrl $jamaBaseUrl -ClientId $jamaClientId -ClientSecret $jamaClientSecret
    if (-not $accessToken) {
        exit 1
    }
    
    # Get projects
    $projects = Get-JamaProjects -BaseUrl $jamaBaseUrl -AccessToken $accessToken
    if ($projects.Count -eq 0) {
        Write-Host "No projects found" -ForegroundColor Red
        exit 1
    }
    
    # Find Decagon project
    $decagonProject = $projects | Where-Object { $_.fields.name -like "*Decagon*" -or $_.fields.name -like "*DECAGON*" }
    if (-not $decagonProject) {
        Write-Host "Could not find Decagon project. Available projects:" -ForegroundColor Red
        $projects | Select-Object -First 5 | ForEach-Object { 
            Write-Host "  - $($_.fields.name) (ID: $($_.id))" -ForegroundColor Gray 
        }
        exit 1
    }
    
    Write-Host "Found Decagon project: $($decagonProject.fields.name) (ID: $($decagonProject.id))" -ForegroundColor Green
    
    # Get items from Decagon project
    $items = Get-JamaItems -BaseUrl $jamaBaseUrl -AccessToken $accessToken -ProjectId $decagonProject.id
    if ($items.Count -eq 0) {
        Write-Host "No items found in Decagon project" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "RAW API RESPONSE ANALYSIS" -ForegroundColor Cyan
    Write-Host "=========================" -ForegroundColor Cyan
    Write-Host "Analyzing first 3 items to see field structure:" -ForegroundColor Yellow
    
    for ($i = 0; $i -lt [Math]::Min(3, $items.Count); $i++) {
        $item = $items[$i]
        Write-Host ""
        Write-Host "Item $($i + 1):" -ForegroundColor White
        Write-Host "  Raw ID: $($item.id)" -ForegroundColor Gray
        Write-Host "  Document Key: $($item.documentKey)" -ForegroundColor Gray
        Write-Host "  Global ID: $($item.globalId)" -ForegroundColor Gray
        Write-Host "  Item Type: $($item.itemType)" -ForegroundColor Gray
        
        Write-Host "  Fields object exists: $($item.fields -ne $null)" -ForegroundColor $(if($item.fields) {'Green'} else {'Red'})
        
        if ($item.fields) {
            $fieldNames = $item.fields | Get-Member -MemberType NoteProperty | Select-Object -ExpandProperty Name
            Write-Host "  Fields available: $($fieldNames -join ', ')" -ForegroundColor Gray
            
            if ($item.fields.name) {
                Write-Host "  Fields.Name: '$($item.fields.name)'" -ForegroundColor Green
            } else {
                Write-Host "  Fields.Name: (empty/null)" -ForegroundColor Red
            }
            
            if ($item.fields.description) {
                Write-Host "  Fields.Description: '$($item.fields.description)'" -ForegroundColor Green
            } else {
                Write-Host "  Fields.Description: (empty/null)" -ForegroundColor Red
            }
        }
        
        # Check for direct properties
        if ($item.name) {
            Write-Host "  Direct Name: '$($item.name)'" -ForegroundColor Green
        } else {
            Write-Host "  Direct Name: (empty/null)" -ForegroundColor Red
        }
        
        if ($item.description) {
            Write-Host "  Direct Description: '$($item.description)'" -ForegroundColor Green
        } else {
            Write-Host "  Direct Description: (empty/null)" -ForegroundColor Red
        }
    }
    
    Write-Host ""
    Write-Host "Field mapping test completed successfully!" -ForegroundColor Green

} catch {
    Write-Host "ERROR: Test failed with exception: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}