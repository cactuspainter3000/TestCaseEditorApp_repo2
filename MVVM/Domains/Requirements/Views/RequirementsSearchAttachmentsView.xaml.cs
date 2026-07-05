using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    /// <summary>
    /// Interaction logic for RequirementsSearchAttachmentsView.xaml
    /// View for searching and extracting requirements from Jama Connect attachments.
    /// Follows Architectural Guide AI patterns for Requirements domain views.
    /// DataContext is provided by parent ViewModel via DataTemplate mapping.
    /// </summary>
    public partial class RequirementsSearchAttachmentsView : UserControl
    {
        public RequirementsSearchAttachmentsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is RequirementsSearchAttachmentsViewModel newViewModel)
            {
                // Refresh project state when view becomes active (async fire-and-forget)
                _ = newViewModel.RefreshCurrentProjectStateAsync();
            }
        }
    }
}