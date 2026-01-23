#!/usr/bin/env pwsh
# Debug Jama Parsing Issues
# Uses the startup view troubleshooting platform to investigate parsing problems

Write-Host "=== Jama Parsing Troubleshooting Script ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "INSTRUCTIONS:" -ForegroundColor Yellow
Write-Host "1. The app should be running (dotnet run in background)"
Write-Host "2. In the app, you should see the startup view"
Write-Host "3. Click the 'Test Jama' button to run the troubleshooting test"
Write-Host "4. Review the results and logs that appear"
Write-Host "5. Click 'Copy All Results & Logs' to copy the troubleshooting data"
Write-Host "6. Come back here and paste the results when ready"
Write-Host ""

Write-Host "WHAT TO LOOK FOR:" -ForegroundColor Green
Write-Host "• HTML entities in descriptions (like &gt; &amp; &lt;)"
Write-Host "• Table parsing issues (missing headers, empty rows)"
Write-Host "• Paragraph parsing problems"
Write-Host "• Rich content extraction failures"
Write-Host "• CreatedBy field parsing (should show user names)"
Write-Host "• Project field mapping (should show project info)"
Write-Host "• RequirementDetail content accuracy"
Write-Host "• Any conversion errors from Jama format to Requirements"
Write-Host ""

Write-Host "CURRENT KNOWN ISSUES:" -ForegroundColor Red
Write-Host "• HTML entities still appearing in parsed requirements"
Write-Host "• CleanHtmlText may not be called for all content paths"
Write-Host "• Need to verify field mapping for CreatedBy, Project, etc."
Write-Host ""

Write-Host "After running the test, paste the troubleshooting data below:" -ForegroundColor Magenta
Write-Host "Press Enter when ready to continue..."
Read-Host

Write-Host ""
Write-Host "=== PASTE TROUBLESHOOTING DATA BELOW ===" -ForegroundColor Cyan
Write-Host "(Paste the copied data from the app, then press Ctrl+C to end input)"

# Read multiline input
$lines = @()
do {
    $line = Read-Host
    if ($line) {
        $lines += $line
    }
} while ($line -or $lines.Count -eq 0)

$troubleshootingData = $lines -join "`n"

Write-Host ""
Write-Host "=== ANALYSIS ===" -ForegroundColor Green

# Save to file for analysis
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$filename = "jama_troubleshooting_$timestamp.txt"
$troubleshootingData | Out-File $filename -Encoding UTF8

Write-Host "Troubleshooting data saved to: $filename"

# Basic analysis
if ($troubleshootingData -match "ERROR") {
    Write-Host "⚠️  ERRORS DETECTED in the output" -ForegroundColor Red
}

if ($troubleshootingData -match "&gt;|&lt;|&amp;|&#") {
    Write-Host "⚠️  HTML ENTITIES DETECTED - CleanHtmlText may not be working correctly" -ForegroundColor Red
    Write-Host "   Check if CleanHtmlText is being called for ALL content paths" -ForegroundColor Yellow
}

if ($troubleshootingData -match "0 table\(s\)|Tables: 0") {
    Write-Host "⚠️  NO TABLES DETECTED - Table parsing may be broken" -ForegroundColor Red
}

if ($troubleshootingData -match "0 requirements|Total Requirements: 0") {
    Write-Host "⚠️  NO REQUIREMENTS RETRIEVED - Check Jama connection" -ForegroundColor Red
}

if ($troubleshootingData -match "CreatedBy.*:\s*$|CreatedBy: \s*$") {
    Write-Host "⚠️  MISSING CREATEDBY DATA - Check field mapping" -ForegroundColor Red
}

if ($troubleshootingData -match "Project.*:\s*$|Project: \s*$") {
    Write-Host "⚠️  MISSING PROJECT DATA - Check field mapping" -ForegroundColor Red
}

# Check for successful CleanHtmlText calls
if ($troubleshootingData -match "CleanHtmlText working: True") {
    Write-Host "✅ CleanHtmlText function is working correctly" -ForegroundColor Green
} else {
    Write-Host "⚠️  No evidence of CleanHtmlText working - check implementation" -ForegroundColor Red
}

# Check for HTML entity counts
if ($troubleshootingData -match "HTML entities found: (\d+)") {
    $entityCount = $Matches[1]
    if ([int]$entityCount -gt 0) {
        Write-Host "⚠️  $entityCount HTML entities found - cleaning may be incomplete" -ForegroundColor Red
    } else {
        Write-Host "✅ No HTML entities found - cleaning appears successful" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "File saved for detailed analysis. Press Enter to exit..."
Read-Host