#!/usr/bin/env pwsh
# quick-requirements-test.ps1 - Quick Requirements test

Write-Host "=== Quick Requirements Navigation Test ===" -ForegroundColor Cyan

# Kill any running app
Get-Process TestCaseEditorApp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "Starting app and opening test workspace..." -ForegroundColor Yellow

# Start app in background and wait a moment
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -WindowStyle Normal
Start-Sleep -Seconds 2

Write-Host "
MANUAL TEST STEPS:
1. Click 'Open Project' or File > Open Project
2. Select the test workspace: test_workspace.tcex.json
3. Wait for project to load (should see requirements loaded in logs)
4. Click 'Requirements' in the side menu
5. Check if details show data instead of '(not set)'

EXPECTED: Requirements details and navigation should be populated
BEFORE FIX: Would show '(not set)' and empty navigation
AFTER FIX: Should show actual requirement data and populated navigation

" -ForegroundColor Green

Write-Host "Press Enter after testing..." -ForegroundColor Yellow
Read-Host

Write-Host "Test complete!" -ForegroundColor Cyan