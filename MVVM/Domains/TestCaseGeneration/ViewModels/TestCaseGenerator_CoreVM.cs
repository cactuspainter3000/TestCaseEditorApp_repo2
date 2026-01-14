using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using EditableDataControl.ViewModels;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;
using VMVerMethod = TestCaseEditorApp.MVVM.Models.VerificationMethod;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// TestCaseGenerator_CoreVM
    /// Robust, complete implementation that exposes the public surface other viewmodels expect:
    /// - ResetForRequirement(Requirement?)
    /// - LooseParagraphs, LooseTableVMs, LooseTablesDtos
    /// - GetSelectedContext delegate (optional)
    /// - GetLooseTableVMsForRequirement / GetLooseParagraphsForRequirement helpers
    ///
    /// This version:
    /// - Handles LooseTable (project model) explicitly,
    /// - Uses object-first casts when iterating collections typed as LooseTable to avoid CS8121,
    /// - Provides TryConvertToTableDto reflection fallback for other table-like shapes.
    /// </summary>
    internal partial class TestCaseGenerator_CoreVM
    {
        // Collections other parts of the app expect
        public ObservableCollection<TableItemViewModel> LooseTableVMs { get; } = new ObservableCollection<TableItemViewModel>();
        public ObservableCollection<object> LooseTablesDtos { get; } = new ObservableCollection<object>();
        public ObservableCollection<string> LooseParagraphs { get; } = new ObservableCollection<string>();

        // Flexible delegate used by older code paths. Consumers may set this to a Func<(IEnumerable<string> paras, IEnumerable<TableDto> tables)>
        // or a similar tuple shape; we use DynamicInvoke to be forgiving.
        public Delegate? GetSelectedContext { get; set; }

        // Optional outputs used by the UI
        public VerificationCaseViewModel? VerificationCaseVM { get; set; }
        public string? LlmOutput { get; set; }

        // ----------------------------
        // Lifecycle / configuration
        // ----------------------------
        public void LoadDefaultsFromWorking(DefaultsBlock? defaults)
        {
            // No-op here; real implementation can seed generator defaults.
        }

        /// <summary>
        /// Reset/populate the generator for the provided requirement.
        /// Prefers Requirement.LooseContent, otherwise uses GetSelectedContext delegate if provided.
        /// Populates LooseParagraphs, LooseTableVMs and LooseTablesDtos.
        /// </summary>
        public void ResetForRequirement(Requirement? r)
        {
            LooseParagraphs.Clear();
            LooseTableVMs.Clear();
            LooseTablesDtos.Clear();

            if (r == null) return;

            // 1) Prefer data embedded in the requirement's LooseContent
            try
            {
                var lc = r.LooseContent;
                if (lc != null)
                {
                    if (lc.Paragraphs != null)
                    {
                        foreach (var p in lc.Paragraphs)
                            if (p != null) LooseParagraphs.Add(p);
                    }

                    if (lc.Tables != null)
                    {
                        foreach (var t in lc.Tables)
                        {
                            if (t == null) continue;

                            // Treat the loop element as object to avoid compile-time type restriction
                            object obj = (object)t;

                            // 1a) If it is the UI VM already, reuse it
                            var existingTivm = obj as TableItemViewModel;
                            if (existingTivm != null)
                            {
                                LooseTableVMs.Add(existingTivm);
                                LooseTablesDtos.Add(existingTivm.SourceDto ?? new TableDto { Title = existingTivm.Title });
                                continue;
                            }

                            // 1b) If it's a project LooseTable model, convert it strongly-typed
                            if (obj is LooseTable lt)
                            {
                                var dtoFromLoose = TableConversionService.ConvertLooseTableToDto(lt);
                                var tivm = TableConversionService.ConvertDtoToTableItemViewModel(dtoFromLoose);
                                LooseTableVMs.Add(tivm);
                                LooseTablesDtos.Add(dtoFromLoose);
                                continue;
                            }

                            // 1c) If it's already a TableDto (unlikely at compile-time for LooseTable collections),
                            // or can be converted via reflection, use that.
                            var dto = obj as TableDto ?? TableConversionService.TryConvertToTableDto(obj);
                            if (dto != null)
                            {
                                var tivm = TableConversionService.ConvertDtoToTableItemViewModel(dto);
                                LooseTableVMs.Add(tivm);
                                LooseTablesDtos.Add(dto);
                                continue;
                            }

                            // Fallback: store raw object for later inspection/conversion attempts
                            LooseTablesDtos.Add(obj);
                        }
                    }

                    // If LooseContent provided anything, accept it
                    if (LooseParagraphs.Count > 0 || LooseTableVMs.Count > 0 || LooseTablesDtos.Count > 0)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[ResetForRequirement] (LooseContent) req={r.Item} paras={LooseParagraphs.Count} tablesVM={LooseTableVMs.Count} tablesDto={LooseTablesDtos.Count}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[ResetForRequirement] LooseContent handling failed: " + ex);
                // ignore and try delegate fallback
            }

            // 2) Try invoking the GetSelectedContext delegate (older code paths)
            if (GetSelectedContext != null)
            {
                try
                {
                    var result = GetSelectedContext.DynamicInvoke();
                    if (result != null)
                    {
                        var type = result.GetType();

                        // paragraphs - try named property 'paras' then Item1
                        var parasObj = type.GetProperty("paras")?.GetValue(result) ?? type.GetProperty("Item1")?.GetValue(result);
                        if (parasObj is IEnumerable parasEnum && !(parasObj is string))
                        {
                            foreach (var p in parasEnum) if (p != null) LooseParagraphs.Add(p.ToString()!);
                        }

                        // tables - try named property 'tables' then Item2
                        var tablesObj = type.GetProperty("tables")?.GetValue(result) ?? type.GetProperty("Item2")?.GetValue(result);
                        if (tablesObj is IEnumerable tablesEnum && !(tablesObj is string))
                        {
                            foreach (var t in tablesEnum)
                            {
                                if (t == null) continue;

                                // Use object-first checks for safety across different runtime shapes
                                object obj = (object)t;

                                var td = obj as TableDto;
                                if (td != null)
                                {
                                    LooseTablesDtos.Add(td);
                                    LooseTableVMs.Add(TableConversionService.ConvertDtoToTableItemViewModel(td));
                                    continue;
                                }

                                var tivm = obj as TableItemViewModel;
                                if (tivm != null)
                                {
                                    LooseTableVMs.Add(tivm);
                                    LooseTablesDtos.Add(tivm.SourceDto ?? new TableDto { Title = tivm.Title });
                                    continue;
                                }

                                if (obj is LooseTable lt)
                                {
                                    var dtoFromLoose = TableConversionService.ConvertLooseTableToDto(lt);
                                    LooseTablesDtos.Add(dtoFromLoose);
                                    LooseTableVMs.Add(TableConversionService.ConvertDtoToTableItemViewModel(dtoFromLoose));
                                    continue;
                                }

                                var maybeDto = TableConversionService.TryConvertToTableDto(obj);
                                if (maybeDto != null)
                                {
                                    LooseTablesDtos.Add(maybeDto);
                                    LooseTableVMs.Add(TableConversionService.ConvertDtoToTableItemViewModel(maybeDto));
                                    continue;
                                }

                                // unknown shape
                                LooseTablesDtos.Add(obj);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[ResetForRequirement] GetSelectedContext invocation failed: " + ex);
                    // ignore delegate invocation errors
                }
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[ResetForRequirement] req={r.Item} paras={LooseParagraphs.Count} tablesVM={LooseTableVMs.Count} tablesDto={LooseTablesDtos.Count}");
            // end ResetForRequirement
        }

        // Conversion helpers were extracted into Services/TableConversionService.cs
        // to keep this core VM focused on orchestration and mapping. Use TableConversionService for conversions.

        // ----------------------------
        // Bridge methods used by TestCaseGenerator_VM
        // ----------------------------

        /// <summary>
        /// Return LooseTableViewModel instances for a requirement.
        /// Uses existing TableItemViewModel instances when present, otherwise converts DTOs or other shapes.
        /// </summary>
        public IEnumerable<LooseTableViewModel> GetLooseTableVMsForRequirement(Requirement? req)
        {
            if (req == null) return Enumerable.Empty<LooseTableViewModel>();

            try { ResetForRequirement(req); } catch { /* ignore */ }

            var outList = new List<LooseTableViewModel>();

            // 1) Use existing TableItemViewModel instances
            if (LooseTableVMs != null && LooseTableVMs.Count > 0)
            {
                int idx = 0;
                foreach (var tivm in LooseTableVMs)
                {
                    if (tivm == null) continue;
                    var tableKey = !string.IsNullOrWhiteSpace(tivm.SourceDto?.Title)
                        ? $"table:{idx}:{SanitizeKey(tivm.SourceDto.Title)}"
                        : $"table:{idx}";
                    var ltv = new LooseTableViewModel(req.Item ?? string.Empty, tableKey, tivm.Title, tivm.Columns, tivm.Rows, innerBackplane: tivm);
                    ltv.IsSelected = tivm.IsSelected;
                    outList.Add(ltv);
                    idx++;
                }
                if (outList.Count > 0) return outList;
            }

            // 2) Convert DTOs or other shapes
            if (LooseTablesDtos != null && LooseTablesDtos.Count > 0)
            {
                int idx = 0;
                foreach (var dtoObj in LooseTablesDtos)
                {
                    try
                    {
                        // Handle exact TableDto first
                        if (dtoObj is TableDto dto)
                        {
                            var cols = new ObservableCollection<ColumnDefinitionModel>();
                            var rows = new ObservableCollection<TableRowModel>();

                            var headers = dto.Columns ?? new List<string>();
                            if (headers.Count == 0)
                            {
                                var maxCols = dto.Rows?.Count > 0 ? dto.Rows.Max(r => r?.Count ?? 0) : 1;
                                headers = Enumerable.Range(0, maxCols).Select(i => $"Column {i + 1}").ToList();
                            }

                            for (int i = 0; i < headers.Count; i++)
                            {
                                var bp = string.IsNullOrWhiteSpace(headers[i]) ? $"c{i}" : headers[i];
                                cols.Add(new ColumnDefinitionModel { Header = headers[i] ?? $"Column {i + 1}", BindingPath = bp });
                            }

                            var dtoRows = dto.Rows ?? new List<List<string>>();
                            foreach (var r in dtoRows)
                            {
                                var tr = new TableRowModel();
                                for (int c = 0; c < cols.Count; c++)
                                {
                                    var colKey = cols[c].BindingPath ?? $"c{c}";
                                    var val = (r != null && c < r.Count) ? (r[c] ?? string.Empty) : string.Empty;
                                    tr[colKey] = val;
                                }
                                rows.Add(tr);
                            }

                            if (rows.Count == 0)
                            {
                                var tr = new TableRowModel();
                                foreach (var cdef in cols) tr[cdef.BindingPath ?? ""] = string.Empty;
                                rows.Add(tr);
                            }

                            var tableKey = !string.IsNullOrWhiteSpace(dto.Title) ? $"table:{idx}:{SanitizeKey(dto.Title)}" : $"table:{idx}";
                            outList.Add(new LooseTableViewModel(req.Item ?? string.Empty, tableKey, dto.Title, cols, rows, innerBackplane: null));
                            idx++;
                        }
                        else
                        {
                            // Try converting unknown shapes to TableDto then handle
                            var maybeDto = TableConversionService.TryConvertToTableDto(dtoObj);
                            if (maybeDto != null)
                            {
                                var cols = new ObservableCollection<ColumnDefinitionModel>();
                                var rows = new ObservableCollection<TableRowModel>();

                                var headers = maybeDto.Columns ?? new List<string>();
                                if (headers.Count == 0)
                                {
                                    var maxCols = maybeDto.Rows?.Count > 0 ? maybeDto.Rows.Max(r => r?.Count ?? 0) : 1;
                                    headers = Enumerable.Range(0, maxCols).Select(i => $"Column {i + 1}").ToList();
                                }

                                for (int i = 0; i < headers.Count; i++)
                                {
                                    var bp = string.IsNullOrWhiteSpace(headers[i]) ? $"c{i}" : headers[i];
                                    cols.Add(new ColumnDefinitionModel { Header = headers[i] ?? $"Column {i + 1}", BindingPath = bp });
                                }

                                var dtoRows = maybeDto.Rows ?? new List<List<string>>();
                                foreach (var r in dtoRows)
                                {
                                    var tr = new TableRowModel();
                                    for (int c = 0; c < cols.Count; c++)
                                    {
                                        var colKey = cols[c].BindingPath ?? $"c{c}";
                                        var val = (r != null && c < r.Count) ? (r[c] ?? string.Empty) : string.Empty;
                                        tr[colKey] = val;
                                    }
                                    rows.Add(tr);
                                }

                                if (rows.Count == 0)
                                {
                                    var tr = new TableRowModel();
                                    foreach (var cdef in cols) tr[cdef.BindingPath ?? ""] = string.Empty;
                                    rows.Add(tr);
                                }

                                var tableKey = !string.IsNullOrWhiteSpace(maybeDto.Title) ? $"table:{idx}:{SanitizeKey(maybeDto.Title)}" : $"table:{idx}";
                                outList.Add(new LooseTableViewModel(req.Item ?? string.Empty, tableKey, maybeDto.Title, cols, rows, innerBackplane: null));
                                idx++;
                            }
                            else if (dtoObj is LooseTableViewModel ltvExisting)
                            {
                                outList.Add(ltvExisting);
                            }
                        }
                    }
                    catch
                    {
                        // ignore conversion errors and continue
                    }
                }

                if (outList.Count > 0) return outList;
            }

            // 3) As last resort, try the GetSelectedContext delegate
            if (GetSelectedContext != null)
            {
                try
                {
                    var ctxResult = GetSelectedContext.DynamicInvoke();
                    if (ctxResult != null)
                    {
                        var rt = ctxResult.GetType();
                        var tablesObj = rt.GetProperty("tables")?.GetValue(ctxResult) ?? rt.GetProperty("Item2")?.GetValue(ctxResult);
                        if (tablesObj is IEnumerable tbls && !(tablesObj is string))
                        {
                            int idx = 0;
                            foreach (var t in tbls)
                            {
                                if (t == null) continue;

                                var dto = t as TableDto;
                                if (dto != null)
                                {
                                    // convert as above
                                    var cols = new ObservableCollection<ColumnDefinitionModel>();
                                    var rows = new ObservableCollection<TableRowModel>();

                                    var headers = dto.Columns ?? new List<string>();
                                    if (headers.Count == 0)
                                    {
                                        var maxCols = dto.Rows?.Count > 0 ? dto.Rows.Max(r => r?.Count ?? 0) : 1;
                                        headers = Enumerable.Range(0, maxCols).Select(i => $"Column {i + 1}").ToList();
                                    }

                                    for (int i = 0; i < headers.Count; i++)
                                        cols.Add(new ColumnDefinitionModel { Header = headers[i] ?? $"Column {i + 1}", BindingPath = string.IsNullOrWhiteSpace(headers[i]) ? $"c{i}" : headers[i] });

                                    var dtoRows = dto.Rows ?? new List<List<string>>();
                                    foreach (var r in dtoRows)
                                    {
                                        var tr = new TableRowModel();
                                        for (int c = 0; c < cols.Count; c++)
                                        {
                                            var colKey = cols[c].BindingPath ?? $"c{c}";
                                            var val = (r != null && c < r.Count) ? (r[c] ?? string.Empty) : string.Empty;
                                            tr[colKey] = val;
                                        }
                                        rows.Add(tr);
                                    }

                                    if (rows.Count == 0) { var tr = new TableRowModel(); foreach (var cdef in cols) tr[cdef.BindingPath ?? ""] = string.Empty; rows.Add(tr); }
                                    var tableKey = !string.IsNullOrWhiteSpace(dto.Title) ? $"table:{idx}:{SanitizeKey(dto.Title)}" : $"table:{idx}";
                                    outList.Add(new LooseTableViewModel(req.Item ?? string.Empty, tableKey, dto.Title, cols, rows, innerBackplane: null));
                                    idx++;
                                }
                                else
                                {
                                    var tivm = t as TableItemViewModel;
                                    if (tivm != null)
                                    {
                                        var tableKey = !string.IsNullOrWhiteSpace(tivm.SourceDto?.Title) ? $"table:{idx}:{SanitizeKey(tivm.SourceDto.Title)}" : $"table:{idx}";
                                        outList.Add(new LooseTableViewModel(req.Item ?? string.Empty, tableKey, tivm.Title, tivm.Columns, tivm.Rows, innerBackplane: tivm));
                                        idx++;
                                        continue;
                                    }

                                    if (t is LooseTable lt)
                                    {
                                        var maybeDtoFromLoose = TableConversionService.ConvertLooseTableToDto(lt);
                                        // convert maybeDto (same pattern)
                                        var cols = new ObservableCollection<ColumnDefinitionModel>();
                                        var rows = new ObservableCollection<TableRowModel>();

                                        var headers = maybeDtoFromLoose.Columns ?? new List<string>();
                                        if (headers.Count == 0)
                                        {
                                            var maxCols = maybeDtoFromLoose.Rows?.Count > 0 ? maybeDtoFromLoose.Rows.Max(r => r?.Count ?? 0) : 1;
                                            headers = Enumerable.Range(0, maxCols).Select(i => $"Column {i + 1}").ToList();
                                        }

                                        for (int i = 0; i < headers.Count; i++)
                                            cols.Add(new ColumnDefinitionModel { Header = headers[i] ?? $"Column {i + 1}", BindingPath = string.IsNullOrWhiteSpace(headers[i]) ? $"c{i}" : headers[i] });

                                        var dtoRows = maybeDtoFromLoose.Rows ?? new List<List<string>>();
                                        foreach (var r in dtoRows)
                                        {
                                            var tr = new TableRowModel();
                                            for (int c = 0; c < cols.Count; c++)
                                            {
                                                var colKey = cols[c].BindingPath ?? $"c{c}";
                                                var val = (r != null && c < r.Count) ? (r[c] ?? string.Empty) : string.Empty;
                                                tr[colKey] = val;
                                            }
                                            rows.Add(tr);
                                        }

                                        if (rows.Count == 0) { var tr = new TableRowModel(); foreach (var cdef in cols) tr[cdef.BindingPath ?? ""] = string.Empty; rows.Add(tr); }
                                        var tableKey = !string.IsNullOrWhiteSpace(maybeDtoFromLoose.Title) ? $"table:{idx}:{SanitizeKey(maybeDtoFromLoose.Title)}" : $"table:{idx}";
                                        outList.Add(new LooseTableViewModel(req.Item ?? string.Empty, tableKey, maybeDtoFromLoose.Title, cols, rows, innerBackplane: null));
                                        idx++;
                                        continue;
                                    }

                                    var maybeDto = TableConversionService.TryConvertToTableDto(t);
                                    if (maybeDto != null)
                                    {
                                        // convert maybeDto (same pattern)
                                        var cols = new ObservableCollection<ColumnDefinitionModel>();
                                        var rows = new ObservableCollection<TableRowModel>();

                                        var headers = maybeDto.Columns ?? new List<string>();
                                        if (headers.Count == 0)
                                        {
                                            var maxCols = maybeDto.Rows?.Count > 0 ? maybeDto.Rows.Max(r => r?.Count ?? 0) : 1;
                                            headers = Enumerable.Range(0, maxCols).Select(i => $"Column {i + 1}").ToList();
                                        }

                                        for (int i = 0; i < headers.Count; i++)
                                            cols.Add(new ColumnDefinitionModel { Header = headers[i] ?? $"Column {i + 1}", BindingPath = string.IsNullOrWhiteSpace(headers[i]) ? $"c{i}" : headers[i] });

                                        var dtoRows = maybeDto.Rows ?? new List<List<string>>();
                                        foreach (var r in dtoRows)
                                        {
                                            var tr = new TableRowModel();
                                            for (int c = 0; c < cols.Count; c++)
                                            {
                                                var colKey = cols[c].BindingPath ?? $"c{c}";
                                                var val = (r != null && c < r.Count) ? (r[c] ?? string.Empty) : string.Empty;
                                                tr[colKey] = val;
                                            }
                                            rows.Add(tr);
                                        }

                                        if (rows.Count == 0) { var tr = new TableRowModel(); foreach (var cdef in cols) tr[cdef.BindingPath ?? ""] = string.Empty; rows.Add(tr); }
                                        var tableKey = !string.IsNullOrWhiteSpace(maybeDto.Title) ? $"table:{idx}:{SanitizeKey(maybeDto.Title)}" : $"table:{idx}";
                                        outList.Add(new LooseTableViewModel(req.Item ?? string.Empty, tableKey, maybeDto.Title, cols, rows, innerBackplane: null));
                                        idx++;
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return Enumerable.Empty<LooseTableViewModel>();
        }

        /// <summary>
        /// Return the loose paragraphs for the requirement (prefers LooseParagraphs then GetSelectedContext).
        /// </summary>
        public IEnumerable<string> GetLooseParagraphsForRequirement(Requirement? req)
        {
            if (req == null) return Enumerable.Empty<string>();

            try { ResetForRequirement(req); } catch { /* ignore */ }

            if (LooseParagraphs != null && LooseParagraphs.Count > 0) return LooseParagraphs.ToList();

            if (GetSelectedContext != null)
            {
                try
                {
                    var ctxResult = GetSelectedContext.DynamicInvoke();
                    if (ctxResult != null)
                    {
                        var rt = ctxResult.GetType();
                        var parasObj = rt.GetProperty("paras")?.GetValue(ctxResult) ?? rt.GetProperty("Item1")?.GetValue(ctxResult);
                        if (parasObj is IEnumerable parasEnum && !(parasObj is string))
                        {
                            var outList = new List<string>();
                            foreach (var p in parasEnum) if (p != null) outList.Add(p.ToString()!);
                            if (outList.Count > 0) return outList;
                        }
                    }
                }
                catch
                {
                    // ignore invocation errors
                }
            }

            return Enumerable.Empty<string>();
        }

        // ----------------------------
        // Helpers
        // ----------------------------
        private static string SanitizeKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (var c in invalid) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // Lightweight VerificationCaseViewModel placeholder (if real type exists elsewhere, remove/replace)
        public class VerificationCaseViewModel
        {
            public string ReqId { get; set; } = string.Empty;
            public string ReqName { get; set; } = string.Empty;
            public string ReqDescription { get; set; } = string.Empty;

            public IReadOnlyList<VMVerMethod> Methods { get; set; } = Array.Empty<VMVerMethod>();
            public VMVerMethod SelectedMethod { get; set; } = VMVerMethod.Inspection;

            public string? ImportedRationale { get; set; }
            public string? ImportedValidationEvidence { get; set; }
            public string? ImportedSupportingNotes { get; set; }
            public IEnumerable<TableDto>? ImportedSupportingTables { get; set; }

            public string GenerationResult { get; set; } = string.Empty;
        }
    }
}