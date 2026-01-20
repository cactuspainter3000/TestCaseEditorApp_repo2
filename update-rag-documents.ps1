#!/usr/bin/env powershell
# Script to re-upload updated RAG documents to AnythingLLM workspace

Write-Host "=== AnythingLLM RAG Document Update Script ===" -ForegroundColor Cyan
Write-Host "Purpose: Upload the updated RAG training documents with ORIGINAL score instructions" -ForegroundColor Yellow
Write-Host ""

# Files that need to be re-uploaded
$ragFiles = @(
    "Config\RAG-JSON-Schema-Training.md"
    "Config\RAG-Learning-Examples.md"
    "Config\RAG-Optimization-Summary.md"
)

Write-Host "📋 Files to update in AnythingLLM workspace:" -ForegroundColor Green
foreach ($file in $ragFiles) {
    if (Test-Path $file) {
        Write-Host "  ✅ $file" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $file (not found)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "🔧 Key Changes Made:" -ForegroundColor Cyan
Write-Host "1. RAG-JSON-Schema-Training.md:" -ForegroundColor White
Write-Host "   - Added explicit instruction to rate ORIGINAL requirement quality" -ForegroundColor Gray
Write-Host "   - Clarified that scores 3-7 are normal for real requirements" -ForegroundColor Gray
Write-Host "   - Added warning against rating improved versions" -ForegroundColor Gray
Write-Host ""
Write-Host "2. RAG-Learning-Examples.md:" -ForegroundColor White
Write-Host "   - Updated all examples to show 'ORIGINAL Quality Score'" -ForegroundColor Gray
Write-Host "   - Added realistic scoring examples (4/10, 3/10, etc.)" -ForegroundColor Gray
Write-Host "   - Emphasized difference between original vs improved scores" -ForegroundColor Gray
Write-Host ""

Write-Host "📁 Manual Upload Required:" -ForegroundColor Yellow
Write-Host "1. Open AnythingLLM web interface (typically http://localhost:3001)" -ForegroundColor White
Write-Host "2. Navigate to your Test Case Editor workspace" -ForegroundColor White
Write-Host "3. Go to 'Data & Documents' section" -ForegroundColor White
Write-Host "4. Delete the old versions of these RAG files" -ForegroundColor White
Write-Host "5. Upload the updated files from the Config\ directory" -ForegroundColor White
Write-Host "6. Wait for documents to process and embed" -ForegroundColor White
Write-Host ""

Write-Host "🧪 After Upload - Test:" -ForegroundColor Cyan
Write-Host "1. Use a deliberately poor requirement like:" -ForegroundColor White
Write-Host "   'The system should work fast and handle data efficiently'" -ForegroundColor Gray
Write-Host "2. Run LLM Analysis" -ForegroundColor White
Write-Host "3. Original Quality Score should now be realistic (3-5, not 10)" -ForegroundColor White
Write-Host ""

Write-Host "Important Notes:" -ForegroundColor Yellow
Write-Host "- The RAG documents provide the 'pre-prompt' context you asked about" -ForegroundColor Gray
Write-Host "- These instructions are loaded before every LLM analysis request" -ForegroundColor Gray  
Write-Host "- Must re-upload to workspace for changes to take effect" -ForegroundColor Gray
Write-Host "- AnythingLLM needs time to process and embed the new documents" -ForegroundColor Gray
Write-Host ""

Write-Host "Expected Result: Original requirements will get realistic quality scores instead of perfect scores!" -ForegroundColor Green