# Restart TestCaseEditorApp with enhanced anti-fabrication prompts
Write-Host "🔄 Restarting TestCaseEditorApp with Enhanced Anti-Fabrication" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green

# Check if app is running
$runningApp = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
if ($runningApp) {
    Write-Host "🛑 Stopping current app (PID: $($runningApp.Id))..." -ForegroundColor Yellow
    Stop-Process -Id $runningApp.Id -Force
    Start-Sleep 2
    Write-Host "✅ App stopped" -ForegroundColor Green
}

# Set environment
$env:LLM_PROVIDER = "anythingllm"

# Build with updated anti-fabrication prompts
Write-Host "🔨 Building with enhanced anti-fabrication prompts..." -ForegroundColor Cyan
$buildResult = Start-Process -FilePath "powershell.exe" -ArgumentList "-Command", "dotnet build --configuration Debug --verbosity quiet" -Wait -PassThru -WindowStyle Hidden

if ($buildResult.ExitCode -eq 0) {
    Write-Host "✅ Build successful!" -ForegroundColor Green
    
    # Launch with new prompts
    Write-Host "🚀 Launching with enhanced prompts..." -ForegroundColor Cyan
    Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -WorkingDirectory (Get-Location)
    Start-Sleep 2
    
    # Verify it's running
    $newApp = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
    if ($newApp) {
        Write-Host "✅ TestCaseEditorApp restarted successfully (PID: $($newApp.Id))" -ForegroundColor Green
    } else {
        Write-Host "❌ Failed to restart app" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}

Write-Host "`n🎯 ENHANCED ANTI-FABRICATION FEATURES:" -ForegroundColor Cyan
Write-Host "✅ Specific prohibition list (connector types, protocols, etc.)" -ForegroundColor White
Write-Host "✅ Mandatory validation check before suggesting technical terms" -ForegroundColor White
Write-Host "✅ Enhanced HallucinationCheck protocol" -ForegroundColor White
Write-Host "✅ Clear examples of what to avoid vs what to do" -ForegroundColor White

Write-Host "`n📊 TEST THE IMPROVEMENTS:" -ForegroundColor Yellow
Write-Host "1. Try the same requirement that created fabrication before" -ForegroundColor White
Write-Host "2. Check if HallucinationCheck correctly identifies fabrications" -ForegroundColor White
Write-Host "3. Compare results with ChatGPT's analysis" -ForegroundColor White

Write-Host "`n🧪 Expected Improvement:" -ForegroundColor Green
Write-Host "AnythingLLM should now avoid adding connector types, protocols, etc." -ForegroundColor White
Write-Host "and should honestly report 'FABRICATED_DETAILS' when it does!" -ForegroundColor White