#!/usr/bin/env powershell

# Test immediate title update when project opens
Write-Host "=== Testing Immediate Project Title Update ===" -ForegroundColor Cyan

# Start the app
Write-Host "Starting TestCaseEditorApp..." -ForegroundColor Green
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

Write-Host "Process ID: $($process.Id)" -ForegroundColor Yellow

# Function to get window title using Windows API
function Get-WindowTitle {
    try {
        $windows = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
        if ($windows) {
            $window = $windows[0]  # Get first window
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
Write-Host "Waiting for app to fully load..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

# Check initial title
$initialTitle = Get-WindowTitle
Write-Host "Initial Title: '$initialTitle'" -ForegroundColor Cyan

# Simulate opening a project
Write-Host ""
Write-Host "Step 1: Click 'Test Case Generator' in side menu..." -ForegroundColor Yellow
Write-Host "Please manually click 'Test Case Generator' in the side menu and press Enter to continue..." -ForegroundColor White
Read-Host

$afterSideMenuTitle = Get-WindowTitle
Write-Host "After clicking Test Case Generator: '$afterSideMenuTitle'" -ForegroundColor Cyan

Write-Host ""
Write-Host "Step 2: Click 'Project' in the navigation..." -ForegroundColor Yellow
Write-Host "Please manually click 'Project' in the navigation and press Enter to continue..." -ForegroundColor White
Read-Host

$afterProjectTitle = Get-WindowTitle
Write-Host "After clicking Project: '$afterProjectTitle'" -ForegroundColor Cyan

Write-Host ""
Write-Host "Step 3: Open a project (click Open Project button and select a project)..." -ForegroundColor Yellow
Write-Host "Please manually open a project and press Enter IMMEDIATELY after the project loads..." -ForegroundColor White
Read-Host

$afterProjectOpenTitle = Get-WindowTitle
Write-Host "IMMEDIATELY after project opens: '$afterProjectOpenTitle'" -ForegroundColor Green

# Wait a moment and check again
Start-Sleep -Seconds 1
$afterWaitTitle = Get-WindowTitle
Write-Host "After 1 second: '$afterWaitTitle'" -ForegroundColor Cyan

Write-Host ""
Write-Host "=== Results Summary ===" -ForegroundColor Cyan
Write-Host "Initial:            '$initialTitle'" -ForegroundColor White
Write-Host "After side menu:    '$afterSideMenuTitle'" -ForegroundColor White  
Write-Host "After Project nav:  '$afterProjectTitle'" -ForegroundColor White
Write-Host "IMMEDIATELY after opening project: '$afterProjectOpenTitle'" -ForegroundColor Yellow
Write-Host "After 1 second:     '$afterWaitTitle'" -ForegroundColor White

Write-Host ""
if ($afterProjectOpenTitle -match "Test Case Generator - .+") {
    Write-Host "✅ SUCCESS: Title updated immediately with project name!" -ForegroundColor Green
} else {
    Write-Host "❌ ISSUE: Title did not update immediately with project name" -ForegroundColor Red
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