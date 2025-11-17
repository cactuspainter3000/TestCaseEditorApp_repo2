using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EditableDataControl.Controls
{
    public static class ScrollSync
    {
        // Group name used to sync multiple ScrollViewers
        public static readonly DependencyProperty GroupProperty =
            DependencyProperty.RegisterAttached(
                "Group", typeof(string), typeof(ScrollSync),
                new PropertyMetadata(null, OnGroupChanged));

        public static void SetGroup(DependencyObject d, string value) => d.SetValue(GroupProperty, value);
        public static string GetGroup(DependencyObject d) => (string)d.GetValue(GroupProperty);

        private static readonly Dictionary<string, List<ScrollViewer>> _groups = new();

        private static void OnGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;
            fe.Loaded -= OnLoaded;
            fe.Unloaded -= OnUnloaded;

            if (e.NewValue is string)
            {
                fe.Loaded += OnLoaded;
                fe.Unloaded += OnUnloaded;
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var group = GetGroup(fe);
            if (string.IsNullOrWhiteSpace(group)) return;

            var sv = FindScrollViewer(fe);
            if (sv == null) return;

            if (!_groups.TryGetValue(group, out var list))
                _groups[group] = list = new List<ScrollViewer>();

            if (!list.Contains(sv))
            {
                list.Add(sv);
                sv.ScrollChanged += (_, args) =>
                {
                    if (args.HorizontalChange == 0) return;
                    foreach (var other in list)
                    {
                        if (other == sv) continue;
                        other.ScrollToHorizontalOffset(args.HorizontalOffset);
                    }
                };
            }
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var group = GetGroup(fe);
            if (string.IsNullOrWhiteSpace(group)) return;

            var sv = FindScrollViewer(fe);
            if (sv == null) return;

            if (_groups.TryGetValue(group, out var list))
            {
                sv.ScrollChanged -= null; // no-op; we didn't keep a delegate ref
                list.Remove(sv);
                if (list.Count == 0) _groups.Remove(group);
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
