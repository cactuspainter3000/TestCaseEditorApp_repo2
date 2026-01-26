using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EditableDataControl.ViewModels;     // ColumnDefinitionModel, TableRowModel, ProviderBackplane
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;  // TableDto
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using Microsoft.Extensions.DependencyInjection;
// using TestCaseEditorApp.Session;          // SessionTableStore, TableSnapshot - TODO: implement session persistence

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public interface ITableViewProvider
    {
        string Title { get; set; }
        ObservableCollection<ColumnDefinitionModel> Columns { get; }
        ObservableCollection<TableRowModel> Rows { get; }
    }

    /// <summary>
    /// Lightweight table VM that can optionally forward writes to an inner ProviderBackplane
    /// (e.g., RequirementTableViewModel) for true model write-through. If no inner is supplied,
    /// it updates its own state and persists to the session store.
    /// </summary>
    public partial class LooseTableViewModel : ObservableObject, ITableViewProvider, ProviderBackplane
    {
        // ---------- Optional inner backplane for write-through to the model ----------
        private readonly ProviderBackplane? _innerBackplane;

        // ---------- Identity for session persistence ----------
        [ObservableProperty] private string requirementId = string.Empty; // e.g., "C1XMA2405-REQ_RC-114"
        [ObservableProperty] private string tableKey = string.Empty;      // e.g., "table:2"

        // ---------- Data (editor contract) ----------
        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private ObservableCollection<ColumnDefinitionModel> columns = new();
        [ObservableProperty] private ObservableCollection<TableRowModel> rows = new();

        // ---------- UX helpers ----------
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private double confidenceScore;
        [ObservableProperty] private bool isModified;

        // New: whether to include this table in the LLM prompt
        [ObservableProperty] private bool includeInPrompt;
        
        // Dirty flag tracking
        [ObservableProperty] private bool isDirty;

        // Editor ViewModel for embedded editing
        [ObservableProperty] private EditableTableEditorViewModel? editorViewModel;

        // UI editing state
        [ObservableProperty] private bool isEditing;

        // Save and Cancel commands for embedded editing
        public IRelayCommand SaveTableCommand => new RelayCommand(SaveTable);
        public IRelayCommand CancelEditCommand => new RelayCommand(CancelEdit);
        public IRelayCommand ExitEditingModeCommand => new RelayCommand(ExitEditingMode);

        public LooseTableViewModel() { }

        private bool _hydratingFromSession;
        private bool _sessionHydratedOnce;

        /// <param name="innerBackplane">
        /// Optional. If provided, ReplaceWith will forward edits to this backplane (true write-through to model).
        /// </param>
        public LooseTableViewModel(
            string requirementId,
            string tableKey,
            string? title,
            ObservableCollection<ColumnDefinitionModel>? columns,
            ObservableCollection<TableRowModel>? rows,
            ProviderBackplane? innerBackplane = null)
        {
            TestCaseEditorApp.Services.Logging.Log.Debug($"[CTOR]  #{GetHashCode()} req={requirementId} key={tableKey}");

            RequirementId = requirementId ?? string.Empty;
            TableKey = tableKey ?? string.Empty;
            _innerBackplane = innerBackplane;

            Title = title ?? string.Empty;
            if (columns is not null) Columns = columns;
            if (rows is not null) Rows = rows;

            NormalizeRows();
            TryLoadFromSession();
        }

        // If IDs change later, try to load a session copy then.
        partial void OnRequirementIdChanged(string value) => TryLoadFromSession();
        partial void OnTableKeyChanged(string value) => TryLoadFromSession();

        // Persist selection whenever the checkbox changes.
        partial void OnIsSelectedChanged(bool value)
        {
            if (_hydratingFromSession) return;
            if (string.IsNullOrWhiteSpace(RequirementId) || string.IsNullOrWhiteSpace(TableKey)) return;
            // TODO: SessionLooseSelectionStore.SetTableSelected(RequirementId, TableKey, value);
        }

        public override string ToString() => Title ?? string.Empty;

        /// <summary>Ensure each row has all current column keys; optionally remove stale keys.</summary>
        public void NormalizeRows(bool pruneStaleKeys = true)
        {
            var keys = Columns
                .Select(c => c?.BindingPath ?? string.Empty)
                .Where(k => k.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var row in Rows)
            {
                // Add missing cells
                foreach (var k in keys)
                    row.EnsureKey(k);

                // Remove cells whose keys are no longer present
                if (pruneStaleKeys)
                {
                    for (int i = row.Cells.Count - 1; i >= 0; i--)
                    {
                        var cellKey = row.Cells[i].Key ?? string.Empty;
                        if (!keys.Contains(cellKey))
                            row.Cells.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>Switch to embedded editing mode instead of opening modal dialog.</summary>
        [RelayCommand]
        private void EditTable()
        {
            // Create editor ViewModel and enter embedded edit mode
            EditorViewModel = EditableTableEditorViewModel.From(Title, Columns, Rows);
            IsEditing = true;
        }

        private void SaveTable()
        {
            // Apply changes from editor ViewModel back to this ViewModel
            if (EditorViewModel != null)
            {
                // Forward to inner backplane first (true write-through to model/DTO if available)
                _innerBackplane?.ReplaceWith(EditorViewModel.Columns, EditorViewModel.Rows);

                // Update title
                Title = EditorViewModel.Title;
                
                // Clear and rebuild columns and rows from editor
                Columns.Clear();
                Rows.Clear();
                
                foreach (var col in EditorViewModel.Columns)
                {
                    Columns.Add(col);
                }
                
                foreach (var row in EditorViewModel.Rows)
                {
                    Rows.Add(row);
                }
                
                // Mark as dirty - save will happen during navigation
                IsDirty = true;
                TestCaseEditorApp.Services.Logging.Log.Debug($"[LooseTableViewModel] Table '{Title}' marked as dirty for requirement '{RequirementId}'");
                
                // Clear editor ViewModel
                EditorViewModel = null;
                
                // Perform post-edit processing
                AfterEditCommit();
            }
            
            // Exit edit mode
            IsEditing = false;
        }

        private void CancelEdit()
        {
            // Clear editor ViewModel without applying changes
            EditorViewModel = null;
            
            // Exit edit mode
            IsEditing = false;
        }

        private void ExitEditingMode()
        {
            // If not editing, nothing to do
            if (!IsEditing) return;

            // If there's no editor VM, just exit
            if (EditorViewModel is null)
            {
                IsEditing = false;
                return;
            }

            // Determine if there are unsaved changes compared to current VM state
            bool hasUnsaved = HasUnsavedChanges();

            if (!hasUnsaved)
            {
                // No changes — behave like cancel (discard editor VM and exit)
                CancelEdit();
                return;
            }

            // Prompt the user: Save and Exit / Discard / Stay
            var result = MessageBox.Show(
                "You have unsaved changes. Save before exiting?\n\nYes = Save and Exit\nNo = Discard Changes\nCancel = Keep Editing",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    // Save applies changes and exits editing mode
                    SaveTable();
                    break;
                case MessageBoxResult.No:
                    // Discard edits and exit
                    CancelEdit();
                    break;
                case MessageBoxResult.Cancel:
                default:
                    // Stay in editing mode
                    break;
            }
        }

        /// <summary>
        /// Save table changes to the source requirement data.
        /// Called from navigation logic when IsDirty is true.
        /// </summary>
        public void SaveToSourceRequirement()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveToSourceRequirement] Starting save for table '{Title}', RequirementId: '{RequirementId}', IsDirty: {IsDirty}");
                
                // Find the requirement in the TestCaseGeneration mediator
                var testCaseGenMediator = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators.ITestCaseGenerationMediator>();
                if (testCaseGenMediator == null) return;

                var requirement = testCaseGenMediator.Requirements.FirstOrDefault(r => r.Item == RequirementId || r.GlobalId == RequirementId);
                if (requirement == null) return;

                // Ensure LooseContent exists
                if (requirement.LooseContent == null)
                    requirement.LooseContent = new RequirementLooseContent();
                
                if (requirement.LooseContent.Tables == null)
                    requirement.LooseContent.Tables = new List<LooseTable>();

                // Convert current table data to LooseTable format
                var looseTable = ConvertToLooseTable();
                
                // Find and replace the matching table in the source data
                var tables = requirement.LooseContent.Tables;
                bool found = false;
                
                for (int i = 0; i < tables.Count; i++)
                {
                    if (tables[i] is LooseTable existingTable && 
                        existingTable.EditableTitle == Title)
                    {
                        tables[i] = looseTable;
                        found = true;
                        break;
                    }
                }
                
                // If not found, add as new table
                if (!found)
                {
                    tables.Add(looseTable);
                }
                
                // Reset dirty flag after successful save
                IsDirty = false;
                
                // Invalidate cache so future requests get updated data
                var testCaseGeneratorVM = App.ServiceProvider?.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels.TestCaseGenerator_VM>();
                if (testCaseGeneratorVM?.TestCaseGenerator != null && !string.IsNullOrEmpty(RequirementId))
                {
                    testCaseGeneratorVM.TestCaseGenerator.InvalidateTableCache(RequirementId);
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveToSourceRequirement] Invalidated cache for requirement: {RequirementId}");
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[SaveToSourceRequirement] Updated source data for table '{Title}' in requirement '{RequirementId}'");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[SaveToSourceRequirement] Failed to update source requirement data for table '{Title}'");
            }
        }

        private LooseTable ConvertToLooseTable()
        {
            var looseTable = new LooseTable
            {
                EditableTitle = Title
            };

            // Convert columns to headers
            var headers = Columns.Select(c => c.Header ?? string.Empty).ToList();
            looseTable.ColumnHeaders = headers;
            
            // Convert rows to raw string data
            var rows = new List<List<string>>();
            foreach (var row in Rows)
            {
                var stringRow = new List<string>();
                foreach (var col in Columns)
                {
                    var cellValue = row[col.BindingPath ?? ""]?.ToString() ?? string.Empty;
                    stringRow.Add(cellValue);
                }
                rows.Add(stringRow);
            }
            looseTable.Rows = rows;

            return looseTable;
        }

        private bool HasUnsavedChanges()
        {
            if (EditorViewModel is null) return false;

            // Compare title
            if (!string.Equals(Title ?? string.Empty, EditorViewModel.Title ?? string.Empty, StringComparison.Ordinal))
                return true;

            // Compare snapshot hashes of columns+rows
            var currentHash = SnapshotHash(Columns, Rows);
            var editorHash = SnapshotHash(EditorViewModel.Columns, EditorViewModel.Rows);
            return !string.Equals(currentHash, editorHash, StringComparison.Ordinal);
        }

        private void AfterEditCommit()
        {
            NormalizeRows();
            Title = (Title ?? string.Empty).Trim();
            IsModified = true;

            // TODO: Session persistence
            //if (!string.IsNullOrWhiteSpace(RequirementId) &&
            //    !string.IsNullOrWhiteSpace(TableKey))
            //{
            //    var snap = TableSnapshot.FromVM(Title, Columns, Rows);
            //    SessionTableStore.Save(RequirementId, TableKey, snap);
            //}
            TestCaseEditorApp.Services.Logging.Log.Debug(
                $"[SAVE][VM] {RequirementId} | {TableKey} | Rows={Rows?.Count} Cols={Columns?.Count} hash={SnapshotHash(Columns, Rows)}");
        }

        private static string SnapshotHash(
            ObservableCollection<ColumnDefinitionModel>? cols,
            ObservableCollection<TableRowModel>? rows)
        {
            var safeCols = cols ?? new ObservableCollection<ColumnDefinitionModel>();
            var safeRows = rows ?? new ObservableCollection<TableRowModel>();
            var sb = new StringBuilder();
            // columns by BindingPath then Header (stable)
            foreach (var c in safeCols)
                sb.Append(c?.BindingPath).Append("=").Append(c?.Header).Append("|");
            sb.Append("||");
            // data row by row, cell by cell in column order
            foreach (var r in safeRows)
            {
                foreach (var c in safeCols)
                {
                    var key = c?.BindingPath ?? "";
                    var val = (key.Length == 0) ? "" : (r?[key] ?? "");
                    sb.Append(val).Append('\u241F'); // unit separator
                }
                sb.Append('\u241E'); // record separator
            }
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 10);
        }

        private void TryLoadFromSession()
        {
            if (_sessionHydratedOnce) return;

            if (string.IsNullOrWhiteSpace(RequirementId) ||
                string.IsNullOrWhiteSpace(TableKey))
                return;

            _sessionHydratedOnce = true;
            _hydratingFromSession = true;

            // TODO: Session persistence
            //if (SessionTableStore.TryGet(RequirementId, TableKey, out var snap))
            //{
            //    TableSnapshot.ApplyToVM(snap, Columns, Rows, out var newTitle);
            //    if (!string.IsNullOrEmpty(newTitle)) Title = newTitle;
            //    TestCaseEditorApp.Services.Logging.Log.Debug($"[LOAD] #{GetHashCode()} {RequirementId} | {TableKey} (found snapshot) hash={SnapshotHash(Columns, Rows)}");
            //}
            //else
            //{
                TestCaseEditorApp.Services.Logging.Log.Debug($"[LOAD] #{GetHashCode()} {RequirementId} | {TableKey} (no snapshot)");
            //}

            // TODO: selection hydrate
            // if (SessionLooseSelectionStore.TryGetTableSelected(RequirementId, TableKey, out var stored))
            //     IsSelected = stored;
            // else
            IsSelected = false; // Start tables in read-only mode, user can click to select/edit
            IsEditing = false;  // Explicitly ensure tables start in read-only mode

            _hydratingFromSession = false;
        }

        // ---------------- ProviderBackplane ----------------
        // Called by the editor on OK via EditableTableEditorViewModel.ApplyTo(backplane)
        public void ReplaceWith(ObservableCollection<ColumnDefinitionModel> newCols,
                                ObservableCollection<TableRowModel> newRows)
        {
            // 1) If we have a real backplane, forward first (true write-through to model/DTO)
            _innerBackplane?.ReplaceWith(newCols, newRows);

            // 2) Update THIS VM’s state so current UI reflects the edit immediately
            Columns.Clear();
            foreach (var c in newCols)
                Columns.Add(new ColumnDefinitionModel { Header = c.Header, BindingPath = c.BindingPath });

            Rows.Clear();
            foreach (var r in newRows)
            {
                var nr = new TableRowModel();
                foreach (var cell in r.Cells)
                    nr[cell.Key] = cell.Value ?? string.Empty;
                Rows.Add(nr);
            }

            // Title comes from the editor's VM; the dialog will set our Title after ApplyTo(...)
            NormalizeRows();
            IsModified = true;

            // TODO: Session persistence
            // 3) Persist to session so navigation (that reads session) sees updated content
            //if (!string.IsNullOrWhiteSpace(RequirementId) &&
            //    !string.IsNullOrWhiteSpace(TableKey))
            //{
            //    var snap = TableSnapshot.FromVM(Title, Columns, Rows);
            //    SessionTableStore.Save(RequirementId, TableKey, snap);
            //}
        }
    }
}