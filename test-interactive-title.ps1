#!/usr/bin/env powershell

# Interactive test script to check window title changes during navigation
Write-Host "=== Testing Dynamic Window Title Changes ===" -ForegroundColor Cyan

# Start the app in background
Write-Host "Starting TestCaseEditorApp..." -ForegroundColor Green
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

# Wait for app to load
Write-Host "Waiting for app to load..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

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

# Check initial title
$initialTitle = Get-AppWindowTitle
Write-Host "Initial title: '$initialTitle'" -ForegroundColor Yellow

# Instructions for user
Write-Host ""
Write-Host "=== Interactive Test ===" -ForegroundColor Cyan
Write-Host "The app should be running now. Please:" -ForegroundColor White
Write-Host "1. Click on different sections in the side menu (Project, TestCase, etc.)" -ForegroundColor White
Write-Host "2. Press ENTER after each navigation to check the title" -ForegroundColor White
Write-Host "3. Type 'exit' to quit the test" -ForegroundColor White
Write-Host ""

# Interactive title checking loop
while ($true) {
    Write-Host "Press ENTER to check current title (or type 'exit' to quit): " -NoNewLine -ForegroundColor Green
    $input = Read-Host
    
    if ($input -eq "exit") {
        break
    }
    
    $currentTitle = Get-AppWindowTitle
    if ($currentTitle) {
        Write-Host "Current title: '$currentTitle'" -ForegroundColor Yellow
        
        if ($currentTitle -ne $initialTitle) {
            Write-Host "✅ Title changed from initial!" -ForegroundColor Green
        } else {
            Write-Host "❌ Title unchanged from initial" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ Could not get title (app may have closed)" -ForegroundColor Red
        break
    }
    Write-Host ""
}

# Clean up
Write-Host "Cleaning up..." -ForegroundColor Green
if ($process -and !$process.HasExited) {
    $process.Kill()
    $process.WaitForExit(5000)
}

Write-Host "Test complete." -ForegroundColor Green