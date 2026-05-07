# Prompt Diagnostics Feature Guide

## Overview
The Prompt Diagnostics feature allows users to view, copy, and compare LLM prompts and responses directly within the Test Case Editor App. This enables prompt optimization, debugging, and cross-LLM testing without leaving the application.

## Location
**Test Case Generation → Prompt Diagnostics Tab**

Access the feature from the third tab in the LLM Test Case Generator view.

## Features

### 1. Prompt Display
- **Auto-capture**: Every test case generation automatically captures the exact prompt sent to AnythingLLM
- **Metadata**: Shows generation timestamp, requirement count, prompt size, and estimated token count
- **Copy Function**: One-click copy of the entire prompt to clipboard
- **Use Case**: Test the same prompt in ChatGPT, Claude, or other LLMs for comparison

### 2. AnythingLLM Response Display
- **Auto-capture**: Captures the raw response from AnythingLLM
- **Readonly View**: Shows exactly what the LLM returned
- **Copy Function**: Copy response to clipboard for analysis
- **Use Case**: Save responses for documentation or compare with external LLM outputs

### 3. External LLM Response Comparison
- **Editable Field**: Paste responses from external LLMs (ChatGPT, Claude, Gemini, etc.)
- **Auto-Compare**: Automatically compares when both responses are present
- **Manual Compare**: Click "Compare Responses" button to trigger comparison

### 4. Comparison Analysis
The comparison provides detailed metrics:

#### Size Comparison
- Character counts for both responses
- Estimated token counts (characters / 4)
- Difference in size

#### Test Case Count
- Automatically detects test case count using multiple heuristics:
  - JSON "id" field detection
  - TC-XXX pattern matching
- Shows which LLM generated more test cases

#### Structure Analysis
- JSON structure validation
- Checks if both responses have proper JSON formatting

#### Content Similarity
- Word-based similarity calculation using Jaccard index
- Percentage score from 0-100%
- Interpretation:
  - >80%: Very similar responses
  - 50-80%: Moderate similarity
  - <50%: Quite different responses

## Workflow Example

### Testing Prompt in External LLM
1. Generate test cases in the main tab
2. Switch to "Prompt Diagnostics" tab
3. Click "📋 Copy Prompt" button
4. Open ChatGPT or Claude in browser
5. Paste the prompt and generate response
6. Copy the external LLM's response
7. Paste into "External LLM Response" field
8. Click "⚖️ Compare Responses"
9. Review comparison metrics

### Prompt Optimization
1. Generate test cases with current prompt
2. Examine the generated prompt in diagnostics
3. Identify potential improvements:
   - Too long? Simplify instructions
   - Missing context? Add more detail
   - Unclear requirements? Improve formatting
4. Test modified prompt in external LLM
5. Compare quality before making changes to the actual prompt

### Debugging Generation Issues
1. If test case generation fails or produces poor results
2. Open Prompt Diagnostics tab
3. Review the generated prompt for issues:
   - Are requirements properly formatted?
   - Is the instruction clear?
   - Are all requirements included?
4. Copy the prompt and test in external LLM to determine if:
   - Issue is with the prompt itself
   - Issue is with AnythingLLM configuration
   - Issue is with the specific LLM model

## Architecture

### Services
- **ITestCaseGenerationService**: Extended with `GenerateTestCasesWithDiagnosticsAsync()`
- **TestCaseGenerationService**: Captures prompt and response during generation
- **TestCaseGenerationResult**: Record type containing `(TestCases, GeneratedPrompt, LLMResponse)`

### ViewModels
- **PromptDiagnosticsViewModel**: 
  - Properties: `GeneratedPrompt`, `AnythingLLMResponse`, `ExternalLLMResponse`, `ComparisonResults`
  - Commands: `CopyPromptCommand`, `CopyAnythingLLMResponseCommand`, `CompareResponsesCommand`, `ClearExternalResponseCommand`, `ClearAllCommand`
  - Methods: `UpdatePrompt()`, `UpdateAnythingLLMResponse()`, `CountTestCasesInResponse()`, `CalculateSimilarity()`

- **LLMTestCaseGeneratorViewModel**:
  - Added `PromptDiagnostics` property for data binding
  - Auto-updates diagnostics after generation

### Views
- **PromptDiagnosticsView.xaml**: Standalone view with prompt/response/comparison UI
- **LLMTestCaseGeneratorView.xaml**: Added new tab with PromptDiagnosticsView integration

## Technical Details

### Prompt Capture
```csharp
var result = await _generationService.GenerateTestCasesWithDiagnosticsAsync(
    requirements, progressCallback, cancellationToken);

_promptDiagnostics.UpdatePrompt(result.GeneratedPrompt, requirements.Count, DateTime.Now);
_promptDiagnostics.UpdateAnythingLLMResponse(result.LLMResponse);
```

### Comparison Algorithm
1. **Size Metrics**: Direct character/token counts
2. **Test Case Detection**: Regex patterns for "id" fields and TC-XXX markers
3. **JSON Validation**: Simple bracket matching
4. **Similarity Score**: Jaccard index on normalized word sets
   - Formula: `|A ∩ B| / |A ∪ B| * 100`
   - Handles word tokenization, case normalization, punctuation removal

### UI Bindings
- All TextBoxes use `FontFamily="Consolas"` for monospace readability
- Readonly fields prevent accidental edits
- External response field uses `TwoWay` binding with `PropertyChanged` trigger
- Comparison results auto-update when external response changes

## Future Enhancements
Potential improvements:
1. **Response History**: Store multiple generation attempts for comparison
2. **Prompt Templates**: Save/load custom prompt templates
3. **Diff View**: Side-by-side highlighted diff of responses
4. **JSON Validation**: Parse and validate JSON structure with error details
5. **Export**: Save prompt/response pairs to file for documentation
6. **Metrics Tracking**: Track which LLM performs best over time
7. **A/B Testing**: Automated testing of multiple prompts/LLMs
8. **Prompt Suggestions**: AI-powered prompt improvement suggestions

## Benefits
- **Debugging**: Quickly identify if issues are with prompt, LLM, or configuration
- **Optimization**: Test prompt variations without modifying code
- **Comparison**: Evaluate different LLMs objectively with same prompt
- **Documentation**: Capture exact prompts used for specific test case generations
- **Learning**: Understand what prompts work best for your requirements
- **Cost Analysis**: Compare quality vs. cost across different LLM providers

## Best Practices
1. **Always review prompts** before testing in external LLMs (may contain sensitive data)
2. **Use descriptive prompts** to make comparison meaningful
3. **Test with same model temperature** when comparing across LLMs
4. **Save successful prompts** for future reference
5. **Document findings** when comparing different LLMs
6. **Consider token limits** when testing in external LLMs

## Related Documentation
- [ARCHITECTURAL_GUIDE_AI.md](../../../ARCHITECTURAL_GUIDE_AI.md) - Architecture patterns
- [RAG_INTEGRATION_GUIDE.md](../../../RAG_INTEGRATION_GUIDE.md) - RAG system integration
- [TestCaseGenerationService.cs](../MVVM/Domains/TestCaseCreation/Services/TestCaseGenerationService.cs) - Implementation
