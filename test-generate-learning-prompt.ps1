# Test script for Generate Learning Prompt functionality

Write-Host "=== Generate Learning Prompt Test ===" -ForegroundColor Green
Write-Host ""

Write-Host "FUNCTIONALITY RESTORED: Generate Learning Prompt" -ForegroundColor Yellow
Write-Host ""

Write-Host "Location: LLM Learning -> Generate Learning Prompt" -ForegroundColor Cyan
Write-Host ""

Write-Host "PURPOSE:" -ForegroundColor Yellow
Write-Host "Shows the COMPLETE system instructions given to AnythingLLM during workspace creation"
Write-Host "versus the CONDENSED prompts sent during regular requirement analysis."
Write-Host ""

Write-Host "WORKFLOW COMPARISON:" -ForegroundColor Yellow
Write-Host ""

Write-Host "WORKSPACE CREATION (One-time setup):" -ForegroundColor Green
Write-Host "- Full detailed system prompt configured in AnythingLLM workspace"
Write-Host "- Complete analysis criteria and formatting rules"
Write-Host "- Anti-fabrication guidelines and examples"  
Write-Host "- Output format specifications"
Write-Host "- Quality assessment framework"
Write-Host ""

Write-Host "RUNTIME ANALYSIS (Every requirement):" -ForegroundColor Blue  
Write-Host "- Condensed prompt with requirement text"
Write-Host "- Supplemental tables/data if present"
Write-Host "- Verification method assumptions"
Write-Host "- Simple request for JSON analysis"
Write-Host ""

Write-Host "LEARNING VALUE:" -ForegroundColor Magenta
Write-Host "- Understanding: See the full context/instructions the AI has"
Write-Host "- Learning: Copy complete prompt for educational use" 
Write-Host "- Debugging: Compare full instructions vs condensed runtime prompts"
Write-Host "- Training: Use as template for other AI analysis systems"
Write-Host ""

Write-Host "OUTPUT:" -ForegroundColor Yellow
Write-Host "- Complete system prompt from AnythingLLMService.GetOptimalSystemPrompt()"
Write-Host "- Explanation of condensed runtime structure"
Write-Host "- Context about optimization guide and training materials"
Write-Host "- All content copied to clipboard for immediate use"
Write-Host ""

Write-Host "=== Ready for Testing ===" -ForegroundColor Green
Write-Host "1. Start TestCaseEditorApp"
Write-Host "2. Navigate to LLM Learning section"  
Write-Host "3. Click Generate Learning Prompt"
Write-Host "4. Check clipboard for complete system instructions"
Write-Host ""

Write-Host "Expected Result: Full system prompt copied to clipboard" -ForegroundColor Yellow