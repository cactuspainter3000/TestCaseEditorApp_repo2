# RAG Learning Documents for Test Case Editor App

This document contains training examples to optimize AnythingLLM's RAG system for requirement analysis and test case generation.

## 🚨 CRITICAL SCORING INSTRUCTION 

**ALWAYS rate the ORIGINAL requirement quality, not your improved version!**
- Most real-world requirements have quality issues (scores 3-7 are normal)
- Perfect 10/10 scores should be rare and only for truly excellent original requirements
- Be honest about original requirement quality to help users improve their writing

## Requirement Analysis Examples

### GOOD Requirements

**Example 1: Clear and Testable (Well-Written Original)**
```
DECAGON-REQ_RC-5: The Test System shall perform Tier 1 Boundary Scan coverage, defined as direct JTAG interface access to 95% or more of UUT interconnected nodes, within 30 seconds of test initiation.

ORIGINAL Quality Score: 9/10
- Clear scope: Boundary scan coverage
- Measurable criteria: 95% coverage threshold  
- Specific timeframe: 30 seconds
- Testable: Can verify coverage percentage and timing
```

**Example 2: Specific and Actionable (Excellent Original)**
```
SYS-REQ-042: The system shall detect input voltage variations exceeding ±5% of nominal 28VDC and generate a fault signal within 100 milliseconds of detection.

ORIGINAL Quality Score: 10/10
- Precise thresholds: ±5% of 28VDC
- Clear action: Generate fault signal
- Timing constraint: 100ms response time
- Verifiable: Measurable electrical parameters
```

### POOR Requirements (Learn from these mistakes)

**Example 1: Vague and Untestable**
```
SYS-REQ-013: The system shall provide user-friendly operation.

ORIGINAL Quality Score: 2/10
Issues in Original:
- "User-friendly" is subjective and unmeasurable
- No specific criteria or metrics
- Cannot determine pass/fail conditions
- Missing operational context

Improved Version (would score 8/10): "The system shall complete standard user workflows (power-on, configuration, test execution) with no more than 3 button presses per workflow and provide visual feedback within 2 seconds of each user action."
```

**Example 2: Multiple Requirements in One**
```
COMM-REQ-008: The communication interface shall support Ethernet and RS-485 protocols and be backwards compatible.

ORIGINAL Quality Score: 3/10
Issues in Original:
- Multiple protocols in single requirement (violates atomicity)
- "Backwards compatible" is vague
- Missing performance specifications

Improved Version (would score 7/10): Split into separate requirements:
- "COMM-REQ-008A: The communication interface shall support Ethernet 10/100 Mbps communication according to IEEE 802.3 standards."
- "COMM-REQ-008B: The communication interface shall support RS-485 communication at 9600-115200 baud rates according to TIA-485-A standards."
```

**Example 3: Common Real-World Quality Levels**
```
TEST-REQ-045: The system should work fast and be reliable.

ORIGINAL Quality Score: 4/10
Issues in Original:
- "Fast" and "reliable" are not quantified
- No measurable performance criteria
- "Should" is weak requirement language
- Missing context for operations

Improved Version (would score 9/10): "The TEST system shall complete each automated test sequence within 15 seconds with a mean time between failures (MTBF) of at least 1000 hours under normal operating conditions."
```

## Test Case Generation Examples

### EXCELLENT Test Cases

**Example: Comprehensive Boundary Scan Test**
```
Title: Verify Tier 1 Boundary Scan Coverage and Timing

Objective: Validate that the Test System achieves 95% boundary scan coverage within 30 seconds

Preconditions:
- UUT properly seated in test fixture
- JTAG interface connected and verified
- Test System initialized and calibrated
- Boundary scan chain integrity confirmed

Test Steps:
1. Initiate boundary scan test sequence
2. Monitor coverage percentage in real-time
3. Record completion time when scan finishes
4. Verify coverage report shows ≥95% node access
5. Confirm all boundary scan registers properly loaded

Expected Results:
- Boundary scan completes within 30 seconds
- Coverage report indicates ≥95% interconnected nodes accessed
- No JTAG communication errors or timeouts
- All identified nodes respond to scan patterns
- Test system generates PASS result

Pass Criteria:
- Coverage ≥95% AND completion time ≤30 seconds
- Zero JTAG protocol errors
- All scanned nodes return expected signatures
```

### Verification Method Guidelines

**TEST Method Example:**
- Use when requirement involves dynamic behavior or performance
- Include specific input stimuli and expected outputs  
- Define clear pass/fail thresholds
- Specify test equipment and measurement procedures

**DEMONSTRATION Method Example:**
- Use when requirement involves operational capability
- Include realistic operational scenarios
- Define observable behaviors and outcomes
- Specify demonstration environment and conditions

**ANALYSIS Method Example:**
- Use when requirement can be verified through calculation
- Include mathematical models and assumptions
- Define analysis methods and tools
- Specify acceptance criteria for calculated results

**INSPECTION Method Example:**
- Use when requirement involves physical characteristics
- Include detailed examination procedures
- Define inspection criteria and standards
- Specify measurement tools and techniques

## Common Requirement Issues and Solutions

### Issue: Vague Performance Terms
**Problem:** "The system shall respond quickly"
**Solution:** "The system shall respond within 500 milliseconds"

### Issue: Missing Context
**Problem:** "The display shall show test results"
**Solution:** "The display shall show test results within 2 seconds of test completion, including PASS/FAIL status, error codes if applicable, and timestamp"

### Issue: Implementation Details
**Problem:** "The system shall use a microcontroller to process signals"
**Solution:** "The system shall process input signals and generate control outputs with latency not exceeding 10 milliseconds"

### Issue: Unmeasurable Criteria
**Problem:** "The interface shall be intuitive"
**Solution:** "The interface shall enable trained operators to complete standard test procedures with no more than 5 navigation steps and no reference to documentation"

## RAG Optimization Guidelines

When analyzing requirements or generating test cases:

1. **Focus on measurability** - Always look for quantifiable criteria
2. **Check atomicity** - One requirement should test one thing
3. **Verify testability** - Can this be proven pass/fail?
4. **Consider context** - Include operational environment factors
5. **Be specific** - Replace vague terms with precise specifications
6. **Think verification** - How would someone actually test this?

Use these examples as reference patterns for quality requirements and comprehensive test cases.