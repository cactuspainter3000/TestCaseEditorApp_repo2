// File: TestCaseEditorApp/Services/TableDto.cs
using System.Collections.Generic;

namespace TestCaseEditorApp.Services
{
    public sealed class TableDto
    {
        public string Title { get; init; } = string.Empty;
        public List<List<string>> Rows { get; init; } = new();
        public List<string> Columns { get; set; } = new();
    }
}

