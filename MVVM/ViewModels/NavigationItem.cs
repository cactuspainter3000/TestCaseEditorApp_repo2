namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Small typed navigation item for bindings and templating.
    public class NavigationItem
    {
        public NavigationItem(string id, string title, string? icon = null)
        {
            Id = id;
            Title = title;
            Icon = icon;
        }

        // A unique id or route key (used by navigation logic)
        public string Id { get; }

        // Display text
        public string Title { get; }

        // Optional icon identifier (font glyph name or image path)
        public string? Icon { get; }

        // Optional payload or metadata can be added here later
    }
}
