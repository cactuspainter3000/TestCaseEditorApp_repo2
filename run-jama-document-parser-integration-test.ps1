<#
Runs the parser-level Jama document integration test from any working directory.
This exercises the full parser path against a local fixture with mocked external services.

Usage:
  .\run-jama-document-parser-integration-test.ps1
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = "Tests\TestCaseEditorApp.Tests\TestCaseEditorApp.Tests.csproj",
    [string]$TestFilter = "FullyQualifiedName~ParseAttachmentAsync_DirectRagTemplateForm_MapsExtractionTriageFields",
    [string]$OutputPath
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptRoot

try {
    $repoProject = Join-Path $scriptRoot 'TestCaseEditorApp.csproj'
    if (-not (Test-Path $repoProject)) {
        throw "Could not locate TestCaseEditorApp.csproj at repo root: $scriptRoot"
    }

    $testProject = Join-Path $scriptRoot $ProjectPath
    if (-not (Test-Path $testProject)) {
        throw "Test project not found at: $testProject"
    }

    Write-Host "Running Jama document parser integration test..." -ForegroundColor Green
    Write-Host "Project: $testProject" -ForegroundColor Cyan
    Write-Host "Filter:  $TestFilter" -ForegroundColor Cyan
    if ($OutputPath) {
        Write-Host "Output:  $OutputPath" -ForegroundColor Cyan
    }
    Write-Host ""

    $dotnetArgs = @('test', $testProject, '--filter', $TestFilter, '--logger', 'console;verbosity=detailed')
    if ($OutputPath) {
        $dotnetArgs += @('--output', $OutputPath)
    }

    dotnet @dotnetArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}