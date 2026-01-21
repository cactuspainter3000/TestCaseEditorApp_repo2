# Enhanced Jama Rich Content Investigation Script
# Addresses OAuth authentication issues and provides comprehensive error diagnostics

param(
    [Parameter(Mandatory=$false)]
    [string]$baseUrl = "https://jama02.rockwellcollins.com/contour",
    
    [Parameter(Mandatory=$false)]
    [string]$clientId = "5f7c6b3d-8e4a-4f2c-9d1e-3a7b8c9e0f2d",
    
    [Parameter(Mandatory=$false)]
    [string]$clientSecret = "7e8f9a1b-2c3d-4e5f-6a7b-8c9d0e1f2a3b"
)

# Enhanced error handling function
function Show-WebError($_err) {
    try {
        if ($null -ne $_err.Exception.Response) {
            $resp = $_err.Exception.Response
            $stream = $resp.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $text = $reader.ReadToEnd()
            $reader.Close()
            $stream.Close()
            
            Write-Host "HTTP Status: $([int]$resp.StatusCode) $($resp.StatusDescription)" -ForegroundColor Red
            Write-Host "Response Body: $text" -ForegroundColor DarkRed
            
            # Look for specific OAuth errors
            if ($text -match '"error":\s*"([^"]+)"') {
                Write-Host "OAuth Error: $($matches[1])" -ForegroundColor Red
            }
            if ($text -match '"error_description":\s*"([^"]+)"') {
                Write-Host "Error Description: $($matches[1])" -ForegroundColor Red
            }
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

# Main execution with enhanced error handling
try {
    Write-Host "Investigating Jama API rich content..." -ForegroundColor Cyan
    Write-Host ""
    
    # First, normalize the base URL
    $NormalizedBaseUrl = $baseUrl.TrimEnd('/').TrimEnd('/contour')
    if (-not $NormalizedBaseUrl.EndsWith('/contour')) {
        $NormalizedBaseUrl += '/contour'
    }
    
    Write-Host "Normalized Base URL: $NormalizedBaseUrl" -ForegroundColor Yellow
    
    # Try OAuth first
    $token = $null
    try {
        $token = Get-JamaOAuthToken -BaseUrl $NormalizedBaseUrl -ClientId $clientId -ClientSecret $clientSecret
        $authHeaders = @{
            Authorization = "Bearer $token"
            Accept = "application/json"
        }
        Write-Host "OAuth authentication successful!" -ForegroundColor Green
    } catch {
        Write-Host "OAuth failed, trying Basic Auth..." -ForegroundColor Yellow
        $authHeaders = Test-BasicAuth -BaseUrl $NormalizedBaseUrl -ClientId $clientId -ClientSecret $clientSecret
        if ($null -eq $authHeaders) {
            Write-Host ""
            Write-Host "ALL AUTHENTICATION METHODS FAILED" -ForegroundColor Red
            Write-Host ""
            Write-Host "Potential Issues:" -ForegroundColor Yellow
            Write-Host "1. Wrong credentials (client ID/secret)" -ForegroundColor Gray
            Write-Host "2. Jama instance doesn't support OAuth client_credentials" -ForegroundColor Gray
            Write-Host "3. Need username/password instead of client ID/secret" -ForegroundColor Gray
            Write-Host "4. Need API token authentication" -ForegroundColor Gray
            Write-Host "5. Wrong base URL or missing context path" -ForegroundColor Gray
            Write-Host ""
            Write-Host "Suggested fixes:" -ForegroundColor Yellow
            Write-Host "- Check if your Jama supports OAuth or requires username/password" -ForegroundColor Gray
            Write-Host "- Try API token auth instead: https://yourjama.com/rest/v1/items?token=YOUR_TOKEN" -ForegroundColor Gray
            Write-Host "- Verify base URL includes correct context path (/contour, etc.)" -ForegroundColor Gray
            return
        }
    }
    
    # Get projects to verify auth
    $projectsUrl = "$NormalizedBaseUrl/rest/v1/projects"
    Write-Host "Getting projects from: $projectsUrl" -ForegroundColor Blue
    
    try {
        $projects = Invoke-RestMethod -Uri $projectsUrl -Headers $authHeaders
        Write-Host "Found $($projects.meta.pageInfo.totalResults) projects" -ForegroundColor Green
        
        if ($projects.data.Count -gt 0) {
            $firstProject = $projects.data[0]
            Write-Host "Using project: '$($firstProject.fields.name)' (ID: $($firstProject.id))" -ForegroundColor Cyan
        
            # Get items from first project - try multiple endpoints
            $projectId = $firstProject.id
            $endpoints = @(
                "/rest/v1/items?project=$projectId&maxResults=5",
                "/rest/v1/projects/$projectId/items?maxResults=5",
                "/rest/v1/abstractitems?project=$projectId&maxResults=5"
            )
            
            $items = $null
            foreach ($endpoint in $endpoints) {
                $itemsUrl = "$NormalizedBaseUrl$endpoint"
                Write-Host "Trying endpoint: $endpoint" -ForegroundColor DarkGray
                
                try {
                    $items = Invoke-RestMethod -Uri $itemsUrl -Headers $authHeaders
                    Write-Host "Endpoint works! Found $($items.meta.pageInfo.totalResults) items" -ForegroundColor Green
                    break
                } catch {
                    Write-Host "Endpoint failed:" -ForegroundColor Red
                    Show-WebError $_
                }
            }
            
            if ($null -eq $items -or $items.data.Count -eq 0) {
                Write-Host "No items found in project" -ForegroundColor Yellow
                return
            }

            # Save raw API response for analysis
            $responseFile = "jama_api_investigation_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
            $items | ConvertTo-Json -Depth 10 | Out-File -FilePath $responseFile -Encoding UTF8
            Write-Host "Raw API response saved to: $responseFile" -ForegroundColor Green

            # Analyze first few items for rich content
            Write-Host ""
            Write-Host "Analyzing rich content in first few items..." -ForegroundColor Cyan
            
            $richContentFound = $false
            $htmlItemsCount = 0
            $tableItemsCount = 0
            
            for ($i = 0; $i -lt [Math]::Min($items.data.Count, 3); $i++) {
                $item = $items.data[$i]
                
                Write-Host ""
                Write-Host "--- ITEM $($i + 1): $($item.fields.name) ---" -ForegroundColor Yellow
                Write-Host "ID: $($item.id)" -ForegroundColor Gray
                Write-Host "Type: $($item.fields.itemType)" -ForegroundColor Gray
                
                # Check description for HTML content
                if ($item.fields.description) {
                    $desc = $item.fields.description
                    Write-Host "Description (first 200 chars): $($desc.Substring(0, [Math]::Min($desc.Length, 200)))" -ForegroundColor Gray
                    
                    if ($desc -match '&lt;[^&gt;]+&gt;') {
                        Write-Host "CONTAINS HTML TAGS - Rich content found!" -ForegroundColor Green
                        $richContentFound = $true
                        $htmlItemsCount++
                        
                        # Look for tables specifically
                        if ($desc -match '&lt;table[^&gt;]*&gt;') {
                            Write-Host "CONTAINS TABLE TAGS - Tables found!" -ForegroundColor Green
                            $tableItemsCount++
                        }
                        
                        # Look for other rich elements
                        if ($desc -match '&lt;p[^&gt;]*&gt;') {
                            Write-Host "CONTAINS PARAGRAPH TAGS" -ForegroundColor Green
                        }
                        if ($desc -match '&lt;ul[^&gt;]*&gt;|&lt;ol[^&gt;]*&gt;|&lt;li[^&gt;]*&gt;') {
                            Write-Host "CONTAINS LIST TAGS" -ForegroundColor Green
                        }
                        if ($desc -match '&lt;img[^&gt;]*&gt;') {
                            Write-Host "CONTAINS IMAGE TAGS" -ForegroundColor Green
                        }
                    } else {
                        Write-Host "Plain text only - no HTML tags" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "No description field" -ForegroundColor Gray
                }
                
                # Check all fields for rich content
                Write-Host "All fields:" -ForegroundColor Gray
                $item.fields.PSObject.Properties | ForEach-Object {
                    $fieldName = $_.Name
                    $fieldValue = $_.Value
                    
                    if ($fieldValue -is [string] -and $fieldValue.Length -gt 0) {
                        $hasHtml = $fieldValue -match '&lt;[^&gt;]+&gt;'
                        $fieldPreview = if ($fieldValue.Length -gt 50) { $fieldValue.Substring(0, 50) + "..." } else { $fieldValue }
                        $htmlIndicator = if ($hasHtml) { " [HTML]" } else { "" }
                        Write-Host "  $fieldName`: $fieldPreview$htmlIndicator" -ForegroundColor DarkGray
                    }
                }
            }
            
            Write-Host ""
            Write-Host "RICH CONTENT SUMMARY:" -ForegroundColor Cyan
            Write-Host "Items analyzed: $([Math]::Min($items.data.Count, 3))" -ForegroundColor White
            Write-Host "Items with HTML: $htmlItemsCount" -ForegroundColor $(if($htmlItemsCount -gt 0){"Green"}else{"Yellow"})
            Write-Host "Items with Tables: $tableItemsCount" -ForegroundColor $(if($tableItemsCount -gt 0){"Green"}else{"Yellow"})
            
            if ($richContentFound) {
                Write-Host ""
                Write-Host "SUCCESS: Rich content (HTML) found in Jama API responses!" -ForegroundColor Green
                Write-Host ""
                Write-Host "SOLUTION PATH:" -ForegroundColor Cyan
                Write-Host "1. Enhance JamaConnectService.ConvertToRequirements() method" -ForegroundColor White
                Write-Host "2. Parse HTML in description fields using HtmlAgilityPack or similar" -ForegroundColor White
                Write-Host "3. Extract table elements to LooseContent.Tables" -ForegroundColor White
                Write-Host "4. Extract paragraph elements to LooseContent.Paragraphs" -ForegroundColor White
                Write-Host "5. Extract other rich elements as needed" -ForegroundColor White
                Write-Host ""
                Write-Host "Next Step: Implement HTML parsing in JamaConnectService" -ForegroundColor Yellow
            } else {
                Write-Host ""
                Write-Host "No HTML content found in analyzed items" -ForegroundColor Yellow
                Write-Host "- Check the saved API response file: $responseFile" -ForegroundColor Gray
                Write-Host "- Rich content might be in other fields or different item types" -ForegroundColor Gray
                Write-Host "- Consider checking requirements vs other item types" -ForegroundColor Gray
            }
            
        } else {
            Write-Host "No projects found" -ForegroundColor Red
        }
        
    } catch {
        Write-Host "Failed to get projects:" -ForegroundColor Red
        Show-WebError $_
    }
    
} catch {
    Write-Host "Script failed:" -ForegroundColor Red
    Show-WebError $_
}

Write-Host ""
Write-Host "Investigation complete!" -ForegroundColor Cyan