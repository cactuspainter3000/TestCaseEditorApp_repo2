#!/usr/bin/env pwsh

Write-Host "=== Capture Jama API Response Data ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "The app successfully imports 20 requirements, but Tables/Supplemental Info are empty." -ForegroundColor Yellow
Write-Host "We need to see what's actually in the API responses." -ForegroundColor Yellow
Write-Host ""

# Kill any running instances first
Write-Host "Stopping any running app instances..." -ForegroundColor Blue
Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Clear old log files so we can see new ones
Write-Host "Clearing old API response files..." -ForegroundColor Blue
Get-ChildItem -Filter "jama_api_response_*" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -Filter "jama_converted_requirements_*" -ErrorAction SilentlyContinue | Remove-Item -Force

# Build the app
Write-Host "Building application..." -ForegroundColor Blue
$buildResult = dotnet build --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful!" -ForegroundColor Green

Write-Host ""
Write-Host "=== INSTRUCTIONS ===" -ForegroundColor Cyan
Write-Host "1. The app will start with enhanced API logging enabled" -ForegroundColor White
Write-Host "2. Import requirements from Jama (the same way you got 20 before)" -ForegroundColor White
Write-Host "3. Look for these files to be created:" -ForegroundColor White
Write-Host "   - jama_api_response_*.json (raw API data)" -ForegroundColor Gray
Write-Host "   - jama_converted_requirements_*.json (converted data)" -ForegroundColor Gray
Write-Host "4. Close the app when import is complete" -ForegroundColor White
Write-Host ""
Write-Host "Starting application..." -ForegroundColor Green

# Start the app
Start-Process -FilePath "dotnet" -ArgumentList "run" -NoNewWindow

Write-Host ""
Write-Host "=== Waiting for API response files ===" -ForegroundColor Yellow
Write-Host "Import some requirements from Jama, then press Enter to check for files..." -ForegroundColor White
Read-Host

Write-Host ""
Write-Host "=== Checking for API response files ===" -ForegroundColor Cyan

$apiFiles = Get-ChildItem -Filter "jama_api_response_*" -ErrorAction SilentlyContinue
$convertedFiles = Get-ChildItem -Filter "jama_converted_requirements_*" -ErrorAction SilentlyContinue

if ($apiFiles.Count -eq 0) {
    Write-Host "No API response files found!" -ForegroundColor Red
    Write-Host "This means the enhanced logging in JamaConnectService is not being triggered." -ForegroundColor Red
    Write-Host ""
    Write-Host "Possible reasons:" -ForegroundColor Yellow
    Write-Host "1. Import didn't actually use JamaConnectService" -ForegroundColor Gray
    Write-Host "2. Build didn't include the enhanced logging code" -ForegroundColor Gray
    Write-Host "3. Import failed before reaching the logging code" -ForegroundColor Gray
    
    # Check if app is still running
    $runningApp = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
    if ($runningApp) {
        Write-Host "App is still running. Try the import again." -ForegroundColor Yellow
    } else {
        Write-Host "App has stopped. Check for any error messages." -ForegroundColor Yellow
    }
} else {
    Write-Host "Found $($apiFiles.Count) API response files!" -ForegroundColor Green
    
    foreach ($file in $apiFiles) {
        Write-Host ""
        Write-Host "=== Analyzing $($file.Name) ===" -ForegroundColor Cyan
        
        try {
            $content = Get-Content $file.FullName -Raw | ConvertFrom-Json
            
            if ($content -is [Array] -and $content.Count -gt 0) {
                Write-Host "Found $($content.Count) items in API response" -ForegroundColor Green
                
                # Analyze first few items
                for ($i = 0; $i -lt [Math]::Min(3, $content.Count); $i++) {
                    $item = $content[$i]
                    Write-Host ""
                    Write-Host "--- Item $($i + 1) ---" -ForegroundColor Yellow
                    Write-Host "ID: $($item.id)" -ForegroundColor Gray
                    Write-Host "Name: $($item.name)" -ForegroundColor Gray
                    
                    if ($item.description) {
                        $desc = $item.description
                        $hasHtml = $desc -match '<[^>]+>'
                        $hasTable = $desc -match '<table'
                        $length = $desc.Length
                        
                        Write-Host "Description length: $length chars" -ForegroundColor Gray
                        Write-Host "Contains HTML: $hasHtml" -ForegroundColor $(if($hasHtml){"Green"}else{"Yellow"})
                        Write-Host "Contains tables: $hasTable" -ForegroundColor $(if($hasTable){"Green"}else{"Yellow"})
                        
                        if ($length -gt 0) {
                            $preview = if ($length -gt 200) { $desc.Substring(0, 200) + "..." } else { $desc }
                            Write-Host "Preview: $preview" -ForegroundColor DarkGray
                        }
                    } else {
                        Write-Host "No description field" -ForegroundColor Yellow
                    }
                }
            } else {
                Write-Host "API response is empty or invalid format" -ForegroundColor Red
            }
        } catch {
            Write-Host "Failed to parse API response file: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

if ($convertedFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Found $($convertedFiles.Count) converted files ===" -ForegroundColor Green
    
    foreach ($file in $convertedFiles) {
        try {
            $content = Get-Content $file.FullName -Raw | ConvertFrom-Json
            $withTables = ($content | Where-Object { $_.LooseContent.Tables.Count -gt 0 }).Count
            $withParagraphs = ($content | Where-Object { $_.LooseContent.Paragraphs.Count -gt 0 }).Count
            
            Write-Host "$($file.Name):" -ForegroundColor Cyan
            Write-Host "  Total requirements: $($content.Count)" -ForegroundColor Gray
            Write-Host "  With tables: $withTables" -ForegroundColor $(if($withTables -gt 0){"Green"}else{"Red"})
            Write-Host "  With paragraphs: $withParagraphs" -ForegroundColor $(if($withParagraphs -gt 0){"Green"}else{"Red"})
        } catch {
            Write-Host "Failed to analyze converted file: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "=== Analysis Complete ===" -ForegroundColor Cyan
Write-Host "  - Rich text formatting: <p>, <div>, <strong>" -ForegroundColor White
Write-Host "  - Embedded images: <img>" -ForegroundColor White
Write-Host "  - Missing fields that might contain rich content" -ForegroundColor White
Write-Host ""

Write-Host "If Description contains HTML, we need to:" -ForegroundColor Yellow
Write-Host "  1. Parse HTML to extract tables → LooseContent.Tables" -ForegroundColor White
Write-Host "  2. Extract text paragraphs → LooseContent.Paragraphs" -ForegroundColor White
Write-Host "  3. Preserve formatting and structure" -ForegroundColor White
Write-Host ""

# Launch the application
.\TestCaseEditorApp.exe