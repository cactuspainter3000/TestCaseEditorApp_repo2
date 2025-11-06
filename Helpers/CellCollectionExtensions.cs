using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EditableDataControl.ViewModels;

namespace TestCaseEditorApp.Helpers
{
    internal static class CellCollectionExtensions
    {
        public static bool ContainsKey(this ObservableCollection<TableRowModel.Cell>? cells, string key)
            => cells?.Any(c => string.Equals(c.Key, key, StringComparison.Ordinal)) == true;

        public static bool TryGetValue(this ObservableCollection<TableRowModel.Cell>? cells, string key, out string? value)
        {
            value = cells?.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal))?.Value;
            return value is not null;
        }

        public static IEnumerable<string> Keys(this ObservableCollection<TableRowModel.Cell>? cells)
            => cells?.Select(c => c.Key).Where(k => !string.IsNullOrEmpty(k)) ?? Enumerable.Empty<string>();
    }
}

