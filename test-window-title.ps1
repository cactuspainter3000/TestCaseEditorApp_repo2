#!/usr/bin/env powershell

# Test script to check window title binding
# This will start the app, wait a moment, then check the window title

Write-Host "Starting TestCaseEditorApp to check window title..." -ForegroundColor Green

# Start the app in background
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

# Wait for app to load
Start-Sleep -Seconds 3

# Get window title using Windows API
Add-Type @"
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    
    public class WindowAPI {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
"@

try {
    # Find window by process ID
    $windows = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
    if ($windows) {
        foreach ($window in $windows) {
            $hwnd = $window.MainWindowHandle
            if ($hwnd -ne [IntPtr]::Zero) {
                $title = New-Object System.Text.StringBuilder 256
                $result = [WindowAPI]::GetWindowText($hwnd, $title, 256)
                if ($result -gt 0) {
                    Write-Host "Found window title: '$($title.ToString())'" -ForegroundColor Yellow
                } else {
                    Write-Host "Could not get window title" -ForegroundColor Red
                }
            }
        }
    } else {
        Write-Host "TestCaseEditorApp process not found" -ForegroundColor Red
    }
} catch {
    Write-Host "Error checking window title: $($_.Exception.Message)" -ForegroundColor Red
}

# Clean up
if ($process -and !$process.HasExited) {
    Write-Host "Stopping TestCaseEditorApp..." -ForegroundColor Green
    $process.Kill()
    $process.WaitForExit(5000)
}

Write-Host "Test complete." -ForegroundColor Green