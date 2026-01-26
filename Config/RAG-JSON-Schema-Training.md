# RAG JSON Schema Training for Requirement Analysis

This document teaches the AnythingLLM RAG system to analyze requirements for test case generation quality.

## CRITICAL: RESPONSE FORMAT RULES

**YOU MUST ALWAYS:**
1. Return ONLY valid JSON - no text before or after
2. No markdown code blocks (```json)
3. No explanatory text outside the JSON
4. Start response with { and end with }
5. Use proper JSON syntax with escaped quotes

## CRITICAL: ISSUE DESCRIPTION FORMAT

**FOR ISSUES ARRAY - USE PAST TENSE TO INDICATE WHAT WAS FIXED:**
- ❌ WRONG: "The term UUT is not explicitly stated as a requirement"
- ✅ CORRECT: "Explicitly stated that UUT refers to Unit Under Test and must be included for clarity"

**FOR RECOMMENDATIONS - USE PRESENT/FUTURE TENSE FOR SUGGESTIONS:**
- ✅ CORRECT: "Define acceptable parameters that constitute a simple connection"
- ✅ CORRECT: "Add specific timing requirements for test completion"

This format indicates that issues describe what has been addressed/fixed in the improved requirement.

## ANALYSIS PURPOSE

**🚨 CRITICAL: You MUST evaluate the USER'S ORIGINAL requirement quality - NOT your improved version!**

Evaluate the original requirement text as written by the user based on how suitable it is for generating effective test cases:
- **Clarity**: Can testers understand what to test from the ORIGINAL text?
- **Testability**: Are there measurable criteria in the ORIGINAL requirement?
- **Completeness**: Does the ORIGINAL requirement provide enough detail?

**RATING RULE**: Rate only the quality of the user's original requirement text before any improvements. Be realistic - most requirements have issues that need fixing (scores 3-7 are normal).

## MANDATORY RESPONSE FORMAT

**ALL requirement analysis responses must be valid JSON matching this exact schema:**

```json
{
  "QualityScore": 7,
  "Analysis": "Clean, readable explanation of why the ORIGINAL requirement is good or bad for test case creation",
  "ImprovedRequirement": "Complete improved requirement text that would be ideal for generating test cases"
}
```

## FIELD DEFINITIONS

### QualityScore
- **Type**: Integer (1-10)
- **Purpose**: Overall suitability of the ORIGINAL requirement text for test case generation
- **Scale**: 1-3 (Poor), 4-6 (Needs Improvement), 7-8 (Good), 9-10 (Excellent)
- **CRITICAL**: Rate the user's original text quality, NOT your improved version!

### Analysis  
- **Type**: String
- **Purpose**: Explain the quality score by identifying what helps or hinders test case creation IN THE ORIGINAL requirement
- **Focus**: Clarity of testing criteria, measurability, completeness for testers in the user's original text

### RewrittenRequirement
- **Type**: String  
- **Purpose**: Single improved requirement that addresses issues found in the original
- **Must**: Be complete, testable requirement text (not instructions!)
- **Note**: This improved version should score higher than the original

## EXAMPLES

### Example 1: Poor Requirement (Like Boundary Scan)
**Input**: "The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT."

**Expected Response - EXACTLY this format:**
```json
{
  "QualityScore": 4,
  "Analysis": "This requirement is problematic for test case generation because 'Tier 1 Boundary Scan coverage' is undefined, making it impossible to create specific test scenarios. Testers would not know what functionality to verify, what constitutes pass/fail criteria, or what specific coverage means. The vague terminology prevents creation of measurable test cases.",
  "RewrittenRequirement": "The Test System shall perform boundary scan testing on the Unit Under Test (UUT) that includes: pin connectivity verification, short/open circuit detection, and boundary register functionality testing with 95% fault detection accuracy and complete coverage of all boundary scan-enabled pins."
}
```

### Example 2: Good Requirement  
**Input**: "The user authentication system shall verify user credentials within 2 seconds."

**Expected Response - EXACTLY this format:**
```json
{
  "QualityScore": 8,
  "Analysis": "This requirement is well-suited for test case generation because it has a clear measurable criterion (2 seconds) and specific functionality (credential verification). Testers can easily create performance test cases with timing measurements. The requirement could be enhanced by specifying load conditions and error handling scenarios.",
  "RewrittenRequirement": "The user authentication system shall verify user credentials within 2 seconds under normal load (up to 100 concurrent users) and return appropriate error messages for invalid credentials within the same timeframe."
}
```

## CRITICAL REMINDERS

- **NO CODE BLOCKS**: Do not wrap JSON in ```json or ```
- **NO EXTRA TEXT**: Response should be ONLY the JSON object  
- **PROPER ESCAPING**: Use \" for quotes inside strings
- **COMPLETE SENTENCES**: Analysis should be full, readable paragraphs
- **SPECIFIC IMPROVEMENTS**: RewrittenRequirement should add measurable details

## REQUIRED FIELDS AND VALUES

### QualityScore
- **Type**: Integer (1-10)
- **Required**: YES
- **Purpose**: Overall requirement quality rating

### HallucinationCheck  
- **Type**: String
- **Required**: YES
- **Valid Values**: "NO_FABRICATION" | "FABRICATED_DETAILS"
- **Purpose**: Self-report on information accuracy

### Issues Array
- **Type**: Array of objects
- **Required**: YES (can be empty [])
- **Purpose**: Identified problems with requirement


#### Issue Object Structure
```json
{
  "Category": "Testability",     // Required: Issue type
  "Severity": "High",           // Required: "Low" | "Medium" | "High" 
  "Description": "Detailed explanation of the issue"  // Required
}
```

**Valid Issue Categories:**
- "Clarity" - Unclear or ambiguous language
- "Testability" - Cannot be verified/tested
- "Completeness" - Missing essential information  
- "Scope" - Too broad or narrow
- "Feasibility" - Implementation concerns
- "Dependencies" - Missing prerequisite information

### Recommendations Array
- **Type**: Array of objects  
- **Required**: YES (can be empty [])
- **Purpose**: Actionable improvement suggestions

#### Recommendation Object Structure
```json
{
  "Category": "Clarity",                    // Required: Improvement type
  "Description": "What to improve",         // Required: Action description
  "SuggestedEdit": "Complete rewritten requirement text"  // Optional but preferred
}
```

**Valid Recommendation Categories:**
- "Clarity" - Language improvements
- "Testability" - Add measurable criteria
- "Structure" - Reorganize content
- "Specificity" - Add concrete details
- "Completeness" - Add missing elements

### FreeformFeedback
- **Type**: String
- **Required**: YES
- **Purpose**: Natural language summary and insights

## EXAMPLE RESPONSES

### Example 1: High Quality Requirement
**Input**: "The user authentication system shall verify user credentials within 2 seconds and deny access after 3 failed attempts."

**Expected JSON Response**:
```json
{
  "QualityScore": 9,
  "HallucinationCheck": "NO_FABRICATION", 
  "Issues": [],
  "Recommendations": [
    {
      "Category": "Completeness",
      "Description": "Consider adding account lockout duration",
      "SuggestedEdit": "The user authentication system shall verify user credentials within 2 seconds and deny access after 3 failed attempts, locking the account for 15 minutes before allowing retry."
    }
  ],
  "FreeformFeedback": "Excellent requirement with clear performance criteria and security measures. Well-defined and testable."
}
```

### Example 2: Poor Quality Requirement (Like the Boundary Scan example)
**Input**: "The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT."

**Expected JSON Response**:
```json
{
  "QualityScore": 4,
  "HallucinationCheck": "NO_FABRICATION",
  "Issues": [
    {
      "Category": "Clarity",
      "Severity": "High",
      "Description": "Tier 1 Boundary Scan coverage is undefined and lacks context"
    },
    {
      "Category": "Completeness", 
      "Severity": "Medium",
      "Description": "Missing success/failure criteria and expected outcomes"
    }
  ],
  "Recommendations": [
    {
      "Category": "Clarity",
      "Description": "Define Tier 1 coverage and add measurable criteria",
      "SuggestedEdit": "The Test System shall perform Tier 1 Boundary Scan coverage on the Unit Under Test (UUT), which includes pin-level connectivity verification, short/open circuit detection, and boundary register functionality testing with 95% fault detection accuracy."
    }
  ],
  "FreeformFeedback": "Requirement lacks critical definitions and measurable criteria. 'Tier 1 coverage' needs specific meaning and testable outcomes."
}
```

### Example 3: Medium Quality Requirement  
**Input**: "The system shall process user requests quickly."

**Expected JSON Response**:
```json
{
  "QualityScore": 5,
  "HallucinationCheck": "NO_FABRICATION",
  "Issues": [
    {
      "Category": "Testability",
      "Severity": "High", 
      "Description": "Vague performance criteria - 'quickly' is not measurable"
    }
  ],
  "Recommendations": [
    {
      "Category": "Testability",
      "Description": "Replace vague terms with specific performance metrics",
      "SuggestedEdit": "The system shall process user requests within 2 seconds for standard operations and within 5 seconds for complex operations under normal load conditions (up to 50 concurrent users)."
    }
  ],
  "FreeformFeedback": "Basic requirement structure but lacks measurable performance criteria needed for testing and validation."
}
```
```

### Example 2: Poor Quality Requirement
**Input**: "The system should be fast and secure."

**Expected JSON Response**:
```json
{
  "QualityScore": 2,
  "HallucinationCheck": "NO_FABRICATION",
  "Issues": [
    {
      "Category": "Clarity",
      "Severity": "High", 
      "Description": "Terms 'fast' and 'secure' are vague and unmeasurable"
    },
    {
      "Category": "Testability",
      "Severity": "High",
      "Description": "No specific criteria provided for verification"
    },
    {
      "Category": "Completeness", 
      "Severity": "High",
      "Description": "Missing context about what system functionality is being described"
    }
  ],
  "Recommendations": [
    {
      "Category": "Specificity",
      "Description": "Define measurable performance and security criteria", 
      "SuggestedEdit": "The user authentication system shall process login requests within 2 seconds and implement multi-factor authentication using industry-standard encryption."
    }
  ],
  "FreeformFeedback": "This requirement is too vague to implement or test. It needs specific, measurable criteria and clear scope definition."
}
```

### Example 3: Mixed Quality Requirement
**Input**: "The application must handle user input validation efficiently while providing good error messages to users."

**Expected JSON Response**:
```json
{
  "QualityScore": 6,
  "HallucinationCheck": "NO_FABRICATION",
  "Issues": [
    {
      "Category": "Clarity",
      "Severity": "Medium",
      "Description": "Term 'efficiently' is subjective without performance metrics"
    },
    {
      "Category": "Clarity", 
      "Severity": "Low",
      "Description": "Definition of 'good error messages' needs specification"
    }
  ],
  "Recommendations": [
    {
      "Category": "Specificity",
      "Description": "Define performance criteria and error message standards",
      "SuggestedEdit": "The application must validate user input within 100ms and display specific, actionable error messages that identify the invalid field and required format."
    }
  ],
  "FreeformFeedback": "The requirement has the right scope but needs quantifiable criteria. The dual focus on validation speed and user experience is good architectural thinking."
}
```

## CRITICAL RULES FOR JSON GENERATION

1. **ALWAYS return valid JSON** - Never include explanatory text outside the JSON structure
2. **Include ALL required fields** - QualityScore, HallucinationCheck, Issues, Recommendations, FreeformFeedback
3. **Use ONLY valid enum values** - For Severity and Category fields
4. **Empty arrays are valid** - Use [] if no issues or recommendations
5. **SuggestedEdit should be complete requirement text** - Not instructions or fragments
6. **Be conservative with HallucinationCheck** - Use "FABRICATED_DETAILS" if adding any technical specifics not in original

## RESPONSE VALIDATION CHECKLIST

Before returning any response, verify:
- ✅ Valid JSON syntax (no trailing commas, proper quotes)
- ✅ All required fields present  
- ✅ QualityScore is integer 1-10
- ✅ HallucinationCheck is valid value
- ✅ Issue/Recommendation categories are from approved lists
- ✅ Severity levels are "Low", "Medium", or "High" only
- ✅ No explanatory text outside JSON structure

## COMMON MISTAKES TO AVOID

❌ **DON'T**: Return explanatory text before or after JSON
❌ **DON'T**: Use invalid category names  
❌ **DON'T**: Include markdown formatting in JSON strings
❌ **DON'T**: Use subjective severity levels like "Critical" or "Minor"
❌ **DON'T**: Return partial JSON or malformed structures
❌ **DON'T**: Include implementation instructions in SuggestedEdit

✅ **DO**: Return clean, valid JSON only
✅ **DO**: Use approved category and severity values
✅ **DO**: Provide complete rewritten requirements in SuggestedEdit
✅ **DO**: Be concise but thorough in descriptions
✅ **DO**: Validate JSON structure before responding

This training document ensures consistent, parseable JSON responses for all requirement analysis requests in the Test Case Editor application.