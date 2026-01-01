# Import Additional Requirements Implementation Plan

## Overview
This document outlines the implementation plan for adding "Import Additional Requirements" functionality with universal requirements scrubbing infrastructure to the TestCaseEditorApp.

## Lessons Learned from Initial Implementation

### Architectural Violations Encountered
1. **Service Location Anti-Pattern**: Direct service instantiation instead of dependency injection
2. **Domain Boundary Violations**: ViewModels directly calling cross-domain services
3. **UI Duplication**: Multiple import buttons in different locations causing confusion
4. **Disconnected Infrastructure**: Scrubber infrastructure created but not integrated into workflow
5. **Code Bloat**: 863 lines added with significant unused enhancements

### Key Insights
1. **Legitimate vs Bloat**: Universal scrubber infrastructure (411 lines) is legitimate functionality, but disconnected enhancements are bloat
2. **Integration Points**: Scrubber must be integrated into actual import workflow, not just available as service
3. **Cross-Domain Coordination**: Import operations must use proper mediator patterns for domain communication
4. **Fail-Fast Architecture**: Constructor injection prevents missing dependencies and architectural violations

### Best Practices Identified
1. **Domain Mediator Communication**: All cross-domain communication must go through mediators
2. **Single Responsibility**: Each UI element should have one clear purpose (avoid duplicate import buttons)
3. **Comprehensive Testing**: Integration testing required for cross-domain workflows
4. **Incremental Implementation**: Build functionality incrementally to avoid large, disconnected changes

## Implementation Todo List

### Phase 1: Core Infrastructure
1. **Create Universal Requirements Scrubber Interface**
   - Create Services/IRequirementDataScrubber.cs (130 lines)
   - Comprehensive interface with ProcessRequirementsAsync method
   - ScrubberResult/ImportContext models, and validation enums for structure/content/duplicates/business rules validation

2. **Implement Universal Requirements Scrubber Service**
   - Create Services/RequirementDataScrubber.cs (281 lines)
   - Multi-phase implementation with structure validation, content normalization, duplicate detection, and business rules
   - Include comprehensive logging and statistics tracking

3. **Register Scrubber Service in DI Container**
   - Add services.AddSingleton<IRequirementDataScrubber, RequirementDataScrubber>(); to App.xaml.cs ConfigureServices method
   - Ensures proper dependency injection

### Phase 2: Cross-Domain Integration
4. **Add ImportAdditionalRequirementsAsync to IWorkspaceManagementMediator**
   - Add Task ImportAdditionalRequirementsAsync(); method signature to IWorkspaceManagementMediator interface
   - Provides cross-domain import coordination

5. **Implement ImportAdditionalRequirementsAsync in WorkspaceManagementMediator**
   - Implement method in WorkspaceManagementMediator.cs with file dialog integration
   - BroadcastToAllDomains(new ImportRequirementsRequest { FilePath = selectedFile, IsAppendMode = true })

6. **Add AdditionalRequirementsImported Event**
   - Add new event class AdditionalRequirementsImported to TestCaseGenerationEvents.cs
   - For append-mode import notifications with Requirements and AppendedCount properties

### Phase 3: UI Integration
7. **Implement ImportAdditionalAsync in SideMenuViewModel**
   - Add ImportAdditionalAsync() method to SideMenuViewModel.cs
   - Calls _workspaceManagementMediator.ImportAdditionalRequirementsAsync() with proper error handling and logging

8. **Enhance HandleImportRequirementsRequest with Append Logic**
   - Modify TestCaseGenerationMediator.HandleImportRequirementsRequest() to support IsAppendMode
   - Integrate scrubber validation and publish AdditionalRequirementsImported events for append operations

9. **Integrate Scrubber into Import Workflow**
   - Connect IRequirementDataScrubber to actual import processing in TestCaseGenerationMediator
   - Call scrubber.ProcessRequirementsAsync() during requirement processing with validation feedback

### Phase 4: Cleanup & Testing
10. **Remove Duplicate Import UI from Requirements View**
    - Remove ImportAdditional command and related UI elements from TestCaseGenerator_VM
    - Avoid duplication with side menu functionality

11. **Clean ViewModelFactory Temporary Integration**
    - Remove any temporary stubs or unused scrubber integration attempts from ViewModelFactory.cs
    - Keep only essential functionality

12. **Test Import Additional Functionality**
    - Test complete workflow: Side menu Import Additional → file dialog → requirements appended → scrubber validation applied → UI updated with append notification

## Architectural Guidelines Compliance

### Domain Separation
- **TestCaseGeneration Domain**: Handles requirement processing and scrubbing
- **WorkspaceManagement Domain**: Handles file operations and cross-domain coordination
- **Shared Services**: Universal scrubber infrastructure

### Communication Patterns
- **UI → Domain Mediator**: SideMenuViewModel calls WorkspaceManagementMediator
- **Cross-Domain**: WorkspaceManagementMediator broadcasts to TestCaseGenerationMediator
- **Event Publishing**: Domain mediators publish domain-specific events

### Service Integration
- **Dependency Injection**: All services registered in DI container
- **Constructor Injection**: ViewModels receive required services via constructors
- **Interface-Based**: All services implement interfaces for testability

## Success Criteria

1. **Functional Requirements**
   - Import Additional Requirements accessible from side menu
   - File dialog opens and allows file selection
   - Requirements are appended to existing project (not replaced)
   - Scrubber validation applied to all imported requirements
   - UI updates to show imported requirements

2. **Technical Requirements**
   - Architectural compliance with domain-driven design
   - Proper cross-domain communication via mediators
   - No service location anti-patterns
   - Comprehensive error handling and logging
   - No UI duplication

3. **Quality Requirements**
   - Universal scrubber works for all import scenarios (new project and additional requirements)
   - Clean, maintainable code following established patterns
   - Comprehensive test coverage for integration scenarios
   - No architectural violations detected by fail-fast validation

## Risk Mitigation

1. **Integration Testing**: Test cross-domain communication thoroughly
2. **Incremental Implementation**: Build and test each phase before moving to next
3. **Architectural Review**: Verify compliance at each step
4. **Code Review**: Ensure all changes follow established patterns
5. **Cleanup Verification**: Remove all unused/disconnected code

## Future Enhancements

1. **Advanced Scrubbing Rules**: Configurable validation rules
2. **Import Progress Feedback**: Real-time progress indicators for large files
3. **Import History**: Track and manage previous import operations
4. **Batch Operations**: Support for importing multiple files simultaneously
5. **Export Integration**: Use scrubber for export validation as well