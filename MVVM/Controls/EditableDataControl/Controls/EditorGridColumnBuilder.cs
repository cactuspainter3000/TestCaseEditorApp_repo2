using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace EditableDataControl.Controls
{
    /// <summary>
    /// Dynamically builds DataGrid columns from a collection of column definitions.
    /// Uses standard WPF binding to TableRowModel's string indexer for simple, reliable data access.
    /// </summary>
    public static class EditorGridColumnBuilder
    {
        public static readonly DependencyProperty ColumnsSourceProperty =
            DependencyProperty.RegisterAttached(
                "ColumnsSource", typeof(IEnumerable), typeof(EditorGridColumnBuilder),
                new PropertyMetadata(null, OnColumnsSourceChanged));

        public static readonly DependencyProperty ColumnWidthProperty =
            DependencyProperty.RegisterAttached(
                "ColumnWidth", typeof(double), typeof(EditorGridColumnBuilder),
                new PropertyMetadata(160.0));

        public static void SetColumnsSource(DependencyObject d, IEnumerable value) => d.SetValue(ColumnsSourceProperty, value);
        public static IEnumerable GetColumnsSource(DependencyObject d) => (IEnumerable)d.GetValue(ColumnsSourceProperty);

        public static void SetColumnWidth(DependencyObject d, double value) => d.SetValue(ColumnWidthProperty, value);
        public static double GetColumnWidth(DependencyObject d) => (double)d.GetValue(ColumnWidthProperty);

        private static readonly Dictionary<DataGrid, INotifyCollectionChanged> _subscriptions = new();

        private static void OnColumnsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            Detach(grid);
            RebuildAll(grid);

            if (e.NewValue is INotifyCollectionChanged ncc)
            {
                _subscriptions[grid] = ncc;
                ncc.CollectionChanged += (_, __) => RebuildAll(grid);
            }

            grid.Unloaded -= Grid_Unloaded;
            grid.Unloaded += Grid_Unloaded;
        }

        private static void Grid_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is DataGrid g) Detach(g);
        }

        private static void Detach(DataGrid grid)
        {
            _subscriptions.Remove(grid);
        }

        private static void RebuildAll(DataGrid grid)
        {
            if (!grid.Dispatcher.CheckAccess())
            {
                grid.Dispatcher.Invoke(() => RebuildAll(grid));
                return;
            }

            // Clear selection safely before rebuilding
            var prevUnit = grid.SelectionUnit;
            try
            {
                grid.SelectionUnit = DataGridSelectionUnit.Cell;
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                grid.UnselectAllCells();
                grid.UnselectAll();
                grid.CurrentCell = new DataGridCellInfo();
            }
            finally { grid.SelectionUnit = prevUnit; }

            grid.Columns.Clear();

            var cols = GetColumnsSource(grid);
            if (cols is null) return;

            var width = GetColumnWidth(grid);
            foreach (var model in cols)
            {
                BuildColumn(grid, model, width);
            }
        }

        private static void BuildColumn(DataGrid grid, object model, double width)
        {
            // Extract BindingPath - this is the key used in TableRowModel's indexer
            var bindingPath = ((dynamic)model).BindingPath as string;
            if (string.IsNullOrWhiteSpace(bindingPath)) return;

            // Create a DataGridTextColumn with direct binding to row[BindingPath]
            var column = new DataGridTextColumn
            {
                Header = model,
                Width = new DataGridLength(width),
                // Bind directly to the TableRowModel's string indexer
                Binding = new Binding($"[{bindingPath}]")
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                }
            };

            // Use white foreground for all text
            var cellForeground = System.Windows.Media.Brushes.White;
            var orangeCaretBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 140, 0));

            // Set ElementStyle for TextBlock display
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, cellForeground));
            column.ElementStyle = textStyle;

            // Set EditingElementStyle for TextBox editing
            var editStyle = new Style(typeof(TextBox));
            editStyle.Setters.Add(new Setter(TextBox.ForegroundProperty, cellForeground));
            editStyle.Setters.Add(new Setter(TextBox.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            editStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, orangeCaretBrush));
            column.EditingElementStyle = editStyle;

            // Don't set CellStyle here - let it inherit from DataGrid.Resources
            // This allows row alternation to work properly

            grid.Columns.Add(column);
        }
    }
}
