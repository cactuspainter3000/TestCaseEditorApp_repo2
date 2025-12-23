using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Controls
{
    public partial class AnimatedDropdownSection : UserControl
    {
        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register("HeaderText", typeof(string), typeof(AnimatedDropdownSection), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register("IsExpanded", typeof(bool), typeof(AnimatedDropdownSection), new PropertyMetadata(false));

        public static readonly DependencyProperty DropdownContentProperty =
            DependencyProperty.Register("DropdownContent", typeof(object), typeof(AnimatedDropdownSection), new PropertyMetadata(null));

        public static readonly DependencyProperty IsMainMenuItemProperty =
            DependencyProperty.Register("IsMainMenuItem", typeof(bool), typeof(AnimatedDropdownSection), new PropertyMetadata(true));

        public string HeaderText
        {
            get { return (string)GetValue(HeaderTextProperty); }
            set { SetValue(HeaderTextProperty, value); }
        }

        public bool IsMainMenuItem
        {
            get { return (bool)GetValue(IsMainMenuItemProperty); }
            set { SetValue(IsMainMenuItemProperty, value); }
        }

        public bool IsExpanded
        {
            get { return (bool)GetValue(IsExpandedProperty); }
            set { SetValue(IsExpandedProperty, value); }
        }

        public object DropdownContent
        {
            get { return GetValue(DropdownContentProperty); }
            set { SetValue(DropdownContentProperty, value); }
        }

        public AnimatedDropdownSection()
        {
            InitializeComponent();
        }
    }
}