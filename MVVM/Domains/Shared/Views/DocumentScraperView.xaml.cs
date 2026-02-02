using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Domains.Shared.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Shared.Views
{
    /// <summary>
    /// Self-contained Document Scraper view that can be embedded as a tab anywhere.
    /// Automatically gets its ViewModel through DI when parent view creates it.
    /// </summary>
    public partial class DocumentScraperView : UserControl
    {
        public DocumentScraperView()
        {
            InitializeComponent();
            
            // Auto-resolve ViewModel through DI container for self-contained operation
            DataContext = App.ServiceProvider?.GetService(typeof(DocumentScraperViewModel));
        }
    }
}