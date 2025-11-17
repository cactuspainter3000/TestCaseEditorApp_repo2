using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class SupplementalInfoEditorWindow : Window
    {
        public List<string> ResultItems { get; private set; }

        public SupplementalInfoEditorWindow(ObservableCollection<ParagraphViewModel> items)
        {
            InitializeComponent();
            
            // Join items with separator
            TextEditor.Text = string.Join(" ||| ", items.Select(p => p.Text));
            
            UpdatePreview();
        }

        private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var text = TextEditor.Text ?? "";
            var items = text.Split(new[] { " ||| " }, System.StringSplitOptions.None)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrEmpty(s))
                           .ToList();

            PreviewList.ItemsSource = items.Any() ? items : new List<string> { "(No items)" };
        }

        private void InsertSplit_Click(object sender, RoutedEventArgs e)
        {
            var cursorPosition = TextEditor.SelectionStart;
            var text = TextEditor.Text ?? "";
            
            // Insert the separator at cursor position
            TextEditor.Text = text.Insert(cursorPosition, " ||| ");
            
            // Move cursor after the inserted separator
            TextEditor.SelectionStart = cursorPosition + 5; // 5 = length of " ||| "
            TextEditor.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var text = TextEditor.Text ?? "";
            var items = text.Split(new[] { " ||| " }, System.StringSplitOptions.None)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrEmpty(s))
                           .ToList();

            if (!items.Any())
            {
                MessageBox.Show(
                    "Please enter at least one item.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ResultItems = items;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
