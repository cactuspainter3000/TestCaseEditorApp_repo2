using EditableDataControl.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace EditableDataControl.Controls
{
    public class EditableDataControl : Control
    {
        static EditableDataControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(EditableDataControl),
                new FrameworkPropertyMetadata(typeof(EditableDataControl)));
        }

        public EditableTableEditorViewModel EditorViewModel
        {
            get => (EditableTableEditorViewModel)GetValue(EditorViewModelProperty);
            set => SetValue(EditorViewModelProperty, value);
        }

        public static readonly DependencyProperty EditorViewModelProperty =
            DependencyProperty.Register(nameof(EditorViewModel),
                typeof(EditableTableEditorViewModel),
                typeof(EditableDataControl),
                new PropertyMetadata(null));
    }
}

namespace EditableDataControl.Controls
{
    using System;
    using System.Collections;
    using System.ComponentModel;
    using System.Linq;
    using System.Reflection;
    using System.Windows;

    /// <summary>
    /// Freezable used to bind a (row, key) pair to a string Value.
    /// Auto-refreshes when Row raises PropertyChanged for Cells / Item[] (or anything).
    /// </summary>
    public class RowCellBinding : Freezable
    {
        public static readonly DependencyProperty RowProperty =
            DependencyProperty.Register(nameof(Row), typeof(object), typeof(RowCellBinding),
                new PropertyMetadata(null, OnRowOrKeyChanged));

        public static readonly DependencyProperty KeyProperty =
            DependencyProperty.Register(nameof(Key), typeof(string), typeof(RowCellBinding),
                new PropertyMetadata(null, OnRowOrKeyChanged));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(RowCellBinding),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        // Store the (source, handler) pair so we can unhook when Row changes
        private static readonly DependencyProperty RowSourceListenerProperty =
            DependencyProperty.RegisterAttached(
                "RowSourceListener",
                typeof((INotifyPropertyChanged Source, EventHandler<PropertyChangedEventArgs> Handler)?),
                typeof(RowCellBinding),
                new PropertyMetadata(null));


        public object? Row
        {
            get => GetValue(RowProperty);
            set => SetValue(RowProperty, value);
        }

        public string? Key
        {
            get => (string?)GetValue(KeyProperty);
            set => SetValue(KeyProperty, value);
        }

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        protected override Freezable CreateInstanceCore() => new RowCellBinding();

        private static void OnRowOrKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = (RowCellBinding)d;

            // 1) Refresh immediately
            RefreshValue(b);

            // 2) If Row changed, rewire listener
            // Unhook old
            if (b.GetValue(RowSourceListenerProperty) is (INotifyPropertyChanged srcOld, EventHandler<PropertyChangedEventArgs> hOld))
            {
                PropertyChangedEventManager.RemoveHandler(srcOld, hOld, string.Empty);
                b.ClearValue(RowSourceListenerProperty);
            }

            // Hook new (if row supports INotifyPropertyChanged)
            if (b.Row is INotifyPropertyChanged srcNew)
            {
                // weak capture to avoid leaks
                var wr = new WeakReference<RowCellBinding>(b);

                EventHandler<PropertyChangedEventArgs> handler = (_, args) =>
                {
                    // Refresh on general or relevant property changes
                    if (string.IsNullOrEmpty(args.PropertyName) ||
                        args.PropertyName == "Cells" ||
                        args.PropertyName == "Item[]")
                    {
                        if (wr.TryGetTarget(out var target))
                            RefreshValue(target);
                    }
                };

                PropertyChangedEventManager.AddHandler(srcNew, handler, string.Empty);
                b.SetValue(RowSourceListenerProperty, (srcNew, handler));
            }
        }


        private static void RefreshValue(RowCellBinding b)
        {
            b.SetCurrentValue(ValueProperty, Read(b.Row, b.Key) ?? "");
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = (RowCellBinding)d;
            Write(b.Row, b.Key, e.NewValue?.ToString() ?? "");
        }

        // === Read/Write helpers (indexer -> Cells -> property) ===
        private static string? Read(object? row, string? key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key)) return "";

            // 1) indexer this[string]
            var idx = row.GetType().GetDefaultMembers().OfType<PropertyInfo>()
                .FirstOrDefault(p =>
                    p.GetIndexParameters().Length == 1 &&
                    p.GetIndexParameters()[0].ParameterType == typeof(string));
            if (idx != null)
            {
                try { return idx.GetValue(row, new object[] { key })?.ToString() ?? ""; } catch { /* ignore */ }
            }

            // 2) Cells list/dictionary with Key/Value
            var cells = row.GetType().GetProperty("Cells")?.GetValue(row) as IEnumerable;
            if (cells != null)
            {
                foreach (var c in cells)
                {
                    var k = c.GetType().GetProperty("Key")?.GetValue(c)?.ToString();
                    if (k == key) return c.GetType().GetProperty("Value")?.GetValue(c)?.ToString() ?? "";
                }
            }

            // 3) property named 'key'
            var p = row.GetType().GetProperty(key);
            return p != null ? p.GetValue(row)?.ToString() ?? "" : "";
        }

        private static void Write(object? row, string? key, string value)
        {
            if (row == null || string.IsNullOrWhiteSpace(key)) return;

            // 1) indexer this[string]
            var idx = row.GetType().GetDefaultMembers().OfType<PropertyInfo>()
                .FirstOrDefault(p =>
                    p.GetIndexParameters().Length == 1 &&
                    p.GetIndexParameters()[0].ParameterType == typeof(string));
            if (idx != null)
            {
                try { idx.SetValue(row, value, new object[] { key }); return; } catch { /* ignore */ }
            }

            // 2) Cells list/dictionary with Key/Value
            var cellsProp = row.GetType().GetProperty("Cells");
            if (cellsProp?.GetValue(row) is System.Collections.IList list)
            {
                object? found = null;
                foreach (var c in list)
                {
                    var k = c.GetType().GetProperty("Key")?.GetValue(c)?.ToString();
                    if (k == key) { found = c; break; }
                }

                if (found == null)
                {
                    var t = list.GetType().GetGenericArguments().FirstOrDefault();
                    if (t != null)
                    {
                        var n = Activator.CreateInstance(t);
                        t.GetProperty("Key")?.SetValue(n, key);
                        t.GetProperty("Value")?.SetValue(n, value ?? "");
                        list.Add(n);
                        return;
                    }
                }
                else
                {
                    found.GetType().GetProperty("Value")?.SetValue(found, value ?? "");
                    return;
                }
            }

            // 3) property named 'key'
            var p = row.GetType().GetProperty(key);
            if (p != null) p.SetValue(row, value ?? "");
        }
    }
}
