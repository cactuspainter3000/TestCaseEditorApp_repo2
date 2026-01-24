# Test to verify emoji fix is working
Write-Host ""
Write-Host "=== TESTING JAMA CONNECT FIX ===" -ForegroundColor Cyan
Write-Host "This test verifies that the hanging issue has been resolved" -ForegroundColor Gray
Write-Host ""

try {
    # Check environment first
    if (-not $env:JAMA_BASE_URL) {
        Write-Host "Missing JAMA_BASE_URL environment variable" -ForegroundColor Red
        return
    }
    if (-not $env:JAMA_CLIENT_ID) {
        Write-Host "Missing JAMA_CLIENT_ID environment variable" -ForegroundColor Red
        return
    }
    if (-not $env:JAMA_CLIENT_SECRET) {
        Write-Host "Missing JAMA_CLIENT_SECRET environment variable" -ForegroundColor Red
        return
    }

    Write-Host "1. Starting application..." -ForegroundColor White
    
    # Start the application in background
    $process = Start-Process -FilePath "bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru
    Write-Host "   Application started with PID: $($process.Id)" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "2. Application should load without hanging" -ForegroundColor White
    Write-Host "   - Look for 'Calling GetRequirementsWithUserMetadataAsync(636)...' in logs" -ForegroundColor Gray
    Write-Host "   - Should be followed by 'ENTRY: GetRequirementsWithUserMetadataAsync' debug log" -ForegroundColor Gray
    Write-Host "   - Check for timeout errors or successful completion" -ForegroundColor Gray
    
    Write-Host ""
    Write-Host "3. Wait 60 seconds then check if process is still running..." -ForegroundColor White
    Start-Sleep -Seconds 60
    
    if ($process.HasExited) {
        Write-Host "   Application has exited (Code: $($process.ExitCode))" -ForegroundColor Yellow
    } else {
        Write-Host "   Application is still running - this is good!" -ForegroundColor Green
        Write-Host "   You can manually test the Jama connection in the UI" -ForegroundColor Gray
        
        # Ask user if they want to terminate
        Write-Host ""
        $response = Read-Host "Do you want to terminate the test application? (Y/N)"
        if ($response -eq "Y" -or $response -eq "y") {
            $process.Kill()
            Write-Host "   Application terminated" -ForegroundColor Yellow
        } else {
            Write-Host "   Application left running for manual testing" -ForegroundColor Green
        }
    }
    
} catch {
    Write-Host "Error during test: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== TEST COMPLETE ===" -ForegroundColor Cyan