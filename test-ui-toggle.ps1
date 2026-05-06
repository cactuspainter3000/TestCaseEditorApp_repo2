#!/usr/bin/env pwsh
# Quick UI Toggle Test Script

Write-Host "🧪 Testing UI Toggle Functionality" -ForegroundColor Cyan

try {
    Write-Host "📦 Building project..." -ForegroundColor Yellow
    dotnet build TestCaseEditorApp.csproj --no-restore -q
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Build successful" -ForegroundColor Green
        
        Write-Host "🚀 Starting application (will auto-close in 10 seconds)..." -ForegroundColor Yellow
        Write-Host "➡️  Navigate to New Project to test the toggle functionality" -ForegroundColor Cyan
        
        # Start the app in background
        $process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal
        
        # Wait 10 seconds then close
        Start-Sleep -Seconds 10
        
        if (-not $process.HasExited) {
            Write-Host "⏰ Auto-closing application..." -ForegroundColor Yellow
            $process.Kill()
            $process.WaitForExit(3000)
        }
        
        Write-Host "✨ Test complete! Check if toggle between Jama and Document import worked." -ForegroundColor Green
    } else {
        Write-Host "❌ Build failed" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Error during test: $_" -ForegroundColor Red
}