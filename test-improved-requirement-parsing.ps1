# Test the IMPROVED REQUIREMENT parsing fix
Write-Host "Testing IMPROVED REQUIREMENT Parsing Fix" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Create test input similar to what we saw in the logs
$testResponse = @"
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

Write-Host "Test Response Content:" -ForegroundColor Yellow
Write-Host $testResponse
Write-Host ""

Write-Host "Key Test Points:" -ForegroundColor Green
Write-Host "1. ✅ IMPROVED REQUIREMENT is on same line with colon" -ForegroundColor Green
Write-Host "2. ✅ Text contains full requirement after colon: 'The Sierra Project's Test System shall be capable...'" -ForegroundColor Green  
Write-Host "3. ✅ Should be extracted correctly by our parsing fix" -ForegroundColor Green
Write-Host ""

Write-Host "Expected Result:" -ForegroundColor Yellow
Write-Host "- ImprovedRequirement should contain: 'The Sierra Project's Test System shall be capable, for any given Unit Under Test (UUT)...'" -ForegroundColor White
Write-Host "- Warning about 'No improved requirement provided' should NOT appear" -ForegroundColor White
Write-Host ""

Write-Host "Ready to test - run the application and analyze a requirement to verify the fix works!" -ForegroundColor Cyan