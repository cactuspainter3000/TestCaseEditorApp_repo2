#!/usr/bin/env pwsh

<#
.SYNOPSIS
Clean the workspace to force system prompt update and test analysis

.DESCRIPTION
This script will delete the existing workspace to force a fresh one with the new system prompt,
then test the improved requirement analysis functionality.
#>

Write-Host "=== Cleaning Workspace and Testing Improved Requirement Analysis ===" -ForegroundColor Green
Write-Host ""

# Check if AnythingLLM is running
$process = Get-Process -Name "AnythingLLM" -ErrorAction SilentlyContinue
if (-not $process) {
    Write-Host "❌ AnythingLLM not running. Please start it first." -ForegroundColor Red
    exit 1
}

Write-Host "✅ AnythingLLM is running (PID: $($process.Id))" -ForegroundColor Green

# Get API key
$apiKey = $env:ANYTHINGLM_API_KEY
if (-not $apiKey) {
    Write-Host "⚠️ No API key found in environment. Checking registry..." -ForegroundColor Yellow
    try {
        $apiKey = Get-ItemPropertyValue -Path "HKCU:\Software\TestCaseEditorApp" -Name "AnythingLLM_API_Key" -ErrorAction SilentlyContinue
        if ($apiKey) {
            Write-Host "✅ API key found in registry" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "❌ No API key found. Please set ANYTHINGLM_API_KEY or run setup." -ForegroundColor Red
        exit 1
    }
}

# Delete existing workspace to force fresh creation
Write-Host ""
Write-Host "🧹 Deleting existing workspace to force new system prompt..." -ForegroundColor Yellow

$headers = @{ 
    "Authorization" = "Bearer $apiKey"
    "Accept" = "application/json"
}

try {
    # Get current workspaces
    $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Headers $headers -Method GET
    
    foreach ($workspace in $response.workspaces) {
        if ($workspace.name -like "*Test Case Editor*" -or $workspace.name -like "*New Project*") {
            Write-Host "Deleting workspace: $($workspace.name)" -ForegroundColor Gray
            try {
                $deleteUrl = "http://localhost:3001/api/v1/workspace/$($workspace.slug)"
                Invoke-RestMethod -Uri $deleteUrl -Headers $headers -Method DELETE
                Write-Host "✅ Deleted workspace: $($workspace.slug)" -ForegroundColor Green
            }
            catch {
                Write-Host "⚠️ Could not delete $($workspace.slug): $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }
}
catch {
    Write-Host "⚠️ Could not list workspaces: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🚀 Starting application to test with fresh workspace..." -ForegroundColor Green

# Build first
Write-Host "Building application..." -ForegroundColor Gray
dotnet build --configuration Debug --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}

# Start the app
Write-Host "✅ Build successful. Starting application..." -ForegroundColor Green
Start-Process ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe"

Write-Host ""
Write-Host "=== Test Instructions ===" -ForegroundColor Cyan
Write-Host "1. Create a new project (this will create fresh workspace with new system prompt)"
Write-Host "2. Add this test requirement:"
Write-Host "   'The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT.'"
Write-Host "3. Run analysis and verify response includes:"
Write-Host "   📝 IMPROVED REQUIREMENT: [Complete rewritten requirement]"
Write-Host "   📊 Quality score and issues"  
Write-Host "   ✅ HALLUCINATION CHECK: NO_FABRICATION"
Write-Host "4. Check logs for 'ImprovedReq=True'"
Write-Host ""
Write-Host "💡 If timeout occurs, the model might be overloaded. Try again or use a lighter model." -ForegroundColor Yellow