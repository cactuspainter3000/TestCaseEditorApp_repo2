using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class SplitTextDialog : Window
    {
        public List<string>? SplitResults { get; private set; }

        public SplitTextDialog(string originalText)
        {
            InitializeComponent();
            TextEditor.Text = originalText;
            TextEditor.TextChanged += TextEditor_TextChanged;
            UpdatePreview();
        }

        private void TextEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var text = TextEditor.Text ?? string.Empty;
            var parts = text.Split(new[] { "|||" }, StringSplitOptions.None)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrWhiteSpace(s))
                           .ToList();

            PreviewList.ItemsSource = parts.Count > 0 ? parts : new List<string> { text };
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            var text = TextEditor.Text ?? string.Empty;
            
            if (!text.Contains("|||"))
            {
                MessageBox.Show("Please add ||| markers where you want to split the text.", 
                    "No Split Markers", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SplitResults = text.Split(new[] { "|||" }, StringSplitOptions.None)
                              .Select(s => s.Trim())
                              .Where(s => !string.IsNullOrWhiteSpace(s))
                              .ToList();

            if (SplitResults.Count < 2)
            {
                MessageBox.Show("Split must result in at least 2 items.", 
                    "Invalid Split", MessageBoxButton.OK, MessageBoxImage.Warning);
                SplitResults = null;
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SplitResults = null;
            DialogResult = false;
            Close();
        }
    }
}
