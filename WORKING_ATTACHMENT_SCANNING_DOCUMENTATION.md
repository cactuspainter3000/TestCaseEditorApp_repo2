# Working Attachment Scanning Process Documentation

## Purpose
This document provides detailed documentation of the current working attachment scanning system before architectural refactoring. The current implementation has architectural violations but working functionality that must be preserved.

## Architectural Violations in Current Implementation

### 1. Direct Service Injection in ViewModel (Anti-Pattern)
- **File**: [RequirementsSearchAttachmentsViewModel.cs](MVVM/Domains/Requirements/ViewModels/RequirementsSearchAttachmentsViewModel.cs#L28-L30)
- **Violation**: ViewModel directly injects `IJamaConnectService` and `IJamaDocumentParserService`
- **Correct Pattern**: Services should be in mediator, ViewModel should only communicate via mediator

### 2. Service Provider Lookup in Mediator (Anti-Pattern)
- **File**: [RequirementsMediator.cs](MVVM/Domains/Requirements/Mediators/RequirementsMediator.cs#L1077)
- **Violation**: `App.ServiceProvider?.GetService(typeof(RequirementsSearchAttachmentsViewModel))`
- **Correct Pattern**: Dependencies should be constructor-injected

### 3. Dependency Injection Configuration (Current State)
- **File**: [App.xaml.cs](App.xaml.cs#L383-L390)
- **Current**: ViewModel registered with service dependencies
- **Should Be**: ViewModel registered with mediator dependency only

## Complete Attachment Scanning Workflow

### Phase 1: Application Startup & Service Registration
1. **DI Container Setup** ([App.xaml.cs](App.xaml.cs#L383-L390))
   ```csharp
   services.AddSingleton<RequirementsSearchAttachmentsViewModel>(provider =>
   {
       var reqMediator = provider.GetRequiredService<IRequirementsMediator>();
       var jamaConnectService = provider.GetRequiredService<IJamaConnectService>();
       var jamaDocumentParserService = provider.GetRequiredService<IJamaDocumentParserService>();
       var workspaceContext = provider.GetRequiredService<IWorkspaceContext>();
       var logger = provider.GetRequiredService<ILogger<RequirementsSearchAttachmentsViewModel>>();
       return new RequirementsSearchAttachmentsViewModel(reqMediator, jamaConnectService, jamaDocumentParserService, workspaceContext, logger);
   });
   ```

2. **Service Dependencies**:
   - `IRequirementsMediator` - Domain mediator (correct pattern)
   - `IJamaConnectService` - VIOLATION: Should be in mediator
   - `IJamaDocumentParserService` - VIOLATION: Should be in mediator
   - `IWorkspaceContext` - OK: UI context service
   - `ILogger<RequirementsSearchAttachmentsViewModel>` - OK: Infrastructure

### Phase 2: ViewModel Constructor Initialization
**File**: [RequirementsSearchAttachmentsViewModel.cs](MVVM/Domains/Requirements/ViewModels/RequirementsSearchAttachmentsViewModel.cs#L35-L61)

1. **Service Assignment** (Lines 43-45) - ARCHITECTURAL VIOLATIONS:
   ```csharp
   _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
   _documentParserService = documentParserService ?? throw new ArgumentNullException(nameof(documentParserService));
   ```

2. **Event Subscription** (Line 49):
   ```csharp
   _mediator.Subscribe<AttachmentScanProgress>(OnAttachmentScanProgress);
   ```

3. **Initial State Setup**:
   - Default project ID: 636
   - Status: "Ready to scan attachments when project opens..."
   - Test attachments added for debugging

### Phase 3: Project Opening Trigger Chain

#### 3.1 OpenProject Workflow Initiation
**File**: [OpenProjectWorkflowViewModel.cs](MVVM/Domains/OpenProject/ViewModels/OpenProjectWorkflowViewModel.cs#L556-L574)

1. **Project ID Determination** - Analyzes workspace for Jama project ID
2. **Cross-Domain Communication**:
   ```csharp
   var requirementsMediator = App.ServiceProvider?.GetService(typeof(IRequirementsMediator)) as IRequirementsMediator;
   await requirementsMediator.TriggerBackgroundAttachmentScanAsync(targetProjectId.Value);
   ```

#### 3.2 Requirements Mediator Processing
**File**: [RequirementsMediator.cs](MVVM/Domains/Requirements/Mediators/RequirementsMediator.cs#L1066-L1087)

1. **Service Lookup** (Line 1077) - ARCHITECTURAL VIOLATION:
   ```csharp
   var searchAttachmentsViewModel = App.ServiceProvider?.GetService(typeof(RequirementsSearchAttachmentsViewModel)) as RequirementsSearchAttachmentsViewModel;
   ```

2. **Background Scan Initiation**:
   ```csharp
   await searchAttachmentsViewModel.StartBackgroundAttachmentScanAsync(projectId);
   ```

### Phase 4: Attachment Scanning Execution

#### 4.1 Background Scan Setup
**File**: [RequirementsSearchAttachmentsViewModel.cs](MVVM/Domains/Requirements/ViewModels/RequirementsSearchAttachmentsViewModel.cs#L438-L477)

1. **Project Configuration**:
   - Set `SelectedProjectId`
   - Fetch project name via `EnsureProjectNameIsAvailable()`
   - Prevent duplicate scans with `_backgroundScanInProgress` flag

2. **UI Thread Updates**:
   ```csharp
   Application.Current.Dispatcher.Invoke(() =>
   {
       AvailableAttachments.Clear();
       StatusMessage = $"🔍 Starting scan for project {projectId}...";
   });
   ```

3. **Background Task Launch**:
   ```csharp
   _ = Task.Run(async () => await SearchAttachmentsWithProgressAsync(isBackgroundScan: true));
   ```

#### 4.2 Attachment Scanning with Progress
**File**: [RequirementsSearchAttachmentsViewModel.cs](MVVM/Domains/Requirements/ViewModels/RequirementsSearchAttachmentsViewModel.cs#L483-L693)

1. **Progress Initialization**:
   ```csharp
   await Application.Current.Dispatcher.InvokeAsync(() =>
   {
       IsBackgroundScanningInProgress = true;
       BackgroundScanProgressText = $"Searching {GetSelectedProjectName()} for attachments. 0%";
       BackgroundScanProgress = 0;
       BackgroundScanTotal = 0;
   });
   ```

2. **Jama API Call** (Line 533-549) - USES DIRECT SERVICE:
   ```csharp
   attachments = await _jamaConnectService.GetProjectAttachmentsAsync(SelectedProjectId, default, (current, total, progressData) =>
   {
       Application.Current.Dispatcher.InvokeAsync(() =>
       {
           var parts = progressData.Split('|');
           var percentage = parts[0];
           var attachmentCount = parts.Length > 1 ? parts[1] : "0";
           BackgroundScanProgressText = $"Searching {GetSelectedProjectName()} for attachments. {percentage} ({attachmentCount} found)";
           BackgroundScanProgress = current;
           BackgroundScanTotal = total;
       });
   });
   ```

3. **Search Query Filtering** (Lines 565-571):
   ```csharp
   if (!string.IsNullOrWhiteSpace(SearchQuery))
   {
       filteredAttachments = attachments!
           .Where(a => a.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
           .ToList();
   }
   ```

4. **UI Updates on Dispatcher Thread** (Lines 573-634):
   - Populate `AvailableAttachments` collection
   - Update progress text and percentages
   - Auto-select first attachment
   - Add placeholder if no attachments found

#### 4.3 Progress Completion
**File**: [RequirementsSearchAttachmentsViewModel.cs](MVVM/Domains/Requirements/ViewModels/RequirementsSearchAttachmentsViewModel.cs#L655-L678)

1. **Final Progress Update**:
   ```csharp
   BackgroundScanProgressText = totalAttachments > 0 ? $"✅ Found {totalAttachments} attachments" : "❌ No attachments found";
   ```

2. **Progress Cleanup**: 4-second delay then clear progress indicators

### Phase 5: UI State Management

#### 5.1 Observable Properties (Current State)
- `AvailableAttachments`: All attachments from API
- `SearchResults`: Filtered attachments for display
- `SelectedAttachmentFilter`: Currently selected attachment
- `IsBackgroundScanningInProgress`: Shows progress overlay
- `BackgroundScanProgressText`: Progress message
- `BackgroundScanProgress/Total`: Progress numbers

#### 5.2 Filter Updates
**File**: [RequirementsSearchAttachmentsViewModel.cs](MVVM/Domains/Requirements/ViewModels/RequirementsSearchAttachmentsViewModel.cs#L107-L144)

- Automatic search result filtering when `SelectedAttachmentFilter` changes
- Updates `SearchResults` collection
- Updates status messages

## Key Dependencies and Service Calls

### Direct Service Usage (Violations)
1. **IJamaConnectService.GetProjectAttachmentsAsync()** - Should be in mediator
2. **IJamaDocumentParserService** - Should be in mediator for future parsing features

### Proper Architecture Patterns Used
1. **IRequirementsMediator** - Domain communication
2. **Event Subscription** - Cross-component communication
3. **IWorkspaceContext** - UI context service
4. **ILogger** - Infrastructure logging

### Cross-Domain Communication
1. **OpenProject → Requirements**: Via service provider lookup (violation)
2. **Progress Events**: Via mediator events (correct)
3. **UI Thread Marshaling**: Via Dispatcher (correct)

## Working Features That Must Be Preserved

### 1. Automatic Scanning on Project Open
- Triggers when workspace changes to project with Jama ID
- Works in background without blocking UI
- Real-time progress updates

### 2. Progress Reporting
- Live progress percentages during API calls
- Attachment count updates
- Visual progress overlay
- Automatic cleanup after completion

### 3. Attachment Collection Management
- Populates dropdown with all available attachments
- Handles empty results with placeholder
- Auto-selects first attachment
- Maintains search filtering

### 4. Error Handling
- Service availability checks
- API error recovery
- UI state restoration
- Logging for debugging

## Architectural Refactoring Requirements

### What Must Move to Mediator
1. **IJamaConnectService.GetProjectAttachmentsAsync()** calls
2. **IJamaDocumentParserService** operations
3. **Progress event publishing**
4. **Error handling and recovery**

### What Stays in ViewModel
1. **UI state management** (progress properties)
2. **Observable collections** (AvailableAttachments, SearchResults)
3. **Command implementations** (as delegated calls to mediator)
4. **Filter logic** (UI-specific filtering)
5. **Dispatcher marshaling** (UI thread updates)

### New Mediator Methods Needed
```csharp
// In IRequirementsMediator
Task<List<JamaAttachment>> ScanProjectAttachmentsAsync(int projectId, IProgress<AttachmentScanProgress> progress);
Task<List<Requirement>> ParseAttachmentRequirementsAsync(int attachmentId);
Task ImportRequirementsAsync(List<Requirement> requirements);
```

### Event Definitions Needed
```csharp
// In Requirements.Events namespace
public record AttachmentScanStarted(int ProjectId);
public record AttachmentScanProgress(int Current, int Total, string ProgressText);
public record AttachmentScanCompleted(int ProjectId, int AttachmentCount, bool Success, string? Error);
```

## Summary

The current attachment scanning system works correctly but violates architectural principles by:
1. Injecting domain services directly into ViewModels
2. Using service provider lookups instead of constructor injection
3. Performing API calls from ViewModels instead of mediators

The refactoring must preserve all working functionality while moving service dependencies to the mediator and establishing proper event-driven communication between domains.