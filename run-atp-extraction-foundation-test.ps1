<#
Runs the ATP extraction foundation integration test from any working directory.
This is intended for comparing the same local extraction foundation behavior across machines.

Usage:
  .\run-atp-extraction-foundation-test.ps1
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = "Tests\TestCaseEditorApp.Tests\TestCaseEditorApp.Tests.csproj",
    [string]$TestFilter = "FullyQualifiedName~ATPExtractionFoundationIntegrationTests",
    [string]$OutputPath
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptRoot

try {
    $repoProject = Join-Path $scriptRoot 'TestCaseEditorApp.csproj'
    if (-not (Test-Path $repoProject)) {
        throw "Could not locate TestCaseEditorApp.csproj at repo root: $scriptRoot"
    }

    $fixturePath = Join-Path $scriptRoot 'Tests\Fixtures\ATP\946-4DC0-001_C4B_DHM_ATP_Rev-.docx'
    if (-not (Test-Path $fixturePath)) {
        throw "Expected ATP fixture not found at: $fixturePath"
    }

    $resolvedProjectPath = Join-Path $scriptRoot $ProjectPath
    if (-not (Test-Path $resolvedProjectPath)) {
        throw "Test project not found at: $resolvedProjectPath"
    }

    Write-Host "Running ATP extraction foundation test..." -ForegroundColor Green
    Write-Host "Project: $resolvedProjectPath" -ForegroundColor Cyan
    Write-Host "Filter:  $TestFilter" -ForegroundColor Cyan
    if ($OutputPath) {
        Write-Host "Output:  $OutputPath" -ForegroundColor Cyan
    }
    Write-Host "Fixture: $fixturePath" -ForegroundColor Cyan
    Write-Host ""

    $dotnetArgs = @('test', $resolvedProjectPath, '--filter', $TestFilter, '--logger', 'console;verbosity=detailed')
    if ($OutputPath) {
        $dotnetArgs += @('--output', $OutputPath)
    }

    dotnet @dotnetArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}