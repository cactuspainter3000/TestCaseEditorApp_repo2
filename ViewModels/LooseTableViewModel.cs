using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using EditableDataControl.ViewModels;     // ColumnDefinitionModel, TableRowModel, ProviderBackplane
using TestCaseEditorApp.Session;          // SessionTableStore, TableSnapshot

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


        // Optional custom editor opener
        [ObservableProperty] private Action<ITableViewProvider>? editTableCallback;

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
            System.Diagnostics.Debug.WriteLine($"[CTOR]  #{GetHashCode()} req={requirementId} key={tableKey}");

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
            SessionLooseSelectionStore.SetTableSelected(RequirementId, TableKey, value);
        }

        public override string ToString() => Title ?? base.ToString();

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

        /// <summary>Open the modal editor; on OK normalize, mark modified, and save to session.</summary>
        [RelayCommand]
        private void EditTable()
        {
            if (EditTableCallback is not null)
            {
                EditTableCallback(this);
                AfterEditCommit();
                return;
            }

            var dlg = new TestCaseEditorApp.MVVM.Views.EditableTableEditorWindow(this)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true)
                AfterEditCommit();
        }

        private void AfterEditCommit()
        {
            NormalizeRows();
            Title = (Title ?? string.Empty).Trim();
            IsModified = true;

            if (!string.IsNullOrWhiteSpace(RequirementId) &&
                !string.IsNullOrWhiteSpace(TableKey))
            {
                var snap = TableSnapshot.FromVM(Title, Columns, Rows);
                SessionTableStore.Save(RequirementId, TableKey, snap);
            }
            System.Diagnostics.Debug.WriteLine(
    $"[SAVE][VM] {RequirementId} | {TableKey} | Rows={Rows?.Count} Cols={Columns?.Count} hash={SnapshotHash(Columns, Rows)}");


        }

        private static string SnapshotHash(
    System.Collections.ObjectModel.ObservableCollection<EditableDataControl.ViewModels.ColumnDefinitionModel> cols,
    System.Collections.ObjectModel.ObservableCollection<EditableDataControl.ViewModels.TableRowModel> rows)
        {
            var sb = new System.Text.StringBuilder();
            // columns by BindingPath then Header (stable)
            foreach (var c in cols)
                sb.Append(c?.BindingPath).Append("=").Append(c?.Header).Append("|");
            sb.Append("||");
            // data row by row, cell by cell in column order
            foreach (var r in rows)
            {
                foreach (var c in cols)
                {
                    var key = c?.BindingPath ?? "";
                    var val = (key.Length == 0) ? "" : (r?[key] ?? "");
                    sb.Append(val).Append('\u241F'); // unit separator
                }
                sb.Append('\u241E'); // record separator
            }
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
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

            if (SessionTableStore.TryGet(RequirementId, TableKey, out var snap))
            {
                TableSnapshot.ApplyToVM(snap, Columns, Rows, out var newTitle);
                if (!string.IsNullOrEmpty(newTitle)) Title = newTitle;
                System.Diagnostics.Debug.WriteLine($"[LOAD] #{GetHashCode()} {RequirementId} | {TableKey} (found snapshot) hash={SnapshotHash(Columns, Rows)}");

            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LOAD] #{GetHashCode()} {RequirementId} | {TableKey} (no snapshot)");

            }

            // selection hydrate
            if (SessionLooseSelectionStore.TryGetTableSelected(RequirementId, TableKey, out var stored))
                IsSelected = stored;
            else
                IsSelected = true;

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

            // Title comes from the editor’s VM; the dialog will set our Title after ApplyTo(...)
            NormalizeRows();
            IsModified = true;

            // 3) Persist to session so navigation (that reads session) sees updated content
            if (!string.IsNullOrWhiteSpace(RequirementId) &&
                !string.IsNullOrWhiteSpace(TableKey))
            {
                var snap = TableSnapshot.FromVM(Title, Columns, Rows);
                SessionTableStore.Save(RequirementId, TableKey, snap);
            }
        }
    }
}
