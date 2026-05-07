# Delete All AnythingLLM Workspaces
# This script removes all workspaces from AnythingLLM instance

param(
    [string]$ApiKey = "",
    [switch]$Force
)

Write-Host "=== Delete All AnythingLLM Workspaces ===" -ForegroundColor Cyan

# Get API key from registry if not provided
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    try {
        $regKey = "HKCU:\SOFTWARE\TestCaseEditorApp\AnythingLLM"
        if (Test-Path $regKey) {
            $ApiKey = Get-ItemProperty -Path $regKey -Name "ApiKey" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty ApiKey
            if ($ApiKey) {
                Write-Host "Using API key from registry" -ForegroundColor Green
            }
        }
    } catch {
        # Ignore registry errors
    }
}

# Prompt for API key if still not found
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "`nAPI key not found in registry. Please provide it:" -ForegroundColor Yellow
    $ApiKey = Read-Host "API Key"
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "No API key provided. Exiting." -ForegroundColor Red
    return
}

# Get all workspaces
Write-Host "`nFetching workspaces..." -ForegroundColor Yellow
try {
    $headers = @{ "Authorization" = "Bearer $ApiKey" }
    $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Method GET -Headers $headers
    
    $workspaces = $response.workspaces
    if (-not $workspaces -or $workspaces.Count -eq 0) {
        Write-Host "No workspaces found." -ForegroundColor Gray
        return
    }
    
    Write-Host "Found $($workspaces.Count) workspace(s):" -ForegroundColor Cyan
    foreach ($ws in $workspaces) {
        Write-Host "  - $($ws.name) ($($ws.slug))" -ForegroundColor White
    }
    
    # Confirm deletion
    if (-not $Force) {
        Write-Host "`nWARNING: This will delete ALL workspaces and their data!" -ForegroundColor Red
        $confirm = Read-Host "Type 'DELETE' to confirm"
        if ($confirm -ne "DELETE") {
            Write-Host "Deletion cancelled." -ForegroundColor Yellow
            return
        }
    }
    
    # Delete each workspace
    Write-Host "`nDeleting workspaces..." -ForegroundColor Yellow
    $deleted = 0
    $failed = 0
    
    foreach ($ws in $workspaces) {
        try {
            Write-Host "Deleting: $($ws.name) ($($ws.slug))... " -NoNewline -ForegroundColor White
            $deleteResponse = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspace/$($ws.slug)" -Method DELETE -Headers $headers
            Write-Host "OK" -ForegroundColor Green
            $deleted++
        } catch {
            Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
            $failed++
        }
    }
    
    Write-Host "`n=== Summary ===" -ForegroundColor Cyan
    Write-Host "Deleted: $deleted" -ForegroundColor Green
    if ($failed -gt 0) {
        Write-Host "Failed: $failed" -ForegroundColor Red
    }
    Write-Host "Complete!" -ForegroundColor Green
    
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
