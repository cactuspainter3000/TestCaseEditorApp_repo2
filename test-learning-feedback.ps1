#!/usr/bin/env pwsh
# Test script for LLM Learning Feedback functionality
# This simulates the unified 15% edit threshold detection and user consent flow

Write-Host "=== LLM Learning Feedback System Test ===" -ForegroundColor Green
Write-Host ""

# Test the basic workflow:
Write-Host "Testing Unified LLM Learning Feedback Workflow:" -ForegroundColor Yellow
Write-Host ""

Write-Host "SCENARIO 1: Manual Requirement Editing" -ForegroundColor Cyan
Write-Host "1. User manually edits improved requirement by >15%" -ForegroundColor White
Write-Host "   Original: 'The system shall process user input efficiently'"
Write-Host "   Edited:   'The system shall process user input with a response time of less than 200ms under normal load conditions'"
Write-Host "   → SaveRequirementEdit() → EditDetectionService.ProcessTextEditAsync() → User Consent Dialog"
Write-Host ""

Write-Host "SCENARIO 2: External LLM Integration (UNIFIED)" -ForegroundColor Cyan
Write-Host "1. User pastes external LLM analysis from clipboard" -ForegroundColor White
Write-Host "   Original: 'The system shall process user input efficiently'" 
Write-Host "   External: 'The system shall process user input with sub-200ms response time under peak load'"
Write-Host "   → FeedLearningToAnythingLLM() → PromptUserForLearningConsentAsync() → User Consent Dialog"
Write-Host ""

Write-Host "✅ UNIFIED CONSENT FLOW (Both Scenarios):" -ForegroundColor Green
Write-Host "2. System detects significant change (>15% threshold)" -ForegroundColor White
Write-Host "   - Calculates text similarity using Levenshtein distance"
Write-Host "   - Determines change percentage"
Write-Host ""

Write-Host "3. System prompts user for learning consent" -ForegroundColor White
Write-Host "   Dialog: 'You've made significant improvements (XX.X% changes) to the AI-generated requirement text."
Write-Host "           Would you like to send your improved version back to the AI to help it learn?'"
Write-Host "   Options: [Yes] [No]"
Write-Host ""

Write-Host "4. If user consents:" -ForegroundColor White
Write-Host "   - Creates LLMLearningFeedback object with original + improved versions"
Write-Host "   - Populates context-specific metadata"
Write-Host "   - Sends feedback to AnythingLLM learning endpoint"
Write-Host "   - Logs success/failure for monitoring"
Write-Host ""

Write-Host "Integration Points:" -ForegroundColor Yellow
Write-Host "✅ HeaderVM.OnRequirementDescriptionChanged() - tracks main requirement edits" -ForegroundColor Green
Write-Host "✅ AnalysisVM.SaveRequirementEdit() - tracks improved requirement edits" -ForegroundColor Green
Write-Host "✅ AnalysisVM.FeedLearningToAnythingLLM() - handles external LLM integration" -ForegroundColor Green
Write-Host "✅ EditDetectionService - orchestrates similarity + learning workflow" -ForegroundColor Green
Write-Host "✅ LLMLearningService - UNIFIED user consent + feedback sending" -ForegroundColor Green
Write-Host ""

Write-Host "🎯 Key Improvement: SINGLE CONSENT METHOD" -ForegroundColor Magenta
Write-Host "Both manual edits AND external LLM integration now use:" -ForegroundColor White
Write-Host "   LLMLearningService.PromptUserForLearningConsentAsync()" -ForegroundColor Yellow
Write-Host "   → Consistent user experience" 
Write-Host "   → No confusion about when consent is required"
Write-Host "   → Single method for teaching AnythingLLM"
Write-Host ""

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "• Threshold: 15% change detection"
Write-Host "• Similarity: Levenshtein distance algorithm"
Write-Host "• Context tracking: 'requirement description', 'improved requirement', 'External LLM Integration', etc."
Write-Host "• User consent: MessageBox dialog with Yes/No options"
Write-Host ""

Write-Host "To test live:" -ForegroundColor Magenta
Write-Host "1. Start TestCaseEditorApp"
Write-Host "2. Import a requirement"
Write-Host "3. MANUAL EDIT: Edit requirement text substantially (>15% change) - see consent dialog"
Write-Host "4. EXTERNAL LLM: Paste external analysis from clipboard - see consent dialog"  
Write-Host "5. Both pathways now have identical user consent experience"
Write-Host ""

Write-Host "=== Test Complete ===" -ForegroundColor Green