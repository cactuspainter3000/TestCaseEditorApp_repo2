# INCOSE Consistent Content Checker — Implementation Summary

## Overview

Added deterministic (rule-based, no-LLM) structural validator for INCOSE-compliant requirement writing. Validates that requirements follow canonical actor-shall-action patterns before LLM analysis.

## Architecture

```
RequirementAnalysisEngine
  ├─ IncoseConsistentContentChecker (instant, deterministic)
  │   └─ Detects ICC-001 through ICC-008 structural issues
  │   └─ Classifies requirement type (General, Event-Driven, State-Driven, etc.)
  │   └─ Generates canonical form suggestion
  │
  ├─ RequirementAnalysisService (LLM-based)
  │   └─ Uses enhanced prompt with INCOSE patterns
```

## Structural Checks (Deterministic)

| Code | Check | Severity | Example |
|------|-------|----------|---------|
| ICC-001 | Empty requirement | High | "" → FAIL |
| ICC-002 | Missing obligation keyword | High | "The system generates..." → FAIL |
| ICC-003 | Uses must/will/should instead of shall | Medium | "The system must..." → WARN |
| ICC-004 | Missing actor (no "The <ACTOR> shall") | High | "Shall generate..." → FAIL |
| ICC-005 | No action verb after "shall" | High | "The system shall a report" → FAIL |
| ICC-006 | Mixed when + while conditionals | Medium | "...when X while Y..." → WARN |
| ICC-007 | Condition clause before "shall" | Low | "When X, the system shall..." → INFO |
| ICC-008 | Conditional keyword but incomplete condition | Medium | "The system shall do when" → WARN |

## Canonical Forms Recognized

**General:**
```
The <ACTOR> shall <ACTION>.
```
Example: "The test system shall generate a diagnostic report."

**Event-Driven:**
```
The <ACTOR> shall <ACTION> when <CONDITION/EVENT>.
```
Example: "The operator interface shall display an alert when a fault condition is detected."

**State-Driven:**
```
The <ACTOR> shall <ACTION> while <STATE/CONDITION>.
```
Example: "The system shall log all transactions while the audit mode is active."

**Optional Feature:**
```
The <ACTOR> shall <ACTION> where <FEATURE CONDITION>.
```
Example: "The system shall provide enhanced diagnostics where the extended option is enabled."

**Timing Constrained:**
```
<Any above> within <TIME LIMIT>.
```
Example: "The system shall complete self-test within 30 seconds."

**Multi-Conditional:**
```
Conditions combined with "and".
```
Example: "The system shall execute when triggered and the mode is active."

## Integration Points

### 1. RequirementAnalysisEngine
- Runs INCOSE check **before** LLM call (instant, zero API cost)
- Converts INCOSE issues to `AnalysisIssue` objects with Category="Consistency"
- **Prepends** INCOSE issues to LLM analysis results (appear first in UI)
- Annotates `FreeformFeedback` with detected pattern type

### 2. RequirementAnalysisPromptBuilder
- Added INCOSE patterns to Criterion 6 ("Consistency & Traceability")
- LLM now knows canonical forms when generating `SuggestedEdit` rewrites
- Prompts LLM to verify consistency violations align with INCOSE structure

### 3. RequirementAnalysis Model
- INCOSE issues appear in `Issues` list with:
  - `Category`: "Consistency"
  - `Severity`: "High"/"Medium"/"Low" (from checker)
  - `Description`: "[INCOSE ICC-NNN] <description>"
  - `Fix`: Actionable suggestion

## Example Flow

**Input Requirement:**
```
Reports must be generated.
```

**INCOSE Check Results:**
```
Passed: false
Issues:
  - [ICC-004] High: Missing actor. No "The <ACTOR> shall" pattern found.
  - [ICC-003] Medium: Uses "must" instead of "shall".
  - [ICC-005] High: No action verb follows "must".
RequirementType: General
CanonicalFormSuggestion: "The <ACTOR> shall <ACTION>."
```

**After LLM Enhancement:**
```
Issues (combined):
  1. [INCOSE ICC-004] Consistency/High: Missing actor...
  2. [INCOSE ICC-003] Consistency/Medium: Uses "must"...
  3. [INCOSE ICC-005] Consistency/High: No action verb...
  4. [Clarity] High: Term "reports" is ambiguous...
  5. [Testability] Medium: No acceptance criteria...

ImprovedRequirement: "The test system shall generate comprehensive diagnostic reports in JSON format."
```

## Unit Tests (33 tests, 100% pass rate)

Categories covered:
- **Passing cases**: General, Event-Driven, State-Driven, Optional-Feature, Timing variants
- **Each violation code**: ICC-001 through ICC-008
- **Edge cases**: Mixed conditionals, condition placement, non-action words
- **Type classification**: Correct categorization for all patterns
- **Real-world examples**: ATP requirements, performance requirements, vague requirements
- **Mapping functions**: `ToAnalysisIssues()` integration

Test examples:
```
Check_GeneralShallPattern_Passes()
  ✓ "The Test System shall generate a diagnostic report."

Check_ShallWithoutActor_ReturnsHighSeverityIssue()
  ✗ "Shall generate a report." → ICC-004

Check_MixedWhenAndWhile_ReturnsMediumIssue()
  ⚠ "...when X while Y..." → ICC-006 (Medium)

Check_RealWorldATPRequirement_Passes()
  ✓ "The DHM test system shall perform boundary scan coverage..."
```

## Benefits

1. **Instant Structural Feedback** — No LLM latency, pure regex validation
2. **Consistent Format** — Encourages INCOSE-compliant writing patterns
3. **Education** — Canonical forms shown to users help them learn requirements writing
4. **Pre-filtering** — Catches obvious problems before expensive LLM call
5. **LLM Augmentation** — LLM focuses on deeper issues (ambiguity, testability) with structural baseline set
6. **Objective Scoring** — Rule-based checks reduce LLM hallucination about structure

## Performance

- Execution time: <1ms per requirement (regex-only, no LLM)
- Memory footprint: Negligible (compiled regex patterns are cached)
- Call site: Early in analysis pipeline, before LLM

## Future Enhancements

1. **Severity Calibration** — Allow requirements engineers to customize which violations block import
2. **Custom Patterns** — Support domain-specific canonical forms (e.g., test procedures)
3. **Batch Validation** — Scan entire requirement sets for consistency
4. **Learning Metrics** — Track which ICC violations are most common by team/project
