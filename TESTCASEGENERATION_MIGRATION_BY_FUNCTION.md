# TestCaseGeneration Domain Migration Analysis by Function

## Overview

This document provides a comprehensive analysis of functionality that needs to be migrated from the TestCaseGeneration domain to the Requirements domain, organized by business function rather than technical component. This functional view helps prioritize migration work based on user impact and business value.

**Analysis Date**: January 17, 2026  
**Current Status**: Requirements domain has ~40% of needed functionality  
**Migration Scope**: 60% of functionality missing

## 🎯 MIGRATION OBJECTIVES & PRINCIPLES

### PRIMARY OBJECTIVE
**Consolidate all requirements-related functionality into a single Requirements domain**, enabling the safe removal of TestCaseGeneration domain without functionality loss.

### CORE PRINCIPLES
1. **No Hybrid Solutions** - Each function must be 100% migrated or 0% migrated
2. **Complete Functional Equivalence** - Users must not lose any existing capabilities
3. **Clean Domain Boundaries** - Requirements domain owns all requirement workflows
4. **Single Source of Truth** - Eliminate duplicate implementations across domains

### ARCHITECTURAL GUIDANCE
📖 **See ARCHITECTURAL_GUIDE_AI.md for detailed implementation patterns:**
- **Domain Organization**: Section on proper domain structure and boundaries
- **Dependency Injection**: Best practices for service registration and resolution
- **Cross-Domain Communication**: Mediator patterns and event coordination
- **ViewModel Architecture**: MVVM patterns and property notification
- **Questions First, Code Second**: Methodology for systematic implementation

### WHAT WE'RE AVOIDING
❌ **Hybrid cross-domain dependencies** (Requirements calling TestCaseGeneration services)  
❌ **Partial migrations** (some features in Requirements, some still in TestCaseGeneration)  
❌ **Duplicate functionality** (same feature implemented in both domains)  
❌ **Temporary bridges** that become permanent technical debt

### END STATE VISION
✅ **Requirements domain** contains ALL requirement-related functionality  
✅ **TestCaseGeneration domain** completely removed from codebase  
✅ **Zero external dependencies** on TestCaseGeneration namespace  
✅ **Clean DI container** with no TestCaseGeneration registrations

---

## 📋 MIGRATION NEEDS BY FUNCTIONAL AREA

### 🔄 IMPORT/EXPORT PROCESSES
**Status**: ❌ **MISSING - Core import workflows not in Requirements domain**

**MIGRATION OBJECTIVE**: Move all requirement import/export functionality to Requirements domain to establish it as the single source of truth for requirement data operations.

**WHY MIGRATE**: 
- Requirements domain should own all requirement data lifecycle operations
- Import/Export is fundamental requirement management functionality
- Current cross-domain dependency creates architectural confusion

**END STATE**: 
- ✅ Requirements domain handles all import workflows independently
- ✅ JAMA integration fully contained within Requirements domain  
- ✅ No dependency on TestCaseGeneration for data operations
- ✅ Single consistent import experience for users

**AVOID**: 
- ❌ Keeping import in TestCaseGeneration while requirements live in Requirements
- ❌ Cross-domain import coordination (Requirements triggering TestCaseGeneration import)
- ❌ Dual import paths or duplicate import services

**Missing Functionality:**
- **Import Additional Requirements** process (SmartRequirementImporter + RequirementImportCoordinator)
- **Document Format Auto-Detection** (DocumentFormatDetector)
- **JAMA Integration** (5 JAMA services: parser, enricher, mapper, etc.)
- **Import/Export ViewModels** (RequirementImportExportViewModel)

**Technical Components:**
- `SmartRequirementImporter.cs`
- `RequirementImportCoordinator.cs`
- `DocumentFormatDetector.cs`
- `JamaAllDataDocxParser.cs`
- `JamaAllDataDocxEnricher.cs`
- `JamaAllDataEnricher.cs`
- `JamaRequirementMapper.cs`
- `JamaImportInspection.cs`
- `RequirementImportExportViewModel.cs`

**Implementation Guidance:**
📖 **ARCHITECTURAL_GUIDE_AI.md References:**
- Service registration patterns for import services
- File operation best practices and error handling
- Import workflow coordination using mediator patterns

**User Impact**: Users cannot import additional requirements or work with JAMA exports

---

### 🧠 LLM-POWERED REQUIREMENT ANALYSIS
**Status**: ⚠️ **PARTIAL - Basic analysis exists, advanced features missing**

**MIGRATION OBJECTIVE**: Consolidate all analysis functionality into Requirements domain to eliminate cross-domain dependencies and provide complete analysis capabilities.

**WHY MIGRATE**: 
- Current hybrid solution creates maintenance complexity
- Analysis is core to requirement management workflow
- Performance features (caching, monitoring) should be co-located with analysis logic
- Requirements domain needs full control over analysis lifecycle

**END STATE**: 
- ✅ Requirements domain contains complete analysis engine
- ✅ All LLM integration contained within Requirements domain
- ✅ Performance monitoring integrated with Requirements workflow
- ✅ No cross-domain references for analysis functionality

**AVOID**: 
- ❌ Keeping current cross-domain AnalysisVM reference
- ❌ Split analysis logic (basic in Requirements, advanced in TestCaseGeneration)
- ❌ Performance monitoring separated from analysis logic

**Existing**: Basic AnalysisVM via cross-domain reference  
**Missing Functionality:**
- **Advanced Analysis Engine** (RequirementAnalysisService - 1,808 lines)
- **Analysis Caching System** (RequirementAnalysisCache)
- **LLM Health Monitoring** (LlmServiceHealthMonitor)
- **Analysis Performance Tracking**
- **Smart Clipboard Operations** for external LLM workflow

**Technical Components:**
- `TestCaseGenerator_AnalysisVM.cs` (1,808 lines)
- `RequirementAnalysisService.cs`
- `IRequirementAnalysisService.cs`
- `RequirementAnalysisCache.cs`
- `LlmServiceHealthMonitor.cs`

**Implementation Guidance:**
📖 **ARCHITECTURAL_GUIDE_AI.md References:**
- LLM service integration patterns and error handling
- Caching strategies for analysis results
- Property notification patterns for analysis state
- Cross-domain service migration methodology

**User Impact**: Analysis works but lacks performance optimization and advanced LLM features

---

### ❓ CLARIFYING QUESTIONS WORKFLOW
**Status**: ❌ **COMPLETELY MISSING**

**MIGRATION OBJECTIVE**: Establish clarifying questions as core requirement analysis capability within Requirements domain.

**WHY MIGRATE**: 
- Clarifying questions improve requirement quality (core Requirements domain responsibility)
- Questions workflow is logically part of requirement refinement process
- LLM-powered question generation is natural extension of requirement analysis

**END STATE**: 
- ✅ Complete questions generation workflow in Requirements domain
- ✅ Question management integrated with requirement lifecycle
- ✅ Questions-to-assumptions conversion workflow available
- ✅ No dependency on TestCaseGeneration for question functionality

**AVOID**: 
- ❌ Keeping questions in TestCaseGeneration while requirements are in Requirements
- ❌ Cross-domain question triggering (Requirements asking TestCaseGeneration to generate questions)
- ❌ Split workflow (questions in TestCaseGeneration, assumptions in Requirements)

**Missing Functionality:**
- **Question Generation Process** (TestCaseGenerator_QuestionsVM - 1,550 lines)
- **Question Categorization** and severity assessment
- **Questions-to-Assumptions Conversion**
- **Clarifying Question Service** (ClarifyingQuestionService)
- **Questions UI** (TestCaseGenerator_QuestionsView.xaml)

**Technical Components:**
- `TestCaseGenerator_QuestionsVM.cs` (1,550 lines)
- `ClarifyingQuestionService.cs`
- `TestCaseGenerator_QuestionsView.xaml`
- Supporting parsing services

**User Impact**: Users cannot generate or manage clarifying questions for requirements

---

### 📋 ASSUMPTIONS MANAGEMENT
**Status**: ❌ **COMPLETELY MISSING**

**MIGRATION OBJECTIVE**: Integrate assumptions management as part of requirement quality workflow within Requirements domain.

**WHY MIGRATE**: 
- Assumptions directly impact requirement interpretation and quality
- Verification method selection is fundamental to requirement validation
- Assumptions should be co-located with the requirements they clarify

**END STATE**: 
- ✅ Complete assumptions workflow in Requirements domain
- ✅ Verification method selection integrated with requirement management
- ✅ Preset assumptions available as part of requirement analysis
- ✅ Assumptions linked to specific requirements for traceability

**AVOID**: 
- ❌ Assumptions managed in TestCaseGeneration while requirements live in Requirements
- ❌ Cross-domain assumption creation (Requirements triggering TestCaseGeneration)
- ❌ Assumptions disconnected from their parent requirements

**Missing Functionality:**
- **Assumptions Creation Workflow** (TestCaseGenerator_AssumptionsVM - 729 lines)
- **Verification Method Selection**
- **Preset Assumptions Management**
- **Custom Instructions Handling**
- **Assumptions UI** (TestCaseGenerator_AssumptionsView.xaml)

**Technical Components:**
- `TestCaseGenerator_AssumptionsVM.cs` (729 lines)
- `TestCaseGenerator_AssumptionsView.xaml`

**User Impact**: No assumptions workflow exists in Requirements domain

---

### 🧪 TEST CASE CREATION PROCESS
**Status**: ❌ **COMPLETELY MISSING**

**MIGRATION OBJECTIVE**: **SPECIAL CASE** - Evaluate if test case creation belongs in Requirements domain or separate TestCaseCreation domain.

**WHY EVALUATE**: 
- Test case creation may be distinct from requirement management
- Could belong in existing TestCaseCreation domain instead of Requirements
- Need clear domain boundaries between requirement analysis and test case generation

**DECISION NEEDED**: 
- 🤔 **Option A**: Migrate to Requirements domain (requirement → test case is single workflow)
- 🤔 **Option B**: Migrate to TestCaseCreation domain (separate test case concerns)
- 🤔 **Option C**: Create dedicated TestGeneration domain

**END STATE** (if migrating to Requirements): 
- ✅ Complete test case creation workflow in Requirements domain
- ✅ Test cases directly linked to source requirements
- ✅ Integrated requirement-to-test-case workflow

**AVOID**: 
- ❌ Test case creation split across multiple domains
- ❌ Cross-domain test case coordination
- ❌ Test cases disconnected from source requirements

**Missing Functionality:**
- **Test Case Creation Workflow** (TestCaseGenerator_CreationVM)
- **Test Case Generation Logic** (TestCaseGenerator_CoreVM)
- **Test Case UI** (TestCaseGenerator_CreationView.xaml)
- **Test Case to Requirement Mapping**

**Technical Components:**
- `TestCaseGenerator_CreationVM.cs`
- `TestCaseGenerator_CoreVM.cs`
- `TestCaseGenerator_CreationView.xaml`

**User Impact**: Cannot create test cases from requirements (core application purpose)

---

### ⚙️ WORKSPACE COORDINATION
**Status**: ⚠️ **PARTIAL - Basic workspace exists, advanced coordination missing**

**Existing**: Requirements main workspace view  
**Missing Functionality:**
- **Advanced Workspace Management** (TestCaseGeneratorMainVM)
- **Multi-workspace Coordination** logic
- **Workspace View Switching** (RequirementsWorkspaceView.xaml)
- **Splash Screen Workflows** (2 splash ViewModels + XAML)

**Technical Components:**
- `TestCaseGeneratorMainVM.cs`
- `TestCaseGeneratorSplashViewModel.cs`
- `TestCaseGeneratorSplashScreenViewModel.cs`
- `RequirementsWorkspaceView.xaml`
- `TestCaseGeneratorSplashScreen.xaml`

**Impact**: Basic workspace works but lacks advanced coordination features

---

### 📊 ADVANCED HEADER/STATUS FUNCTIONALITY
**Status**: ⚠️ **PARTIAL - Basic header exists, LLM integration missing**

**Existing**: Basic header (project info, basic stats)  
**Missing Functionality:**
- **LLM Status Integration** in header
- **Verification Method UI Elements**
- **Default Assumptions Display**
- **File Operation Commands** (Import/Export/Save)
- **Visual Requirement Highlights**

**Technical Components:**
- `TestCaseGenerator_HeaderVM.cs` (398 lines)
- `TestCaseGenerator_HeaderContext.cs`
- `TestCaseGenerator_HeaderView.xaml`
- `LlmServiceStatusIndicator.xaml`

**Impact**: Header works but lacks advanced features users expect

---

### 🔗 CROSS-DOMAIN COMMUNICATION
**Status**: ⚠️ **PARTIAL - Basic events work, advanced coordination missing**

**Existing**: Basic cross-domain events via dual mediator subscription  
**Missing Functionality:**
- **Complete Mediator Interface** (TestCaseGenerationMediator methods)
- **Advanced Event Coordination**
- **Domain State Synchronization**
- **Cross-domain Data Sharing**

**Technical Components:**
- `ITestCaseGenerationMediator.cs`
- `TestCaseGenerationMediator.cs`
- Event coordination logic

**Impact**: Basic communication works but may have edge cases

---

### 📈 PERFORMANCE & MONITORING
**Status**: ❌ **COMPLETELY MISSING**

**Missing Functionality:**
- **Analysis Result Caching** (RequirementAnalysisCache)
- **LLM Service Health Monitoring**
- **Performance Metrics Tracking**
- **Cache Statistics Display**
- **Service Status Indicators** (LlmServiceStatusIndicator.xaml)

**Technical Components:**
- `RequirementAnalysisCache.cs`
- `LlmServiceHealthMonitor.cs`
- `LlmServiceStatusIndicator.xaml`

**Impact**: No performance optimization or health monitoring

---

### 🎨 UI COMPONENTS & CONVERTERS
**Status**: ⚠️ **PARTIAL - Core UI exists, specialized components missing**

**Existing**: Basic tables, paragraphs, analysis UI controls  
**Missing Functionality:**
- **Title Management UI** (TestCaseGenerator_TitleView.xaml)
- **Import Workflow Converters** (ImportWorkflowConverters)
- **Text Highlighting Converters** (2 highlighting converters)
- **Bracket/Fix Text Converters**

**Technical Components:**
- `TestCaseGenerator_TitleVM.cs`
- `TestCaseGenerator_TitleView.xaml`
- `ImportWorkflowConverters.cs`
- `FixTextHighlightConverter.cs`
- `BracketHighlightConverter.cs`

**Impact**: Basic UI works but lacks polish and specialized features

---

## 🎯 FUNCTIONAL MIGRATION PRIORITY

### 🚨 CRITICAL (Application Core Functions)
1. **Test Case Creation Process** - Core application purpose
2. **Import Additional Requirements** - Primary user workflow  
3. **LLM Analysis Engine** - Core analysis functionality

**📖 Implementation Strategy Reference**: See ARCHITECTURAL_GUIDE_AI.md "Questions First, Code Second" methodology for systematic approach to critical function migration.

### 🔴 HIGH (Major Features)
4. **Clarifying Questions Workflow** - Key LLM feature
5. **Assumptions Management** - Required for test case quality
6. **Performance & Monitoring** - User experience

### 🟡 MEDIUM (Enhanced Features)
7. **Advanced Workspace Coordination** - UI polish
8. **Advanced Header Functionality** - Status/monitoring
9. **Cross-domain Communication** - Edge case handling

### 🔵 LOW (Nice to Have)
10. **UI Components & Converters** - Polish and visual enhancements

---

## 📋 FUNCTIONAL READINESS ASSESSMENT

### ✅ FUNCTIONS READY (40%)
- Basic requirement browsing/navigation
- Basic requirement analysis (via cross-domain reference)  
- Basic workspace display
- Basic header information

### ❌ FUNCTIONS MISSING (60%)
- Test case creation (CRITICAL)
- Import additional requirements (CRITICAL)
- Clarifying questions workflow
- Assumptions management
- Performance monitoring
- Advanced LLM features

---

## 🚀 RECOMMENDED MIGRATION APPROACH

### Phase 1: Core Workflows (CRITICAL)
**Priority**: 🚨 **IMMEDIATE**
- Migrate **Import Additional Requirements** functionality
- Migrate **Test Case Creation Process**
- Migrate **Advanced Analysis Engine**

📖 **ARCHITECTURAL_GUIDE_AI.md Implementation Guidance:**
- Follow "Domain Service Migration" patterns for systematic service movement
- Use "Dependency Discovery" methodology to identify all related components
- Apply "Anti-Pattern Detection" to avoid hybrid solutions

### Phase 2: LLM Features (HIGH)
**Priority**: 🔴 **HIGH**
- Migrate **Clarifying Questions Workflow**
- Migrate **Assumptions Management**
- Migrate **Performance & Monitoring**

📖 **ARCHITECTURAL_GUIDE_AI.md Implementation Guidance:**
- LLM service integration patterns and health monitoring
- ViewModel architecture for complex UI workflows
- Event coordination for cross-component communication

### Phase 3: Enhanced Features (MEDIUM)
**Priority**: 🟡 **MEDIUM**
- Migrate **Advanced Workspace Coordination**
- Migrate **Advanced Header Functionality**
- Complete **Cross-domain Communication**

📖 **ARCHITECTURAL_GUIDE_AI.md Implementation Guidance:**
- Workspace coordination patterns and view management
- Cross-domain mediator consolidation strategies
- XAML troubleshooting and UI binding patterns

### Phase 4: Polish Features (LOW)
**Priority**: 🔵 **LOW**
- Migrate **UI Components & Converters**
- Final refinements and optimization

---

## 📊 MIGRATION IMPACT ASSESSMENT

**High Impact Functions** (User-facing core features):
- Import Additional Requirements
- Test Case Creation Process
- Clarifying Questions Workflow
- Assumptions Management

**Medium Impact Functions** (User experience):
- Advanced Analysis Engine
- Performance & Monitoring
- Advanced Workspace Coordination

**Low Impact Functions** (Polish and edge cases):
- UI Components & Converters
- Cross-domain Communication edge cases

---

## 🚀 MIGRATION SUCCESS CRITERIA

### ✅ FUNCTIONAL COMPLETENESS VALIDATION
Each migrated function must pass these criteria before considering migration complete:

1. **No Cross-Domain Dependencies** - Zero references to TestCaseGeneration namespace
2. **Complete Feature Parity** - All existing functionality preserved
3. **Independent Operation** - Function works entirely within Requirements domain
4. **Clean Integration** - Proper DI registration and mediator integration
5. **User Acceptance** - No loss of user experience or capabilities

### ✅ ANTI-PATTERN DETECTION
Before marking any migration complete, validate these anti-patterns are NOT present:

- ❌ **Hybrid Dependencies**: Requirements calling TestCaseGeneration services
- ❌ **Partial Migrations**: Some features migrated, others still in TestCaseGeneration  
- ❌ **Temporary Bridges**: "Quick fixes" that create permanent cross-domain coupling
- ❌ **Duplicate Implementations**: Same functionality in both domains
- ❌ **Broken Event Chains**: Events published in one domain but consumed in another

### ✅ MIGRATION VALIDATION CHECKLIST
For each functional area, confirm:

- [ ] **All related ViewModels migrated** to Requirements domain
- [ ] **All related Services migrated** to Requirements domain  
- [ ] **All related Views/XAML migrated** to Requirements domain
- [ ] **DI container registrations updated** to Requirements namespace
- [ ] **External dependencies updated** (ViewModelFactory, ViewConfiguration, etc.)
- [ ] **Cross-domain event publishing** removed or consolidated
- [ ] **Integration tests pass** for the migrated functionality
- [ ] **User workflows function identically** to pre-migration behavior

📖 **ARCHITECTURAL_GUIDE_AI.md Validation References:**
- Anti-pattern detection methodology for systematic validation
- Integration testing patterns for domain migration
- DI container validation and troubleshooting
- Cross-domain communication validation strategies

This validation ensures we achieve true consolidation rather than creating a complex hybrid system.

---

## 📝 NOTES

- This analysis is based on systematic review of all TestCaseGeneration domain components
- Migration estimates assume preservation of existing functionality
- Priority levels consider both user impact and technical complexity
- Cross-domain dependencies require careful coordination during migration

📖 **ARCHITECTURAL_GUIDE_AI.md Additional Resources:**
- Complete architectural patterns and implementation chains
- "Questions First, Code Second" methodology for systematic development
- Cross-domain communication workflows and best practices
- Migration strategies and service coordination patterns
- XAML troubleshooting and UI patterns for view migration
- Anti-pattern detection and systematic validation techniques

**For detailed implementation guidance on any migration aspect, consult ARCHITECTURAL_GUIDE_AI.md sections on:**
- Domain organization and boundary management
- Service migration and dependency injection patterns
- ViewModel architecture and property notification
- Event system coordination and mediator patterns
- UI component migration and XAML best practices

**Last Updated**: January 17, 2026

---

## 🛠️ MIGRATION EXECUTION TOOLKIT

### 📁 FILE PATH MAPPING TEMPLATES

**ViewModel Migration Pattern:**
```
FROM: MVVM/Domains/TestCaseGeneration/ViewModels/{ComponentName}.cs
TO:   MVVM/Domains/Requirements/ViewModels/Requirements_{ComponentName}.cs
```

**Service Migration Pattern:**
```
FROM: MVVM/Domains/TestCaseGeneration/Services/{ServiceName}.cs
TO:   MVVM/Domains/Requirements/Services/{ServiceName}.cs
```

**View Migration Pattern:**
```
FROM: MVVM/Domains/TestCaseGeneration/Views/{ViewName}.xaml
TO:   MVVM/Domains/Requirements/Views/Requirements_{ViewName}.xaml
```

### 🔍 DEPENDENCY DISCOVERY CHECKLIST

**For each component being migrated, systematically check:**
- [ ] **Grep search**: `TestCaseGeneration.*{ComponentName}` across entire codebase
- [ ] **DI registrations**: Search `App.xaml.cs` for registration patterns
- [ ] **Using statements**: Find all `using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.*`
- [ ] **ViewModelFactory**: Check constructor dependencies and GetService calls
- [ ] **ViewConfigurationService**: Look for hardcoded ViewModel instantiation
- [ ] **External mediator subscriptions**: Search for TestCaseGenerationEvents usage
- [ ] **XAML DataTemplates**: Search for `{x:Type testgen:ComponentName}` patterns

### ⚙️ COMMON MIGRATION CODE PATTERNS

**Namespace Update Pattern:**
```csharp
// BEFORE
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;

// AFTER  
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
```

**DI Registration Update Pattern:**
```csharp
// BEFORE (App.xaml.cs)
services.AddTransient<TestCaseGenerator_AnalysisVM>();
services.AddSingleton<IRequirementAnalysisService, RequirementAnalysisService>();

// AFTER
services.AddTransient<Requirements_AnalysisViewModel>();
services.AddSingleton<IRequirementAnalysisService, Requirements.Services.RequirementAnalysisService>();
```

**Cross-Domain Event Subscription Pattern:**
```csharp
// BEFORE (hybrid approach)
_testCaseGenerationMediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);

// AFTER (consolidated approach)
_mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
```

### 🧪 MIGRATION VALIDATION COMMANDS

**Build Validation:**
```powershell
# After each component migration
dotnet build TestCaseEditorApp.csproj | findstr "error\|warning"
```

**Dependency Reference Check:**
```powershell
# Search for remaining TestCaseGeneration references
findstr /R /S "TestCaseGeneration" *.cs *.xaml
```

**DI Container Validation:**
```csharp
// Add to App.xaml.cs for validation
var testCaseGenServices = services.Where(s => s.ServiceType.Namespace?.Contains("TestCaseGeneration") == true);
if (testCaseGenServices.Any()) throw new InvalidOperationException("TestCaseGeneration services still registered");
```

### 🚨 COMMON MIGRATION PITFALLS & SOLUTIONS

**Pitfall 1: Circular Dependencies During Migration**
```
Problem: Requirements domain trying to use partially-migrated TestCaseGeneration components
Solution: Migrate entire dependency chain at once, or use temporary interfaces
```

**Pitfall 2: XAML DataTemplate Resolution Failures**
```
Problem: XAML still referencing old ViewModel namespaces
Solution: Search all .xaml files for DataTemplate x:Type references and update
```

**Pitfall 3: Event System Conflicts**  
```
Problem: Dual event publishing causes duplicate handlers
Solution: Use event aliases during transition, remove after migration complete
```

**Pitfall 4: Service Locator Anti-Pattern**
```
Problem: Using App.ServiceProvider.GetService in ViewModels
Solution: Proper constructor injection following ARCHITECTURAL_GUIDE_AI.md patterns
```

### 📊 MIGRATION ORDER CONSTRAINTS

**MUST MIGRATE TOGETHER (Dependency Groups):**
1. **Analysis Group**: `RequirementAnalysisService` + `RequirementAnalysisCache` + `TestCaseGenerator_AnalysisVM`
2. **Import Group**: `SmartRequirementImporter` + `RequirementImportCoordinator` + `RequirementImportExportViewModel`  
3. **JAMA Group**: All 5 JAMA services must migrate as a unit
4. **Mediator Group**: `ITestCaseGenerationMediator` + `TestCaseGenerationMediator` + event definitions

**CANNOT MIGRATE UNTIL DEPENDENCIES EXIST:**
- ViewModels cannot migrate until their required Services exist in Requirements domain
- Views cannot migrate until their ViewModels exist in Requirements domain
- External components cannot be updated until Requirements domain registrations are complete

### 🎯 STEP-BY-STEP MIGRATION TEMPLATES

**Service Migration Template:**
1. [ ] Create `MVVM/Domains/Requirements/Services/` directory if needed
2. [ ] Copy service file with namespace update
3. [ ] Update all internal using statements to Requirements namespace  
4. [ ] Update DI registration in `App.xaml.cs`
5. [ ] Search and replace all external references to use Requirements namespace
6. [ ] Validate build succeeds
7. [ ] Run functionality test

**ViewModel Migration Template:**
1. [ ] Ensure all dependent services already migrated to Requirements domain
2. [ ] Copy ViewModel file with naming convention (Requirements_*)
3. [ ] Update namespace and using statements
4. [ ] Update constructor dependencies to use Requirements services
5. [ ] Update DI registration in `App.xaml.cs`
6. [ ] Update ViewModelFactory dependencies
7. [ ] Update ViewConfigurationService instantiation
8. [ ] Search/replace all external references
9. [ ] Validate build and UI functionality

**View Migration Template:**
1. [ ] Ensure ViewModel already migrated
2. [ ] Copy XAML file with naming convention (Requirements_*)
3. [ ] Update namespace declarations in XAML
4. [ ] Update DataContext bindings if hardcoded
5. [ ] Update any x:Type references to use Requirements namespace
6. [ ] Update corresponding .xaml.cs code-behind file
7. [ ] Update DataTemplate registrations in App.xaml
8. [ ] Validate UI renders correctly

### 🧪 FUNCTIONAL TESTING VERIFICATION

**Per-Function Test Scripts:**

**Import/Export Testing:**
```powershell
# Test import functionality after migration
# 1. Import a JAMA document
# 2. Verify requirements appear in Requirements domain UI  
# 3. Verify no TestCaseGeneration dependencies triggered
```

**Analysis Testing:**
```csharp
// Test analysis workflow after migration
// 1. Select a requirement in Requirements domain
// 2. Trigger analysis via "Analyze Now" button
// 3. Verify analysis results appear correctly
// 4. Verify caching works (re-analyze same requirement)
// 5. Verify LLM health monitoring functional
```

**Cross-Domain Event Testing:**
```csharp
// Test event coordination after migration
// 1. Trigger requirement selection in Requirements domain
// 2. Verify other domains receive RequirementsEvents (not TestCaseGenerationEvents)
// 3. Verify no duplicate events published
```

### 🔧 ROLLBACK PROCEDURES

**If Migration Fails:**
1. **Immediate Rollback**: `git checkout HEAD~1` to previous working state
2. **Partial Rollback**: Revert DI registrations while keeping migrated files for analysis
3. **Dependency Repair**: Use dependency discovery checklist to identify what broke
4. **Incremental Fix**: Migrate smaller component groups rather than entire functions

**Rollback Safety Checks:**
- [ ] Application builds successfully
- [ ] All domain UIs render without binding errors
- [ ] Core workflows (import, analysis) function normally
- [ ] No runtime exceptions in dependency resolution