# TestCaseEditorApp - Project Context for AI

> **🎯 For Complete Architectural Patterns**: See `ARCHITECTURAL_GUIDE_AI.md`  
> **Purpose**: Project-specific context, domains, and quick reference information

## 🚨 ARCHITECTURAL COMPLIANCE REMINDER

**BEFORE implementing any code changes involving:**
- ViewModels, services, dependency injection
- Cross-domain communication, mediators 
- New components or architectural patterns

**ALWAYS first consult `ARCHITECTURAL_GUIDE_AI.md` to:**
- Identify existing patterns and implementation chains
- Avoid anti-patterns (like `new SomeService()` in ViewModels)
- Follow "Questions First, Code Second" methodology
- Use proper DI container patterns (`App.ServiceProvider?.GetService<T>()`)

---

## Project Overview

**WPF Application (.NET 8)** with domain-driven architecture for test case generation and workflow management. Uses **dependency injection**, **mediator pattern**, and **MVVM** with fail-fast validation.

### Core Domains
- **TestCaseGeneration**: Requirements → Assumptions → Questions → Test Cases → Export
- **TestFlow**: Flow diagram creation and validation  
- **WorkspaceManagement**: Project/file operations
- **ChatGptExportAnalysis**: LLM integration analysis

---

## 🖥️ Workspace Architecture

### **5-Workspace Pattern**
- **MainWorkspace**: Primary content area
- **HeaderWorkspace**: Context-specific headers  
- **NavigationWorkspace**: Domain-specific navigation
- **TitleWorkspace**: Domain-specific titles
- **SideMenuWorkspace**: Global menu coordination

### **Coordination Rules**
- Each workspace controlled by its assigned ViewModel
- Cross-workspace communication via domain mediators ONLY
- ViewAreaCoordinator manages coordinated workspace switches
- NO direct ViewModel-to-ViewModel communication

---

## 🚀 Quick Reference

### **Running Tests**
```powershell
.\run-tests.ps1                    # All test projects
.\run-tests.ps1 -StopOnFailure    # Stop on first failure
```

### **Building & Debugging**
- Main project: `TestCaseEditorApp.csproj`
- Build warnings: `build_warnings*.txt` files
- Test isolation: `Directory.Build.targets`

### **Key Files & Locations**
- **Config**: `Config/defaults.catalog.template.json`, `Config/verification-methods.json`
- **Domain Structure**: `MVVM/Domains/{DomainName}/ViewModels/`, `/Services/`, `/Mediators/`
- **Shared Services**: `Services/` (root level), `MVVM/Utils/`, `Converters/`, `Helpers/`
- **AI Optimization**: `Config/RAG-*.md` files

---

## ⚙️ Configuration & Environment

### **Environment Variables**
- `LLM_PROVIDER`: ollama|openai|noop (default: ollama)
- `OLLAMA_MODEL`: Model name (default: phi4-mini:3.8b-q4_K_M)

### **LLM Integration Testing**
- `test-enhanced-integration.ps1` - Full LLM pipeline testing
- `test-logic-conflicts.ps1` - Conflict resolution testing  
- `test-rag-status.ps1` - RAG system validation

---

## 📋 Current Project Status

### **Domain Organization** (In Progress)
**Phase 1**: Moving ViewModels from legacy `MVVM/ViewModels/` to domain-specific locations
- ✅ RequirementsViewModel, TestCaseGeneratorViewModel moved
- 🔄 Additional TestCaseGenerator_* ViewModels being relocated

### **Documentation Status**
- ✅ `ARCHITECTURAL_GUIDE_AI.md` - **Primary source for all implementation patterns**
- ✅ `ARCHITECTURAL_GUIDELINES.md` - Human-readable decision framework
- 🗂️ Legacy documents marked as superseded, kept for historical reference

---

## 🧭 For Implementation Guidance

**Use `ARCHITECTURAL_GUIDE_AI.md` for:**
- ✅ Complete implementation chains and dependency discovery
- ✅ Anti-pattern detection and systematic validation  
- ✅ Cross-domain communication workflows
- ✅ XAML troubleshooting and UI patterns
- ✅ Migration strategies and service coordination
- ✅ "Questions First, Code Second" methodology

**This file provides:** Project context and quick reference only.

**This file provides:** Project context and quick reference only.