namespace TestCaseEditorApp.MVVM.Models;

public sealed class DefaultsBlock
{
    public int Version { get; set; } = 1;
    public DefaultsCatalogDto Catalog { get; set; } = new();
    public DefaultsState State { get; set; } = new();
}

public sealed class DefaultsCatalogDto
{
    public List<DefaultItem> Items { get; set; } = new();
    public List<DefaultPreset> Presets { get; set; } = new();
}

public sealed class DefaultsState
{
    public List<string> EnabledKeys { get; set; } = new();
    public string? SelectedPreset { get; set; }
}


