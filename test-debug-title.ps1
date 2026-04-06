#!/usr/bin/env powershell

# Enhanced debugging test
Write-Host "=== Enhanced Debugging Test ===" -ForegroundColor Cyan

# Start the app
Write-Host "Starting TestCaseEditorApp..." -ForegroundColor Green
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

Write-Host "Process ID: $($process.Id)" -ForegroundColor Yellow
Write-Host "Process Name: $($process.ProcessName)" -ForegroundColor Yellow

# Function to get detailed window info
function Get-AppWindowInfo {
    try {
        $windows = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
        Write-Host "Found $($windows.Count) TestCaseEditorApp processes" -ForegroundColor Cyan
        
        if ($windows) {
            for ($i = 0; $i -lt $windows.Count; $i++) {
                $window = $windows[$i]
                Write-Host "Process $($i + 1):" -ForegroundColor White
                Write-Host "  ID: $($window.Id)" -ForegroundColor Gray
                Write-Host "  MainWindowHandle: $($window.MainWindowHandle)" -ForegroundColor Gray
                Write-Host "  MainWindowTitle: '$($window.MainWindowTitle)'" -ForegroundColor Gray
                Write-Host "  HasExited: $($window.HasExited)" -ForegroundColor Gray
                Write-Host "  Responding: $($window.Responding)" -ForegroundColor Gray
                
                $hwnd = $window.MainWindowHandle
                if ($hwnd -ne [IntPtr]::Zero) {
                    Add-Type @"
                        using System;
                        using System.Runtime.InteropServices;
                        using System.Text;
                        
                        public class WindowAPI {
                            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
                            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
                            
                            [DllImport("user32.dll")]
                            [return: MarshalAs(UnmanagedType.Bool)]
                            public static extern bool IsWindowVisible(IntPtr hWnd);
                        }
"@
                    $title = New-Object System.Text.StringBuilder 256
                    $result = [WindowAPI]::GetWindowText($hwnd, $title, 256)
                    $isVisible = [WindowAPI]::IsWindowVisible($hwnd)
                    
                    Write-Host "  API Title: '$($title.ToString())'" -ForegroundColor Yellow
                    Write-Host "  API Title Length: $result" -ForegroundColor Gray
                    Write-Host "  Window Visible: $isVisible" -ForegroundColor Gray
                    
                    return $title.ToString()
                } else {
                    Write-Host "  No main window handle" -ForegroundColor Red
                }
            }
        } else {
            Write-Host "No TestCaseEditorApp processes found" -ForegroundColor Red
        }
        return $null
    } catch {
        Write-Host "Error getting window info: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Check immediately
Write-Host ""
Write-Host "=== Immediate Check ===" -ForegroundColor Cyan
$title1 = Get-AppWindowInfo

# Wait 2 seconds
Write-Host ""
Write-Host "=== After 2 seconds ===" -ForegroundColor Cyan
Start-Sleep -Seconds 2
$title2 = Get-AppWindowInfo

# Wait 5 more seconds (total 7 seconds, should be enough for auto-test)
Write-Host ""
Write-Host "=== After 7 seconds total ===" -ForegroundColor Cyan
Start-Sleep -Seconds 5
$title3 = Get-AppWindowInfo

# Final check
Write-Host ""
Write-Host "=== Results Summary ===" -ForegroundColor Cyan
Write-Host "Immediate: '$title1'" -ForegroundColor Yellow
Write-Host "After 2s:  '$title2'" -ForegroundColor Yellow
Write-Host "After 7s:  '$title3'" -ForegroundColor Yellow

# Clean up
Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Green
if ($process -and !$process.HasExited) {
    $process.Kill()
    $process.WaitForExit(5000)
}

Write-Host "Enhanced debugging test complete." -ForegroundColor Green