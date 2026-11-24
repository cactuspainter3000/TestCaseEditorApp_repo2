namespace TestCaseEditorApp.Services;

public sealed record JamaDraftOption(int Id, string Name, int? ChildItemType = null)
{
    public override string ToString() => $"{Name} (ID {Id})";
}

public sealed record JamaDraftOptions(
    IReadOnlyList<JamaDraftOption> ItemTypes,
    IReadOnlyList<JamaDraftOption> Containers,
    IReadOnlyList<JamaDraftOption> RelationshipTypes);
