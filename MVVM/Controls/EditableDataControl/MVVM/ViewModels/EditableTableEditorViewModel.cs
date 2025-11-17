// File: EditableDataControl/ViewModels/EditableTableEditorViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace EditableDataControl.ViewModels
{
    public class ColumnDefinitionModel : ObservableObject
    {
        private string _header = "";
        private string _bindingPath = "";
        public string Header { get => _header; set => SetProperty(ref _header, value); }
        public string BindingPath { get => _bindingPath; set => SetProperty(ref _bindingPath, value); }
    }

    public class TableRowModel : ObservableObject, ICustomTypeDescriptor
    {
        public ObservableCollection<Cell> Cells { get; } = new();

        public string this[string key]
        {
            get => Cells.FirstOrDefault(c => c.Key == key)?.Value ?? "";
            set
            {
                var c = Cells.FirstOrDefault(x => x.Key == key);
                if (c is null) Cells.Add(new Cell { Key = key, Value = value });
                else c.Value = value;
                // Notify WPF that the indexer value changed
                OnPropertyChanged("Item[]");
                OnPropertyChanged($"Item[{key}]");
            }
        }

        public void EnsureKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!Cells.Any(c => c.Key == key)) Cells.Add(new Cell { Key = key, Value = "" });
        }

        // ICustomTypeDescriptor implementation to allow WPF to bind to string indexer
        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties()
        {
            var props = new List<PropertyDescriptor>();
            foreach (var cell in Cells)
            {
                props.Add(new CellPropertyDescriptor(cell.Key));
            }
            return new PropertyDescriptorCollection(props.ToArray());
        }

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes) 
            => ((ICustomTypeDescriptor)this).GetProperties();

        private class CellPropertyDescriptor : PropertyDescriptor
        {
            private readonly string _key;
            public CellPropertyDescriptor(string key) : base(key, null) => _key = key;
            public override Type ComponentType => typeof(TableRowModel);
            public override bool IsReadOnly => false;
            public override Type PropertyType => typeof(string);
            public override bool CanResetValue(object component) => false;
            public override object GetValue(object component) => ((TableRowModel)component)[_key];
            public override void ResetValue(object component) { }
            public override void SetValue(object component, object value) 
                => ((TableRowModel)component)[_key] = value?.ToString() ?? "";
            public override bool ShouldSerializeValue(object component) => false;
        }

        // Other ICustomTypeDescriptor members - delegate to default behavior
        AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);
        string ICustomTypeDescriptor.GetClassName() => TypeDescriptor.GetClassName(this, true);
        string ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);
        TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);
        EventDescriptor ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);
        PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);
        object ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);
        EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);
        EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);
        object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd) => this;

        public class Cell : ObservableObject
        {
            private string _key = "";
            private string _value = "";
            public string Key { get => _key; set => SetProperty(ref _key, value); }
            public string Value { get => _value; set => SetProperty(ref _value, value); }
        }
    }

    public interface ProviderBackplane
    {
        void ReplaceWith(ObservableCollection<ColumnDefinitionModel> newCols,
                         ObservableCollection<TableRowModel> newRows);
    }

    public partial class EditableTableEditorViewModel : ObservableObject
    {
        [ObservableProperty] private string _title = "";
        public ObservableCollection<ColumnDefinitionModel> Columns { get; } = new();
        public ObservableCollection<TableRowModel> Rows { get; } = new();

        // ---- Explicit command properties (used by XAML) ----
        public IRelayCommand AddRowCommand { get; }
        public IRelayCommand AddColumnCommand { get; }
        public IRelayCommand<TableRowModel> InsertRowAboveCommand { get; }
        public IRelayCommand<TableRowModel> InsertRowBelowCommand { get; }
        public IRelayCommand<TableRowModel> MoveRowToFirstCommand { get; }
        public IRelayCommand<TableRowModel> DeleteRowCommand { get; }
        public IRelayCommand<ColumnDefinitionModel> MoveColumnLeftCommand { get; }
        public IRelayCommand<ColumnDefinitionModel> MoveColumnRightCommand { get; }
        public IRelayCommand<ColumnDefinitionModel> DeleteColumnCommand { get; }
        public IRelayCommand<Controls.CellToHeaderArgs> MoveCellToColumnHeaderCommand { get; }
        public IRelayCommand<Controls.CellToHeaderArgs?> MoveCellToTitleCommand { get; }
        public IRelayCommand<object?> MoveCellToTitleFromCellCommand { get; }
        public IRelayCommand<TableRowModel?> MoveRowToColumnHeadersCommand { get; }


        public EditableTableEditorViewModel()
        {
            // cell commands
            MoveCellToTitleCommand = new RelayCommand<Controls.CellToHeaderArgs?>(MoveCellToTitle);
            MoveCellToTitleFromCellCommand = new RelayCommand<object?>(MoveCellToTitleFromCell);
            MoveCellToColumnHeaderCommand = new RelayCommand<Controls.CellToHeaderArgs>(MoveCellToColumnHeader);

            // row commands
            AddRowCommand = new RelayCommand(AddRow);
            InsertRowAboveCommand = new RelayCommand<TableRowModel>(InsertRowAbove);
            InsertRowBelowCommand = new RelayCommand<TableRowModel>(InsertRowBelow);
            MoveRowToFirstCommand = new RelayCommand<TableRowModel>(MoveRowToFirst);
            DeleteRowCommand = new RelayCommand<TableRowModel>(DeleteRow);
            MoveRowToColumnHeadersCommand = new RelayCommand<TableRowModel?>(MoveRowToColumnHeaders,
                r => r != null && Columns.Any(c => !string.IsNullOrWhiteSpace(c.BindingPath) &&
                !string.IsNullOrWhiteSpace(r![c.BindingPath]))
    );

            // column commands
            AddColumnCommand = new RelayCommand(AddColumn);
            MoveColumnLeftCommand = new RelayCommand<ColumnDefinitionModel>(MoveColumnLeft);
            MoveColumnRightCommand = new RelayCommand<ColumnDefinitionModel>(MoveColumnRight);
            DeleteColumnCommand = new RelayCommand<ColumnDefinitionModel>(DeleteColumn);
        }

        public static EditableTableEditorViewModel From(
            string? title,
            ObservableCollection<ColumnDefinitionModel>? columns,
            ObservableCollection<TableRowModel>? rows)
        {
            var vm = new EditableTableEditorViewModel { Title = title ?? string.Empty };

            // Copy columns (guard nulls)
            if (columns != null)
            {
                foreach (var c in columns)
                {
                    var header = c?.Header ?? string.Empty;
                    var key = c?.BindingPath ?? string.Empty; // keep as-is (don’t invent keys here)
                    vm.Columns.Add(new ColumnDefinitionModel { Header = header, BindingPath = key });
                }
            }

            // Copy rows using the copied column list (guard nulls & empty keys)
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r is null) continue;
                    var nr = new TableRowModel();

                    foreach (var c in vm.Columns)
                    {
                        var key = c.BindingPath ?? string.Empty;
                        if (key.Length == 0) continue;            // skip columns without a key
                                                                  // TableRowModel indexer already tolerates missing keys and returns ""
                        nr[key] = r[key];                          // copy cell value
                    }

                    vm.Rows.Add(nr);
                }
            }

            vm.NormalizeRows(); // ensures every row has every defined key
            return vm;
        }


        private TableRowModel NewRowFromColumns()
        {
            var r = new TableRowModel();
            foreach (var c in Columns)
                if (!string.IsNullOrWhiteSpace(c.BindingPath))
                    r.EnsureKey(c.BindingPath);
            return r;
        }

        public void ApplyTo(ProviderBackplane backplane)
        {
            backplane.ReplaceWith(Columns, Rows);
        }

        // -------- Cell commands --------
        private void MoveCellToTitleFromCell(object? param)
        {
            if (param is not System.Windows.Controls.DataGridCell cell) return;
            if (cell.DataContext is not TableRowModel row) return;
            if (cell.Column?.Header is not ColumnDefinitionModel col) return;

            MoveCellToTitle(new Controls.CellToHeaderArgs(row, col));
        }

        private void MoveCellToTitle(Controls.CellToHeaderArgs? args)
        {
            if (args is null || args.Row is null || args.Column is null) return;

            var key = args.Column.BindingPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return;

            var val = args.Row[key];
            if (!string.IsNullOrWhiteSpace(val))
            {
                Title = val;
                args.Row[key] = string.Empty; // clear the source cell after moving
            }
        }

        private void MoveCellToColumnHeader(Controls.CellToHeaderArgs? args)
        {
            if (args?.Column is null || args.Row is null) return;
            var key = args.Column.BindingPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key)) return;

            var value = args.Row[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                args.Column.Header = value;

                args.Row[key] = string.Empty;
            }
        }

        // -------- Row commands --------
        private void AddRow() => Rows.Add(NewRowFromColumns());

        private void InsertRowAbove(TableRowModel? anchor)
        {
            var i = anchor is null ? -1 : Rows.IndexOf(anchor);
            var nr = NewRowFromColumns();
            Rows.Insert(i < 0 ? 0 : i, nr);
        }

        private void InsertRowBelow(TableRowModel? anchor)
        {
            var i = anchor is null ? -1 : Rows.IndexOf(anchor);
            var nr = NewRowFromColumns();
            Rows.Insert(i < 0 ? Rows.Count : i + 1, nr);
        }

        private void MoveRowToFirst(TableRowModel? row)
        {
            if (row is null) return;
            var i = Rows.IndexOf(row);
            if (i > 0) Rows.Move(i, 0);
        }

        private void DeleteRow(TableRowModel? row)
        {
            if (row is null) return;
            var i = Rows.IndexOf(row);
            if (i >= 0) Rows.RemoveAt(i);
        }

        private void MoveColumnLeft(ColumnDefinitionModel? col)
        {
            if (col is null) return;
            var i = Columns.IndexOf(col);
            if (i > 0) { Columns.RemoveAt(i); Columns.Insert(i - 1, col); }
        }

        private void MoveColumnRight(ColumnDefinitionModel? col)
        {
            if (col is null) return;
            var i = Columns.IndexOf(col);
            if (i >= 0 && i < Columns.Count - 1) { Columns.RemoveAt(i); Columns.Insert(i + 1, col); }
        }

        private void DeleteColumn(ColumnDefinitionModel? col)
        {
            if (col is null) return;
            var i = Columns.IndexOf(col);
            if (i < 0) return;

            foreach (var r in Rows)
            {
                var key = col.BindingPath ?? "";
                var hit = r.Cells.FirstOrDefault(x => x.Key == key);
                if (hit is not null) r.Cells.Remove(hit);
            }
            Columns.RemoveAt(i);
        }

        private void AddColumn()
        {
            // Compute a unique BindingPath (c0, c1, ...), even if columns were deleted/reordered
            var nextIndex = 0;
            var used = Columns.Select(c => c?.BindingPath).Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet();
            while (used.Contains($"c{nextIndex}")) nextIndex++;

            var binding = $"c{nextIndex}";
            var header = $"Column {Columns.Count + 1}";

            Columns.Add(new ColumnDefinitionModel
            {
                Header = header,
                BindingPath = binding
            });

            // Ensure each existing row has a slot for the new column
            foreach (var r in Rows)
                r.EnsureKey(binding);
        }

        private void MoveRowToColumnHeaders(TableRowModel? row)
        {
            if (row is null) return;

            // 1) Move non-empty cell values to their column headers
            foreach (var c in Columns)
            {
                var key = c.BindingPath;
                if (string.IsNullOrWhiteSpace(key)) continue;

                var value = row[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    c.Header = value;
                }
            }

            // 2) Clear the entire row after moving
            foreach (var c in Columns)
            {
                var key = c.BindingPath;
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!string.IsNullOrEmpty(row[key]))
                {
                    row[key] = string.Empty;
                }
            }
        }

        private void NormalizeRows()
        {
            foreach (var r in Rows)
                foreach (var c in Columns)
                    if (!string.IsNullOrWhiteSpace(c.BindingPath))
                        r.EnsureKey(c.BindingPath);
        }
    }
}
