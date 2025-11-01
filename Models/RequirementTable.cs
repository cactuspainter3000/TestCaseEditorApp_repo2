using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

#nullable enable

namespace TestCaseEditorApp.MVVM.Models
{
    public partial class RequirementTable : ObservableObject
    {
        // === Existing fields ===
        [ObservableProperty] private string _editableTitle = string.Empty;
        [ObservableProperty] private List<List<string>> _table = new();

        // Confidence / stats (you already had these)
        [ObservableProperty] private int _columnCount;
        [ObservableProperty] private int _rowCount;
        [ObservableProperty] private bool _firstRowLooksLikeHeader;
        [ObservableProperty] private bool _isUniformRowLength = true;
        [ObservableProperty] private double _confidenceScore = 1.0; // 0..1

        // === NEW: metadata used by the VM for persistence ===
        [ObservableProperty] private Guid _tableId = Guid.Empty;              // stable identity (optional)
        [ObservableProperty] private List<string> _columnKeys = new();        // persisted keys, display order
        [ObservableProperty] private List<string> _columnHeaders = new();     // persisted headers, display order

        // Re-run heuristics when the raw table changes
        partial void OnTableChanged(List<List<string>> value) => AnalyzeConfidence();

        public void AnalyzeConfidence()
        {
            RowCount = Table?.Count ?? 0;
            ColumnCount = (RowCount > 0) ? Table!.Max(r => r?.Count ?? 0) : 0;

            if (RowCount == 0 || ColumnCount == 0)
            {
                IsUniformRowLength = true;
                FirstRowLooksLikeHeader = false;
                ConfidenceScore = 0.0;
                return;
            }

            IsUniformRowLength = Table!.All(r => (r?.Count ?? 0) == ColumnCount);

            var first = Table![0] ?? new List<string>();
            bool anyNonEmpty = first.Any(c => !string.IsNullOrWhiteSpace(c));
            int numericCells = first.Count(c => IsNumeric(c));
            int nonEmpty = first.Count(c => !string.IsNullOrWhiteSpace(c));

            FirstRowLooksLikeHeader = anyNonEmpty && (numericCells * 2 < Math.Max(1, nonEmpty));

            double score = 1.0;
            if (!IsUniformRowLength) score -= 0.40;
            if (!FirstRowLooksLikeHeader) score -= 0.30;
            if (RowCount <= 1 || ColumnCount <= 1) score -= 0.20;

            ConfidenceScore = Math.Clamp(Math.Round(score, 2), 0.0, 1.0);
        }

        private static bool IsNumeric(string? s)
            => !string.IsNullOrWhiteSpace(s) &&
               double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }
}

