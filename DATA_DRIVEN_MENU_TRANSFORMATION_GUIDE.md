# Data-Driven Menu Transformation Guide

> **🚀 SUPERSEDED**: This document has been superseded by `ARCHITECTURAL_GUIDE_AI.md`  
> **UI modernization patterns** from this document have been consolidated into the comprehensive AI guide.  
> **Use this document for**: Project-specific implementation details and historical reference only.

## PROJECT OVERVIEW

**Objective**: Transform the hardcoded Test Case Generator dropdown into a clean, data-driven template system that recreates identical behavior through declarative data.

**Current Problem**: Test Case Generator uses complex hardcoded XAML with conditional StackPanels based on `{Binding Id}` values - messy, complicated, non-reusable.

**Target Solution**: Clean data models + reusable templates = identical behavior through declarative code.

## PROJECT RULES & GUARDRAILS

### ❌ WHAT WE ARE NOT DOING
- Fixing the existing Test Case Generator code
- Changing how the current menu looks or behaves  
- Creating new UI patterns or behaviors
- Optimizing the current implementation
- Debugging existing functionality

### ✅ WHAT WE ARE DOING
- **Extract** all behaviors/patterns from existing working implementation
- **Design** data models that can represent all content types
- **Create** templates that render data into identical UI
- **Preserve** exact animations, styling, and interactions
- **Enable** declarative menu definition through data/code

### 🎯 SUCCESS CRITERIA
1. **Same Visual Result**: New system produces identical appearance/behavior
2. **Data-Driven**: Menu structure defined in C# code, not hardcoded XAML
3. **Reusable**: Templates can be used for any dropdown menu in the app
4. **Clean Code**: No more conditional StackPanels with Id-based visibility
5. **Maintainable**: Adding new menu items requires only data changes

## IMPLEMENTATION PHASES

### **PHASE 1: DATA MODEL DESIGN**

**Objective**: Create data classes that can represent all content types found in Test Case Generator

**Required Content Types**:
- **MenuAction**: Simple buttons (🆕 New Project, 📁 Open Project, etc.)
- **SectionHeader**: Category dividers (📚 Learning Workflow, 🎓 Advanced Training)
- **ConditionalGroup**: Groups that show/hide based on step selection
- **ComplexAction**: Buttons with custom layouts (DockPanel with separated icon/text)
- **ToggleAction**: Buttons with state indicators (✔ Export for ChatGPT)

**Data Model Structure**:
```csharp
// Base class
public abstract class MenuContentItem : ObservableObject
{
    public string Id { get; set; }
    public bool IsVisible { get; set; } = true;
}

// Specific content types
public class MenuAction : MenuContentItem
public class SectionHeader : MenuContentItem  
public class ConditionalGroup : MenuContentItem
// etc.
```

**Files to Create**:
- `/MVVM/Models/DataDrivenMenu/MenuContentItem.cs` (base class)
- `/MVVM/Models/DataDrivenMenu/MenuAction.cs`
- `/MVVM/Models/DataDrivenMenu/SectionHeader.cs`
- `/MVVM/Models/DataDrivenMenu/ConditionalGroup.cs`
- `/MVVM/Models/DataDrivenMenu/MenuSection.cs` (top-level container)

**Phase 1 Success Criteria**:
- [ ] All content types from Test Case Generator can be represented in data
- [ ] Data models support Commands, Icons, Text, Visibility, Grouping
- [ ] Models include all properties needed for styling/behavior

### **PHASE 2: TEMPLATE DESIGN**

**Objective**: Create DataTemplates that render each content type with exact styling/behavior

**Required Templates**:
- **MenuActionTemplate**: Renders Button with SideMenuPopupButtonStyle
- **SectionHeaderTemplate**: Renders DropdownSectionHeader equivalent  
- **ConditionalGroupTemplate**: Renders group with visibility binding
- **ComplexActionTemplate**: Renders custom layouts (DockPanel, etc.)

**Template Features**:
- Exact styling match (SideMenuPopupButtonStyle, colors, margins)
- Proper emoji handling (patterns A, B, C from analysis)
- Command binding support
- Tooltip support
- Custom layout support

**Files to Create**:
- `/MVVM/Controls/DataDrivenMenu/MenuContentTemplates.xaml`
- `/MVVM/Controls/DataDrivenMenu/DataDrivenMenuControl.xaml`
- `/MVVM/Controls/DataDrivenMenu/DataDrivenMenuControl.xaml.cs`

**Phase 2 Success Criteria**:
- [ ] Templates produce identical visual appearance to original
- [ ] All styling matches exactly (colors, fonts, spacing, margins)
- [ ] Emoji patterns render correctly (simple, separated, section headers)
- [ ] Hover effects work identically
- [ ] Command binding works

### **PHASE 3: ANIMATION & BEHAVIOR IMPLEMENTATION**

**Objective**: Implement exact dropdown animation and interaction behavior

**Required Animations** (from analysis):
- **Main Section**: ScaleY (0.35s expand, 0.30s collapse) + Opacity (0.25s, 0.20s)
- **Easing**: AccelerationRatio="0.15" DecelerationRatio="0.85"
- **Sub-Items**: MaxHeight (0.4s, 0.35s) + ScaleY (0.4s, 0.35s) + Opacity (0.35s, 0.3s)
- **Setup**: ClipToBounds="True", initial ScaleY="0", Opacity="0"

**Interaction Patterns**:
- Invisible ToggleButton (Opacity="0") for click handling
- Visible AnimatedChevron overlay (IsHitTestVisible="False")
- Fixed text position during expand/collapse
- Selection highlighting with left accent stripe

**Files to Update**:
- `/MVVM/Controls/DataDrivenMenu/DataDrivenMenuControl.xaml` (add animations)
- Create animation resources/styles as needed

**Phase 3 Success Criteria**:
- [ ] Expansion animation matches exactly (timing, easing, properties)
- [ ] Text position remains fixed during animation
- [ ] Chevron rotation behavior identical
- [ ] Click areas work correctly (invisible button + visible chevron)
- [ ] Performance matches original (smooth, responsive)

### **PHASE 4: TEST CASE GENERATOR CONVERSION**

**Objective**: Convert existing Test Case Generator data into new format and validate

**Data Conversion**:
- Extract all hardcoded content from existing XAML
- Convert to data model instances
- Group by step categories (project, requirements, llm-learning, testcase-creation)
- Preserve all commands, tooltips, icons, conditional logic

**Test Implementation**:
- Create data structure in SideMenuViewModel
- Wire up new DataDrivenMenuControl alongside existing (for comparison)
- Validate identical appearance and behavior

**Files to Update**:
- `/MVVM/ViewModels/SideMenuViewModel.cs` (add data structure)
- `/MVVM/Views/SideMenuView.xaml` (add test instance)

**Phase 4 Success Criteria**:
- [ ] All Test Case Generator content represented in data
- [ ] New system renders identically to original
- [ ] All commands work correctly
- [ ] All conditional visibility logic works
- [ ] Performance is equivalent

### **PHASE 5: FINAL VALIDATION & CLEANUP**

**Objective**: Ensure complete parity and prepare for production use

**Validation Tasks**:
- Side-by-side comparison of old vs new
- Test all interactions, hover effects, animations
- Verify command execution
- Check edge cases and error states
- Performance testing

**Cleanup Tasks**:
- Remove test/experimental code
- Add comprehensive documentation
- Create usage examples
- Plan rollout to other menus

**Phase 5 Success Criteria**:
- [ ] 100% visual/behavioral parity achieved
- [ ] All functionality tested and working
- [ ] Code is clean, documented, maintainable
- [ ] Ready for production deployment

## CONTENT TYPE ANALYSIS REFERENCE

### **From Test Case Generator Analysis**:

**Simple Actions** (Pattern A):
```
🆕 New Project, ⚡ Quick Import, 📁 Open Project, 💾 Save Project, 📤 Unload Project
📥 Import Additional Requirements, ⚡ Analyze All Requirements
🔍 Analyze Unanalyzed, 🔄 Re-analyze Modified, etc.
```

**Section Headers**:
```
📚 Learning Workflow, 🎓 Advanced Training, ⚡ Quick Commands, 📤 Export Options
```

**Complex Actions** (Pattern B):
```
📝 + "Open Export" (DockPanel layout)
"Export for ChatGPT" + ✔ (conditional checkmark)
```

**Conditional Groups**:
```
project → New/Open/Save/Close buttons
requirements → Import/Analyze buttons  
llm-learning → Learning/Training/Commands/Export sections
testcase-creation → Export to Jama
```

## IMPLEMENTATION CHECKPOINTS

**Before Starting Each Phase**:
- [ ] Does this move us toward data-driven menu definition?
- [ ] Are we replicating existing behavior exactly?
- [ ] Will this make the code cleaner and more maintainable?

**Before Any Code Changes**:
- [ ] Is this task defined in this guide?
- [ ] Does it align with current phase objectives?
- [ ] Will it preserve existing functionality?

## REFERENCE MATERIALS

**Key Files for Analysis**:
- `/MVVM/Views/SideMenuView.xaml` (lines 35-420) - Test Case Generator implementation
- `/MVVM/ViewModels/SideMenuViewModel.cs` - Data structures and commands
- `/MVVM/Models/StepDescriptor.cs` - Current step model
- `/Resources/ButtonStyles.xaml` (SideMenuPopupButtonStyle)
- `/MVVM/Controls/DropdownSectionHeader.xaml` - Section header styling

**Animation Reference**:
- Main header: ScaleY (0.35s/0.30s) + Opacity (0.25s/0.20s) + AccelDecel easing
- Sub-items: MaxHeight + ScaleY + Opacity (0.4s/0.35s) + Linear easing

**Styling Reference**:
- Button: SideMenuPopupButtonStyle, transparent background, orange hover
- Text: Caption font size, left-aligned, text wrapping enabled
- Icons: Orange accent color, 4-6px spacing from text
- Layout: 6,5 button padding, 0,1 margins, stretch horizontal

## SUCCESS METRICS

1. **Code Quality**: Eliminate all conditional StackPanel visibility logic
2. **Maintainability**: Adding new menu item requires only data change
3. **Reusability**: Templates work for any dropdown menu in app
4. **Performance**: No regression in animation smoothness or responsiveness
5. **Functionality**: 100% feature parity with original implementation

---

**Last Updated**: December 24, 2025  
**Status**: Ready to begin Phase 1 - Data Model Design