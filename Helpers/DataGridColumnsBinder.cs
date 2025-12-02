using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using EditableDataControl.ViewModels;          // ColumnDefinitionModel, TableRowModel
using TestCaseEditorApp.Converters;            // RowCellConverter

namespace TestCaseEditorApp.Helpers
{
    /// <summary>
    /// Attached helper to bind a collection of ColumnDefinitionModel to a DataGrid's Columns.
    /// Usage: Helpers:DataGridColumnsBinder.ColumnsSource="{Binding MyColumnDefinitions}"
    /// </summary>
    public static class DataGridColumnsBinder
    {
        public static readonly DependencyProperty ColumnsSourceProperty =
            DependencyProperty.RegisterAttached(
                "ColumnsSource",
                typeof(IEnumerable),
                typeof(DataGridColumnsBinder),
                new PropertyMetadata(null, OnColumnsSourceChanged));

        public static void SetColumnsSource(DependencyObject d, IEnumerable? value) =>
            d.SetValue(ColumnsSourceProperty, value);

        public static IEnumerable? GetColumnsSource(DependencyObject d) =>
            (IEnumerable?)d.GetValue(ColumnsSourceProperty);

        private static readonly DependencyProperty SubscriptionsProperty =
            DependencyProperty.RegisterAttached(
                "Subscriptions",
                typeof(SubscriptionBag),
                typeof(DataGridColumnsBinder),
                new PropertyMetadata(null));

        private static void SetSubscriptions(DependencyObject d, SubscriptionBag? bag) =>
            d.SetValue(SubscriptionsProperty, bag);

        private static SubscriptionBag? GetSubscriptions(DependencyObject d) =>
            (SubscriptionBag?)d.GetValue(SubscriptionsProperty);

        private sealed class SubscriptionBag
        {
            public INotifyCollectionChanged CollectionChangedSource = null!;
            public NotifyCollectionChangedEventHandler CollectionChangedHandler = null!;
            public readonly System.Collections.Generic.List<INotifyPropertyChanged> Items = new();
        }

        private static void OnColumnsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            var newCols = e.NewValue as IEnumerable;

            // Tear down previous subscriptions
            var oldBag = GetSubscriptions(grid);
            if (oldBag != null)
            {
                try
                {
                    if (oldBag.CollectionChangedSource != null && oldBag.CollectionChangedHandler != null)
                        oldBag.CollectionChangedSource.CollectionChanged -= oldBag.CollectionChangedHandler;
                }
                catch
                {
                    // swallow any unsubscribe exceptions (best-effort cleanup)
                }

                foreach (var npc in oldBag.Items)
                    npc.PropertyChanged -= OnColumnPropertyChanged;

                SetSubscriptions(grid, null);
            }

            RebuildColumns(grid, newCols);

            if (newCols is INotifyCollectionChanged incc)
            {
                NotifyCollectionChangedEventHandler handler = (s, args) =>
                {
                    if (args.OldItems != null)
                        foreach (var item in args.OldItems.OfType<INotifyPropertyChanged>())
                            item.PropertyChanged -= OnColumnPropertyChanged;
                    if (args.NewItems != null)
                        foreach (var item in args.NewItems.OfType<INotifyPropertyChanged>())
                            item.PropertyChanged += OnColumnPropertyChanged;

                    // Rebuild on collection changes (Add/Remove/Replace/Reset)
                    // If you want smarter incremental updates, replace this with targeted updates.
                    RebuildColumns(grid, newCols);
                };

                incc.CollectionChanged += handler;

                var bag = new SubscriptionBag { CollectionChangedSource = incc, CollectionChangedHandler = handler };
                foreach (var item in newCols.Cast<object>().OfType<INotifyPropertyChanged>())
                {
                    item.PropertyChanged += OnColumnPropertyChanged;
                    bag.Items.Add(item);
                }
                SetSubscriptions(grid, bag);
            }

            // Local handler to respond to column-definition property changes
            void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs args)
            {
                // Recreate columns only for changes that affect header or binding
                if (args.PropertyName is nameof(ColumnDefinitionModel.Header) or nameof(ColumnDefinitionModel.BindingPath))
                    RebuildColumns(grid, newCols);
            }
        }

        private static void RebuildColumns(DataGrid dg, IEnumerable? cols)
        {
            dg.Columns.Clear();
            if (cols == null) return;

            foreach (var item in cols)
            {
                if (item is not ColumnDefinitionModel c) continue;

                // Create a binding that passes the whole row into a converter which will extract the cell value
                var binding = new Binding(".")
                {
                    Converter = new RowCellConverter(),
                    ConverterParameter = c.BindingPath,
                    Mode = BindingMode.OneWay
                };

                var col = new DataGridTextColumn
                {
                    Header = string.IsNullOrWhiteSpace(c.Header) ? c.BindingPath : c.Header,
                    Binding = binding,
                    Width = DataGridLength.Auto
                };

                // ElementStyle to trim long text rather than wrap
                var elementStyle = new Style(typeof(TextBlock));
                elementStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                col.ElementStyle = elementStyle;

                dg.Columns.Add(col);
            }
        }
    }
}