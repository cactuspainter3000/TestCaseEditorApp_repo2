#!/usr/bin/env pwsh

Write-Host "Testing Jama Project Loading" -ForegroundColor Cyan
Write-Host "============================" -ForegroundColor Cyan

# Test Jama configuration first
Write-Host "`nChecking environment variables..." -ForegroundColor Yellow
$jamaUrl = $env:JAMA_BASE_URL
$jamaClientId = $env:JAMA_CLIENT_ID
$jamaClientSecret = $env:JAMA_CLIENT_SECRET

if ($jamaUrl) {
    Write-Host "✅ JAMA_BASE_URL: $jamaUrl" -ForegroundColor Green
} else {
    Write-Host "❌ JAMA_BASE_URL not set" -ForegroundColor Red
}

if ($jamaClientId) {
    Write-Host "✅ JAMA_CLIENT_ID: $jamaClientId" -ForegroundColor Green
} else {
    Write-Host "❌ JAMA_CLIENT_ID not set" -ForegroundColor Red
}

if ($jamaClientSecret) {
    Write-Host "✅ JAMA_CLIENT_SECRET: [SET]" -ForegroundColor Green
} else {
    Write-Host "❌ JAMA_CLIENT_SECRET not set" -ForegroundColor Red
}

Write-Host "`nLaunch the app and:" -ForegroundColor Yellow
Write-Host "1. Go to New Project" -ForegroundColor Gray
Write-Host "2. Toggle the Import Requirements section" -ForegroundColor Gray
Write-Host "3. Check if projects populate" -ForegroundColor Gray
Write-Host "4. Check console logs for any project loading messages" -ForegroundColor Gray

Write-Host "`nLooking for these log messages:" -ForegroundColor Yellow
Write-Host "- '[JamaConnect] Projects API response:'" -ForegroundColor Gray
Write-Host "- '[LoadJamaProjects] Starting to load projects...'" -ForegroundColor Gray
Write-Host "- '[LoadJamaProjects] Final AvailableProjects count:'" -ForegroundColor Gray