namespace LucidReader.Core.Model;

public enum ContentSource
{
    Feed = 0,
    Extracted = 1
}

public enum OfflineState
{
    None = 0,
    Pending = 1,
    Downloaded = 2,
    Failed = 3
}
