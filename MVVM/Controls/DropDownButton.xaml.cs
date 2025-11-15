using System.Windows;
using System.Windows.Controls;

namespace TestCaseEditorApp.Controls
{
    public partial class DropDownButton : UserControl
    {
        public DropDownButton()
        {
            InitializeComponent();
        }

        private void PART_Button_Click(object sender, RoutedEventArgs e)
        {
            var cm = PART_Button.ContextMenu;
            if (cm != null)
            {
                cm.PlacementTarget = PART_Button;
                cm.IsOpen = true;
            }
        }
    }
}