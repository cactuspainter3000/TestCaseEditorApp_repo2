Write-Host "Testing Enhanced Error Handling for Jama API" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# Build first
Write-Host "Building project..." -ForegroundColor Yellow
$buildResult = dotnet build --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green

# Run the enhanced test script
Write-Host ""
Write-Host "Running enhanced Jama API tests..." -ForegroundColor Yellow
Write-Host "Expected behavior:" -ForegroundColor Gray
Write-Host "  File download capabilities should work" -ForegroundColor Gray  
Write-Host "  Abstract search should fallback gracefully" -ForegroundColor Gray
Write-Host "  Activities tracking should return empty list" -ForegroundColor Gray
Write-Host "  Enhanced requirements should fallback to basic API or empty list" -ForegroundColor Gray

# Run the test
try {
    .\test-complete-improved-requirement.ps1
    Write-Host ""
    Write-Host "Test completed! Check the logs above for fallback behavior." -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "Test script failed: $_" -ForegroundColor Red
}