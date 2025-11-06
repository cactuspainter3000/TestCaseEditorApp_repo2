using EditableDataControl.ViewModels;
using System.Data;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using TestCaseEditorApp.Interfaces;

namespace TestCaseEditorApp.Helpers
{
    public static class DataGridHelper
    {
        /// <summary>
        /// Rebuilds the columns of a DataGrid based on the provided column definitions.
        /// Intended for use with rows bound to Dictionary&lt;string, string&gt;.
        /// </summary>
        /// new
        public static void RebuildGridColumns(DataGrid grid, IEnumerable<ColumnDefinitionModel> columns)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            if (columns == null)
                return;

            grid.Columns.Clear();

            foreach (var col in columns)
            {
                if (string.IsNullOrWhiteSpace(col.BindingPath))
                {
                    Debug.WriteLine($"[WARNING] Skipping column with empty BindingPath. Header = '{col.Header}'");
                    continue;
                }

                var textColumn = new DataGridTextColumn
                {
                    Header = col.Header,
                    Binding = new Binding(col.BindingPath)
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                        ValidatesOnDataErrors = true
                    },
                    IsReadOnly = false
                };

                grid.Columns.Add(textColumn);
                Debug.WriteLine($"[RebuildGridColumns] Added column: Header = '{col.Header}', BindingPath = '{col.BindingPath}'");
            }

            Debug.WriteLine($"[RebuildGridColumns] Final column count: {grid.Columns.Count}");
        }

        public static void RebuildGridColumns(DataGrid grid, ITableViewProvider tableProvider)
        {
            if (tableProvider == null)
                throw new ArgumentNullException(nameof(tableProvider));

            RebuildGridColumns(grid, (IEnumerable<ColumnDefinitionModel>)tableProvider.Columns);
        }

    }
}
