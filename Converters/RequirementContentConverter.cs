using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TestCaseEditorApp.MVVM.Models;
using EditableDataControl.ViewModels;
using EditableDataControl.Controls;

namespace TestCaseEditorApp.Converters
{
    /// <summary>
    /// Converts a Requirement object into a rich content view with inline tables.
    /// Combines description text with embedded tables from LooseContent.
    /// </summary>
    public class RequirementContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Requirement requirement)
                return new List<FrameworkElement>();

            var contentElements = new List<FrameworkElement>();

            // Add description text (use cleaned version if available to avoid table duplication)
            if (!string.IsNullOrWhiteSpace(requirement.Description))
            {
                var descriptionToUse = requirement.LooseContent?.CleanedDescription ?? requirement.Description;
                
                // Clean description by removing any remaining table data if not already cleaned
                var cleanDescription = CleanDescriptionFromTableData(descriptionToUse, requirement.LooseContent?.Tables);
                
                // Split description into paragraphs
                var paragraphs = cleanDescription
                    .Split(new[] { "\r\n\r\n", "\n\n", "\r\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                if (paragraphs.Count == 0 && cleanDescription.Contains('\n'))
                {
                    paragraphs = cleanDescription
                        .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();
                }

                if (paragraphs.Count == 0 && !string.IsNullOrWhiteSpace(cleanDescription))
                    paragraphs.Add(cleanDescription.Trim());

                // Add each paragraph as a TextBlock
                foreach (var paragraph in paragraphs)
                {
                    var textBlock = new TextBlock
                    {
                        Text = paragraph,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 12),
                        LineHeight = 20
                    };
                    contentElements.Add(textBlock);
                }
            }

            // Add inline tables from LooseContent
            if (requirement.LooseContent?.Tables?.Any() == true)
            {
                foreach (var table in requirement.LooseContent.Tables)
                {
                    var tableElement = CreateTableElement(table);
                    if (tableElement != null)
                    {
                        contentElements.Add(tableElement);
                    }
                }
            }

            // If no content at all, show a placeholder
            if (contentElements.Count == 0)
            {
                contentElements.Add(new TextBlock
                {
                    Text = "No description available",
                    FontStyle = FontStyles.Italic,
                    Opacity = 0.7
                });
            }

            return contentElements;
        }

        private FrameworkElement? CreateTableElement(LooseTable looseTable)
        {
            if (looseTable.Rows?.Any() != true)
                return null;

            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 16)
            };

            // Add table title if available - use muted text color for metadata labels
            if (!string.IsNullOrWhiteSpace(looseTable.EditableTitle))
            {
                var titleBlock = new TextBlock
                {
                    Text = looseTable.EditableTitle,
                    FontWeight = FontWeights.Normal,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = System.Windows.Application.Current.TryFindResource("Brush.Text.Tertiary") as System.Windows.Media.Brush
                };
                panel.Children.Add(titleBlock);
            }

            // Create read-only DataGrid for table display
            var tableControl = CreateReadOnlyDataGrid(looseTable);
            if (tableControl != null)
            {
                panel.Children.Add(tableControl);
            }

            return panel;
        }

        /// <summary>
        /// Removes table data from description text when tables are available separately
        /// </summary>
        private static string CleanDescriptionFromTableData(string description, List<LooseTable>? tables)
        {
            if (tables?.Any() != true)
                return description;

            var cleanedDescription = description;

            foreach (var table in tables)
            {
                if (table.Rows?.Any() == true)
                {
                    // Remove ALL table content - headers and data
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row)
                        {
                            if (!string.IsNullOrWhiteSpace(cell))
                            {
                                // Remove this cell value from the description
                                cleanedDescription = System.Text.RegularExpressions.Regex.Replace(
                                    cleanedDescription, 
                                    System.Text.RegularExpressions.Regex.Escape(cell.Trim()) + @"\s*",
                                    "",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            }
                        }
                    }
                    
                    // Also remove common table header patterns
                    cleanedDescription = System.Text.RegularExpressions.Regex.Replace(
                        cleanedDescription,
                        @"\b(CCA\s+Part\s+Number|Part\s+Number|CCA\s+Description|Description)\s*",
                        "",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }

            // Clean up extra whitespace and line breaks
            cleanedDescription = System.Text.RegularExpressions.Regex.Replace(cleanedDescription, @"\s{2,}", " ");
            cleanedDescription = System.Text.RegularExpressions.Regex.Replace(cleanedDescription, @"\n\s*\n", "\n");
            
            return cleanedDescription.Trim();
        }

        /// <summary>
        /// Creates a read-only DataGrid to display table data
        /// </summary>
        private static UIElement? CreateReadOnlyDataGrid(LooseTable table)
        {
            if (!table.Rows.Any())
            {
                return null; // Return null if no data
            }

            // Use proper headers from Jama API data
            var columns = new System.Collections.ObjectModel.ObservableCollection<ColumnDefinitionModel>();
            
            if (table.ColumnHeaders.Any())
            {
                // Use headers from Jama API, but skip empty headers
                for (int i = 0; i < table.ColumnHeaders.Count; i++)
                {
                    var headerText = table.ColumnHeaders[i]?.Trim();
                    if (!string.IsNullOrWhiteSpace(headerText))
                    {
                        columns.Add(new ColumnDefinitionModel
                        {
                            Header = headerText,
                            BindingPath = $"Col{i}"
                        });
                    }
                }
            }
            else
            {
                // Fallback headers
                columns.Add(new ColumnDefinitionModel { Header = "CCA Part Number", BindingPath = "Col0" });
                columns.Add(new ColumnDefinitionModel { Header = "CCA Description", BindingPath = "Col1" });
            }

            // Create data rows - data is already cleaned at parse time
            var rows = new System.Collections.ObjectModel.ObservableCollection<TableRowModel>();
            
            foreach (var sourceRow in table.Rows)
            {
                var tableRow = new TableRowModel();
                
                // Only create data for the columns we actually defined
                for (int colIndex = 0; colIndex < columns.Count; colIndex++)
                {
                    var cellValue = colIndex < sourceRow.Count ? (sourceRow[colIndex] ?? string.Empty) : string.Empty;
                    tableRow[$"Col{colIndex}"] = cellValue;
                }
                
                rows.Add(tableRow);
            }

            // For Jama requirements, create a read-only DataGrid instead of EditableDataControl
            // This matches how doc imports display tables in Requirements_TablesControl.xaml
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementContentConverter] Creating read-only DataGrid for table with {columns.Count} columns and {rows.Count} rows");
            
            var dataGrid = new System.Windows.Controls.DataGrid
            {
                ItemsSource = rows,
                AutoGenerateColumns = false,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                IsReadOnly = true,
                MinHeight = 140,
                GridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility.All,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Background = System.Windows.Application.Current.TryFindResource("Brush.Background.Menu") as System.Windows.Media.Brush,
                Foreground = System.Windows.Application.Current.TryFindResource("Brush.Text.Primary") as System.Windows.Media.Brush,
                BorderBrush = System.Windows.Application.Current.TryFindResource("Brush.Border.Default") as System.Windows.Media.Brush,
                BorderThickness = new Thickness(1),
                RowBackground = System.Windows.Application.Current.TryFindResource("Brush.Background.Menu") as System.Windows.Media.Brush,
                AlternatingRowBackground = System.Windows.Application.Current.TryFindResource("Brush.Background.Menu.Hover") as System.Windows.Media.Brush,
                ColumnHeaderHeight = 32,
                Margin = new Thickness(0, 8, 0, 16)
            };

            // Create a darker column header style to match the theme
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.BackgroundProperty, System.Windows.Application.Current.TryFindResource("Brush.Background.Header") as System.Windows.Media.Brush));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.ForegroundProperty, System.Windows.Application.Current.TryFindResource("Brush.Text.Primary") as System.Windows.Media.Brush));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.FontWeightProperty, System.Windows.Application.Current.TryFindResource("FontWeight.SemiBold") ?? FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.FontSizeProperty, System.Windows.Application.Current.TryFindResource("FontSize.Caption") ?? 12.0));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.PaddingProperty, new Thickness(8, 6, 8, 6)));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.BorderBrushProperty, System.Windows.Application.Current.TryFindResource("Brush.Border.Default") as System.Windows.Media.Brush));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            dataGrid.ColumnHeaderStyle = headerStyle;

            // Create columns dynamically
            foreach (var column in columns)
            {
                var dataGridColumn = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = column.Header,
                    Binding = new System.Windows.Data.Binding($"[{column.BindingPath}]"),
                    IsReadOnly = true
                };
                dataGrid.Columns.Add(dataGridColumn);
            }

            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementContentConverter] Read-only DataGrid created - no editing buttons, just table display");

            return dataGrid;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}