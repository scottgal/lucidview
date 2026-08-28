namespace LucidReader.Core.Model;

public sealed record Folder
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
    public long? ParentId { get; init; }
}
