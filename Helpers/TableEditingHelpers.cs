using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EditableDataControl.ViewModels;

namespace TestCaseEditorApp.Helpers
{
    /// <summary>
    /// Lightweight helpers for cloning and normalizing editable table data
    /// (BindingPath = stable key, Header = display text).
    /// </summary>
    internal static class TableEditingHelpers
    {
        /// <summary>
        /// Deep-clone columns (Header + BindingPath).
        /// </summary>
        internal static ObservableCollection<ColumnDefinitionModel> CloneColumns(
            ObservableCollection<ColumnDefinitionModel> source)
        {
            var result = new ObservableCollection<ColumnDefinitionModel>();
            if (source == null) return result;

            foreach (var c in source)
            {
                if (c == null) continue;
                result.Add(new ColumnDefinitionModel
                {
                    Header = c.Header ?? string.Empty,
                    BindingPath = c.BindingPath ?? string.Empty
                });
            }

            return result;
        }

        /// <summary>
        /// Deep-clone rows (copy all cells; nulls become empty strings).
        /// </summary>
        internal static ObservableCollection<TableRowModel> CloneRows(
            ObservableCollection<TableRowModel> source)
        {
            var rows = new ObservableCollection<TableRowModel>();
            if (source == null) return rows;

            foreach (var r in source)
            {
                if (r == null) continue;

                var nr = new TableRowModel();

                // r.Cells is ObservableCollection<TableRowModel.Cell>, not a dictionary
                if (r.Cells != null)
                {
                    foreach (var cell in r.Cells)
                    {
                        if (cell == null) continue;
                        nr.Cells.Add(new TableRowModel.Cell
                        {
                            Key = cell.Key ?? string.Empty,
                            Value = cell.Value ?? string.Empty
                        });
                    }
                }

                rows.Add(nr);
            }

            return rows;
        }

        /// <summary>
        /// Ensure each row contains all current column keys (missing → ""),
        /// and optionally prunes keys not present in columns.
        /// </summary>
        internal static void NormalizeRowsTo(
            ObservableCollection<ColumnDefinitionModel> columns,
            ObservableCollection<TableRowModel> rows,
            bool pruneStaleKeys = true)
        {
            if (rows == null) return;

            // Build a set of valid keys from columns
            var columnList = columns ?? new ObservableCollection<ColumnDefinitionModel>();
            var keys = new HashSet<string>(
                columnList
                    .Select(c => c?.BindingPath ?? string.Empty)
                    .Where(k => !string.IsNullOrWhiteSpace(k)),
                StringComparer.Ordinal);

            foreach (var row in rows)
            {
                if (row == null) continue;

                // Add missing keys
                foreach (var k in keys)
                    row.EnsureKey(k);

                // Remove keys that no longer exist as columns
                if (pruneStaleKeys && row.Cells != null)
                {
                    var toRemove = row.Cells
                        .Where(c => c == null || !keys.Contains(c.Key))
                        .ToList();

                    foreach (var dead in toRemove)
                        row.Cells.Remove(dead);
                }
            }
        }
    }
}
