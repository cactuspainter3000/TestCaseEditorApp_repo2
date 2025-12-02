using EditableDataControl.ViewModels;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EditableDataControl.Controls
{
    /// <summary>
    /// Context menu injector for DataGrid cells.
    /// Uses VM commands if present; otherwise falls back to inline ops.
    /// Includes:
    /// - Move to Title / Move to Column Header
    /// - Insert Row Above / Insert Row Below
    /// - Move Row Up / Move Row Down (inline fallback only, no VM dependency)
    /// - Move Row to Column Headers
    /// - Delete Row
    /// - Cut / Copy / Paste / Clear Content
    /// - Debug item
    /// </summary>
    public static class DebugCellMenu
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached(
                "Enable",
                typeof(bool),
                typeof(DebugCellMenu),
                new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);
        public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

        private static readonly DependencyProperty LastPressedCellProperty =
            DependencyProperty.RegisterAttached(
                "LastPressedCell",
                typeof(DataGridCell),
                typeof(DebugCellMenu),
                new PropertyMetadata(null));

        private static DataGridCell? GetLastPressedCell(DependencyObject d) => (DataGridCell?)d.GetValue(LastPressedCellProperty);
        private static void SetLastPressedCell(DependencyObject d, DataGridCell? cell) => d.SetValue(LastPressedCellProperty, cell);

#if DEBUG
        private static bool _toldNotACell;
#endif

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            if ((bool)e.NewValue)
            {
                grid.PreviewMouseRightButtonDown += Grid_PreviewMouseRightButtonDown;
                grid.PreviewMouseRightButtonUp += Grid_PreviewMouseRightButtonUp;
                grid.ContextMenuOpening += Grid_ContextMenuOpening; // keyboard invoke
                grid.Unloaded += Grid_Unloaded;
            }
            else
            {
                grid.PreviewMouseRightButtonDown -= Grid_PreviewMouseRightButtonDown;
                grid.PreviewMouseRightButtonUp -= Grid_PreviewMouseRightButtonUp;
                grid.ContextMenuOpening -= Grid_ContextMenuOpening;
                grid.Unloaded -= Grid_Unloaded;
            }
        }

        private static void Grid_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                grid.PreviewMouseRightButtonDown -= Grid_PreviewMouseRightButtonDown;
                grid.PreviewMouseRightButtonUp -= Grid_PreviewMouseRightButtonUp;
                grid.ContextMenuOpening -= Grid_ContextMenuOpening;
                grid.Unloaded -= Grid_Unloaded;
            }
        }

        private static void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;
            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            SetLastPressedCell(grid, cell);
        }

        private static void Grid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid) return;

            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject)
                       ?? GetLastPressedCell(grid);

            if (cell is null)
            {
#if DEBUG
                if (!_toldNotACell)
                {
                    _toldNotACell = true;
                    MessageBox.Show("DebugCellMenu: right-click seen, but not on a DataGridCell (try inside a cell).",
                                    "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                }
#endif
                return;
            }

            OpenMenuForCell(cell);
            e.Handled = true;
        }

        private static void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not DataGrid grid) return;

            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject)
                       ?? GetLastPressedCell(grid);

            if (cell is null) return;

            OpenMenuForCell(cell);
            e.Handled = true;
        }

        private static void OpenMenuForCell(DataGridCell cell)
        {
            var grid = FindAncestor<DataGrid>(cell);
            var vm = grid?.DataContext as EditableTableEditorViewModel;

            var row = cell.DataContext as TableRowModel;
            var col = cell.Column?.Header as ColumnDefinitionModel;
            var key = col?.BindingPath ?? string.Empty;

            string current = (row != null && !string.IsNullOrEmpty(key)) ? (row[key] ?? string.Empty) : string.Empty;
            bool hasCell = row != null && !string.IsNullOrEmpty(key);

            var menu = new ContextMenu { PlacementTarget = cell };

            // ----- Cell -> title/header -----
            if (vm != null && row != null && col != null)
            {
                var args = new global::EditableDataControl.Controls.CellToHeaderArgs(row, col);

                var moveCellToTitleCmd = vm.MoveCellToTitleCommand;
                var miMoveTitle = new MenuItem { Header = "Move to Title" };
                miMoveTitle.IsEnabled = moveCellToTitleCmd?.CanExecute(args) == true;
                miMoveTitle.Click += (_, __) =>
                {
                    if (moveCellToTitleCmd?.CanExecute(args) == true)
                        moveCellToTitleCmd.Execute(args);
                };
                menu.Items.Add(miMoveTitle);

                var moveCellToColumnHeaderCmd = vm.MoveCellToColumnHeaderCommand;
                var miMoveHeader = new MenuItem { Header = "Move to Column Header" };
                miMoveHeader.IsEnabled = moveCellToColumnHeaderCmd?.CanExecute(args) == true;
                miMoveHeader.Click += (_, __) =>
                {
                    if (moveCellToColumnHeaderCmd?.CanExecute(args) == true)
                        moveCellToColumnHeaderCmd.Execute(args);
                };
                menu.Items.Add(miMoveHeader);
            }

            // ----- Row operations -----
            if (row != null)
            {
                var rowsList = GetRowsList(grid!, vm);
                var idx = GetRowIndex(rowsList, row);
                var cols = vm?.Columns ?? new System.Collections.ObjectModel.ObservableCollection<ColumnDefinitionModel>();
                bool hasList = rowsList != null && idx >= 0;

                // Insert Row Above
                var miInsAbove = new MenuItem { Header = "Insert Row Above" };
                if (vm?.InsertRowAboveCommand != null)
                {
                    miInsAbove.IsEnabled = vm.InsertRowAboveCommand.CanExecute(row);
                    miInsAbove.Click += (_, __) =>
                    {
                        if (vm.InsertRowAboveCommand.CanExecute(row))
                            vm.InsertRowAboveCommand.Execute(row);
                    };
                }
                else
                {
                    miInsAbove.IsEnabled = hasList;
                    miInsAbove.Click += (_, __) => InsertRowAboveInline(rowsList!, idx, cols);
                }
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                menu.Items.Add(miInsAbove);

                // Insert Row Below
                var miInsBelow = new MenuItem { Header = "Insert Row Below" };
                if (vm?.InsertRowBelowCommand != null)
                {
                    miInsBelow.IsEnabled = vm.InsertRowBelowCommand.CanExecute(row);
                    miInsBelow.Click += (_, __) =>
                    {
                        if (vm.InsertRowBelowCommand.CanExecute(row))
                            vm.InsertRowBelowCommand.Execute(row);
                    };
                }
                else
                {
                    miInsBelow.IsEnabled = hasList;
                    miInsBelow.Click += (_, __) => InsertRowBelowInline(rowsList!, idx, cols);
                }
                menu.Items.Add(miInsBelow);

                // ---- Move Row Up (inline only) ----
                var miMoveUp = new MenuItem { Header = "Move Row Up" };
                miMoveUp.Click += (_, __) =>
                {
                    var list = GetRowsList(grid!, vm);
                    if (list is null) return;
                    var curIndex = GetRowIndex(list, row);
                    MoveRowUpInline(list, curIndex);
                };
                menu.Items.Add(miMoveUp);

                // ---- Move Row Down (inline only) ----
                var miMoveDown = new MenuItem { Header = "Move Row Down" };
                miMoveDown.Click += (_, __) =>
                {
                    var list = GetRowsList(grid!, vm);
                    if (list is null) return;
                    var curIndex = GetRowIndex(list, row);
                    MoveRowDownInline(list, curIndex);
                };
                menu.Items.Add(miMoveDown);


                // Move Row to Column Headers
                var miRowToHeaders = new MenuItem { Header = "Move Row to Column Headers" };
                if (vm?.MoveRowToColumnHeadersCommand != null)
                {
                    miRowToHeaders.IsEnabled = vm.MoveRowToColumnHeadersCommand.CanExecute(row);
                    miRowToHeaders.Click += (_, __) =>
                    {
                        if (vm.MoveRowToColumnHeadersCommand.CanExecute(row))
                            vm.MoveRowToColumnHeadersCommand.Execute(row);
                    };
                }
                else
                {
                    miRowToHeaders.IsEnabled = RowHasAnyNonEmpty(row, cols);
                    miRowToHeaders.Click += (_, __) => MoveRowToHeadersInline(row, cols);
                }
                menu.Items.Add(miRowToHeaders);

                // Delete Row
                var miDeleteRow = new MenuItem { Header = "Delete Row" };
                if (vm?.DeleteRowCommand != null)
                {
                    miDeleteRow.IsEnabled = vm.DeleteRowCommand.CanExecute(row);
                    miDeleteRow.Click += (_, __) =>
                    {
                        if (vm.DeleteRowCommand.CanExecute(row))
                            vm.DeleteRowCommand.Execute(row);
                    };
                }
                else
                {
                    miDeleteRow.IsEnabled = hasList;
                    miDeleteRow.Click += (_, __) => DeleteRowInline(rowsList!, row);
                }
                menu.Items.Add(miDeleteRow);

                // Refresh fallback enable states whenever the menu opens
                menu.Opened += (_, __) =>
                {
                    var list = GetRowsList(grid!, vm);
                    var curIndex = list is null ? -1 : GetRowIndex(list, row);

                    if (vm?.InsertRowAboveCommand == null)
                        miInsAbove.IsEnabled = list is not null && curIndex >= 0;

                    if (vm?.InsertRowBelowCommand == null)
                        miInsBelow.IsEnabled = list is not null && curIndex >= 0;

                    miMoveUp.IsEnabled = list is not null && curIndex > 0;
                    miMoveDown.IsEnabled = list is not null && curIndex >= 0 && curIndex < list.Count - 1;
                };

            }

            if (menu.Items.Count > 0) menu.Items.Add(new Separator());

            // ----- Edit actions -----
            var miCut = new MenuItem { Header = "Cut", IsEnabled = hasCell && !string.IsNullOrEmpty(current) };
            miCut.Click += (_, __) =>
            {
                if (!hasCell) return;
                try { Clipboard.SetText(current); } catch { }
                row![key] = string.Empty;
            };
            menu.Items.Add(miCut);

            var miCopy = new MenuItem { Header = "Copy", IsEnabled = hasCell && !string.IsNullOrEmpty(current) };
            miCopy.Click += (_, __) =>
            {
                if (!hasCell) return;
                try { Clipboard.SetText(current); } catch { }
            };
            menu.Items.Add(miCopy);

            bool canPaste = false;
            try { canPaste = Clipboard.ContainsText(); } catch { }
            var miPaste = new MenuItem { Header = "Paste", IsEnabled = hasCell && canPaste };
            miPaste.Click += (_, __) =>
            {
                if (!hasCell) return;
                try { row![key] = Clipboard.GetText() ?? string.Empty; } catch { }
            };
            menu.Items.Add(miPaste);

            var miClear = new MenuItem { Header = "Clear Content", IsEnabled = hasCell && !string.IsNullOrEmpty(current) };
            miClear.Click += (_, __) => { if (hasCell) row![key] = string.Empty; };
            menu.Items.Add(miClear);

            // ----- Debug sanity item -----
            menu.Items.Add(new Separator());
            var miDebug = new MenuItem { Header = "Move to Title (DEBUG)" };
            miDebug.Click += (_, __) =>
                MessageBox.Show("We moved it!", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
            menu.Items.Add(miDebug);

            // Open & cleanup
            cell.ContextMenu = menu;
            menu.IsOpen = true;
            menu.Closed += (_, __) =>
            {
                if (ReferenceEquals(cell.ContextMenu, menu))
                    cell.ClearValue(FrameworkElement.ContextMenuProperty);
            };
        }

        // -------- Inline helpers (fallbacks) --------

        private static IList? GetRowsList(DataGrid grid, EditableTableEditorViewModel? vm)
        {
            if (vm?.Rows is IList il) return il;
            if (grid.ItemsSource is IList ils) return ils;
            return null;
        }

        private static int GetRowIndex(IList? list, object row)
            => list is null ? -1 : list.IndexOf(row);

        private static TableRowModel CreateEmptyRow(System.Collections.Generic.IEnumerable<ColumnDefinitionModel> cols)
        {
            var r = new TableRowModel();
            foreach (var c in cols)
            {
                var key = c?.BindingPath;
                if (!string.IsNullOrWhiteSpace(key))
                    r[key!] = string.Empty;
            }
            return r;
        }

        private static void InsertRowAboveInline(IList list, int index, System.Collections.Generic.IEnumerable<ColumnDefinitionModel> cols)
        {
            if (index < 0) return;
            list.Insert(index, CreateEmptyRow(cols));
        }

        private static void InsertRowBelowInline(IList list, int index, System.Collections.Generic.IEnumerable<ColumnDefinitionModel> cols)
        {
            if (index < 0) return;
            list.Insert(index + 1, CreateEmptyRow(cols));
        }

        private static void MoveRowUpInline(IList list, int index)
        {
            if (index <= 0) return;
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(index - 1, item);
        }

        private static void MoveRowDownInline(IList list, int index)
        {
            if (index < 0 || index >= list.Count - 1) return;
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(index + 1, item);
        }

        private static void DeleteRowInline(IList list, object row)
        {
            list.Remove(row);
        }

        private static bool RowHasAnyNonEmpty(TableRowModel row, System.Collections.Generic.IEnumerable<ColumnDefinitionModel> cols)
        {
            foreach (var c in cols)
            {
                var key = c?.BindingPath;
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!string.IsNullOrWhiteSpace(row[key])) return true;
            }
            return false;
        }

        private static void MoveRowToHeadersInline(TableRowModel row, System.Collections.Generic.IEnumerable<ColumnDefinitionModel> cols)
        {
            foreach (var c in cols)
            {
                var key = c?.BindingPath;
                if (string.IsNullOrWhiteSpace(key)) continue;
                var val = row[key];
                if (!string.IsNullOrWhiteSpace(val))
                    c!.Header = val;
            }

            foreach (var c in cols)
            {
                var key = c?.BindingPath;
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!string.IsNullOrEmpty(row[key]))
                    row[key] = string.Empty;
            }
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is T t) return t;
            return null;
        }

    }
}
