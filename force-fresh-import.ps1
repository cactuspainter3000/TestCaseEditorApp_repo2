#!/usr/bin/env pwsh
# Force Fresh Jama Import - Clear Cache and Test
# This script helps debug persistent HTML entity issues

Write-Host "=== Force Fresh Jama Import Test ===" -ForegroundColor Cyan
Write-Host ""

# Check if app is running and suggest closing it
$processes = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "⚠️  TestCaseEditorApp is running (${processes.Count} instance(s))" -ForegroundColor Yellow
    Write-Host "   Close the app first to ensure fresh data loading" -ForegroundColor Yellow
    Write-Host ""
}

# Clear potential cache files
$cacheFiles = @(
    "jama_api_response_636_*.json",
    "test_workspace.tcex.json",
    "*.tcex.json"
)

Write-Host "🧹 Clearing potential cache files..." -ForegroundColor Green
foreach ($pattern in $cacheFiles) {
    $files = Get-ChildItem $pattern -ErrorAction SilentlyContinue
    if ($files) {
        Write-Host "   Removing: $($files.Name -join ', ')"
        $files | Remove-Item -Force
    }
}

# Check for build artifacts
Write-Host ""
Write-Host "🔨 Checking build status..." -ForegroundColor Green
$result = dotnet build --verbosity quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Build successful" -ForegroundColor Green
} else {
    Write-Host "   ❌ Build failed - fix errors first" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🚀 Ready for fresh import test!" -ForegroundColor Magenta
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host "1. Start the app: dotnet run"
Write-Host "2. Create a new project using Decagon (636)"
Write-Host "3. Watch for HTML entities in the imported requirements"
Write-Host "4. If you still see &lt; &gt; &amp; entities, let me know:"
Write-Host "   - Exactly where you see them (description, name, tables)"
Write-Host "   - Which specific requirement(s) show the problem"
Write-Host ""
Write-Host "🔍 Debug: Check if CleanHtmlText is being called..." -ForegroundColor Cyan
Write-Host "   Look for log messages like '[JamaConnect] Item XYZ: CleanHtmlText called'"