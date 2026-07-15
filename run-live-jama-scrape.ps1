<#
Launches TestCaseEditorApp with Jama credentials already configured for a live scrape run.
This is intended for comparing the real app path across machines using the same environment.

Usage:
  .\run-live-jama-scrape.ps1 -BaseUrl https://... -ClientId ... -ClientSecret ... -ProjectId 686
  .\run-live-jama-scrape.ps1 -ProjectId 686 -TestContainerId 19853308
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = $env:JAMA_BASE_URL,
    [string]$ClientId = $env:JAMA_CLIENT_ID,
    [string]$ClientSecret = $env:JAMA_CLIENT_SECRET,
    [int]$ProjectId = $(if ($env:JAMA_PROJECT_ID) { [int]$env:JAMA_PROJECT_ID } else { 0 }),
    [int]$TestContainerId = $(if ($env:JAMA_TEST_CONTAINER_ID) { [int]$env:JAMA_TEST_CONTAINER_ID } else { 0 }),
    [switch]$SkipBuild
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptRoot

try {
    $repoProject = Join-Path $scriptRoot 'TestCaseEditorApp.csproj'
    $appPath = Join-Path $scriptRoot 'bin\Debug\net8.0-windows\TestCaseEditorApp.exe'

    if (-not (Test-Path $repoProject)) {
        throw "Could not locate TestCaseEditorApp.csproj at repo root: $scriptRoot"
    }

    if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
        throw "BaseUrl is required. Pass -BaseUrl or set JAMA_BASE_URL."
    }

    if ([string]::IsNullOrWhiteSpace($ClientId)) {
        throw "ClientId is required. Pass -ClientId or set JAMA_CLIENT_ID."
    }

    if ([string]::IsNullOrWhiteSpace($ClientSecret)) {
        throw "ClientSecret is required. Pass -ClientSecret or set JAMA_CLIENT_SECRET."
    }

    if ($ProjectId -le 0) {
        throw "ProjectId is required. Pass -ProjectId or set JAMA_PROJECT_ID to a positive integer."
    }

    $env:JAMA_BASE_URL = $BaseUrl
    $env:JAMA_CLIENT_ID = $ClientId
    $env:JAMA_CLIENT_SECRET = $ClientSecret
    $env:JAMA_PROJECT_ID = $ProjectId.ToString()

    if ($TestContainerId -gt 0) {
        $env:JAMA_TEST_CONTAINER_ID = $TestContainerId.ToString()
    }

    Write-Host "Preparing live Jama scrape session..." -ForegroundColor Green
    Write-Host "BaseUrl:       $BaseUrl" -ForegroundColor Cyan
    Write-Host "ProjectId:     $ProjectId" -ForegroundColor Cyan
    if ($TestContainerId -gt 0) {
        Write-Host "TestContainer: $TestContainerId" -ForegroundColor Cyan
    }
    Write-Host ""

    if (-not $SkipBuild) {
        Write-Host "Building application..." -ForegroundColor Yellow
        dotnet build $repoProject
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed."
        }
    }

    if (-not (Test-Path $appPath)) {
        throw "Application executable not found at: $appPath. Build the project first or omit -SkipBuild only after a successful build."
    }

    Write-Host "Launching application..." -ForegroundColor Green
    Write-Host "In the app, open Workshop and run the scrape flow against the selected attachment." -ForegroundColor Cyan
    Start-Process -FilePath $appPath -WorkingDirectory $scriptRoot | Out-Null
}
finally {
    Pop-Location
}