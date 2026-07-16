# Extractor Handoff Prep

This document captures the extractor-only hardening sequence from the troubleshooting branch and the recommended integration sequence for the real branch.

## 1) Recommended Cherry-Pick Order

Use this exact order:

1. `67a3beb` - Add local document extraction path independent of Jama attachment scan
2. `8ac7c0b` - Tighten ATP fallback filters to reject test headings and setup boilerplate
3. `af78273` - Harden RAG extraction against procedural noise and add ATR guardrails
4. `8d949f7` - Refine context sanitizer to preserve technical should-always clauses
5. `70116e0` - Preserve normative clauses when recommendation wording appears
6. `b0755ac` - Add sanitizer edge-case regressions for mixed recommendation lines

## 2) Commit Scope Notes

### `67a3beb`
Touches:
- `Services/JamaDocumentParserService.cs`
- `Services/IJamaDocumentParserService.cs`
- `Services/DependencyInjection/ViewModelExtensions.cs`
- `MVVM/Domains/Workshop/ViewModels/WorkshopReproViewModel.cs`
- `MVVM/Domains/Workshop/Views/WorkshopDesignerReproView.xaml`

Note: this commit includes workshop/troubleshooter plumbing plus extractor API surface. If the real branch already has equivalent troubleshooting plumbing, port only the extractor/interface deltas needed.

### `8ac7c0b`
Touches only:
- `Services/JamaDocumentParserService.cs`

### `af78273`
Touches:
- `Services/JamaDocumentParserService.cs`
- `Tests/TestCaseEditorApp.Tests/Integration/ATPExtractionFoundationIntegrationTests.cs`
- `Tests/TestCaseEditorApp.Tests/Integration/JamaDocumentParserIntegrationTests.cs`
- `Tests/TestCaseEditorApp.Tests/JamaDocumentParserContextSanitizationTests.cs`

### `8d949f7`
Touches:
- `Services/JamaDocumentParserService.cs`
- `Tests/TestCaseEditorApp.Tests/JamaDocumentParserContextSanitizationTests.cs`

### `70116e0`
Touches:
- `Services/JamaDocumentParserService.cs`
- `Tests/TestCaseEditorApp.Tests/JamaDocumentParserContextSanitizationTests.cs`

### `b0755ac`
Touches only:
- `Tests/TestCaseEditorApp.Tests/JamaDocumentParserContextSanitizationTests.cs`

## 3) Suggested Integration Procedure (Real Branch)

1. Create integration branch from the real target branch.
2. Cherry-pick in order:

```powershell
git cherry-pick 67a3beb
git cherry-pick 8ac7c0b
git cherry-pick af78273
git cherry-pick 8d949f7
git cherry-pick 70116e0
git cherry-pick b0755ac
```

3. Resolve conflicts with extractor-first priority in:
- `Services/JamaDocumentParserService.cs`
- `Tests/TestCaseEditorApp.Tests/Integration/JamaDocumentParserIntegrationTests.cs`
- `Tests/TestCaseEditorApp.Tests/Integration/ATPExtractionFoundationIntegrationTests.cs`
- `Tests/TestCaseEditorApp.Tests/JamaDocumentParserContextSanitizationTests.cs`

4. Run extraction confidence suite:

```powershell
dotnet test Tests/TestCaseEditorApp.Tests/TestCaseEditorApp.Tests.csproj --filter "JamaDocumentParserContextSanitizationTests|JamaDocumentParserIntegrationTests|ATPExtractionFoundationIntegrationTests"
```

## 4) LLM Integration Gate Sequence

After extractor commits are integrated and green:

1. Enable LLM path behind a toggle only (no hard cutover).
2. Run the matrix with detailed logs:

```powershell
dotnet test Tests/TestCaseEditorApp.Tests/TestCaseEditorApp.Tests.csproj --filter "ParseAttachmentAsync_RealAtrFixture_LowContextCoverage_WithProcedurePoison_ExcludesPoisonFromPromptAndFinal|ParseAttachmentAsync_RealAtrFixture_RagContextStageMatrix_PreservesTechnicalCanary_WithoutProcedureReintroduction|ParseAttachmentAsync_RealAtpFixture_TemplateFormOutput_PreservesCanaries_EndToEnd|ParseLocalDocumentAsync_RealAtpFixture_MaintainsCurrentExtractionBaseline" -l "console;verbosity=detailed"
```

3. Confirm these acceptance conditions:
- ATP local baseline remains stable (`72` in current fixture guard).
- Procedural poison does not appear in prompt/final output under low-context ATR path.
- Technical canaries persist from input/context into final output.
- Sanitization telemetry reports sensible line-removal counts and recovery behavior.

4. Keep fallback enabled until multiple clean runs are observed.

## 5) Freeze Rule

After integration tests are green, avoid new extraction heuristics unless a fixture or production sample demonstrates a concrete regression. Prefer adding a failing test first, then a minimal fix.
