using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace TestCaseEditorApp.Helpers
{
    public static class DialogHelper
    {
        public static string? ShowInputDialog(string message, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = "Rename Column",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };

            var stack = new StackPanel { Margin = new Thickness(10) };

            var label = new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 5)
            };

            var inputBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 10),
                MinWidth = 360
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                IsDefault = true
            };
            okButton.Click += (_, _) => dialog.DialogResult = true;

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Margin = new Thickness(5, 0, 0, 0),
                IsCancel = true
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            stack.Children.Add(label);
            stack.Children.Add(inputBox);
            stack.Children.Add(buttonPanel);

            dialog.Content = stack;

            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (owner != null)
                dialog.Owner = owner;

            bool? result = dialog.ShowDialog();
            return result == true ? inputBox.Text : null;
        }
    }
}
