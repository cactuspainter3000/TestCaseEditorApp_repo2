#!/usr/bin/env pwsh

Write-Host "=== Testing Parser Architecture After Refactoring ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Natural Language Response (with IMPROVED REQUIREMENT fix)
$naturalLanguageResponse = @"
QUALITY SCORE: [75/100]

ISSUES FOUND:
- Clarity Issue (Medium): The term "UUT" is not explicitly stated as being part of Tier 1 Boundary Scan coverage | Fix: Define UUT within the context
- Completeness Issue (High): No specific actions or requirements for achieving Tier 1 Boundary Scan are provided | Fix: Specify the expected outcomes

STRENGTHS:
- [The use of supplementary context helps clarify some terms]
- [The requirement ties in with established tiers]

IMPROVED REQUIREMENT: The Sierra Project's Test System shall be capable, for any given Unit Under Test (UUT), of performing Tier 1 Boundary Scan coverage by verifying connectivity between devices using Bscan chain check and programming/configuring memory devices accessible from a scannable device on the JTAG bus.

RECOMMENDATIONS:
- Category: Clarity | Description: Define "UUT" within Tier 1 context | Rationale: Improves understanding
- Category: Completeness | Description: Add specific criteria | Rationale: Makes testing easier

HALLUCINATION CHECK:
NO_FABRICATION

OVERALL ASSESSMENT:
The requirement has a moderate quality score with some areas for improvement.
"@

# Test 2: JSON Response
$jsonResponse = @"
```json
{
    "QualityScore": 85,
    "IssuesFound": [
        {
            "Category": "Clarity",
            "Severity": "Medium", 
            "Description": "Missing technical details",
            "Fix": "Add specific parameters"
        }
    ],
    "Strengths": [
        "Well-defined scope",
        "Clear acceptance criteria"
    ],
    "ImprovedRequirement": "The system shall perform boundary scan testing with 95% coverage on all JTAG-enabled devices.",
    "Recommendations": [
        {
            "Category": "Technical",
            "Description": "Add performance metrics",
            "Rationale": "Enables better validation"
        }
    ],
    "HallucinationCheck": "NO_FABRICATION",
    "OverallAssessment": "High quality requirement with minor improvements needed"
}
```
"@

Write-Host "Test Case 1: Natural Language Response" -ForegroundColor Yellow
Write-Host "Expected: Should extract IMPROVED REQUIREMENT text after colon on same line" -ForegroundColor Gray
Write-Host "Content preview: ${naturalLanguageResponse.Substring(0, [Math]::Min(200, $naturalLanguageResponse.Length))}..." -ForegroundColor Gray
Write-Host ""

Write-Host "Test Case 2: JSON Response" -ForegroundColor Yellow  
Write-Host "Expected: Should parse JSON and extract ImprovedRequirement field" -ForegroundColor Gray
Write-Host "Content preview: ${jsonResponse.Substring(0, [Math]::Min(200, $jsonResponse.Length))}..." -ForegroundColor Gray
Write-Host ""

Write-Host "Key Test Points:" -ForegroundColor Green
Write-Host "✅ IMPROVED REQUIREMENT parsing bug fix should work (same-line extraction)" -ForegroundColor Green
Write-Host "✅ JSON parsing should work through JsonResponseParser" -ForegroundColor Green  
Write-Host "✅ Natural Language parsing should work through NaturalLanguageResponseParser" -ForegroundColor Green
Write-Host "✅ ResponseParserManager should automatically detect format" -ForegroundColor Green
Write-Host "✅ Both parsers should return proper RequirementAnalysis objects" -ForegroundColor Green
Write-Host ""

Write-Host "Architecture Validation:" -ForegroundColor Magenta
Write-Host "- Old parsing methods (400+ lines) have been removed" -ForegroundColor Magenta
Write-Host "- New specialized parser classes handle format-specific logic" -ForegroundColor Magenta
Write-Host "- Chain-of-responsibility pattern allows automatic format detection" -ForegroundColor Magenta
Write-Host "- RequirementAnalysisService now focuses on business logic only" -ForegroundColor Magenta
Write-Host ""

Write-Host "To test manually:" -ForegroundColor Cyan
Write-Host "1. Start the TestCaseEditorApp" -ForegroundColor Cyan  
Write-Host "2. Import requirements and run analysis" -ForegroundColor Cyan
Write-Host "3. Verify both JSON and Natural Language responses work" -ForegroundColor Cyan
Write-Host "4. Check that IMPROVED REQUIREMENT text is extracted correctly" -ForegroundColor Cyan
Write-Host ""

Write-Host "Expected Behavior:" -ForegroundColor White
Write-Host "- JSON responses: Parsed by JsonResponseParser → clean structured data" -ForegroundColor White
Write-Host "- Natural Language responses: Parsed by NaturalLanguageResponseParser → extracted fields" -ForegroundColor White
Write-Host "- IMPROVED REQUIREMENT: Text after colon on same line should be extracted" -ForegroundColor White
Write-Host "- Performance: Should maintain 27-48 second response times" -ForegroundColor White

Write-Host ""
Write-Host "=== Parser Architecture Test Ready ===" -ForegroundColor Cyan