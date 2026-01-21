Write-Host "Testing NotificationWorkspaceViewModel LED Fix" -ForegroundColor Cyan

Write-Host "Building application..." -ForegroundColor Yellow
dotnet build .\TestCaseEditorApp.csproj -c Debug --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful" -ForegroundColor Green

$exe = Join-Path $PSScriptRoot "bin\Debug\net8.0-windows\TestCaseEditorApp.exe"
if (!(Test-Path $exe)) {
    Write-Host "EXE not found at: $exe" -ForegroundColor Red
    exit 1
}

Write-Host "Starting app to test LED status..." -ForegroundColor Yellow
$appProcess = Start-Process -FilePath $exe -PassThru

Start-Sleep -Seconds 3

if ($appProcess.HasExited) {
    Write-Host "App crashed during startup" -ForegroundColor Red
    Write-Host "Exit code: $($appProcess.ExitCode)" -ForegroundColor Red
    exit 1
}

Write-Host "App started successfully (PID: $($appProcess.Id))" -ForegroundColor Green
Start-Sleep -Seconds 2

Write-Host "App should now be running with LED status indicator" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to close the app and exit test..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

try {
    $appProcess.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    if (!$appProcess.HasExited) {
        Stop-Process -Id $appProcess.Id -Force
    }
    Write-Host "App closed successfully" -ForegroundColor Green
} catch {
    Write-Host "Warning: App may still be running" -ForegroundColor Yellow
}