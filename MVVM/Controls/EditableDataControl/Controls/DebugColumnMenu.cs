using EditableDataControl.ViewModels;   // ColumnDefinitionModel, TableRowModel, EditableTableEditorViewModel
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EditableDataControl.Controls
{
    /// <summary>
    /// Column-header context menu injector for DataGrid.
    /// Adds:
    ///  - "Move Header → Title (clear)"
    ///  - "Move Header → First Row (clear)"
    /// Enable on a DataGrid with: local:DebugColumnMenu.EnableHeader="True"
    /// </summary>
    public static class DebugColumnMenu
    {
        public static readonly DependencyProperty EnableHeaderProperty =
            DependencyProperty.RegisterAttached(
                "EnableHeader",
                typeof(bool),
                typeof(DebugColumnMenu),
                new PropertyMetadata(false, OnEnableHeaderChanged));

        public static void SetEnableHeader(DependencyObject d, bool value) => d.SetValue(EnableHeaderProperty, value);
        public static bool GetEnableHeader(DependencyObject d) => (bool)d.GetValue(EnableHeaderProperty);

        private static void OnEnableHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            if ((bool)e.NewValue)
            {
                // Right-click path
                grid.PreviewMouseRightButtonUp += Grid_PreviewMouseRightButtonUp_Header;
                // Keyboard path (Menu key / Shift+F10 on a focused header)
                grid.ContextMenuOpening += Grid_ContextMenuOpening_Header;
                grid.Unloaded += Grid_Unloaded;
            }
            else
            {
                grid.PreviewMouseRightButtonUp -= Grid_PreviewMouseRightButtonUp_Header;
                grid.ContextMenuOpening -= Grid_ContextMenuOpening_Header;
                grid.Unloaded -= Grid_Unloaded;
            }
        }

        private static void Grid_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                grid.PreviewMouseRightButtonUp -= Grid_PreviewMouseRightButtonUp_Header;
                grid.ContextMenuOpening -= Grid_ContextMenuOpening_Header;
                grid.Unloaded -= Grid_Unloaded;
            }
        }

        // Mouse path
        private static void Grid_PreviewMouseRightButtonUp_Header(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid) return;

            var header = FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
            if (header is null) return; // not on a header

            OpenHeaderMenu(header);
            e.Handled = true; // suppress default TextBox menu in header template
        }

        // Keyboard path (Shift+F10/Menu key on a focused header)
        private static void Grid_ContextMenuOpening_Header(object sender, ContextMenuEventArgs e)
        {
            if (sender is not DataGrid) return;

            var header = FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
            if (header is null) return;

            OpenHeaderMenu(header);
            e.Handled = true;
        }

        private static void OpenHeaderMenu(DataGridColumnHeader header)
        {
            var grid = FindAncestor<DataGrid>(header);
            if (grid is null) return;

            var vm = grid.DataContext as EditableTableEditorViewModel;

            // 🔧 FIX: support any column type
            var colDef = header.Column?.Header as ColumnDefinitionModel;
            if (colDef is null) return;

            var headerText = colDef.Header ?? string.Empty;
            var key = colDef.BindingPath ?? string.Empty;

            var menu = new ContextMenu { PlacementTarget = header };

            // Move Header → Title (clear)
            {
                var mi = new MenuItem { Header = "Move Header → Title (clear)" };
                mi.IsEnabled = !string.IsNullOrWhiteSpace(headerText);
                mi.Click += (_, __) =>
                {
                    if (vm is null) return;
                    vm.Title = headerText;
                    colDef.Header = string.Empty;
                };
                menu.Items.Add(mi);
            }

            // Move Header → First Row (clear)
            {
                var mi = new MenuItem { Header = "Move Header → First Row (clear)" };
                var firstRow = vm?.Rows?.FirstOrDefault();
                mi.IsEnabled = firstRow is not null
                               && !string.IsNullOrWhiteSpace(key)
                               && !string.IsNullOrWhiteSpace(headerText);
                mi.Click += (_, __) =>
                {
                    var r = vm?.Rows?.FirstOrDefault();
                    if (r is null || string.IsNullOrWhiteSpace(key)) return;
                    r[key] = headerText;       // use row indexer
                    colDef.Header = string.Empty;
                };
                menu.Items.Add(mi);
            }

            header.ContextMenu = menu;
            menu.IsOpen = true;

            menu.Closed += (_, __) =>
            {
                if (ReferenceEquals(header.ContextMenu, menu))
                    header.ClearValue(FrameworkElement.ContextMenuProperty);
            };
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
                if (d is T t) return t;
            return null;
        }
    }
}
