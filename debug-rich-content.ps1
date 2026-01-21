#!/usr/bin/env pwsh

# Debug Rich Content - Enhanced Investigation Script
# Tests Jama import with detailed logging to capture HTML content in API responses

Write-Host "=== Jama Rich Content Investigation ===" -ForegroundColor Cyan
Write-Host "This script will test Jama import with enhanced logging to detect HTML/table content" -ForegroundColor White
Write-Host ""

# Check if app is still running
$runningApps = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
if ($runningApps) {
    Write-Host "✅ TestCaseEditorApp is already running" -ForegroundColor Green
} else {
    Write-Host "🚀 Starting TestCaseEditorApp..." -ForegroundColor Yellow
    Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -NoNewWindow
    Start-Sleep -Seconds 3
}

Write-Host ""
Write-Host "=== TESTING INSTRUCTIONS ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. In the app, go to New Project" -ForegroundColor White
Write-Host "2. Create a new project and choose Jama Connect import" -ForegroundColor White  
Write-Host "3. Configure your Jama credentials and import a project" -ForegroundColor White
Write-Host "4. After import, check the generated files in this directory:" -ForegroundColor White
Write-Host ""

Write-Host "WHAT TO LOOK FOR:" -ForegroundColor Cyan
Write-Host "📁 jama_api_response_*.json - Raw API response files" -ForegroundColor Gray
Write-Host "📄 Check console output for HTML detection messages:" -ForegroundColor Gray
Write-Host "   • 'Description analysis: HTML=True, Tables=True'" -ForegroundColor Gray
Write-Host "   • 'HTML Preview: <table>...' content" -ForegroundColor Gray
Write-Host ""

Write-Host "If Description fields contain HTML tables, we'll need to:" -ForegroundColor Yellow
Write-Host "• Parse HTML content like the Word document import does" -ForegroundColor White
Write-Host "• Extract <table> elements into LooseContent.Tables" -ForegroundColor White
Write-Host "• Convert <p> tags into LooseContent.Paragraphs" -ForegroundColor White
Write-Host ""

Write-Host "Press Enter after testing to analyze results..." -ForegroundColor Green
Read-Host

# Check for generated API response files
Write-Host ""
Write-Host "=== ANALYSIS RESULTS ===" -ForegroundColor Cyan

$apiFiles = Get-ChildItem -Path "." -Filter "jama_api_response_*.json" -ErrorAction SilentlyContinue
if ($apiFiles) {
    Write-Host "✅ Found API response files:" -ForegroundColor Green
    foreach ($file in $apiFiles) {
        Write-Host "   📁 $($file.Name)" -ForegroundColor Gray
        
        # Quick analysis of the JSON content
        try {
            $content = Get-Content $file.FullName -Raw
            $hasDescription = $content -match '"description"'
            $hasHtml = $content -match '<[a-zA-Z]+'
            $hasTable = $content -match '<table'
            
            Write-Host "      Description fields: $hasDescription" -ForegroundColor $(if($hasDescription){"Green"}else{"Yellow"})
            Write-Host "      HTML content: $hasHtml" -ForegroundColor $(if($hasHtml){"Green"}else{"Red"})
            Write-Host "      Table content: $hasTable" -ForegroundColor $(if($hasTable){"Green"}else{"Red"})
        }
        catch {
            Write-Host "      ⚠️ Could not analyze file: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "❌ No API response files found" -ForegroundColor Red
    Write-Host "   Make sure you completed a Jama import in the app" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== NEXT STEPS ===" -ForegroundColor Yellow
Write-Host ""

if ($apiFiles) {
    $firstFile = $apiFiles[0]
    Write-Host "To examine the raw API structure:" -ForegroundColor White
    Write-Host "code `"$($firstFile.FullName)`"" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "If HTML content was found:" -ForegroundColor White
    Write-Host "• We need to implement HTML parsing in ConvertToRequirements()" -ForegroundColor Gray
    Write-Host "• Extract tables using HtmlAgilityPack or similar" -ForegroundColor Gray
    Write-Host "• Convert HTML tables to LooseTable structures" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "If NO HTML content:" -ForegroundColor White
    Write-Host "• Check additional API endpoints for rich content" -ForegroundColor Gray
    Write-Host "• Test /items/{id}/relationships, /attachments endpoints" -ForegroundColor Gray
    Write-Host "• Verify API parameters like ?include=attachments" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Investigation complete! Check the analysis above." -ForegroundColor Cyan