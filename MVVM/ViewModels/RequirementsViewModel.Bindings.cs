using CommunityToolkit.Mvvm.ComponentModel;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Minimal partial to add HasMeta used by RequirementsView.xaml bindings.
    public partial class RequirementsViewModel : ObservableObject
    {
        // HasMeta used by the view (Border visibility / style)
        [ObservableProperty]
        private bool hasMeta;
    }
}