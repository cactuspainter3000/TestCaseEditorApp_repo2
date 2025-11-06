using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EditableDataControl.ViewModels;          // ColumnDefinitionModel, TableRowModel
using TestCaseEditorApp.Converters;            // RowCellConverter
using System.Globalization;
using System.Windows.Markup;

namespace TestCaseEditorApp.Helpers
{
    public static class DataGridColumnsBinder
    {
        public static readonly DependencyProperty ColumnsSourceProperty =
            DependencyProperty.RegisterAttached(
                "ColumnsSource",
                typeof(IEnumerable),
                typeof(DataGridColumnsBinder),
                new PropertyMetadata(null, OnColumnsSourceChanged));

        public static void SetColumnsSource(DependencyObject d, IEnumerable value) =>
            d.SetValue(ColumnsSourceProperty, value);

        public static IEnumerable GetColumnsSource(DependencyObject d) =>
            (IEnumerable)d.GetValue(ColumnsSourceProperty);

        private static readonly DependencyProperty SubscriptionsProperty =
            DependencyProperty.RegisterAttached(
                "Subscriptions",
                typeof(SubscriptionBag),
                typeof(DataGridColumnsBinder),
                new PropertyMetadata(null));

        private static void SetSubscriptions(DependencyObject d, SubscriptionBag bag) =>
            d.SetValue(SubscriptionsProperty, bag);

        private static SubscriptionBag GetSubscriptions(DependencyObject d) =>
            (SubscriptionBag)d.GetValue(SubscriptionsProperty);

        private sealed class SubscriptionBag
        {
            public INotifyCollectionChanged CollectionChangedSource;
            public readonly System.Collections.Generic.List<INotifyPropertyChanged> Items = new();
        }

        private static void OnColumnsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            var newCols = e.NewValue as IEnumerable;   // 👈 moved up

            // Tear down previous subscriptions
            var oldBag = GetSubscriptions(grid);
            if (oldBag != null)
            {
                if (oldBag.CollectionChangedSource != null)
                    oldBag.CollectionChangedSource.CollectionChanged -= (_, __) => { };
                foreach (var npc in oldBag.Items)
                    npc.PropertyChanged -= OnColumnPropertyChanged;
                SetSubscriptions(grid, null);
            }

            RebuildColumns(grid, newCols);

            if (newCols is INotifyCollectionChanged incc)
            {
                void onCollChanged(object? s, NotifyCollectionChangedEventArgs args)
                {
                    if (args.OldItems != null)
                        foreach (var item in args.OldItems.OfType<INotifyPropertyChanged>())
                            item.PropertyChanged -= OnColumnPropertyChanged;
                    if (args.NewItems != null)
                        foreach (var item in args.NewItems.OfType<INotifyPropertyChanged>())
                            item.PropertyChanged += OnColumnPropertyChanged;

                    RebuildColumns(grid, newCols);
                }

                incc.CollectionChanged += onCollChanged;

                var bag = new SubscriptionBag { CollectionChangedSource = incc };
                foreach (var item in newCols.Cast<object>().OfType<INotifyPropertyChanged>())
                {
                    item.PropertyChanged += OnColumnPropertyChanged;
                    bag.Items.Add(item);
                }
                SetSubscriptions(grid, bag);
            }

            void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs args)
            {
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

            // Text (display) template
            var cellTemplateFactory = new FrameworkElementFactory(typeof(TextBlock));
                var binding = new Binding(".")   // bind the whole row (TableRowModel)
                {
                    Converter = new RowCellConverter(),
                    ConverterParameter = c.BindingPath,   // e.g., "c0", "Voltage", etc.
                    Mode = BindingMode.OneWay             // read-only path; avoid exceptions
                };

                var col = new DataGridTextColumn
                {
                    Header = string.IsNullOrWhiteSpace(c.Header) ? c.BindingPath : c.Header,
                    Binding = binding,
                    Width = DataGridLength.Auto
                };

                col.ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters =
    {
        new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis),
        // or: new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)
    }
                };

                dg.Columns.Add(col);
            }
    }



}
}




//// TestCaseEditorApp/Helpers/DataGridColumnsBinder.cs
//using System;
//using System.Collections;
//using System.Collections.Specialized;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Media;
//using EditableDataControl.ViewModels; // for ColumnDefinitionModel type

//namespace TestCaseEditorApp.Helpers
//{
//    public static class DataGridColumnsBinder
//    {
//        // Attach your Columns collection (ObservableCollection<ColumnDefinitionModel>)
//        public static readonly DependencyProperty ColumnsSourceProperty =
//            DependencyProperty.RegisterAttached(
//                "ColumnsSource",
//                typeof(IEnumerable),
//                typeof(DataGridColumnsBinder),
//                new PropertyMetadata(null, OnColumnsSourceChanged));

//        public static void SetColumnsSource(DependencyObject d, IEnumerable value) =>
//            d.SetValue(ColumnsSourceProperty, value);

//        public static IEnumerable GetColumnsSource(DependencyObject d) =>
//            (IEnumerable)d.GetValue(ColumnsSourceProperty);

//        private static readonly DependencyProperty SubscriptionProperty =
//            DependencyProperty.RegisterAttached(
//                "Subscription",
//                typeof(INotifyCollectionChanged),
//                typeof(DataGridColumnsBinder),
//                new PropertyMetadata(null, OnSubscriptionChanged));

//        private static void OnSubscriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
//        {
//            if (e.OldValue is INotifyCollectionChanged oldObs)
//                oldObs.CollectionChanged -= OnColumnsCollectionChanged;
//            if (e.NewValue is INotifyCollectionChanged newObs)
//                newObs.CollectionChanged += OnColumnsCollectionChanged;
//        }

//        private static void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
//        {
//            // Find the DataGrid that owns this subscription and rebuild
//            // (sender is the ObservableCollection<ColumnDefinitionModel>)
//            foreach (var obj in Application.Current.Windows)
//            {
//                if (obj is not Window win) continue;
//                if (!FindAndRebuildForWindow(win, sender)) continue;
//            }
//        }

//        private static bool FindAndRebuildForWindow(DependencyObject root, object? targetCollection)
//        {
//            if (root is DataGrid dg)
//            {
//                var sub = (INotifyCollectionChanged)dg.GetValue(SubscriptionProperty);
//                if (ReferenceEquals(sub, targetCollection))
//                {
//                    RebuildColumns(dg, GetColumnsSource(dg));
//                    return true;
//                }
//            }

//            int count = VisualTreeHelper.GetChildrenCount(root);
//            for (int i = 0; i < count; i++)
//            {
//                var child = VisualTreeHelper.GetChild(root, i);
//                if (FindAndRebuildForWindow(child, targetCollection))
//                    return true;
//            }
//            return false;
//        }

//        private static void OnColumnsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
//        {
//            if (d is not DataGrid dg) return;

//            // swap subscription
//            dg.SetValue(SubscriptionProperty, e.NewValue as INotifyCollectionChanged);

//            // build now
//            RebuildColumns(dg, e.NewValue as IEnumerable);

//            // also rebuild on DataGrid load to be safe
//            dg.Loaded -= DgOnLoaded;
//            dg.Loaded += DgOnLoaded;
//        }

//        private static void DgOnLoaded(object sender, RoutedEventArgs e)
//        {
//            var dg = (DataGrid)sender;
//            RebuildColumns(dg, GetColumnsSource(dg));
//        }

//        private static void RebuildColumns(DataGrid dg, IEnumerable? cols)
//        {
//            dg.Columns.Clear();
//            if (cols == null) return;

//            foreach (var item in cols)
//            {
//                if (item is not ColumnDefinitionModel c) continue;

//                var binding = new Binding($"[{c.BindingPath}]")
//                {
//                    Mode = BindingMode.TwoWay,
//                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
//                    ValidatesOnDataErrors = false,
//                    ValidatesOnExceptions = false
//                };

//                var col = new DataGridTextColumn
//                {
//                    Header = string.IsNullOrWhiteSpace(c.Header) ? c.BindingPath : c.Header,
//                    Binding = binding,
//                    Width = DataGridLength.Auto
//                };

//                dg.Columns.Add(col);
//            }
//        }
//    }
//}

