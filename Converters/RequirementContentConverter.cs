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

            // Add table title if available
            if (!string.IsNullOrWhiteSpace(looseTable.EditableTitle))
            {
                var titleBlock = new TextBlock
                {
                    Text = looseTable.EditableTitle,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 165, 0)) // Orange accent
                };
                panel.Children.Add(titleBlock);
            }

            // Create EditableDataControl for proper table display
            var tableControl = CreateEditableDataControl(looseTable);
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
        /// Creates an EditableDataControl to display table data with proper MVVM binding
        /// </summary>
        private static EditableDataControl.Controls.EditableDataControl? CreateEditableDataControl(LooseTable table)
        {
            System.Diagnostics.Debug.WriteLine($"Table has {table.Rows.Count} rows");
            
            if (!table.Rows.Any())
            {
                System.Diagnostics.Debug.WriteLine("No table rows found");
                return null; // Return null if no data
            }

            // Use proper headers from Jama API data
            var columns = new System.Collections.ObjectModel.ObservableCollection<ColumnDefinitionModel>();
            
            if (table.ColumnHeaders.Any())
            {
                // Use headers from Jama API
                for (int i = 0; i < table.ColumnHeaders.Count; i++)
                {
                    columns.Add(new ColumnDefinitionModel
                    {
                        Header = table.ColumnHeaders[i],
                        BindingPath = $"Col{i}"
                    });
                }
            }
            else
            {
                // Fallback headers
                columns.Add(new ColumnDefinitionModel { Header = "CCA Part Number", BindingPath = "Col0" });
                columns.Add(new ColumnDefinitionModel { Header = "CCA Description", BindingPath = "Col1" });
            }
            
            System.Diagnostics.Debug.WriteLine($"Using headers: [{string.Join(", ", columns.Select(c => c.Header))}]");

            // Create data rows - data is already cleaned at parse time
            var rows = new System.Collections.ObjectModel.ObservableCollection<TableRowModel>();
            
            foreach (var sourceRow in table.Rows)
            {
                var tableRow = new TableRowModel();
                
                for (int colIndex = 0; colIndex < columns.Count && colIndex < sourceRow.Count; colIndex++)
                {
                    var cellValue = sourceRow[colIndex] ?? string.Empty;
                    tableRow[$"Col{colIndex}"] = cellValue;
                }
                
                rows.Add(tableRow);
            }

            // Create the ViewModel
            var editorViewModel = EditableTableEditorViewModel.From("Table Data", columns, rows);
            
            // Create the control
            var tableControl = new EditableDataControl.Controls.EditableDataControl
            {
                EditorViewModel = editorViewModel,
                Margin = new Thickness(0, 8, 0, 16),
                MinHeight = 200, // Ensure table has reasonable height
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)) // Dark background to match theme
            };

            System.Diagnostics.Debug.WriteLine($"Created EditableDataControl with {columns.Count} columns and {rows.Count} rows");
            return tableControl;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}