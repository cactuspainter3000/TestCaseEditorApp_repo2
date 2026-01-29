# RAG Integration Implementation Guide

## Overview

This document describes the full RAG (Retrieval-Augmented Generation) integration implemented for the TestCaseEditorApp. The integration provides comprehensive tracking, diagnostics, and optimization of RAG usage during test case generation.

## Architecture

### Core Components

#### 1. RAGContextService (`Services/RAGContextService.cs`)
**Purpose**: Tracks RAG requests, document usage, and effectiveness metrics.

**Key Responsibilities**:
- **Request Tracking**: Logs all LLM requests with timestamps, duration, and success status
- **Document Usage Tracking**: Records which documents were retrieved and their relevance
- **Metrics Collection**: Maintains running statistics on success rates, response times, and document effectiveness
- **Recommendation Generation**: Analyzes performance data to suggest optimizations

**Key Methods**:
```csharp
void TrackRAGRequest(string workspaceSlug, string prompt, string response, bool successful, TimeSpan duration)
void TrackDocumentUsage(string documentName, bool wasRelevant, string extractedContext = null)
RAGContextSummary GetRAGContextSummary(string workspaceSlug)
async Task<bool> EnsureRAGConfiguredAsync(string workspaceSlug, bool forceRefresh = false)
RAGPerformanceMetrics GetPerformanceMetrics()
void ClearHistory()
string ExportAnalytics()
```

**Data Structures**:
- `RAGRequest`: Individual request record with timing and content metadata
- `RAGContextSummary`: Aggregated metrics for a workspace
- `DocumentUsageStats`: Performance statistics for individual documents
- `RAGPerformanceMetrics`: Overall system metrics

---

#### 2. TestCaseGenerationService Integration (`MVVM/Domains/TestCaseCreation/Services/TestCaseGenerationService.cs`)
**Purpose**: Integrates RAG tracking into test case generation workflow.

**Changes Made**:
- **Constructor**: Added optional `RAGContextService` parameter for DI
- **Pre-Generation**: Calls `EnsureRAGConfiguredAsync()` to verify documents are uploaded
- **Request Tracking**: Calls `TrackRAGRequest()` after LLM response with timing data
- **Metrics**: Captures success/failure and response duration for analytics

**Flow**:
1. Ensure RAG workspace is configured with documents
2. Send prompt to LLM via AnythingLLM service
3. Track the request with `TrackRAGRequest()`
4. Handle response and return test cases
5. Metrics automatically available via RAGContextService

---

#### 3. RAGDiagnosticsViewModel (`MVVM/Domains/TestCaseCreation/ViewModels/RAGDiagnosticsViewModel.cs`)
**Purpose**: Provides UI-ready data for displaying RAG diagnostics.

**Observable Properties** (for UI binding):
```csharp
string TotalRequestsDisplay
string SuccessRateDisplay
string AverageResponseTimeDisplay
string DocumentCountDisplay
string AverageDocumentRelevanceDisplay
ObservableCollection<RAGWorkspaceMetricsDisplay> WorkspaceMetrics
ObservableCollection<string> Recommendations
string AnalyticsExport
bool IsLoading
string StatusMessage
bool HasData
```

**Key Methods**:
```csharp
async Task RefreshDiagnosticsAsync()           // Load metrics from service
void ClearDiagnostics()                         // Reset tracking data
string ExportAnalyticsText()                    // Get analytics for export
```

**Display Models**:
- `RAGWorkspaceMetricsDisplay`: Formatted metrics for UI
- `DocumentUsageDisplay`: Document stats with relevance indicators

---

### Integration Points

#### Dependency Injection (App.xaml.cs)

**RAGContextService Registration**:
```csharp
services.AddSingleton<RAGContextService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<RAGContextService>>();
    var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
    return new RAGContextService(logger, anythingLLMService);
});
```

**TestCaseGenerationService Registration** (with RAG injection):
```csharp
services.AddSingleton<ITestCaseGenerationService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<TestCaseGenerationService>>();
    var anythingLLMService = provider.GetRequiredService<AnythingLLMService>();
    var ragContextService = provider.GetService<RAGContextService>(); // Optional
    return new TestCaseGenerationService(logger, anythingLLMService, ragContextService);
});
```

**RAGDiagnosticsViewModel Registration**:
```csharp
services.AddTransient<RAGDiagnosticsViewModel>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<RAGDiagnosticsViewModel>>();
    var ragContextService = provider.GetRequiredService<RAGContextService>();
    return new RAGDiagnosticsViewModel(logger, ragContextService);
});
```

---

## Metrics Tracked

### Request-Level Metrics
- **Timestamp**: When the request was made
- **Workspace Slug**: Which workspace handled the request
- **Prompt Length**: Size of generated prompt
- **Response Length**: Size of LLM response
- **Success Status**: Whether request succeeded
- **Duration**: Total time from request to response
- **Previews**: First 200 chars of prompt and response

### Document-Level Metrics
- **Retrieval Count**: How many times document was used
- **Relevance Score**: Percentage of retrievals deemed relevant
- **Extraction Context**: Sample of what was extracted
- **Status**: High/Medium/Low/None relevance indicator

### Workspace-Level Metrics
- **Total Requests**: Cumulative requests for workspace
- **Success Rate**: Percentage of successful requests
- **Average Duration**: Mean response time
- **Document Statistics**: All documents used in workspace

### System-Level Metrics
- **Overall Success Rate**: Total success percentage
- **Average Response Time**: Mean across all requests
- **Document Count**: Number of unique documents tracked
- **Average Relevance**: Mean relevance score across documents

---

## Performance Recommendations

The system automatically generates recommendations based on performance:

### Success Rate Analysis
```
✓ EXCELLENT SUCCESS RATE (100%)  → "RAG configuration is working well"
✓ HIGH SUCCESS RATE (80-100%)    → "No optimization recommendations"
⚠ MODERATE SUCCESS RATE (50-80%) → "Some requests failing. Monitor logs"
⚠ LOW SUCCESS RATE (<50%)        → "Increase timeout values, review config"
```

### Response Time Analysis
```
✓ FAST RESPONSES (<5s)           → "RAG processing is efficient"
⏱ SLOW RESPONSES (>2 min)        → "Consider reducing batch size or faster model"
```

### Document Usage Analysis
```
📄 UNUSED DOCUMENTS              → "May be irrelevant or threshold too high"
❌ LOW RELEVANCE DOCUMENTS       → "Consider updating or removing"
```

### Batch Size Analysis
```
📝 LARGE PROMPTS (>5KB)          → "Use batch processing with smaller sets"
```

---

## Usage Examples

### Basic Usage - Displaying Metrics in UI

```csharp
// In your UI ViewModel or code-behind
public partial class SomeViewModel : ObservableRecipient
{
    private readonly RAGDiagnosticsViewModel _ragDiagnosticsVM;
    
    public SomeViewModel(RAGDiagnosticsViewModel ragDiagnosticsVM)
    {
        _ragDiagnosticsVM = ragDiagnosticsVM;
    }
    
    public async Task LoadRAGMetricsAsync()
    {
        await _ragDiagnosticsVM.RefreshDiagnosticsAsync();
        // Now bind UI to _ragDiagnosticsVM properties
    }
}
```

### XAML Binding Example

```xaml
<!-- Display RAG Metrics -->
<StackPanel>
    <TextBlock Text="{Binding TotalRequestsDisplay, Mode=OneWay}" />
    <TextBlock Text="{Binding SuccessRateDisplay, Mode=OneWay}" />
    <TextBlock Text="{Binding AverageResponseTimeDisplay, Mode=OneWay}" />
    
    <!-- Workspace Metrics -->
    <ItemsControl ItemsSource="{Binding WorkspaceMetrics}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <StackPanel>
                    <TextBlock Text="{Binding WorkspaceSlug}" />
                    <TextBlock Text="{Binding SuccessRatePercent, StringFormat='{0:F1}%'}" />
                    <TextBlock Text="{Binding StatusIndicator}" />
                </StackPanel>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
    
    <!-- Recommendations -->
    <ItemsControl ItemsSource="{Binding Recommendations}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <TextBlock Text="{Binding}" />
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```

### Programmatic Access

```csharp
// Get all metrics
var metrics = _ragContextService.GetPerformanceMetrics();
Console.WriteLine($"Overall success rate: {metrics.OverallSuccessRate:F1}%");
Console.WriteLine($"Average response time: {metrics.AverageResponseTime.TotalSeconds:F2}s");

// Get workspace-specific metrics
var workspaceSummary = _ragContextService.GetRAGContextSummary("test-case-generation");
Console.WriteLine($"Workspace success rate: {workspaceSummary.SuccessRate:F1}%");

// Export analytics
var analytics = _ragContextService.ExportAnalytics();
File.WriteAllText("rag-analytics.txt", analytics);

// Clear history (for testing/reset)
_ragContextService.ClearHistory();
```

---

## Configuration

### Workspace Configuration
The system automatically ensures workspaces are configured with:
- **Temperature**: 0.3 (for consistent, deterministic responses)
- **Context History**: 20 (retain context from previous exchanges)
- **Similarity Threshold**: 0 (use all relevant documents)
- **TopN Documents**: 4 (retrieve top 4 relevant documents)
- **Search Preference**: Hybrid (use best of semantic + keyword search)

### Training Documents
Three RAG training documents are automatically uploaded to workspaces:
1. **RAG-JSON-Schema-Training.md**: JSON structure examples for test cases
2. **RAG-Learning-Examples.md**: Real test case examples for reference
3. **RAG-Optimization-Summary.md**: Best practices for test generation

### System Prompt
Comprehensive system prompt includes:
- Anti-fabrication rules (no making up requirements)
- Quality standards for test cases
- Similarity detection instructions
- JSON format requirements

---

## Null-Safety & Graceful Fallback

The integration is designed to be completely optional and graceful:

```csharp
// In TestCaseGenerationService
if (_ragContextService != null)
{
    // RAG configuration available, use it
    var configured = await _ragContextService.EnsureRAGConfiguredAsync(WORKSPACE_SLUG);
}

// Generate test cases continues regardless of RAG availability
var response = await _anythingLLMService.SendChatMessageAsync(...);

// Track metrics if service available
_ragContextService?.TrackRAGRequest(...);
```

If `RAGContextService` is not available or fails:
- Test case generation **continues without error**
- No metrics are collected (but no crash occurs)
- System degrades gracefully
- User can still generate test cases successfully

---

## Monitoring & Debugging

### Logging
The system logs at multiple levels:

```csharp
// Information level - key events
_logger.LogInformation("[RAG] Request tracked: Success={Success}, Duration={Duration}ms");

// Debug level - detailed operations
_logger.LogDebug("[RAG] Verifying documents in workspace '{Workspace}'");

// Warning level - suboptimal conditions
_logger.LogWarning("[RAG] Failed to upload training documents");

// Error level - failures
_logger.LogError(ex, "[RAG] Error ensuring RAG configuration");
```

### Analytics Export
Full analytics can be exported for analysis:

```
=== RAG PERFORMANCE ANALYTICS ===
Total Requests: 42
Success Rate: 92.9%
Average Response Time: 45.23s
Documents Tracked: 3

--- Workspace: test-case-generation ---
  Requests: 42 (Success: 39, Failed: 3)
  Success Rate: 92.9%
  Avg Duration: 45.23s
  Document Stats:
    - RAG-JSON-Schema-Training.md: 87.5% relevance (14 retrievals)
    - RAG-Learning-Examples.md: 95.0% relevance (20 retrievals)
    - RAG-Optimization-Summary.md: 72.0% relevance (8 retrievals)
  Recommendations:
    ✓ EXCELLENT SUCCESS RATE: RAG configuration is working well
    ✓ FAST RESPONSES: RAG processing is efficient
```

---

## Future Enhancements

### Planned Features (Phase 2)

1. **Automatic Parameter Tuning**
   - Adjust similarity threshold based on effectiveness
   - Dynamically change topN based on document relevance
   - Modify temperature if too many fabrications detected

2. **Document Effectiveness Feedback**
   - Track which documents actually helped vs. hurt quality
   - Auto-disable consistently irrelevant documents
   - Recommend new documents based on failure patterns

3. **Per-Requirement-Type RAG Settings**
   - Different settings for functional vs. non-functional requirements
   - Domain-specific document prioritization
   - Tailored similarity thresholds per domain

4. **RAG Performance Dashboard**
   - Real-time metrics visualization
   - Historical trend analysis
   - Document heatmap (which are most effective)
   - A/B testing different configurations

5. **Vector Store Integration**
   - Store document embeddings for faster retrieval
   - Custom vector similarity calculations
   - Query expansion and refinement

---

## Troubleshooting

### No Metrics Showing
1. Verify `RAGContextService` is registered in DI
2. Check that test case generation has been executed at least once
3. Call `RefreshDiagnosticsAsync()` to load metrics from service
4. Check logs for any RAG configuration errors

### Low Success Rate
1. Check AnythingLLM is running and workspace exists
2. Verify training documents are uploaded (system does this automatically)
3. Look at recommendations for specific hints
4. Review workspace configuration settings
5. Check if prompts are too large (batch size)

### Slow Response Times
1. Reduce batch size (generate fewer requirements at once)
2. Increase per-request timeout in `AnythingLLMService`
3. Check system resources (CPU/memory for LLM)
4. Consider using faster model if available

### Document Relevance Issues
1. Review training documents for quality
2. Check similarity threshold (currently 0, uses all documents)
3. Look at extracted context to see what LLM is using
4. Consider uploading domain-specific examples

---

## Code Files Modified/Created

### New Files
- `Services/RAGContextService.cs` - Core RAG tracking and diagnostics
- `MVVM/Domains/TestCaseCreation/ViewModels/RAGDiagnosticsViewModel.cs` - UI integration

### Modified Files
- `App.xaml.cs` - Added DI registrations for RAG services
- `MVVM/Domains/TestCaseCreation/Services/TestCaseGenerationService.cs` - Integrated RAG tracking

### Configuration/Documentation
- This guide: `RAG_INTEGRATION_GUIDE.md`

---

## API Reference

### RAGContextService Public Methods

#### TrackRAGRequest
```csharp
public void TrackRAGRequest(string workspaceSlug, string prompt, string? response, 
    bool successful, TimeSpan duration)
```
Logs a single RAG request for tracking and analysis.

**Parameters**:
- `workspaceSlug`: AnythingLLM workspace identifier
- `prompt`: Full prompt sent to LLM
- `response`: LLM response (nullable)
- `successful`: Whether request succeeded
- `duration`: Time elapsed for request

#### TrackDocumentUsage
```csharp
public void TrackDocumentUsage(string documentName, bool wasRelevant, 
    string? extractedContext = null)
```
Records document usage in RAG context.

**Parameters**:
- `documentName`: Name of retrieved document
- `wasRelevant`: Whether document was useful
- `extractedContext`: Sample of extracted content

#### GetRAGContextSummary
```csharp
public RAGContextSummary GetRAGContextSummary(string workspaceSlug)
```
Returns aggregated metrics for a specific workspace.

#### GetPerformanceMetrics
```csharp
public RAGPerformanceMetrics GetPerformanceMetrics()
```
Returns overall system metrics across all workspaces.

#### EnsureRAGConfiguredAsync
```csharp
public async Task<bool> EnsureRAGConfiguredAsync(string workspaceSlug, 
    bool forceRefresh = false)
```
Verifies and configures RAG for a workspace.

**Parameters**:
- `workspaceSlug`: Workspace to configure
- `forceRefresh`: Force document re-upload

**Returns**: True if successfully configured

#### ExportAnalytics
```csharp
public string ExportAnalytics()
```
Returns formatted analytics string for debugging/export.

#### ClearHistory
```csharp
public void ClearHistory()
```
Clears all tracked data (for testing/reset).

---

## Version History

- **v1.0** (Current): Initial RAG integration with diagnostics and tracking
  - RAGContextService for metrics collection
  - RAGDiagnosticsViewModel for UI display
  - Integration into test case generation workflow
  - Comprehensive logging and analytics export

---

## Support & Questions

For issues or questions about the RAG integration:
1. Check logs for detailed error messages (look for `[RAG]` prefix)
2. Export analytics for debugging: `_ragContextService.ExportAnalytics()`
3. Review recommendations for optimization hints
4. Verify AnythingLLM service is running and configured correctly
5. Check that workspace "test-case-generation" exists in AnythingLLM

