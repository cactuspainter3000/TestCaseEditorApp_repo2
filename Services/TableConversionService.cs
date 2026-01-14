using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EditableDataControl.ViewModels;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    public static class TableConversionService
    {
        public static TableItemViewModel ConvertDtoToTableItemViewModel(TableDto dto)
        {
            try
            {
                return new TableItemViewModel(dto, editCallback: null);
            }
            catch
            {
                var safeDto = dto ?? new TableDto { Title = string.Empty, Columns = new List<string>(), Rows = new List<List<string>>() };
                return new TableItemViewModel(safeDto, editCallback: null);
            }
        }

        public static TableDto ConvertLooseTableToDto(LooseTable lt)
        {
            var headers = new List<string>();
            if (lt.ColumnHeaders != null && lt.ColumnHeaders.Count > 0)
                headers = lt.ColumnHeaders.ToList();
            else if (lt.ColumnKeys != null && lt.ColumnKeys.Count > 0)
                headers = lt.ColumnKeys.ToList();

            var rows = new List<List<string>>();
            var srcRows = lt.Rows ?? new List<List<string>>();
            foreach (var r in srcRows)
            {
                var rowList = (r ?? new List<string>()).Select(cell => cell ?? string.Empty).ToList();
                rows.Add(rowList);
            }

            if (headers.Count == 0 && rows.Count > 0)
            {
                var maxCols = rows.Max(r => r?.Count ?? 0);
                for (int i = 0; i < maxCols; i++) headers.Add($"Column {i + 1}");
            }

            return new TableDto
            {
                Title = lt.EditableTitle ?? string.Empty,
                Columns = headers,
                Rows = rows
            };
        }

        public static TableDto? TryConvertToTableDto(object? obj)
        {
            if (obj == null) return null;

            // Already a TableDto
            if (obj is TableDto td) return td;

            // Already a TableItemViewModel
            if (obj is TableItemViewModel tivm)
            {
                if (tivm.SourceDto != null) return tivm.SourceDto;

                var headers = tivm.Columns?.Select(c => c.Header ?? string.Empty).ToList() ?? new List<string>();
                var rows = new List<List<string>>();
                foreach (var r in tivm.Rows ?? new ObservableCollection<TableRowModel>())
                {
                    var list = new List<string>();
                    foreach (var c in tivm.Columns ?? new System.Collections.ObjectModel.ObservableCollection<ColumnDefinitionModel>())
                    {
                        var key = c.BindingPath ?? string.Empty;
                        string val = string.Empty;
                        try { val = r?[key] ?? string.Empty; }
                        catch
                        {
                            var p = r?.GetType().GetProperty("Item", new[] { typeof(string) });
                            if (p != null) val = p.GetValue(r, new object[] { key })?.ToString() ?? string.Empty;
                        }
                        list.Add(val ?? string.Empty);
                    }
                    rows.Add(list);
                }

                return new TableDto { Title = tivm.Title ?? string.Empty, Columns = headers, Rows = rows };
            }

            // Explicit LooseTable handling (strongly-typed)
            if (obj is LooseTable lt)
            {
                return ConvertLooseTableToDto(lt);
            }

            // Reflection-based conversion for unknown shapes (attempt to extract Title / Columns / Rows)
            var type = obj.GetType();

            // Title candidates
            string title = type.GetProperty("EditableTitle")?.GetValue(obj)?.ToString()
                           ?? type.GetProperty("Title")?.GetValue(obj)?.ToString()
                           ?? type.GetProperty("Name")?.GetValue(obj)?.ToString()
                           ?? string.Empty;

            // Columns candidates - try Columns property
            var columns = new List<string>();
            var colsPropVal = type.GetProperty("Columns")?.GetValue(obj);
            if (colsPropVal is IEnumerable colsEnum && !(colsPropVal is string))
            {
                foreach (var c in colsEnum)
                {
                    if (c == null) { columns.Add(string.Empty); continue; }
                    if (c is string s) { columns.Add(s); continue; }
                    var header = c.GetType().GetProperty("Header")?.GetValue(c)?.ToString()
                                 ?? c.GetType().GetProperty("Text")?.GetValue(c)?.ToString()
                                 ?? c.ToString();
                    columns.Add(header ?? string.Empty);
                }
            }

            // Rows candidates - try Rows property
            var rowsList = new List<List<string>>();
            var rowsPropVal = type.GetProperty("Rows")?.GetValue(obj);
            if (rowsPropVal is IEnumerable rowsEnum && !(rowsPropVal is string))
            {
                foreach (var rowObj in rowsEnum)
                {
                    if (rowObj == null) { rowsList.Add(new List<string>()); continue; }

                    if (rowObj is IEnumerable rowCells && !(rowObj is string))
                    {
                        var rl = new List<string>();
                        foreach (var cell in rowCells) rl.Add(cell?.ToString() ?? string.Empty);
                        rowsList.Add(rl);
                        continue;
                    }

                    var cellsProp = rowObj.GetType().GetProperty("Cells")?.GetValue(rowObj);
                    if (cellsProp is IEnumerable cellsEnum && !(cellsProp is string))
                    {
                        var rl = new List<string>();
                        foreach (var cell in cellsEnum) rl.Add(cell?.ToString() ?? string.Empty);
                        rowsList.Add(rl);
                        continue;
                    }

                    // Fallback single cell row
                    rowsList.Add(new List<string> { rowObj.ToString() ?? string.Empty });
                }
            }

            // Nothing useful extracted?
            if (columns.Count == 0 && rowsList.Count == 0)
            {
                var maybeDto = type.GetProperty("Dto")?.GetValue(obj) as TableDto;
                if (maybeDto != null) return maybeDto;
                return null;
            }

            // Synthesize headers if necessary
            if (columns.Count == 0 && rowsList.Count > 0)
            {
                var maxCols = rowsList.Max(r => r?.Count ?? 0);
                for (int i = 0; i < maxCols; i++) columns.Add($"Column {i + 1}");
            }

            return new TableDto { Title = title ?? string.Empty, Columns = columns, Rows = rowsList };
        }
    }
}
