# Delete All AnythingLLM Workspaces
# WARNING: This will permanently delete ALL workspaces and their data!

param(
    [string]$BaseUrl = "http://localhost:3001",
    [string]$ApiKey = "JVJGN9Q-HG4M4Q4-GW3CVWS-AC8XG1G",
    [switch]$Force,
    [switch]$WhatIf
)

Write-Host "AnythingLLM Workspace Cleanup Utility" -ForegroundColor Red
Write-Host "=====================================" -ForegroundColor Red
Write-Host "Base URL: $BaseUrl" -ForegroundColor Yellow
Write-Host "API Key: $($ApiKey.Substring(0,8))..." -ForegroundColor Yellow
Write-Host

if (!$Force -and !$WhatIf) {
    Write-Host "WARNING: This will DELETE ALL workspaces and their uploaded documents!" -ForegroundColor Red
    Write-Host "WARNING: This action cannot be undone!" -ForegroundColor Red
    Write-Host
    $confirmation = Read-Host "Are you sure you want to continue? Type 'DELETE ALL' to confirm"
    if ($confirmation -ne "DELETE ALL") {
        Write-Host "Operation cancelled." -ForegroundColor Green
        exit
    }
}

# Set up headers with API key
$headers = @{
    "Authorization" = "Bearer $ApiKey"
    "Content-Type" = "application/json"
}

try {
    # Get all workspaces
    Write-Host "Fetching workspace list..." -ForegroundColor Yellow
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/v1/workspaces" -Method GET -Headers $headers
    
    if (!$response -or !$response.workspaces) {
        Write-Host "No workspaces found or unable to fetch workspace list." -ForegroundColor Green
        exit
    }
    
    $workspaces = $response.workspaces
    Write-Host "Found $($workspaces.Count) workspaces to delete:" -ForegroundColor Yellow
    
    # List all workspaces
    foreach ($workspace in $workspaces) {
        $created = if ($workspace.createdAt) { 
            [DateTime]::Parse($workspace.createdAt).ToString("yyyy-MM-dd") 
        } else { 
            "Unknown" 
        }
        Write-Host "  - [$($workspace.id)] $($workspace.name) (slug: $($workspace.slug), created: $created)" -ForegroundColor White
    }
    
    Write-Host
    
    if ($WhatIf) {
        Write-Host "WhatIf: Would delete $($workspaces.Count) workspaces" -ForegroundColor Cyan
        exit
    }
    
    # Delete each workspace
    $deleted = 0
    $errors = 0
    
    foreach ($workspace in $workspaces) {
        try {
            Write-Host "Deleting workspace: $($workspace.name) (slug: $($workspace.slug))..." -ForegroundColor Yellow -NoNewline
            
            $deleteResponse = Invoke-RestMethod -Uri "$BaseUrl/api/v1/workspace/$($workspace.slug)" -Method DELETE -Headers $headers -ErrorAction Stop
            
            Write-Host " SUCCESS" -ForegroundColor Green
            $deleted++
        }
        catch {
            Write-Host " FAILED: $($_.Exception.Message)" -ForegroundColor Red
            $errors++
        }
        
        Start-Sleep -Milliseconds 100  # Small delay to avoid overwhelming the API
    }
    
    Write-Host
    Write-Host "Cleanup Summary:" -ForegroundColor Cyan
    Write-Host "  Successfully deleted: $deleted workspaces" -ForegroundColor Green
    Write-Host "  Failed to delete: $errors workspaces" -ForegroundColor Red
    Write-Host "  Total processed: $($workspaces.Count) workspaces" -ForegroundColor White
    
    if ($errors -eq 0) {
        Write-Host
        Write-Host "All workspaces have been successfully deleted!" -ForegroundColor Green
    }
}
catch {
    Write-Host "Error fetching workspaces: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Make sure AnythingLLM is running on $BaseUrl and API key is valid" -ForegroundColor Yellow
}

Write-Host
Write-Host "Done." -ForegroundColor White