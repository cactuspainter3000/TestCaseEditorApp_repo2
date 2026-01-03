#!/usr/bin/env powershell

# Automated test to validate title update mechanism
Write-Host "=== Automated Title Update Test ===" -ForegroundColor Cyan

# Start the app
Write-Host "Starting TestCaseEditorApp..." -ForegroundColor Green
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

# Function to get window title
function Get-AppWindowTitle {
    try {
        $windows = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
        if ($windows) {
            foreach ($window in $windows) {
                $hwnd = $window.MainWindowHandle
                if ($hwnd -ne [IntPtr]::Zero) {
                    Add-Type @"
                        using System;
                        using System.Runtime.InteropServices;
                        using System.Text;
                        
                        public class WindowAPI {
                            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
                        }
"@
                    $title = New-Object System.Text.StringBuilder 256
                    $result = [WindowAPI]::GetWindowText($hwnd, $title, 256)
                    if ($result -gt 0) {
                        return $title.ToString()
                    }
                }
            }
        }
        return $null
    } catch {
        return $null
    }
}

# Wait for app to load and check initial title
Start-Sleep -Seconds 2
$initialTitle = Get-AppWindowTitle
Write-Host "Initial title: '$initialTitle'" -ForegroundColor Yellow

# Wait for auto-test to trigger (should be after 3 seconds from MainViewModel construction)
Write-Host "Waiting for auto-test to trigger (should happen ~3 seconds after app start)..." -ForegroundColor Cyan
Start-Sleep -Seconds 4

# Check title after auto-test
$afterAutoTestTitle = Get-AppWindowTitle
Write-Host "Title after auto-test: '$afterAutoTestTitle'" -ForegroundColor Yellow

# Analyze results
Write-Host ""
Write-Host "=== Analysis ===" -ForegroundColor Cyan
if ($afterAutoTestTitle -eq $initialTitle) {
    Write-Host "❌ FAILED: Title did not change after auto-test" -ForegroundColor Red
    Write-Host "   This indicates the NavigationService → MainViewModel → Window binding chain is broken" -ForegroundColor Red
} else {
    Write-Host "✅ SUCCESS: Title changed!" -ForegroundColor Green
    Write-Host "   Initial: '$initialTitle'" -ForegroundColor White
    Write-Host "   After:   '$afterAutoTestTitle'" -ForegroundColor White
    Write-Host "   The navigation service title mechanism is working!" -ForegroundColor Green
}

# Wait a bit more to see any delayed updates
Write-Host ""
Write-Host "Waiting 3 more seconds to check for any delayed updates..." -ForegroundColor Cyan
Start-Sleep -Seconds 3
$finalTitle = Get-AppWindowTitle
if ($finalTitle -ne $afterAutoTestTitle) {
    Write-Host "Additional title change detected: '$finalTitle'" -ForegroundColor Yellow
}

# Clean up
Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Green
if ($process -and !$process.HasExited) {
    $process.Kill()
    $process.WaitForExit(5000)
}

Write-Host "Automated test complete." -ForegroundColor Green