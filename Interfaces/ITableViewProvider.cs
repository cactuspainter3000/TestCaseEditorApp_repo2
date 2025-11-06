using System.Collections.ObjectModel;
using System.Data;
using EditableDataControl.ViewModels;

namespace TestCaseEditorApp.Interfaces
{
    public interface ITableViewProvider
    {
        string EditableTitle { get; set; }
        ObservableCollection<ColumnDefinitionModel> Columns { get; set; }
        ObservableCollection<object> Rows { get; set; }
        DataTable TableView { get; set; }
        void CommitEditedTable(DataTable edited);
    }
}

