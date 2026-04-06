#!/usr/bin/env pwsh

<#
.SYNOPSIS
Test New Project button functionality with debug output capture

.DESCRIPTION
This script builds and runs the TestCaseEditorApp with debug output capture
to diagnose the "New Project - ViewModel not found" issue.

Uses the AI Guide troubleshooting methodology to systematically trace DI resolution.
#>

param()

Write-Host "=== New Project Debug Test ===" -ForegroundColor Cyan
Write-Host "Following AI Guide troubleshooting patterns..." -ForegroundColor Yellow

# Build the application
Write-Host "`n1. Building application..." -ForegroundColor Green
$buildResult = dotnet build --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host "Build succeeded" -ForegroundColor Green

# Start application with debug console attached
Write-Host "`n2. Starting application with debug capture..." -ForegroundColor Green
Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "  - Application will start" -ForegroundColor White
Write-Host "  - Click the 'New Project' button in the main menu" -ForegroundColor White  
Write-Host "  - Check if debug messages appear in this console" -ForegroundColor White
Write-Host "  - Press Ctrl+C in this console to stop when done" -ForegroundColor White

Write-Host "`nStarting TestCaseEditorApp..." -ForegroundColor Cyan

# Run the application and capture debug output
try {
    & ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe"
}
catch {
    Write-Host "Application exited with error: $_" -ForegroundColor Red
}

Write-Host "`nApplication closed." -ForegroundColor Cyan