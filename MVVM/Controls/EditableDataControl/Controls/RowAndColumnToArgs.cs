// File: EditableDataControl/Controls/RowAndColumnToArgs.cs
using EditableDataControl.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace EditableDataControl.Controls
{
    /// <summary>
    /// Parameter object carrying both the row and the column for a cell action.
    /// </summary>
    public sealed class CellToHeaderArgs
    {
        public TableRowModel Row { get; }
        public ColumnDefinitionModel Column { get; }

        public CellToHeaderArgs(TableRowModel row, ColumnDefinitionModel column)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            Column = column ?? throw new ArgumentNullException(nameof(column));
        }
    }

    /// <summary>
    /// IMultiValueConverter that packs (row, column) into CellToHeaderArgs.
    /// Binding[0]: TableRowModel (cell's DataContext)
    /// Binding[1]: ColumnDefinitionModel (column header model)
    /// </summary>
    public sealed class RowAndColumnToArgsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is { Length: >= 2 } &&
                values[0] is TableRowModel row &&
                values[1] is ColumnDefinitionModel col)
            {
                return new CellToHeaderArgs(row, col);
            }

            return Binding.DoNothing;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
