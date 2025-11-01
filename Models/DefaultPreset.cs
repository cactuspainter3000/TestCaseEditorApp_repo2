namespace TestCaseEditorApp.MVVM.Models;

public sealed class DefaultPreset
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> EnabledKeys { get; init; } = Array.Empty<string>();
    public string[] OnKeys { get; set; } = System.Array.Empty<string>();
}

//public sealed class DefaultPreset
//{
//    public string Name { get; set; } = "";
//    public string[] OnKeys { get; set; } = System.Array.Empty<string>();
//}