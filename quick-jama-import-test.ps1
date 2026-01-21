# Quick test to examine Jama API response logs
Write-Host "=== Jama Import Rich Content Investigation ===" -ForegroundColor Cyan

Write-Host ""
Write-Host "The app should now be running. To investigate rich content:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. In the app, navigate to Requirements domain" -ForegroundColor White
Write-Host "2. Click 'Import Additional Requirements'" -ForegroundColor White  
Write-Host "3. Select 'Jama Connect' option" -ForegroundColor White
Write-Host "4. Enter Jama connection details and import" -ForegroundColor White
Write-Host "5. Watch the console logs for raw API responses" -ForegroundColor White
Write-Host ""
Write-Host "Look for these patterns in console output:" -ForegroundColor Magenta
Write-Host "  - Description field content" -ForegroundColor White
Write-Host "  - HTML tags: <table>, <tr>, <td>, <p>, <div>" -ForegroundColor White
Write-Host "  - Rich text formatting" -ForegroundColor White
Write-Host "  - JSON structure of API responses" -ForegroundColor White
Write-Host ""
Write-Host "Key Questions to Answer:" -ForegroundColor Yellow
Write-Host "  1. Does Description field contain HTML?" -ForegroundColor White
Write-Host "  2. Are there embedded <table> tags?" -ForegroundColor White
Write-Host "  3. Do we need additional API endpoints for attachments/relationships?" -ForegroundColor White
Write-Host ""
Write-Host "If Description has HTML tables, we need to enhance JamaConnectService.cs" -ForegroundColor Green
Write-Host "to parse HTML similar to how JamaAllDataDocxParser.cs handles Word docs" -ForegroundColor Green
Write-Host ""
Write-Host "Press Enter after testing the Jama import..." -ForegroundColor Yellow
Read-Host

Write-Host ""
Write-Host "What did you observe in the console logs?" -ForegroundColor Cyan
Write-Host "1. Did Description field contain HTML tags?" -ForegroundColor White
Write-Host "2. Were there <table> elements in the response?" -ForegroundColor White
Write-Host "3. What was the structure of the raw JSON?" -ForegroundColor White
Write-Host ""
Write-Host "Type your findings:" -ForegroundColor Yellow
$findings = Read-Host

Write-Host ""
Write-Host "Based on your findings:" -ForegroundColor Green
if ($findings -like "*html*" -or $findings -like "*table*") {
    Write-Host "✓ HTML content detected - Need to implement HTML parsing" -ForegroundColor Green
    Write-Host "  Next step: Enhance JamaConnectService with HTML table extraction" -ForegroundColor White
} else {
    Write-Host "✓ No HTML detected - May need additional API endpoints" -ForegroundColor Yellow
    Write-Host "  Next step: Investigate /relationships and /attachments endpoints" -ForegroundColor White
}