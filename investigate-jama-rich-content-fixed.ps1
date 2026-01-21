# Fixed Jama Rich Content Investigation Script
# Uses exact OAuth implementation from JamaConnectService

param(
    [Parameter(Mandatory=$false)]
    [string]$baseUrl = $env:JAMA_BASE_URL,
    
    [Parameter(Mandatory=$false)]
    [string]$clientId = $env:JAMA_CLIENT_ID,
    
    [Parameter(Mandatory=$false)]
    [string]$clientSecret = $env:JAMA_CLIENT_SECRET
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
        } else {
            Write-Host "Error: $($_err.Exception.Message)" -ForegroundColor Red
        }
    } catch {
        Write-Host "Error: $($_err.Exception.Message)" -ForegroundColor Red
    }
}

# Fixed OAuth token function - matches JamaConnectService exactly
function Get-JamaOAuthTokenFixed($BaseUrl, $ClientId, $ClientSecret) {
    $BaseUrl = $BaseUrl.TrimEnd('/')
    $tokenUrl = "$BaseUrl/rest/oauth/token"
    
    Write-Host "  Getting OAuth token from: $tokenUrl" -ForegroundColor DarkGray

    $authBytes = [System.Text.Encoding]::UTF8.GetBytes("$ClientId`:$ClientSecret")
    $authHeader = [Convert]::ToBase64String($authBytes)

    # Create form data exactly like JamaConnectService
    $formData = @{
        grant_type = "client_credentials"
        scope = "token_information"
    }

    try {
        $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers @{
            Authorization = "Basic $authHeader"
            "Content-Type" = "application/x-www-form-urlencoded"
        } -Body $formData
        
        Write-Host "  OAuth successful! Token obtained." -ForegroundColor Green
        return $response.access_token
    } catch {
        Write-Host "  OAuth failed:" -ForegroundColor Red
        Show-WebError $_
        return $null
    }
}

# Main execution
try {
    Write-Host "Investigating Jama API rich content using EXACT app authentication..." -ForegroundColor Cyan
    Write-Host ""
    
    if ([string]::IsNullOrEmpty($baseUrl) -or [string]::IsNullOrEmpty($clientId) -or [string]::IsNullOrEmpty($clientSecret)) {
        Write-Host "Missing required parameters. Using environment variables:" -ForegroundColor Yellow
        Write-Host "JAMA_BASE_URL: $(if($env:JAMA_BASE_URL) {$env:JAMA_BASE_URL} else {'NOT SET'})" -ForegroundColor Gray
        Write-Host "JAMA_CLIENT_ID: $(if($env:JAMA_CLIENT_ID) {$env:JAMA_CLIENT_ID} else {'NOT SET'})" -ForegroundColor Gray  
        Write-Host "JAMA_CLIENT_SECRET: $(if($env:JAMA_CLIENT_SECRET) {'SET'} else {'NOT SET'})" -ForegroundColor Gray
        
        if ([string]::IsNullOrEmpty($env:JAMA_BASE_URL)) {
            Write-Host "ERROR: JAMA_BASE_URL environment variable not set!" -ForegroundColor Red
            return
        }
        
        $baseUrl = $env:JAMA_BASE_URL
        $clientId = $env:JAMA_CLIENT_ID
        $clientSecret = $env:JAMA_CLIENT_SECRET
    }
    
    Write-Host "Using Base URL: $baseUrl" -ForegroundColor Yellow
    
    # Try OAuth with exact app implementation
    $token = Get-JamaOAuthTokenFixed -BaseUrl $baseUrl -ClientId $clientId -ClientSecret $clientSecret
    
    if ($null -eq $token) {
        Write-Host "AUTHENTICATION FAILED - Cannot proceed with investigation" -ForegroundColor Red
        return
    }
    
    $authHeaders = @{
        Authorization = "Bearer $token"
        Accept = "application/json"
    }
    
    # Get projects to verify auth
    $projectsUrl = "$baseUrl/rest/v1/projects"
    Write-Host "Getting projects from: $projectsUrl" -ForegroundColor Blue
    
    try {
        $projects = Invoke-RestMethod -Uri $projectsUrl -Headers $authHeaders
        Write-Host "SUCCESS! Found $($projects.meta.pageInfo.totalResults) projects" -ForegroundColor Green
        
        if ($projects.data.Count -gt 0) {
            $firstProject = $projects.data[0]
            Write-Host "Using project: '$($firstProject.fields.name)' (ID: $($firstProject.id))" -ForegroundColor Cyan
        
            # Get items from first project
            $projectId = $firstProject.id
            $itemsUrl = "$baseUrl/rest/v1/items?project=$projectId&maxResults=10"
            Write-Host "Getting items from: $itemsUrl" -ForegroundColor Blue
            
            $items = Invoke-RestMethod -Uri $itemsUrl -Headers $authHeaders
            Write-Host "Found $($items.meta.pageInfo.totalResults) items" -ForegroundColor Green

            # Save raw API response for analysis
            $responseFile = "jama_api_success_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
            $items | ConvertTo-Json -Depth 10 | Out-File -FilePath $responseFile -Encoding UTF8
            Write-Host "Raw API response saved to: $responseFile" -ForegroundColor Green

            # Analyze first few items for rich content
            Write-Host ""
            Write-Host "=== ANALYZING RICH CONTENT ===" -ForegroundColor Cyan
            
            $richContentFound = $false
            $htmlItemsCount = 0
            $tableItemsCount = 0
            
            for ($i = 0; $i -lt [Math]::Min($items.data.Count, 5); $i++) {
                $item = $items.data[$i]
                
                Write-Host ""
                Write-Host "--- ITEM $($i + 1): $($item.fields.name) ---" -ForegroundColor Yellow
                Write-Host "ID: $($item.id)" -ForegroundColor Gray
                Write-Host "Type: $($item.fields.itemType)" -ForegroundColor Gray
                
                # Check description for HTML content
                if ($item.fields.description) {
                    $desc = $item.fields.description
                    $descLength = $desc.Length
                    $descPreview = if ($descLength -gt 200) { $desc.Substring(0, 200) + "..." } else { $desc }
                    
                    Write-Host "Description ($descLength chars): $descPreview" -ForegroundColor Gray
                    
                    # Look for HTML tags (escape angle brackets for PowerShell)
                    $htmlPattern = '<[^>]+>'
                    if ($desc -match $htmlPattern) {
                        Write-Host "*** CONTAINS HTML TAGS - Rich content found! ***" -ForegroundColor Green
                        $richContentFound = $true
                        $htmlItemsCount++
                        
                        # Look for specific elements
                        if ($desc -match '<table[^>]*>') {
                            Write-Host "*** CONTAINS TABLE TAGS ***" -ForegroundColor Green
                            $tableItemsCount++
                            
                            # Extract table content for analysis
                            if ($desc -match '(<table[^>]*>.*?</table>)') {
                                $tableContent = $matches[1]
                                Write-Host "Table content preview: $($tableContent.Substring(0, [Math]::Min(200, $tableContent.Length)))" -ForegroundColor DarkCyan
                            }
                        }
                        
                        # Look for other rich elements
                        if ($desc -match '<p[^>]*>') {
                            Write-Host "*** CONTAINS PARAGRAPH TAGS ***" -ForegroundColor Green
                        }
                        if ($desc -match '<ul[^>]*>|<ol[^>]*>|<li[^>]*>') {
                            Write-Host "*** CONTAINS LIST TAGS ***" -ForegroundColor Green
                        }
                        if ($desc -match '<img[^>]*>') {
                            Write-Host "*** CONTAINS IMAGE TAGS ***" -ForegroundColor Green
                        }
                        
                        # Show HTML structure
                        $htmlTags = [regex]::Matches($desc, '<[^>]+>') | ForEach-Object { $_.Value } | Sort-Object -Unique
                        Write-Host "HTML tags found: $($htmlTags -join ', ')" -ForegroundColor DarkCyan
                        
                    } else {
                        Write-Host "Plain text only - no HTML tags" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "No description field" -ForegroundColor Gray
                }
                
                # Check all fields for rich content
                Write-Host "All fields with content:" -ForegroundColor Gray
                $item.fields.PSObject.Properties | Where-Object { 
                    $_.Value -is [string] -and $_.Value.Length -gt 0 
                } | ForEach-Object {
                    $fieldName = $_.Name
                    $fieldValue = $_.Value
                    
                    $hasHtml = $fieldValue -match '<[^>]+>'
                    $fieldPreview = if ($fieldValue.Length -gt 60) { $fieldValue.Substring(0, 60) + "..." } else { $fieldValue }
                    $htmlIndicator = if ($hasHtml) { " [HTML]" } else { "" }
                    Write-Host "  $fieldName`: $fieldPreview$htmlIndicator" -ForegroundColor DarkGray
                }
            }
            
            Write-Host ""
            Write-Host "=== RICH CONTENT SUMMARY ===" -ForegroundColor Cyan
            Write-Host "Items analyzed: $([Math]::Min($items.data.Count, 5))" -ForegroundColor White
            Write-Host "Items with HTML: $htmlItemsCount" -ForegroundColor $(if($htmlItemsCount -gt 0){"Green"}else{"Yellow"})
            Write-Host "Items with Tables: $tableItemsCount" -ForegroundColor $(if($tableItemsCount -gt 0){"Green"}else{"Yellow"})
            
            if ($richContentFound) {
                Write-Host ""
                Write-Host "*** SUCCESS: RICH CONTENT (HTML) FOUND IN JAMA API! ***" -ForegroundColor Green
                Write-Host ""
                Write-Host "=== IMPLEMENTATION PLAN ===" -ForegroundColor Cyan
                Write-Host "The JamaConnectService needs to parse HTML in description fields:" -ForegroundColor White
                Write-Host ""
                Write-Host "1. Add HtmlAgilityPack NuGet package for HTML parsing" -ForegroundColor Yellow
                Write-Host "2. Enhance ConvertToRequirements() method:" -ForegroundColor Yellow
                Write-Host "   - Parse HTML in description field" -ForegroundColor Gray
                Write-Host "   - Extract table elements -> LooseContent.Tables" -ForegroundColor Gray
                Write-Host "   - Extract paragraph elements -> LooseContent.Paragraphs" -ForegroundColor Gray
                Write-Host "   - Handle lists, images, and other rich elements" -ForegroundColor Gray
                Write-Host ""
                Write-Host "3. Test with imported requirements to verify Tables and Supplemental Info tabs populate" -ForegroundColor Yellow
                Write-Host ""
                Write-Host "Raw data saved to: $responseFile" -ForegroundColor Gray
            } else {
                Write-Host ""
                Write-Host "No HTML content found in analyzed items" -ForegroundColor Yellow
                Write-Host "Check the saved response file for manual analysis: $responseFile" -ForegroundColor Gray
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