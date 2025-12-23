using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Controls
{
    public partial class HierarchicalMenuControlSimple : UserControl
    {
        public static readonly DependencyProperty MenuItemsProperty =
            DependencyProperty.Register(nameof(MenuItems), typeof(ObservableCollection<MenuHierarchyItem>), 
                typeof(HierarchicalMenuControlSimple), new PropertyMetadata(null, OnMenuItemsChanged));

        public ObservableCollection<MenuHierarchyItem>? MenuItems
        {
            get => (ObservableCollection<MenuHierarchyItem>?)GetValue(MenuItemsProperty);
            set => SetValue(MenuItemsProperty, value);
        }

        public HierarchicalMenuControlSimple()
        {
            InitializeComponent();
        }

        private static void OnMenuItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HierarchicalMenuControlSimple control)
            {
                control.MenuItemsControl.ItemsSource = e.NewValue as System.Collections.IEnumerable;
            }
        }
    }
}