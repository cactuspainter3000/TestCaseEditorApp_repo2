#!/usr/bin/env pwsh
# Test the RAG-optimized requirement analysis performance

Write-Host "Starting requirement analysis performance test..." -ForegroundColor Green

# Build first
Write-Host "Building project..." -ForegroundColor Cyan
dotnet build TestCaseEditorApp.csproj --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Check if Ollama is running
Write-Host "Checking Ollama status..." -ForegroundColor Cyan
try {
    $response = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 5
    Write-Host "✓ Ollama is running with models: $($response.models.name -join ', ')" -ForegroundColor Green
} catch {
    Write-Host "⚠ Ollama not available, will use NoopTextGenerationService" -ForegroundColor Yellow
}

# Check AnythingLLM status
Write-Host "Checking AnythingLLM status..." -ForegroundColor Cyan
try {
    $anythingResponse = Invoke-RestMethod -Uri "http://localhost:3001/api/system/system-vectors" -TimeoutSec 5
    Write-Host "✓ AnythingLLM is running" -ForegroundColor Green
} catch {
    Write-Host "⚠ AnythingLLM not available at localhost:3001" -ForegroundColor Yellow
}

# Start the application with debug output
Write-Host "Starting TestCaseEditorApp..." -ForegroundColor Cyan
Write-Host "⭐ Watch for RAG optimization logs in the console output" -ForegroundColor Yellow
Write-Host "⭐ Look for '[RequirementAnalysisService] Attempting RAG analysis...' messages" -ForegroundColor Yellow
Write-Host ""

# Run the app
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -Wait

Write-Host "Test completed." -ForegroundColor Green