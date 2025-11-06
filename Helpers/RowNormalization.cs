using System;
using System.Collections.Generic;
using System.Linq;
using EditableDataControl.ViewModels;

namespace TestCaseEditorApp.Helpers
{
    /// <summary>Ensures each row has a cell for every known key.</summary>
    internal static class RowNormalization
    {
        public static void NormalizeRowsTo(IEnumerable<TableRowModel> rows, IEnumerable<string> allKeys)
        {
            if (rows is null) return;
            var keySet = new HashSet<string>(allKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            foreach (var row in rows)
            {
                // Build a fast lookup of existing keys in this row
                var existing = row?.Cells?.Select(c => c.Key).ToHashSet(StringComparer.Ordinal)
                               ?? new HashSet<string>(StringComparer.Ordinal);

                // Add any missing cells with empty value
                foreach (var key in keySet)
                {
                    if (!existing.Contains(key))
                    {
                        row?.Cells?.Add(new TableRowModel.Cell { Key = key, Value = string.Empty });
                    }
                }
            }
        }

        /// <summary>Collects the union of all keys present across rows.</summary>
        public static IEnumerable<string> CollectAllKeys(IEnumerable<TableRowModel> rows)
        {
            return rows?
                .SelectMany(r => r?.Cells ?? Enumerable.Empty<TableRowModel.Cell>())
                .Select(c => c.Key)
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct(StringComparer.Ordinal)
                ?? Enumerable.Empty<string>();
        }

        // Overload kept for backward compatibility with older call sites
        public static void NormalizeRows(IEnumerable<TableRowModel> rows)
        {
            // Normalize each row to the union of all keys present
            var keys = CollectAllKeys(rows);
            NormalizeRowsTo(rows, keys);
        }

        // Overload kept for call sites that pass an explicit key sequence
        public static void NormalizeRows(IEnumerable<TableRowModel> rows, IEnumerable<string> keys)
        {
            NormalizeRowsTo(rows, keys);
        }
    }
}

//using System.Collections.ObjectModel;
//using System.Linq;
//using EditableDataControl.ViewModels;


//namespace TestCaseEditorApp.Helpers
//{
//    /// <summary>Row normalization helpers to keep rows aligned to current columns.</summary>
//    public static class RowNormalization
//    {
//        public static void NormalizeRows(ObservableCollection<ColumnDefinitionModel> columns,
//                                         ObservableCollection<TableRowModel> rows,
//                                         bool pruneStaleKeys = true)
//        {
//            var keys = columns.Select(c => c.BindingPath ?? string.Empty).ToArray();

//            foreach (var row in rows)
//            {
//                // add missing
//                foreach (var k in keys)
//                    if (!row.Cells.ContainsKey(k))
//                        row.Cells[k] = string.Empty;

//                if (pruneStaleKeys)
//                {
//                    var toRemove = row.Cells.Keys.Where(k => !keys.Contains(k)).ToList();
//                    foreach (var dead in toRemove)
//                        row.Cells.Remove(dead);
//                }
//            }
//        }
//    }
//}
