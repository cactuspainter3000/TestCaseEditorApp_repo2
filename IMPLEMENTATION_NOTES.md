# Implementation Notes & Lessons Learned

> **Purpose**: Capture both successes and failures during implementation to avoid the "figure out → revert → struggle to reimplement" cycle.
> **Usage**: Document what works, what doesn't, and the exact steps that led to success.

---

## 🎯 **Current Implementation: Save Functionality**
*Started: December 29, 2025*

### **5-Step Pattern Application**

#### **Step 1: Architecture Understanding ✅**
- **Save Domain Owner**: WorkspaceManagementMediator 
- **Data Sources**: Multiple domains (TestCaseGeneration has requirements/test cases)
- **Save Types**: Manual (user command) + Auto-save (timer)
- **Current State**: `SaveProjectAsync()` exists but TODO implementation

#### **Step 2: New Pattern Implementation** 
*[Track what we try and results]*

**❌ Failed Attempts:**
- [ ] *Document failures here*

**✅ Working Solutions:**
- [ ] *Document successes here*

#### **Step 3: DI Chain Validation**
*[Track dependency wiring]*

**Expected Chain:**
```
SideMenuViewModel → ViewAreaCoordinator → WorkspaceManagementMediator → IPersistenceService
```

**Validation Results:**
- [ ] *Document actual vs expected*

#### **Step 4: Cross-Domain Communication Testing**
*[Track event flow]*

**Expected Events:**
- `SaveRequested` → gather data from all domains
- `SaveStarted` → progress updates  
- `SaveCompleted` → success/failure notification

**Actual Results:**
- [ ] *Document what actually happens*

#### **Step 5: Legacy Cleanup**
*[Track what gets deleted/replaced]*

**Legacy Code Found:**
- `SideMenuViewModel.SaveProjectCommand` → `/* TODO: Implement save */`
- `MainViewModel.SaveWorkspaceCommand` → `/* TODO: Implement save workspace */`

**Cleanup Actions:**
- [ ] *Document what we actually delete*

### **Key Decisions & Rationale**

| Decision | Rationale | Alternative Considered | Result |
|----------|-----------|----------------------|---------|
| *Decision 1* | *Why we chose this* | *What we didn't do* | *Did it work?* |

### **Code Snippets That Work**
*[Exact code that solves specific problems]*

```csharp
// Solution for: [Problem description]
// Context: [When to use this]
[Working code snippet]
```

### **Anti-Patterns Discovered**
*[Things we tried that failed]*

- **Don't do:** [Failed approach]
- **Why it failed:** [Root cause]
- **Do instead:** [Working approach]

---

## 📋 **Implementation Template** 
*[Copy this for each new feature]*

### **Feature: [Name]**
*Started: [Date]*

#### **5-Step Pattern Application**
1. **Architecture Understanding**: [Domain ownership, data flows, existing state]
2. **New Pattern Implementation**: [What we're building]
3. **DI Chain Validation**: [Expected vs actual dependency flow] 
4. **Cross-Domain Communication**: [Event flows and testing]
5. **Legacy Cleanup**: [What gets deleted/replaced]

#### **Success/Failure Log**
- ✅ **Success**: [What worked and why]
- ❌ **Failure**: [What failed and why]  
- 🔄 **Retry**: [What we're trying next]

#### **Final Implementation Recipe**
*[Step-by-step instructions for clean replication]*

1. [Exact step 1]
2. [Exact step 2]  
3. [Exact step 3]

---

## 🧠 **Knowledge Database**

### **Patterns That Always Work**
- **Cross-domain communication**: `BroadcastToAllDomains(event)` + `HandleBroadcastNotification`
- **UI thread safety**: `Application.Current?.Dispatcher.Invoke(() => { ... })`
- **DI wiring**: Constructor injection with null checks

### **Anti-Patterns to Avoid**
- **Mixed architectures**: Don't combine static + domain mediators
- **Incomplete DI chains**: Verify end-to-end dependency flow
- **Direct ViewModel references**: Always go through mediators

### **Common Gotchas**
- **Threading**: Cross-domain events often need UI thread marshaling
- **Timing**: Late subscribers need replay capability  
- **Validation**: Constructor injection catches missing dependencies early

---

*Last Updated: December 29, 2025*