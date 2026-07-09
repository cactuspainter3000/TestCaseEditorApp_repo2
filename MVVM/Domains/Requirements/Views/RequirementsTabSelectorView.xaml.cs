using System.Windows.Controls;
using System.Windows.Threading;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    /// <summary>
    /// Requirements Tab Selector View - displays tabs for Main, Cleanup, and Attachments workspaces
    /// DO NOT auto-load tabs - only create ViewModels when user explicitly clicks a tab
    /// </summary>
    public partial class RequirementsTabSelectorView : UserControl
    {
        public RequirementsTabSelectorView()
        {
            InitializeComponent();
        }
    }
}
