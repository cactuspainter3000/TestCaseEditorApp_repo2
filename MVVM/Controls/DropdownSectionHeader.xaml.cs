using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Controls
{
    public partial class DropdownSectionHeader : UserControl
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(string), typeof(DropdownSectionHeader), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register("Header", typeof(string), typeof(DropdownSectionHeader), new PropertyMetadata(string.Empty));

        public string Icon
        {
            get { return (string)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public string Header
        {
            get { return (string)GetValue(HeaderProperty); }
            set { SetValue(HeaderProperty, value); }
        }

        public DropdownSectionHeader()
        {
            InitializeComponent();
        }
    }
}