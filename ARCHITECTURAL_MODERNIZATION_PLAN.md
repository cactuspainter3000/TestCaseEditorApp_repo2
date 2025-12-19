# TestCaseEditorApp Architectural Modernization Plan
**Version**: 1.0  
**Date**: December 19, 2025  
**Status**: In Progress

---

## 🎯 **OVERVIEW & OBJECTIVES**

### Primary Goals
1. **Reduce MainViewModel complexity** - Currently 4,481 lines carrying excessive UI orchestration weight
2. **Implement proper domain-driven architecture** - Move ViewModels to appropriate domain boundaries
3. **Enable systematic scalability** - Create foundation for future modular development
4. **Maintain stability** - Incremental approach with validation at each step

### Success Metrics
- MainViewModel reduced below 2,000 lines
- All domain ViewModels properly organized
- Zero compilation/runtime regressions
- Composite ViewModels decomposed into focused components

---

## 📋 **PHASE 1: DOMAIN ORGANIZATION** *(Current Phase)*

### **Objective**: Move domain-specific ViewModels from `MVVM/ViewModels/` to proper domain locations

### **Approach**: Systematic relocation with incremental validation
- ✅ Build validation after each move
- ✅ Runtime testing for critical paths
- ✅ Namespace updates and using statement management

### **Progress Status**

#### ✅ **COMPLETED STEPS**
| Step | ViewModel | Status | Build | Runtime |
|------|-----------|---------|-------|---------|
| 1 | RequirementsViewModel | ✅ Complete | ✅ Pass | ✅ Pass |
| 2 | TestCaseGeneratorViewModel | ✅ Complete | ✅ Pass | ✅ Pass |
| 3 | TestCaseGenerator_CoreVM | ✅ Complete | ✅ Pass | ✅ Pass |
| 4 | TestCaseGenerator_AnalysisVM | ✅ Complete | ✅ Pass | ⏳ Pending |

#### 🔄 **REMAINING STEPS (Phase 1)**
| Step | Target ViewModel | Domain | Est. Complexity |
|------|------------------|--------|-----------------|
| 5 | TestCaseGenerator_VM | TestCaseGeneration | Medium |
| 6 | TestCaseGenerator_NavigationVM | TestCaseGeneration | Medium |
| 7 | TestCaseGenerator_HeaderVM | TestCaseGeneration | Low |
| 8 | TestCaseGenerator_QuestionsVM | TestCaseGeneration | Low |
| 9 | TestCaseGenerator_CreationVM | TestCaseGeneration | Medium |
| 10 | TestCaseGenerator_AssumptionsVM | TestCaseGeneration | Low |

#### **Notes**
- `TestCaseGenerator_NavigationService.cs` & `TestCaseGenerator_HeaderContext.cs` are services/models, not ViewModels
- Each step includes: File move → Namespace update → Build test → Runtime validation

---

## 📋 **PHASE 2: COMPOSITE VIEWMODEL DECOMPOSITION** *(Next Phase)*

### **Objective**: Break down monolithic ViewModels into focused, single-responsibility components

### **Target Architecture**
```
TestCaseGenerator_VM (Coordinator)
├── TestCaseGenerator_MetadataVM     [NEW]
├── TestCaseGenerator_TablesVM       [Existing, to be extracted]  
├── TestCaseGenerator_SupplementalVM [NEW]
└── TestCaseGenerator_AnalysisVM     [Already domain-organized]
```

### **Decomposition Strategy**

#### **2.1 TestCaseGenerator_VM Breakdown**
**Current State**: 669-line monolithic ViewModel handling 4 distinct concerns
**Target State**: Lightweight coordinator + 4 focused ViewModels

##### **Extract TestCaseGenerator_MetadataVM**
- **Scope**: All metadata chips display logic
- **Responsibilities**: 
  - Requirements metadata visualization
  - Core field display (ID, Name, FDAL, etc.)
  - Date timeline management
  - Chip filtering and organization
- **Extraction Target**: ~200-300 lines
- **Dependencies**: Requirement model, chip infrastructure

##### **Extract TestCaseGenerator_TablesVM**  
- **Scope**: Table management and display
- **Responsibilities**:
  - Table collection management
  - Table selection state
  - Table editing workflows
  - Table-specific commands
- **Extraction Target**: ~150-200 lines
- **Dependencies**: TableItemViewModel, table services

##### **Extract TestCaseGenerator_SupplementalVM**
- **Scope**: Supplemental content (paragraphs) management
- **Responsibilities**:
  - Paragraph collection display
  - Supplemental content editing
  - Content selection workflows
- **Extraction Target**: ~100-150 lines
- **Dependencies**: Paragraph models, content services

#### **2.2 RequirementsViewModel Cleanup**
**Current State**: Bindings split across 2 files unnecessarily
**Target State**: Consolidated single file

**Action Items**:
- Merge `RequirementsViewModel.Bindings.cs` into main file
- Add `[ObservableProperty] private bool hasMeta;` to main file
- Delete redundant bindings file

#### **2.3 Additional Composite Candidates**
- **NavigationViewModel**: Consider splitting navigation concerns
- **MainViewModel**: Progressive reduction through domain extraction
- **ImportWorkflowViewModel**: Evaluate for workflow-specific breakdowns

### **Implementation Timeline (Phase 2)**
```
Week 1: TestCaseGenerator_MetadataVM extraction
Week 2: TestCaseGenerator_SupplementalVM extraction  
Week 3: RequirementsViewModel consolidation
Week 4: Integration testing and optimization
```

---

## 📋 **PHASE 3: MAINVIEWMODEL MODERNIZATION** *(Future Phase)*

### **Objective**: Transform MainViewModel from monolithic controller to lightweight coordinator

### **Current Issues** 
- 4,481 lines of mixed concerns
- Direct UI manipulation logic
- Multiple domain responsibilities
- Navigation, persistence, workflow coordination all intermingled

### **Target Architecture**
```
MainViewModel (Lightweight Coordinator)
├── WorkspaceCoordinator     [NEW]
├── NavigationCoordinator    [NEW] 
├── PersistenceCoordinator   [NEW]
├── UIStateManager           [NEW]
└── DomainMediators          [Existing]
    ├── TestCaseGenerationMediator
    ├── TestFlowMediator
    └── [Other domain mediators]
```

### **Modernization Strategy**

#### **3.1 Extract Core Coordinators**
- **WorkspaceCoordinator**: Project lifecycle, dirty state, file operations
- **NavigationCoordinator**: View switching, modal management, navigation state  
- **PersistenceCoordinator**: Save/load operations, auto-save, backup management
- **UIStateManager**: Window state, layout, user preferences

#### **3.2 Strengthen Domain Mediation**
- Ensure all domain operations go through mediators
- Remove direct domain logic from MainViewModel
- Implement proper command/query separation

#### **3.3 Progressive Extraction Approach**
1. **Workspace Operations** (Save, Load, New, etc.) → WorkspaceCoordinator
2. **Navigation Logic** → NavigationCoordinator  
3. **Persistence Concerns** → PersistenceCoordinator
4. **UI State Management** → UIStateManager
5. **Final Integration** → Lightweight MainViewModel

---

## 🛠️ **IMPLEMENTATION GUIDELINES**

### **Development Standards**

#### **File Organization**
```
MVVM/
├── Domains/
│   ├── TestCaseGeneration/
│   │   ├── ViewModels/          [Domain ViewModels]
│   │   ├── Mediators/           [Domain coordination]
│   │   ├── Services/            [Domain services]
│   │   └── Views/               [Domain-specific views]
│   └── [Other domains]/
├── ViewModels/                  [Shared/general ViewModels]
├── Models/                      [Shared models]
└── Views/                       [General views]
```

#### **Naming Conventions**
- **Domain ViewModels**: `{Domain}_{Component}VM.cs`
- **Coordinators**: `{Purpose}Coordinator.cs`  
- **Mediators**: `{Domain}Mediator.cs`
- **Namespaces**: `TestCaseEditorApp.MVVM.Domains.{Domain}.ViewModels`

#### **Validation Requirements**
- ✅ **Build validation**: Every change must compile clean
- ✅ **Runtime testing**: Critical paths must be manually verified
- ✅ **Unit tests**: New components require test coverage
- ✅ **Integration validation**: End-to-end workflows tested

### **Quality Gates**

#### **Phase 1 Completion Criteria**
- [ ] All TestCaseGenerator_* ViewModels moved to domain
- [ ] All builds passing with only warnings (no errors)
- [ ] Application startup and basic navigation functional
- [ ] Requirements import/export workflows operational

#### **Phase 2 Completion Criteria** 
- [ ] TestCaseGenerator_VM under 200 lines (coordinator only)
- [ ] All sub-ViewModels properly encapsulated
- [ ] XAML binding updates completed
- [ ] Performance maintained or improved

#### **Phase 3 Completion Criteria**
- [ ] MainViewModel under 2,000 lines  
- [ ] Clear separation of concerns achieved
- [ ] All domain operations mediated
- [ ] No regression in functionality

---

## 🎯 **SUCCESS METRICS & VALIDATION**

### **Technical Metrics**
- **Code Size**: MainViewModel lines < 2,000
- **Compilation**: Zero build errors
- **Performance**: No regression in startup/operation time
- **Test Coverage**: >80% for new components

### **Architectural Metrics**
- **Domain Separation**: All domain ViewModels properly located
- **Responsibility Focus**: Single responsibility principle maintained
- **Coupling**: Reduced interdependencies between components
- **Maintainability**: Clear component boundaries

### **User Experience Metrics**
- **Functionality**: No feature regression
- **Stability**: No crashes introduced
- **Performance**: Responsive UI maintained
- **Workflow**: All user workflows preserved

---

## 📅 **TIMELINE & MILESTONES**

### **Phase 1: Domain Organization** *(2-3 weeks)*
- **Week 1**: Steps 5-7 (TestCaseGenerator_VM, NavigationVM, HeaderVM)
- **Week 2**: Steps 8-10 (QuestionsVM, CreationVM, AssumptionsVM)
- **Week 3**: Phase 1 validation and documentation

### **Phase 2: Composite Decomposition** *(4 weeks)*
- **Week 1**: MetadataVM extraction and testing
- **Week 2**: SupplementalVM extraction and testing  
- **Week 3**: RequirementsViewModel consolidation
- **Week 4**: Integration testing and optimization

### **Phase 3: MainViewModel Modernization** *(6-8 weeks)*
- **Weeks 1-2**: Coordinator extraction planning and design
- **Weeks 3-4**: WorkspaceCoordinator and NavigationCoordinator implementation
- **Weeks 5-6**: PersistenceCoordinator and UIStateManager implementation  
- **Weeks 7-8**: Integration, testing, and performance optimization

---

## 🚨 **RISK MANAGEMENT**

### **Identified Risks**

#### **High Priority Risks**
1. **Namespace Conflicts**: Moving ViewModels may break XAML bindings
   - *Mitigation*: Systematic using statement updates and build validation
2. **Runtime Binding Failures**: XAML DataTemplates may lose ViewModel references
   - *Mitigation*: Runtime testing after each move
3. **Performance Regression**: Additional layers may impact performance
   - *Mitigation*: Performance monitoring and optimization

#### **Medium Priority Risks**
1. **Merge Conflicts**: Multiple developers working on ViewModel changes
   - *Mitigation*: Clear communication and sequential implementation
2. **Testing Coverage Gaps**: Complex ViewModels may have hidden dependencies
   - *Mitigation*: Comprehensive manual testing and unit test expansion

### **Rollback Procedures**
- **Phase 1**: Git branch per step, easy individual rollback
- **Phase 2**: Feature flags for new ViewModel structure  
- **Phase 3**: Gradual coordinator introduction with fallback mechanisms

---

## 📚 **REFERENCES & RESOURCES**

### **Existing Documentation**
- `ARCHITECTURAL_GUIDELINES.md` - Domain architecture principles
- `VIEWMODEL_CONSOLIDATION_PLAN.md` - Original consolidation analysis

### **Key Files**
- `MVVM/ViewModels/MainViewModel.cs` - Primary refactoring target
- `MVVM/Domains/TestCaseGeneration/` - Target domain organization
- `MVVM/ViewModels/IViewModelFactory.cs` - ViewModel instantiation patterns

### **Related Implementation**
- Domain Mediator patterns (already implemented)
- BaseDomainViewModel foundation (established)
- ViewModelFactory infrastructure (functional)

---

## 📝 **CHANGE LOG**

| Date | Version | Changes | Author |
|------|---------|---------|---------|
| 2025-12-19 | 1.0 | Initial comprehensive plan created | System |

---

## ✅ **NEXT ACTIONS**

### **Immediate (This Session)**
1. Complete Phase 1, Step 5: Move TestCaseGenerator_VM to domain
2. Validate build and runtime functionality  
3. Plan Step 6 implementation approach

### **Short Term (Next Session)**
1. Continue Phase 1 steps 6-10
2. Maintain build validation discipline
3. Document any discovered dependencies

### **Medium Term (Next Week)**
1. Complete Phase 1 validation
2. Begin Phase 2 planning and design
3. Set up testing infrastructure for composite decomposition