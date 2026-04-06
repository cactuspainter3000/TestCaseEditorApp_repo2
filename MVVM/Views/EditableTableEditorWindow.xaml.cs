using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AppColumn = EditableDataControl.ViewModels.ColumnDefinitionModel;
// app-side types:
using AppProvider = TestCaseEditorApp.MVVM.ViewModels.ITableViewProvider;
using AppRow = EditableDataControl.ViewModels.TableRowModel;
// Use the canonical viewmodel type via an alias so references are unambiguous
using EditorVm = EditableDataControl.ViewModels.EditableTableEditorViewModel;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class EditableTableEditorWindow : Window, EditableDataControl.ViewModels.ProviderBackplane
    {
        private readonly AppProvider _provider;
        private readonly ObservableCollection<EditableDataControl.ViewModels.ColumnDefinitionModel> _scratchCols = new();
        private readonly ObservableCollection<EditableDataControl.ViewModels.TableRowModel> _scratchRows = new();
        private string _scratchTitle = "";

        public EditableTableEditorWindow(AppProvider provider, Window? owner = null)
        {
            InitializeComponent();
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

            // 1) Build editor columns from provider
            var providerCols = _provider.Columns ?? new System.Collections.ObjectModel.ObservableCollection<EditableDataControl.ViewModels.ColumnDefinitionModel>();
            var eCols = new ObservableCollection<EditableDataControl.ViewModels.ColumnDefinitionModel>(
                providerCols.Select(c => new EditableDataControl.ViewModels.ColumnDefinitionModel
                {
                    Header = c.Header,
                    BindingPath = c.BindingPath // take exactly what's there
                }));

            // 2) Normalize: ensure every column has a non-empty BindingPath
            int colIndex = 0;
            foreach (var c in eCols)
            {
                if (string.IsNullOrWhiteSpace(c.BindingPath))
                {
                    c.BindingPath = string.IsNullOrWhiteSpace(c.Header)
                        ? $"col_{colIndex}"
                        : c.Header;
                }
                colIndex++;
            }

            // 3) Build a map from provider columns to editor keys
            var keyMap = providerCols
                .Zip(eCols, (src, dst) => new
                {
                    SourceKey = string.IsNullOrWhiteSpace(src.BindingPath) ? src.Header : src.BindingPath,
                    EditorKey = dst.BindingPath
                })
                .ToList();

            // 4) Clone rows using the normalized editor keys
            var eRows = new ObservableCollection<EditableDataControl.ViewModels.TableRowModel>();
            var providerRows = _provider.Rows?.Cast<EditableDataControl.ViewModels.TableRowModel>().ToList() ?? new System.Collections.Generic.List<EditableDataControl.ViewModels.TableRowModel>();
            TestCaseEditorApp.Services.Logging.Log.Debug($"\n=== Cloning {providerRows.Count} rows ===");

            int rowIdx = 0;
            foreach (var r in providerRows)
            {
                var er = new EditableDataControl.ViewModels.TableRowModel();
                foreach (var km in keyMap)
                {
                    var val = TryGetCellValue(r, km.SourceKey) ?? "";
                    er[km.EditorKey] = val;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"  Row {rowIdx}, Key '{km.EditorKey}' = '{val}'");
                }
                eRows.Add(er);
                rowIdx++;
            }

            // 5) Create VM and bind (use the disambiguated alias)
            TestCaseEditorApp.Services.Logging.Log.Debug($"=== Creating VM with {eCols.Count} columns and {eRows.Count} rows ===");
            var vm = EditorVm.From(_provider.Title, eCols, eRows);
            TestCaseEditorApp.Services.Logging.Log.Debug($"VM created: Columns={vm?.Columns.Count ?? 0}, Rows={vm?.Rows.Count ?? 0}");

            // Set the EditorViewModel property - binding will handle the rest
            if (EditorControl != null && vm != null)
            {
                EditorControl.EditorViewModel = vm;
                TestCaseEditorApp.Services.Logging.Log.Debug($"Set EditorControl.EditorViewModel to VM with {vm.Rows.Count} rows");
            }
            
            // Also set window DataContext for consistency
            DataContext = vm;

            if (owner != null) Owner = owner;
        }

        // VM pushes final values here
        public void ReplaceWith(ObservableCollection<EditableDataControl.ViewModels.ColumnDefinitionModel> newCols,
                                ObservableCollection<EditableDataControl.ViewModels.TableRowModel> newRows)
        {
            var vm = DataContext as EditorVm;
            _scratchTitle = vm?.Title ?? _scratchTitle;

            _scratchCols.Clear();
            foreach (var c in newCols)
                _scratchCols.Add(new EditableDataControl.ViewModels.ColumnDefinitionModel
                {
                    Header = c.Header,
                    BindingPath = c.BindingPath
                });

            _scratchRows.Clear();
            foreach (var r in newRows)
            {
                var nr = new EditableDataControl.ViewModels.TableRowModel();
                foreach (var c in _scratchCols)
                {
                    var key = c.BindingPath ?? string.Empty;
                    nr[key] = r[key];
                }
                _scratchRows.Add(nr);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var editorVm = DataContext as EditorVm;
            TestCaseEditorApp.Services.Logging.Log.Debug($"[Editor OK] DataContext type: {editorVm?.GetType().FullName ?? "<null>"}");

            if (editorVm == null)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (_provider is EditableDataControl.ViewModels.ProviderBackplane providerBackplane)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[Editor OK] Provider IS backplane: {_provider.GetType().FullName}");
                editorVm.ApplyTo(providerBackplane);
                TestCaseEditorApp.Services.Logging.Log.Debug("[Editor OK] Applied to provider backplane.");
                _provider.Title = editorVm.Title ?? string.Empty;
                DialogResult = true;
                Close();
                return;
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[Editor OK] Provider is NOT backplane: {_provider.GetType().FullName} — using scratch path.");
            editorVm.ApplyTo(this);

            // Write scratch back to provider (no model write-through here!)
            _provider.Title = _scratchTitle;

            _provider.Columns.Clear();
            foreach (var c in _scratchCols)
                _provider.Columns.Add(new AppColumn { Header = c.Header, BindingPath = c.BindingPath });

            _provider.Rows.Clear();
            foreach (var r in _scratchRows)
            {
                var ar = new AppRow();
                foreach (var c in _scratchCols)
                {
                    var key = c.BindingPath ?? string.Empty;
                    TrySetCellValue(ar, key, r[key]);
                }
                _provider.Rows.Add(ar);
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Helper: read/write your app row without assuming an indexer
        private static string TryGetCellValue(AppRow appRow, string key)
        {
            var idx = appRow.GetType().GetProperty("Item", new[] { typeof(string) });
            if (idx != null) return idx.GetValue(appRow, new object[] { key })?.ToString() ?? "";

            var cellsProp = appRow.GetType().GetProperty("Cells");
            var cells = cellsProp?.GetValue(appRow) as System.Collections.IEnumerable;
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    var k = cell.GetType().GetProperty("Key")?.GetValue(cell)?.ToString();
                    if (k == key) return cell.GetType().GetProperty("Value")?.GetValue(cell)?.ToString() ?? "";
                }
            }

            var p = appRow.GetType().GetProperty(key);
            if (p != null) return p.GetValue(appRow)?.ToString() ?? "";

            return "";
        }

        private static void TrySetCellValue(AppRow appRow, string key, string value)
        {
            var idx = appRow.GetType().GetProperty("Item", new[] { typeof(string) });
            if (idx != null) { idx.SetValue(appRow, value, new object[] { key }); return; }

            var cellsProp = appRow.GetType().GetProperty("Cells");
            var cells = cellsProp?.GetValue(appRow) as System.Collections.IList;
            if (cells != null)
            {
                object? found = null;
                foreach (var cell in cells)
                {
                    var k = cell.GetType().GetProperty("Key")?.GetValue(cell)?.ToString();
                    if (k == key) { found = cell; break; }
                }
                if (found == null)
                {
                    var cellType = cells.GetType().GetGenericArguments().FirstOrDefault();
                    if (cellType != null)
                    {
                            var newCell = System.Activator.CreateInstance(cellType);
                            if (newCell != null)
                            {
                                cellType.GetProperty("Key")?.SetValue(newCell, key);
                                cellType.GetProperty("Value")?.SetValue(newCell, value ?? "");
                                cells.Add(newCell);
                            }
                        return;
                    }
                }
                else
                {
                    found.GetType().GetProperty("Value")?.SetValue(found, value ?? "");
                    return;
                }
            }

            var p = appRow.GetType().GetProperty(key);
            if (p != null) { p.SetValue(appRow, value ?? ""); return; }
        }

        private static string GetValueByKey(AppRow row, string key)
        {
            var cell = row.Cells?.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
            return cell?.Value ?? string.Empty;
        }
    }
}