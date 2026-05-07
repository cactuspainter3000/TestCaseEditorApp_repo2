#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Update Jama Connect OAuth credentials
    
.DESCRIPTION
    This script helps you update your Jama Connect OAuth credentials safely.
    It will prompt for new credentials and update your environment variables.
    
.EXAMPLE
    .\update-jama-credentials.ps1
    
.NOTES
    Created to fix "invalid_client" OAuth errors
#>

param(
    [string]$ClientId = "",
    [string]$ClientSecret = "",
    [string]$BaseUrl = "https://jama02.rockwellcollins.com/contour"
)

Write-Host "🔐 JAMA CONNECT CREDENTIAL UPDATER" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Check current credentials
Write-Host "📋 Current Environment Variables:" -ForegroundColor Yellow
$currentBaseUrl = [Environment]::GetEnvironmentVariable("JAMA_BASE_URL", "User")
$currentClientId = [Environment]::GetEnvironmentVariable("JAMA_CLIENT_ID", "User")
$currentSecret = [Environment]::GetEnvironmentVariable("JAMA_CLIENT_SECRET", "User")

Write-Host "   JAMA_BASE_URL: $($currentBaseUrl ?? '[NOT SET]')" -ForegroundColor Gray
Write-Host "   JAMA_CLIENT_ID: $($currentClientId ?? '[NOT SET]')" -ForegroundColor Gray
Write-Host "   JAMA_CLIENT_SECRET: $(if($currentSecret) { '[SET - ' + $currentSecret.Length + ' chars]' } else { '[NOT SET]' })" -ForegroundColor Gray
Write-Host ""

# Get new credentials if not provided
if (-not $ClientId -or -not $ClientSecret) {
    Write-Host "🚨 INVALID_CLIENT ERROR DETECTED!" -ForegroundColor Red
    Write-Host "   Your current OAuth credentials are being rejected by Jama." -ForegroundColor Red
    Write-Host "   Contact your Jama administrator to get new credentials." -ForegroundColor Red
    Write-Host ""
    
    Write-Host "📝 Enter new credentials (leave blank to keep current):" -ForegroundColor Yellow
    
    if (-not $ClientId) {
        $newClientId = Read-Host "   New CLIENT_ID"
        if ($newClientId) { $ClientId = $newClientId }
    }
    
    if (-not $ClientSecret) {
        $newSecret = Read-Host "   New CLIENT_SECRET" -AsSecureString
        if ($newSecret.Length -gt 0) {
            $ClientSecret = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($newSecret))
        }
    }
}

# Update credentials if provided
if ($ClientId -and $ClientSecret) {
    Write-Host ""
    Write-Host "🔄 Updating environment variables..." -ForegroundColor Green
    
    [Environment]::SetEnvironmentVariable("JAMA_BASE_URL", $BaseUrl, "User")
    [Environment]::SetEnvironmentVariable("JAMA_CLIENT_ID", $ClientId, "User")
    [Environment]::SetEnvironmentVariable("JAMA_CLIENT_SECRET", $ClientSecret, "User")
    
    Write-Host "✅ Environment variables updated!" -ForegroundColor Green
    Write-Host ""
    Write-Host "📋 New Environment Variables:" -ForegroundColor Yellow
    Write-Host "   JAMA_BASE_URL: $BaseUrl" -ForegroundColor Gray
    Write-Host "   JAMA_CLIENT_ID: $ClientId" -ForegroundColor Gray
    Write-Host "   JAMA_CLIENT_SECRET: [SET - $($ClientSecret.Length) chars]" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "⚠️  IMPORTANT: Restart your application to use the new credentials!" -ForegroundColor Yellow
    Write-Host "   Or run this to refresh current session:" -ForegroundColor Yellow
    Write-Host "   `$env:JAMA_BASE_URL='$BaseUrl'" -ForegroundColor Cyan
    Write-Host "   `$env:JAMA_CLIENT_ID='$ClientId'" -ForegroundColor Cyan
    Write-Host "   `$env:JAMA_CLIENT_SECRET='$ClientSecret'" -ForegroundColor Cyan
    
} else {
    Write-Host "❌ No credentials provided - no changes made" -ForegroundColor Red
    Write-Host ""
    Write-Host "💡 To get new credentials:" -ForegroundColor Yellow
    Write-Host "   1. Contact your Jama administrator" -ForegroundColor Gray
    Write-Host "   2. Ask them to verify your OAuth app registration" -ForegroundColor Gray
    Write-Host "   3. Request new CLIENT_ID and CLIENT_SECRET if needed" -ForegroundColor Gray
}

Write-Host ""
Write-Host "🔧 Test authentication with: .\TestCaseEditorApp.exe (Test Auth Only button)" -ForegroundColor Cyan