# CORRECT ARCHITECTURE STANDARD 
> **Definitive Pattern for All ViewConfiguration Implementations**
> **Based on**: ARCHITECTURAL_GUIDE_AI.md - Single Source of Truth

---

## 🎯 **THE DEFINITIVE PATTERN**

### **Core Principle**
**ViewModels + DataTemplates** - NEVER UserControls + DataContext

### **Standard Implementation Chain**

```csharp
private ViewConfiguration Create{Domain}Configuration(object? context)
{
    // 1. RESOLVE VIEWMODELS FROM DI (Never create UserControls manually)
    var titleVM = App.ServiceProvider?.GetService<{Domain}_TitleViewModel>();
    var headerVM = App.ServiceProvider?.GetService<{Domain}_HeaderViewModel>();
    var mainVM = App.ServiceProvider?.GetService<{Domain}_MainViewModel>();
    var navigationVM = App.ServiceProvider?.GetService<{Domain}_NavigationViewModel>();
    var notificationVM = App.ServiceProvider?.GetService<{Domain}_NotificationViewModel>();
    
    // 2. VALIDATE ALL VIEWMODELS RESOLVED (Fail-fast principle)
    if (titleVM == null) throw new InvalidOperationException("{Domain}_TitleViewModel not registered in DI container");
    if (headerVM == null) throw new InvalidOperationException("{Domain}_HeaderViewModel not registered in DI container");
    if (mainVM == null) throw new InvalidOperationException("{Domain}_MainViewModel not registered in DI container");
    if (navigationVM == null) throw new InvalidOperationException("{Domain}_NavigationViewModel not registered in DI container");
    if (notificationVM == null) throw new InvalidOperationException("{Domain}_NotificationViewModel not registered in DI container");
    
    // 3. RETURN VIEWMODELS DIRECTLY (WPF DataTemplates handle View creation)
    return new ViewConfiguration(
        sectionName: "{Domain}",
        titleViewModel: titleVM,         // ViewModel - DataTemplate renders View
        headerViewModel: headerVM,       // ViewModel - DataTemplate renders View
        contentViewModel: mainVM,        // ViewModel - DataTemplate renders View
        navigationViewModel: navigationVM, // ViewModel - DataTemplate renders View
        notificationViewModel: notificationVM, // ViewModel - DataTemplate renders View
        context: context
    );
}
```

---

## 🏗️ **COMPLETE IMPLEMENTATION REQUIREMENTS**

### **1. DI Registration** (App.xaml.cs)
```csharp
// REQUIRED: All ViewModels must be registered
services.AddTransient<{Domain}_TitleViewModel>();
services.AddTransient<{Domain}_HeaderViewModel>();
services.AddTransient<{Domain}_MainViewModel>();
services.AddTransient<{Domain}_NavigationViewModel>();
services.AddTransient<{Domain}_NotificationViewModel>();
```

### **2. DataTemplates** (MainWindow.xaml or ResourceDictionary)
```xml
<!-- REQUIRED: Each ViewModel needs corresponding DataTemplate -->
<DataTemplate DataType="{x:Type domain:{Domain}_TitleViewModel}">
    <domainviews:{Domain}_TitleView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_HeaderViewModel}">
    <domainviews:{Domain}_HeaderView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_MainViewModel}">
    <domainviews:{Domain}_MainView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_NavigationViewModel}">
    <domainviews:{Domain}_NavigationView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_NotificationViewModel}">
    <domainviews:{Domain}_NotificationView />
</DataTemplate>
```

### **3. Views Creation** (Domain/Views folder)
```
CRITICAL: Copy from TestCaseGeneration domain - NEVER create custom
- Copy TestCaseGenerator_TitleView.xaml → {Domain}_TitleView.xaml
- Copy TestCaseGenerator_HeaderView.xaml → {Domain}_HeaderView.xaml  
- Copy TestCaseGeneratorMainView.xaml → {Domain}_MainView.xaml
- Copy TestCaseGenerator_NavigationControl.xaml → {Domain}_NavigationView.xaml
- Copy TestCaseGeneratorNotificationView.xaml → {Domain}_NotificationView.xaml
```

### **4. ViewModels Creation** (Domain/ViewModels folder)
```
CRITICAL: Inherit from BaseDomainViewModel
- ALL ViewModels must have constructor: (I{Domain}Mediator mediator, ILogger<VM> logger)
- ALL properties required by copied Views must exist
- Follow event subscription patterns from TestCaseGeneration domain
```

### **5. Switch Pattern** (ViewConfigurationService.cs)
```csharp
return sectionName?.ToLowerInvariant() switch
{
    // Use EXACT lowercase of what SideMenuViewModel.NavigateToSection() calls
    "{lowercasesectionname}" => Create{Domain}Configuration(context),
    
    // Keep any legacy aliases for backward compatibility
    "{oldalias}" or "{another alias}" => Create{Domain}Configuration(context),
    
    // ...
};
```

---

## 🚨 **FORBIDDEN PATTERNS**

### ❌ **NEVER Do These**
1. **Manual UserControl Creation**:
   ```csharp
   // WRONG - Manual UserControl creation
   var control = new SomeView();
   control.DataContext = viewModel;
   return control;
   ```

2. **Cached ViewModel Approaches**:
   ```csharp
   // WRONG - Complex caching logic
   if (_cachedViewModel == null) {
       _cachedViewModel = mediator.SomeViewModel;
   }
   return _cachedViewModel;
   ```

3. **PlaceholderViewModels for Core Workspaces**:
   ```csharp
   // WRONG - Placeholders for real content
   titleViewModel: new PlaceholderViewModel("Some Title")
   ```

4. **Mixed Approaches in Same Configuration**:
   ```csharp
   // WRONG - Mixing ViewModels and UserControls
   contentViewModel: actualViewModel,        // ViewModel
   navigationViewModel: userControlInstance  // UserControl
   ```

5. **Switch Pattern Mismatches**:
   ```csharp
   // WRONG - Case sensitivity issues
   SideMenu calls: NavigateToSection("TestCaseGenerator")
   Switch expects: "testcase" or "test case generator"
   ```

---

## ✅ **VALIDATION CHECKLIST**

Before marking any configuration method as complete:

- [ ] **DI Registration**: All ViewModels registered in App.xaml.cs
- [ ] **DataTemplates**: All ViewModels have corresponding DataTemplates
- [ ] **Views Exist**: All Views copied from TestCaseGeneration reference implementation  
- [ ] **ViewModels Complete**: All properties required by Views implemented
- [ ] **Switch Pattern**: Exact match between SideMenu calls and switch cases
- [ ] **Build Success**: Zero compilation errors
- [ ] **Navigation Test**: Menu item shows correct domain views
- [ ] **No Placeholders**: No PlaceholderViewModels in production paths
- [ ] **No UserControls**: No manual UserControl creation in configuration
- [ ] **Consistent Pattern**: Same approach as other working domains (Startup, Dummy)

---

## 📊 **REFERENCE IMPLEMENTATIONS**

### **✅ Perfect Examples**
- **Startup Domain**: Complete ViewModels + DataTemplates pattern
- **Dummy Domain**: Blueprint implementation following all guidelines

### **❌ Anti-Pattern Examples** 
- **Current TestCaseGenerator**: Manual UserControl creation (needs migration)
- **Current Project**: PlaceholderViewModels for title/header (needs fixing)
- **Friday Working Version**: Complex cached ViewModels (outdated approach)

---

## 🎯 **SUCCESS METRICS**

A configuration method is **architecturally compliant** when:
1. **Zero manual UserControl creation**
2. **All ViewModels resolved from DI**
3. **DataTemplates handle View rendering**
4. **Switch pattern matches menu calls exactly**
5. **Builds and navigates without errors**
6. **Follows same pattern as other domains**

**Goal**: All domain configurations should be **indistinguishable in structure**, only differing in the specific ViewModel types resolved.