#!/usr/bin/env pwsh
# Debug script to test learning consent conditions
# This helps identify why the consent prompt might not be appearing

Write-Host "=== Learning Consent Debug Guide ===" -ForegroundColor Green
Write-Host ""

Write-Host "🔍 TROUBLESHOOTING: Learning consent not showing after 'Update' click" -ForegroundColor Yellow
Write-Host ""

Write-Host "📋 EXACT STEPS TO TRIGGER LEARNING CONSENT:" -ForegroundColor Cyan
Write-Host "1. Import a requirement (any requirement)" -ForegroundColor White
Write-Host "2. Run LLM analysis to generate 'Improved Requirement' text"
Write-Host "3. Click Edit button next to the improved requirement"  
Write-Host "4. Make SIGNIFICANT changes (>15% different text)"
Write-Host "5. Click Update (this calls SaveRequirementEdit())"
Write-Host "6. Consent dialog should appear IF all conditions met"
Write-Host ""

Write-Host "⚠️  COMMON REASONS WHY CONSENT DOESN'T APPEAR:" -ForegroundColor Red
Write-Host ""

Write-Host "❌ Condition 1: No Improved Requirement text exists" -ForegroundColor Yellow
Write-Host "   • Solution: Run LLM analysis first to generate improved requirement"
Write-Host "   • Check: Does the Analysis section show Improved Requirement?"
Write-Host ""

Write-Host "❌ Condition 2: Changes are too small (<15% threshold)" -ForegroundColor Yellow
Write-Host "   • Solution: Make MORE substantial changes to the text"
Write-Host "   • Example: Change 'process efficiently' → 'process with under 200ms response time under peak load'"
Write-Host "   • Threshold: Need at least 15% text difference (measured by Levenshtein distance)"
Write-Host ""

Write-Host "❌ Condition 3: No original text to compare against" -ForegroundColor Yellow
Write-Host "   • Original text must exist before editing"
Write-Host "   • Check: Does requirement.Analysis.ImprovedRequirement have content?"
Write-Host ""

Write-Host "❌ Condition 4: EditDetectionService not available" -ForegroundColor Yellow
Write-Host "   • Service should be injected via DI in AnalysisVM constructor"
Write-Host "   • Check: App.xaml.cs registers IEditDetectionService"
Write-Host ""

Write-Host "❌ Condition 5: LLMLearningService not available" -ForegroundColor Yellow
Write-Host "   • Service should be injected and available"
Write-Host "   • Check: App.xaml.cs registers ILLMLearningService"
Write-Host ""

Write-Host "🔍 DEBUG PROCESS:" -ForegroundColor Cyan
Write-Host ""

Write-Host "Step 1: Check if you are editing the RIGHT text field" -ForegroundColor White
Write-Host "   • Learning consent ONLY triggers for Improved Requirement editing"
Write-Host "   • NOT for main requirement description editing"
Write-Host "   • Look for the Edit button in the Analysis section"
Write-Host ""

Write-Host "Step 2: Verify you have substantial changes" -ForegroundColor White  
Write-Host "   • Original: The system shall process user input efficiently"
Write-Host "   • Good change: The system shall process user input with response time under 200ms under normal load conditions and under 500ms under peak load"
Write-Host "   • Bad change: The system shall process user input very efficiently - too small"
Write-Host ""

Write-Host "Step 3: Check logs for debug information" -ForegroundColor White
Write-Host "   • Look for: [AnalysisVM] SaveRequirementEdit called for improved requirement"
Write-Host "   • Look for: Significant edit detected - XX% changes in improved requirement"
Write-Host "   • Look for: Edit does not exceed 15% threshold - means changes too small"
Write-Host ""

Write-Host "📝 EXACT TEST SCENARIO:" -ForegroundColor Magenta
Write-Host ""
Write-Host "1. Create/open project with requirement"
Write-Host "2. Navigate to Test Case Generator"
Write-Host "3. Select a requirement in navigation"
Write-Host "4. Click Run Analysis to generate improved requirement"
Write-Host "5. In Analysis section, click Edit next to improved requirement"
Write-Host "6. Replace text with something significantly different (aim for >20% change)"
Write-Host "7. Click Update button"
Write-Host "8. Learning consent dialog should appear"
Write-Host ""

Write-Host "🎯 MOST LIKELY CAUSE:" -ForegroundColor Green
Write-Host "Changes are probably too small (<15% threshold)" -ForegroundColor White
Write-Host "Try making MORE dramatic changes to the improved requirement text!" -ForegroundColor White
Write-Host ""

Write-Host "=== Debug Guide Complete ===" -ForegroundColor Green