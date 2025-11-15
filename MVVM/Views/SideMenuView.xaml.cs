using System.Windows.Controls;
using TestCaseEditorApp.Controls;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class SideMenuView : UserControl
    {
        public SideMenuView()
        {
            InitializeComponent();
        }

        private void StepsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // close any open DropDownButton popup when selection changes
            DropDownManager.CloseOpenPopup();
        }
    }
}