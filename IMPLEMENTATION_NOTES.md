# Implementation Notes & Lessons Learned

> **Purpose**: Capture both successes and failures during implementation to avoid the "figure out → revert → struggle to reimplement" cycle.  
> **Usage**: Document what works, what doesn't, and the exact steps that led to success.  
> **⚠️ For AI Agents**: See `ARCHITECTURAL_GUIDE_AI.md` for consolidated methodologies and systematic validation patterns.

---

## 🚨 JAMA CONNECT REGRESSION — ROOT CAUSE ANALYSIS (May 2026)

### **What Broke and Why**

Between the March 13, 2026 snapshot (`40c99d4`) and the April 6, 2026 merge (`dd34fef`), the working Jama "Import Requirements" project-list UI was silently destroyed.

**Symptoms:**
- "Jama connection unavailable" error when clicking Browse in New Project flow
- OAuth token exchange succeeded but projects never loaded
- Error persisted across multiple rollback attempts (April 11, January 14) because we were rolling back to the wrong commits

**Root Cause — Three separate issues stacked:**

1. **April 6 PR (`dd34fef`) rebuilt `NewProjectWorkflowViewModel` from scratch.**  
   The refactor's stated goal was "shrink MainWindow/MainViewModel — extract helpers and diagnostics." In doing so, it replaced the working Jama project-list UI (auto-loads projects on open, shows `AvailableProjects` listbox, displays "Found N projects") with a new file-dialog-based "Select Document" flow. The Jama UI moved to a dialog (`JamaProjectSelectionDialog.xaml`) that was never properly wired up.

2. **OAuth scope regression in commit `7e284c7` (January 4, 2026).**  
   The token request was changed from:
   ```csharp
   new FormUrlEncodedContent(new[] {
       new KeyValuePair<string, string>("grant_type", "client_credentials"),
       new KeyValuePair<string, string>("scope", "token_information")
   })
   ```
   to:
   ```csharp
   new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
   ```
   The Rockwell Collins Jama instance requires `scope=token_information` in the token request. Without it, the token is issued with insufficient permissions. **This regression recurs on every snapshot restore** because the fix is not in the historical commits — it must be re-applied manually after every rollback.

3. **`TestConnectionAsync()` gate blocked Browse flow** (added post-March 13).  
   `SelectDocumentAsync()` called `TestConnectionAsync()` first. Since `/rest/v1/projects` returns HTTP 500 (`ArrayIndexOutOfBoundsException` — a Jama server-side pagination bug), `TestConnection` always returned `false`, preventing `GetProjectsAsync()` from ever being called.

### **How We Fixed It**

- Restored to **March 13, 2026 snapshot (`40c99d4`)** — the exact code confirmed working in a photo taken March 16.
- Re-applied the OAuth scope fix (`scope=token_information` via `FormUrlEncodedContent`) to `Services/JamaConnectService.cs`.
- Committed as `a88519d` on branch `patch-preview`.

### **The False Trails**

- Rolling back to **January 14 (`e4444d7`)** didn't work — that code had a completely different Import Requirements flow (in `MVVM/Views/ImportRequirementsWorkflowView.xaml`), not the Jama project-list screen.
- Rolling back to **April 11 (`7b8e29c`)** didn't work — it was already post-refactor (the April 6 PR had already broken Jama by then).
- Removing the `TestConnectionAsync` gate alone didn't work because the OAuth scope regression was also present.

### **Standing Rules Going Forward**

1. **Never touch `NewProjectWorkflowViewModel.cs` or `NewProject_MainView.xaml` without verifying the Jama project list still appears.**
2. **After every snapshot restore, check `Services/JamaConnectService.cs` for the OAuth token request and ensure it uses `FormUrlEncodedContent` with `scope=token_information`.**
3. **`TestConnectionAsync()` must NOT gate the project-load flow.** The Jama server at Rockwell Collins returns HTTP 500 on `/rest/v1/projects` due to a server-side pagination bug. This makes `TestConnection` always fail even when OAuth is working. Use it only for the "Test Connection" button, not as a guard in Browse/load flows.
4. **The working Jama UI commit is `40c99d4` (March 13, 2026).** If Jama breaks again, compare against this commit first: `git diff 40c99d4 HEAD -- MVVM/Domains/NewProject/ViewModels/NewProjectWorkflowViewModel.cs`
5. **Do not hardcode machine-specific drive paths in scripts or source code.** Use environment-based and platform APIs (`Path.GetTempPath()`, `Environment.SpecialFolder`, `%TEMP%`, `%USERPROFILE%`) so the repo remains portable across drives (for example C: and D:).

---

## 📋 PLANNED IMPROVEMENTS (post-May 2026 stabilization)

These improvements exist in the April 6 refactor (`dd34fef`) and are worth selectively cherry-picking, **without touching the NewProject/Jama code**:

| Improvement | Source Commit | Risk |
|-------------|--------------|------|
| Lazy LLM loading — fixes startup blocking | `47505e9` (Mar 26) | Low |
| Workspace identity restoration fix | `7b8e29c` (Apr 11) | Low |
| OpenProject UI improvements (recent files, metadata) | `dd34fef` | Medium — review carefully |
| Jama project ID visibility/diagnostics | `40cae65` (Apr 23) | Low |
| Workspace open fix in New Project | `991a5c5` (Apr 23) | Medium |

**Do NOT cherry-pick the full `dd34fef` merge.** It deleted ~15,000 lines of services and rebuilt multiple ViewModels. Bring changes forward file-by-file with a build verification after each.

---
