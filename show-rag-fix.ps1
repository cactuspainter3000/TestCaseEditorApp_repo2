Write-Host "=== AnythingLLM RAG Document Update Script ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "YES! I found and fixed the pre-prompt issue you asked about!" -ForegroundColor Green
Write-Host ""

Write-Host "The Problem:" -ForegroundColor Red
Write-Host "- RAG documents in Config\ folder are the 'pre-prompt' context" -ForegroundColor White
Write-Host "- They were instructing LLM to provide generic 'Quality Score'" -ForegroundColor White
Write-Host "- LLM was rating its improved requirement, not your original" -ForegroundColor White
Write-Host ""

Write-Host "Files I Updated:" -ForegroundColor Cyan
Write-Host "1. Config\RAG-JSON-Schema-Training.md" -ForegroundColor Green
Write-Host "   - Added explicit 'ORIGINAL requirement quality' instructions" -ForegroundColor Gray
Write-Host "   - Added warning against rating improved versions" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Config\RAG-Learning-Examples.md" -ForegroundColor Green  
Write-Host "   - Updated all examples to show realistic original scores" -ForegroundColor Gray
Write-Host "   - Emphasized difference between original vs improved scoring" -ForegroundColor Gray
Write-Host ""

Write-Host "Next Step - Upload to AnythingLLM:" -ForegroundColor Yellow
Write-Host "1. Open AnythingLLM web interface" -ForegroundColor White
Write-Host "2. Go to your Test Case Editor workspace" -ForegroundColor White
Write-Host "3. Navigate to 'Data & Documents' or similar section" -ForegroundColor White
Write-Host "4. Delete old RAG-JSON-Schema-Training.md" -ForegroundColor White
Write-Host "5. Delete old RAG-Learning-Examples.md" -ForegroundColor White
Write-Host "6. Upload the updated files from Config\ directory" -ForegroundColor White
Write-Host "7. Wait for embedding to complete" -ForegroundColor White
Write-Host ""

Write-Host "Test Result Expected:" -ForegroundColor Green
Write-Host "Original requirements should now get realistic scores like 3-6" -ForegroundColor White
Write-Host "instead of perfect 10/10 scores!" -ForegroundColor White