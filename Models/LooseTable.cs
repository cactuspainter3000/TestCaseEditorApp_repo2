using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace TestCaseEditorApp.MVVM.Models
{
    /// <summary>
    /// Pure data model for a loose table imported from Word.
    /// Holds a title and raw cell data (rows of strings).
    /// No UI or editor logic belongs here.
    /// 
    /// Added ColumnHeaders and ColumnKeys so header promotions in the editor
    /// can be persisted and round-tripped.
    /// </summary>
    public partial class LooseTable : ObservableObject
    {
        /// <summary>Editable title shown above the table in the UI.</summary>
        [ObservableProperty]
        private string editableTitle = string.Empty;

        /// <summary>
        /// Raw rows of cell values. Each inner list is a row; cells are strings.
        /// Columns are implicit by widest row.
        /// </summary>
        [ObservableProperty]
        private List<List<string>> rows = new();

        /// <summary>
        /// Optional persisted column headers. When present, the UI will use these values
        /// to build ColumnDefinitionModel.Header values instead of synthesizing "Column 1..." labels.
        /// </summary>
        [ObservableProperty]
        private List<string> columnHeaders = new();

        /// <summary>
        /// Optional persisted column keys/binding paths (e.g., "c0","c1" or custom keys).
        /// When present these are used as BindingPath values for generated ColumnDefinitionModel entries.
        /// If absent, keys are synthesized from headers or as "c{i}".
        /// </summary>
        [ObservableProperty]
        private List<string> columnKeys = new();
    }
}