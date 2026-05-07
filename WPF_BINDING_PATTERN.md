# WPF Binding Refresh Pattern - Critical Implementation Guide

## The Problem

When updating an `[ObservableProperty]` to a new value that is the **SAME object reference** (not a new instance), WPF's binding system doesn't detect the property change. All converters and dependent bindings continue using the stale cached value.

**Example:**
```csharp
// ❌ DOES NOT WORK - Same object reference, no UI refresh
CurrentRequirement = eventData.Requirement;  // Still stale in UI
```

## The Solution

Force a property change notification by setting to `null` first, then to the new value. This makes WPF re-evaluate all bindings, converters, and dependent properties.

**Pattern:**
```csharp
// ✅ CORRECT - Force binding refresh
CurrentRequirement = null;              // Triggers change notification
CurrentRequirement = eventData.Requirement;  // Triggers second change notification
UpdateRequirementState();               // Refresh dependent UI state
```

## When This Happens

- ✅ Data is updated in the model (e.g., `requirement.Description` changed)
- ✅ Event fires with the updated object
- ✅ ViewModel receives the event
- ❌ BUT: ViewModel already holds a reference to that same object
- ❌ Setting to the same reference = no change notification fired
- ❌ UI continues showing stale/old data
- ⚠️ Users see the commit/update didn't work

## Real Example: Requirement Update Commit

```csharp
private void OnRequirementUpdated(RequirementsEvents.RequirementUpdated eventData)
{
    if (eventData.Requirement?.GlobalId == CurrentRequirement?.GlobalId)
    {
        // ✅ CORRECT - Force WPF to refresh all bindings to this object
        CurrentRequirement = null;
        CurrentRequirement = eventData.Requirement;
        UpdateRequirementState();
        
        _logger.LogInformation("Requirement display refreshed for {GlobalId}", 
            eventData.Requirement.GlobalId);
    }
}
```

## Why This Works

1. `CurrentRequirement = null` → All bindings receive `null`, UI clears
2. `CurrentRequirement = eventData.Requirement` → All bindings receive the object reference again
3. WPF sees this as two separate property changes
4. All converters re-run with fresh data
5. All dependent bindings re-evaluate
6. UI displays updated values

## Common Binding Scenarios Affected

- Converters (e.g., `RequirementContentConverter`) operating on the property
- Nested property bindings (e.g., `{Binding CurrentRequirement.Description}`)
- ItemsControl `ItemsSource` bindings with converters
- Any `UpdateLayout()` or `InvalidateVisual()` dependency on the property

## Historical Case Study

**Requirement Update Commit Feature (Jan 2026)**

When the "Update Requirement with Re-write" button was implemented:
- ✅ Mediator published `RequirementUpdated` event correctly
- ✅ ViewModels received the event
- ❌ BUT: UI didn't refresh because `CurrentRequirement` was set to the same object reference

**Solution Applied:**
- Modified `JamaRequirementsMainViewModel.OnRequirementUpdated()` to use the null-then-set pattern
- Result: UI now immediately displays updated requirement text

**Lesson:** Always force refresh when updating `[ObservableProperty]` properties from event handlers, even when the data itself has changed.
