using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.Controls
{
    public partial class NavigationDropdownControl : UserControl, INotifyPropertyChanged
    {
        #region Dependency Properties

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable),
                typeof(NavigationDropdownControl), new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object),
                typeof(NavigationDropdownControl), new PropertyMetadata(null, OnSelectedItemChanged));

        public static readonly DependencyProperty EmptyTextProperty =
            DependencyProperty.Register(nameof(EmptyText), typeof(string),
                typeof(NavigationDropdownControl), new PropertyMetadata("Select an item..."));

        public static readonly DependencyProperty SelectedItemDisplayPathProperty =
            DependencyProperty.Register(nameof(SelectedItemDisplayPath), typeof(string),
                typeof(NavigationDropdownControl), new PropertyMetadata("ToString"));

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(nameof(Command), typeof(ICommand),
                typeof(NavigationDropdownControl), new PropertyMetadata(null));

        #endregion

        #region Properties

        public IEnumerable ItemsSource
        {
            get => (IEnumerable)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public object SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public string EmptyText
        {
            get => (string)GetValue(EmptyTextProperty);
            set => SetValue(EmptyTextProperty, value);
        }

        public string SelectedItemDisplayPath
        {
            get => (string)GetValue(SelectedItemDisplayPathProperty);
            set => SetValue(SelectedItemDisplayPathProperty, value);
        }

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        private bool _isDropdownOpen;
        public bool IsDropdownOpen
        {
            get => _isDropdownOpen;
            set
            {
                if (SetProperty(ref _isDropdownOpen, value))
                {
                    OnPropertyChanged(nameof(IsDropdownOpen));
                }
            }
        }

        private string _selectedItemDisplayText = "";
        public string SelectedItemDisplayText
        {
            get => _selectedItemDisplayText;
            private set => SetProperty(ref _selectedItemDisplayText, value);
        }

        #endregion

        #region Commands

        public ICommand SelectItemCommand { get; }

        #endregion

        public NavigationDropdownControl()
        {
            InitializeComponent();
            SelectItemCommand = new RelayCommand<object?>(OnSelectItem);
        }

        #region Event Handlers

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NavigationDropdownControl control)
            {
                control.UpdateSelectedItemDisplayText();
            }
        }

        private void OnSelectItem(object? item)
        {
            if (item == null) return;
            
            SelectedItem = item;
            IsDropdownOpen = false;
            
            // Execute parent command if provided
            Command?.Execute(item);
        }

        #endregion

        #region Helper Methods

        private void UpdateSelectedItemDisplayText()
        {
            if (SelectedItem == null)
            {
                SelectedItemDisplayText = "";
                return;
            }

            try
            {
                // Use reflection to get the display property value
                if (SelectedItemDisplayPath != "ToString")
                {
                    var property = SelectedItem.GetType().GetProperty(SelectedItemDisplayPath);
                    if (property != null)
                    {
                        SelectedItemDisplayText = property.GetValue(SelectedItem)?.ToString() ?? "";
                        return;
                    }
                }

                // Fallback to ToString()
                SelectedItemDisplayText = SelectedItem.ToString() ?? "";
            }
            catch
            {
                SelectedItemDisplayText = SelectedItem.ToString() ?? "";
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}