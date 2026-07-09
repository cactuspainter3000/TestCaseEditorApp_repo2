#Requires -Version 5.1
<#
.SYNOPSIS
Launch TestCaseEditorApp in workshop mode targeting the "To be deleted" container.

.DESCRIPTION
Sets JAMA_TEST_CONTAINER_ID=19853308 (project 686, "To be deleted" folder)
and launches the app. All extracted requirements will be imported to this
container instead of the default location, making it easy to test and clean up.

.EXAMPLE
.\launch-workshop-mode.ps1
#>

$ErrorActionPreference = "Stop"

Write-Host "🧪 Launching TestCaseEditorApp in WORKSHOP MODE" -ForegroundColor Cyan
Write-Host "   Target: Jama Project 686, Container 19853308 ('To be deleted')" -ForegroundColor Cyan
Write-Host ""

# Set environment variable for this process and children
$env:JAMA_TEST_CONTAINER_ID = "19853308"

# Launch the app
Write-Host "Starting application..." -ForegroundColor Green
$appPath = Join-Path $PSScriptRoot "bin\Debug\net8.0-windows\TestCaseEditorApp.exe"

if (-not (Test-Path $appPath)) {
    Write-Host "❌ Error: Application not found at $appPath" -ForegroundColor Red
    Write-Host "   Please build the project first: dotnet build TestCaseEditorApp.csproj" -ForegroundColor Yellow
    exit 1
}

& $appPath

Write-Host ""
Write-Host "✅ Workshop session ended." -ForegroundColor Green
Write-Host "   To clean up: Delete the 'To be deleted' container in Jama project 686" -ForegroundColor Yellow
