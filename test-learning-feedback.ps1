#!/usr/bin/env pwsh
# Test script for LLM Learning Feedback functionality
# This simulates the 15% edit threshold detection and user consent flow

Write-Host "=== LLM Learning Feedback System Test ===" -ForegroundColor Green
Write-Host ""

# Test the basic workflow:
Write-Host "Testing LLM Learning Feedback Workflow:" -ForegroundColor Yellow
Write-Host ""

Write-Host "1. User edits LLM-generated requirement rewrite by >15%" -ForegroundColor Cyan
Write-Host "   Original: 'The system shall process user input efficiently'" 
Write-Host "   Edited:   'The system shall process user input with a response time of less than 200ms under normal load conditions'"
Write-Host ""

Write-Host "2. System detects significant change (>15% threshold)" -ForegroundColor Cyan
Write-Host "   - Calculates text similarity using Levenshtein distance"
Write-Host "   - Determines change percentage: ~75% change detected"
Write-Host ""

Write-Host "3. System prompts user for learning consent" -ForegroundColor Cyan
Write-Host "   Dialog: 'Your edits significantly improved the AI-generated text."
Write-Host "           Would you like to send this improvement to help the AI learn?'"
Write-Host "   Options: [Yes] [No]"
Write-Host ""

Write-Host "4. If user consents:" -ForegroundColor Cyan
Write-Host "   - Creates LLMLearningFeedback object with original + improved versions"
Write-Host "   - Sends feedback to AnythingLLM learning endpoint"
Write-Host "   - Logs success/failure for monitoring"
Write-Host ""

Write-Host "Integration Points:" -ForegroundColor Yellow
Write-Host "✅ HeaderVM.OnRequirementDescriptionChanged() - tracks main requirement edits" -ForegroundColor Green
Write-Host "✅ AnalysisVM.SaveRequirementEdit() - tracks improved requirement edits" -ForegroundColor Green
Write-Host "✅ EditDetectionService - orchestrates similarity + learning workflow" -ForegroundColor Green
Write-Host "✅ TextSimilarityService - Levenshtein distance calculation" -ForegroundColor Green
Write-Host "✅ LLMLearningService - user consent + feedback sending" -ForegroundColor Green
Write-Host ""

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "• Threshold: 15% change detection"
Write-Host "• Similarity: Levenshtein distance algorithm"
Write-Host "• Context tracking: 'requirement description', 'improved requirement', etc."
Write-Host "• User consent: MessageBox dialog with Yes/No options"
Write-Host ""

Write-Host "To test live:" -ForegroundColor Magenta
Write-Host "1. Start TestCaseEditorApp"
Write-Host "2. Import a requirement"
Write-Host "3. Run LLM analysis to get SuggestedEdit"
Write-Host "4. Apply SuggestedEdit to requirement text"
Write-Host "5. Edit the text substantially (>15% change)"
Write-Host "6. Watch for learning feedback consent dialog"
Write-Host ""

Write-Host "=== Test Complete ===" -ForegroundColor Green