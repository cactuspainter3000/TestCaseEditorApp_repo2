# UI Redesign Progress - Dashboard & Requirements Hub Consolidation

**Status**: Foundation complete, Requirements Hub consolidation in progress  
**Date**: 2026-07-09  
**Branch**: `refactor/requirements-cleanup`

---

## What's Been Completed ✅

### 1. **Theme System** (Commit: a9f9ed8)
- Created `ThemeConfig.cs` with 4 pre-built themes: Dark Orange (default), Dark Blue, Dark Purple, Dark Green
- Created `ThemeService.cs` for runtime theme management
- Registered ThemeService as SINGLETON in DI (`CoreServiceExtensions.cs`)
- Users can switch themes at any point; all views will be notified

**Files**:
- `MVVM/Services/Theme/ThemeConfig.cs`
- `MVVM/Services/Theme/ThemeService.cs`

### 2. **Dashboard Landing Page** (Commit: a9f9ed8)
- Created `DashboardViewModel.cs` - handles Create/Open Workshop and theme selection
- Created `DashboardView.xaml` - clean, centered UI with:
  - Two primary buttons: ✨ Create Workshop, 📁 Open Workshop
  - Theme selector buttons (dynamically generated from available themes)
  - Status messages and loading indicator
- Registered DashboardViewModel as SINGLETON in `ViewModelExtensions.cs`

**Files**:
- `MVVM/Domains/Dashboard/ViewModels/DashboardViewModel.cs`
- `MVVM/Domains/Dashboard/Views/DashboardView.xaml`
- `MVVM/Domains/Dashboard/Views/DashboardView.xaml.cs`

### 3. **DI Infrastructure Updates**
- Added Theme imports to `CoreServiceExtensions.cs`
- Added Dashboard imports to `ViewModelExtensions.cs`
- ThemeService and DashboardViewModel properly registered

---

## What Needs to Be Done (Phase 2+) ⏳

### **Phase 3: Consolidate Requirements Hub**

Currently the app has 5 scattered workspace tabs:
- UnifiedRequirementsMainView (requirement details)
- RequirementsTabSelectorView (tab bar)
- RequirementsUtilitiesView (utilities/buttons)
- CleanupView (requirement editing)
- RequirementsSearchAttachmentsView (Jama scraping)

**Goal**: Merge into ONE coherent Requirements Hub with clear zones:

```
┌─────────────────────────────────────────────────┐
│ [Search/Filter]  [Attachment Scraper]  [More]   │  ← Top toolbar
├──────────────┬───────────────────────────────────┤
│              │                                   │
│ Requirement  │ Requirement Details               │
│ List         │ + Inline Test Cases               │
│              │ + Analysis Controls               │
│              │ [LLM Analyze] [Generate Tests]    │
│              ├───────────────────────────────────┤
│              │ LLM Analysis Output               │
│              │ (logs, status, results)           │
│              │                                   │
└──────────────┴───────────────────────────────────┘
```

#### Sub-tasks:

**3.1 Create UnifiedRequirementsHubView.xaml**
- Left panel: Searchable requirement list (from RequirementsIndexViewModel)
- Right panel: Requirement details (from UnifiedRequirementsMainViewModel)
- Inline test case display (from TestCaseCreationMediator)
- Bottom panel: LLM analysis output (from IRequirementAnalysisService)
- Top toolbar: Search box, Attachment Scraper button, utilities button

**3.2 Create UnifiedRequirementsHubViewModel.cs**
- Orchestrates all the above components
- Handles requirement selection → populate details/tests
- Exposes attachment scraper as modal/panel
- Integrates LLM analysis for current requirement
- Manages "Analyze" and "Generate Tests" commands

**3.3 Remove/Archive Old Views**
- Mark RequirementsTabSelectorView as deprecated
- Move utility features from RequirementsUtilitiesView into tabs/modals within Hub
- Archive or repurpose CleanupView (may move to separate "Batch Edit" mode)

**3.4 Update Navigation**
- When user opens a workshop → navigate to Dashboard
- From Dashboard, "Open Workshop" → go to Requirements Hub (not Startup workspace)
- Theme selection on Dashboard is global (applies to all views)

**3.5 Update DataTemplate Registration**
- In `MainWindowResources.xaml`: Add `<DataTemplate DataType="{x:Type local:UnifiedRequirementsHubViewModel}" ...`
- Update existing templates to use theme colors dynamically (use ThemeService bindings)

---

### **Phase 4: Integrate with MainWindow Navigation**

**Goal**: Make Dashboard the entry point (replace Startup workspace)

**Tasks**:
- Update `App.xaml.cs` to show Dashboard on startup
- Update MainWindow.xaml to include Dashboard DataTemplate
- Update NavigationService/ViewAreaCoordinator to route to Dashboard first
- Test: Open app → see Dashboard → click buttons → navigate to appropriate workspace

---

### **Phase 5: Apply Theme System to All Views**

Currently colors are hardcoded. Next phase:
- Create `ThemeBrushConverter` to bind UI colors to ThemeService.CurrentTheme
- Update all XAML views to use `{Binding CurrentTheme.AccentPrimary, Source={StaticResource ThemeService}}`
- Remove hardcoded color values (#FF8C00, #1E1E1E, etc.)
- Ensure all custom controls respect theme colors

---

## Architecture Notes

### **Mediators & ViewModels Relationship**

- **Dashboard** → Uses INewProjectMediator, IOpenProjectMediator
- **Requirements Hub** → Uses IRequirementsMediator, IRequirementAnalysisService, TestCaseCreationMediator
- **Attachment Scraper** → Modal/panel within Hub (tied to IRequirementsMediator)
- **LLM Analysis** → Bottom panel within Hub (tied to IRequirementAnalysisService)

### **Singleton vs Transient**

- **DashboardViewModel**: SINGLETON (persistent across app lifetime)
- **UnifiedRequirementsHubViewModel**: SINGLETON (maintains requirement list/selection)
- **Theme-related services**: SINGLETON (global app theme)
- **Modal ViewModels** (Attachment Scraper, etc.): TRANSIENT (created per use)

### **Data Flow**

```
Dashboard (theme selection)
  ↓
User clicks "Open Workshop"
  ↓
OpenProjectMediator.OpenProjectAsync()
  ↓
Navigation to Requirements Hub
  ↓
Hub displays requirement list + details + tests
  ↓
User clicks "Analyze" → LLM Analysis updates bottom panel
  ↓
User clicks "Generate Tests" → New tests shown inline
  ↓
User clicks "Attachment Scraper" → Modal opens
```

---

## Remaining Questions/Decisions

1. **Attachment Scraper UI**: Should it be:
   - A modal dialog over the Requirements Hub?
   - A slide-out panel on the right?
   - A separate "tab" within the Hub?
   
   **Recommendation**: Modal dialog (cleaner separation, less UI complexity)

2. **LLM Analysis Panel Size**: Should it be:
   - Fixed height (50% of viewport)?
   - Resizable?
   - Collapsible?
   
   **Recommendation**: Resizable with collapsed default (user can expand when needed)

3. **Test Cases Display**: Should tests:
   - Show inline below requirement details?
   - Be in a separate "Tests" tab?
   - Be in a collapsible section?
   
   **Recommendation**: Separate "Tests" tab (keeps detail view cleaner)

---

## Files Changed So Far

**New Files**:
- `MVVM/Services/Theme/ThemeConfig.cs`
- `MVVM/Services/Theme/ThemeService.cs`
- `MVVM/Domains/Dashboard/ViewModels/DashboardViewModel.cs`
- `MVVM/Domains/Dashboard/Views/DashboardView.xaml`
- `MVVM/Domains/Dashboard/Views/DashboardView.xaml.cs`

**Modified Files**:
- `Services/DependencyInjection/CoreServiceExtensions.cs` (added ThemeService registration)
- `Services/DependencyInjection/ViewModelExtensions.cs` (added DashboardViewModel registration)

---

## Testing Checklist

- [ ] Build succeeds with no errors
- [ ] App launches and shows Dashboard
- [ ] Create Workshop button navigates to new project creation
- [ ] Open Workshop button navigates to open project dialog
- [ ] Theme buttons change app colors (when integrated)
- [ ] Requirements Hub opens and shows requirement list
- [ ] Clicking a requirement shows details + tests inline
- [ ] LLM Analyze button works and shows output in bottom panel
- [ ] Attachment Scraper modal opens and closes cleanly
- [ ] Delete feature works from Utilities

---

## Next Session TODO

**High Priority**:
1. [ ] Create `UnifiedRequirementsHubView.xaml` (unified layout)
2. [ ] Create `UnifiedRequirementsHubViewModel.cs` (orchestration)
3. [ ] Add Dashboard DataTemplate to `MainWindowResources.xaml`
4. [ ] Update app startup to show Dashboard first
5. [ ] Test Dashboard → Create Workshop flow
6. [ ] Test Dashboard → Open Workshop flow

**Medium Priority**:
7. [ ] Create Attachment Scraper modal integration
8. [ ] Apply ThemeService colors to all XAML views
9. [ ] Remove old workspace tabs (RequirementsTabSelectorView, etc.)

**Lower Priority**:
10. [ ] Optimize performance with lazy-loading
11. [ ] Add keyboard shortcuts
12. [ ] Add animation/transitions

---

## Build Command

```powershell
cd .
dotnet build TestCaseEditorApp.csproj
```

## Git Branch

```powershell
git checkout refactor/requirements-cleanup
git pull origin refactor/requirements-cleanup
```

---

**Last Updated**: 2026-07-09 | **Status**: Foundation ready for Requirements Hub consolidation phase
