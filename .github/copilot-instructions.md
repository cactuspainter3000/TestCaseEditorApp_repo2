# TestCaseEditorApp - Project Context for AI

> **🎯 For Complete Architectural Patterns**: See `ARCHITECTURAL_GUIDE_AI.md`  
> **Purpose**: Project-specific context, domains, and quick reference information

## 🚨 ARCHITECTURAL COMPLIANCE REMINDER

**BEFORE implementing any code changes involving:**
````instructions
# TestCaseEditorApp — Copilot / AI Agent Quick Guide

Purpose: Give an AI agent the minimal, high-value knowledge to be immediately productive in this repo.

Core commands
- Build: `dotnet build TestCaseEditorApp.csproj` (run from repo root)
- Quick build+verify XAML: open solution in IDE or run the same `dotnet build`; XAML compile errors surface there
- Tests: `.
	run-tests.ps1` (root) — `-StopOnFailure` supported

Big-picture architecture (short)
- WPF (.NET 8) MVVM app organized by domains under `MVVM/Domains/`.
- Key patterns: dependency injection, mediator pattern, 5-workspace UI composition (Main/Title/Header/Navigation/SideMenu).
- View wiring uses DataTemplates in `Resources/MainWindowResources.xaml` and `MVVM/Views/MainWindow.xaml` (ViewModel → DataTemplate → View).

Important coding conventions (repo-specific)
- Never `new` services inside ViewModels. Use the DI accessor: `App.ServiceProvider?.GetService<T>()`.
- Cross-domain communication goes through mediators (domain `Mediators` folders). No ViewModel→ViewModel direct calls.
- Title/header/navigation: many domains set `titleViewModel` or `navigationViewModel` to `null` and handle header/navigation internally — follow `Services/ViewConfigurationService.cs` for examples (Requirements domain pattern).

Files you will read first
- `ARCHITECTURAL_GUIDE_AI.md` — canonical design and anti-patterns (read before making architectural changes).
- `Services/ViewConfigurationService.cs` — central place that maps domain ViewModels → workspace configuration; changing it affects many UI flows.
- `App.xaml.cs` — DI registrations. When removing ViewModels, update registrations here.
- `Resources/MainWindowResources.xaml` and `MVVM/Views/MainWindow.xaml` — DataTemplates and namespace declarations. Remove unused DataTemplates and xmlns entries before deleting views.

Safe deletion checklist (views/viewmodels)
1. Grep for references: search XAML and C# for the type/namespace.
2. Remove DataTemplate / xmlns references from `Resources/MainWindowResources.xaml` and `MVVM/Views/MainWindow.xaml` first.
3. Remove DI registration in `App.xaml.cs` for the type.
4. Remove the file(s) and run `dotnet build` immediately. Fix any compile/XAML errors.
5. Update `Services/ViewConfigurationService.cs` to avoid calling deleted ViewModels (use `null` pattern where appropriate).

Common pitfalls to avoid
- Deleting a ViewModel without updating `ViewConfigurationService` or mediators causes compile/runtime failures.
- Removing a `clr-namespace` declaration while `views:` or `sharedviews:` is still referenced will cause XAML MC3000 errors — remove templates/usages first.
- Some domains (e.g., `TestCaseGenerator_Mode`) are distinct from legacy `TestCaseGeneration` — confirm domain separation before mass deletion.

Integration points & environment
- LLM integration controlled by `LLM_PROVIDER` and `OLLAMA_MODEL` environment variables.
- Services under `MVVM/Domains/*/Services` often provide shared logic (e.g., `SmartRequirementImporter`, `LlmServiceHealthMonitor`) — other domains may still depend on them.

Developer workflow recommendations for AI agents
- Always run `dotnet build` after code removal or DI changes; XAML compile errors appear during build.
- Make small, reversible commits and run `.
	run-tests.ps1` when changing logic.
- When changing UI wiring, update DataTemplates, namespace declarations, DI registrations, and `ViewConfigurationService` in that order.

Where to ask for deeper rules
- For any cross-domain or DI change, consult `ARCHITECTURAL_GUIDE_AI.md` and ask the maintainers if in doubt.

If this guide missed something important, tell me which file or pattern is unclear and I will iterate.
````