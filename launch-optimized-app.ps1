# Launch TestCaseEditorApp with optimized prompts
Write-Host "🚀 Launching TestCaseEditorApp with Optimized AI Prompts" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green

# Set environment for AnythingLLM
$env:LLM_PROVIDER = "anythingllm"
Write-Host "✅ LLM Provider set to: $($env:LLM_PROVIDER)" -ForegroundColor Yellow

# Check if app is already running
$runningApps = Get-Process -Name "*TestCaseEditorApp*" -ErrorAction SilentlyContinue
if ($runningApps) {
    Write-Host "⚠️  TestCaseEditorApp is already running (PID: $($runningApps.Id -join ', '))" -ForegroundColor Yellow
    Write-Host "   You can close it and rerun this script if needed." -ForegroundColor White
    exit 0
}

try {
    # Try to launch from executable first (fastest)
    if (Test-Path ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe") {
        Write-Host "📦 Starting from executable..." -ForegroundColor Cyan
        Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -WorkingDirectory (Get-Location)
        Write-Host "✅ TestCaseEditorApp launched successfully!" -ForegroundColor Green
    } else {
        Write-Host "📦 Executable not found, building first..." -ForegroundColor Cyan
        
        # Build with a new PowerShell process to avoid file locks
        $buildResult = Start-Process -FilePath "powershell.exe" -ArgumentList "-Command", "dotnet build --configuration Debug" -Wait -PassThru -WindowStyle Hidden
        
        if ($buildResult.ExitCode -eq 0) {
            Write-Host "✅ Build successful, launching..." -ForegroundColor Green
            Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -WorkingDirectory (Get-Location)
        } else {
            Write-Host "❌ Build failed. Try running 'dotnet build' manually." -ForegroundColor Red
            exit 1
        }
    }
    
    Write-Host "`n🎯 TESTING YOUR OPTIMIZED PROMPTS:" -ForegroundColor Cyan
    Write-Host "1. Navigate to Test Case Generation" -ForegroundColor White
    Write-Host "2. Add a test requirement (try these):" -ForegroundColor White
    Write-Host "   • 'The system shall provide adequate boundary scan coverage'" -ForegroundColor Gray
    Write-Host "   • 'The system shall validate inputs and log results'" -ForegroundColor Gray
    Write-Host "3. Run analysis and verify:" -ForegroundColor White
    Write-Host "   ✅ No 'Fix:' directive text in results" -ForegroundColor Yellow
    Write-Host "   ✅ Clean JSON format" -ForegroundColor Yellow
    Write-Host "   ✅ SuggestedEdit has actual requirement text" -ForegroundColor Yellow
    Write-Host "   ✅ Consistent results on multiple runs" -ForegroundColor Yellow
    
} catch {
    Write-Host "❌ Error launching app: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Try running 'dotnet run' manually or check for process locks." -ForegroundColor Yellow
}

Write-Host "`n🎉 Your AI cognition pattern optimizations are now active!" -ForegroundColor Green