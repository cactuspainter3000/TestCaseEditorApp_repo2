#!/usr/bin/env powershell

# Quick test for section navigation title updates
Write-Host "=== Testing Section Navigation Title Updates ===" -ForegroundColor Cyan

# Start the app
Write-Host "Starting TestCaseEditorApp..." -ForegroundColor Green
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

# Function to get window title
function Get-WindowTitle {
    try {
        $windows = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
        if ($windows) {
            $window = $windows[0]
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
                return $title.ToString()
            }
        }
        return "No window found"
    } catch {
        return "Error: $($_.Exception.Message)"
    }
}

# Wait for app to start
Start-Sleep -Seconds 3

# Check initial title
$initialTitle = Get-WindowTitle
Write-Host "Initial Title: '$initialTitle'" -ForegroundColor Yellow

Write-Host ""
Write-Host "Please click 'Test Case Generator' in the side menu and press Enter..." -ForegroundColor Cyan
Read-Host

$afterClickTitle = Get-WindowTitle
Write-Host "After clicking Test Case Generator: '$afterClickTitle'" -ForegroundColor Green

if ($afterClickTitle -eq "Test Case Generator") {
    Write-Host "✅ SUCCESS: Title immediately changed to 'Test Case Generator'!" -ForegroundColor Green
} else {
    Write-Host "❌ ISSUE: Expected 'Test Case Generator', got '$afterClickTitle'" -ForegroundColor Red
}

# Clean up
Write-Host ""
Write-Host "Press Enter to close app..." -ForegroundColor Yellow
Read-Host

if ($process -and !$process.HasExited) {
    $process.Kill()
    $process.WaitForExit(5000)
}

Write-Host "Test complete." -ForegroundColor Green