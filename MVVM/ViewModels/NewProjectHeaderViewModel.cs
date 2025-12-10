using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// Simple header ViewModel that just displays "Create New Project" title.
    /// The actual workflow interface remains in the main content area.
    /// </summary>
    public partial class NewProjectHeaderViewModel : ObservableObject
    {
        [ObservableProperty] private string title = "Create New Project";
        [ObservableProperty] private string subtitle = "Setting up your new project workspace";
    }
}