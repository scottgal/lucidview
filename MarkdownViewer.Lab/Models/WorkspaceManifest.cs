using System;
using System.Collections.Generic;

namespace MarkdownViewer.Lab.Models;

public sealed record WorkspaceManifest(
    string Name,
    DateTimeOffset CreatedUtc,
    IReadOnlyList<AttachedFolder> AttachedFolders,
    IReadOnlyList<LibraryEntry> Library,
    PersonalCorpusConfig PersonalCorpus);

public sealed record AttachedFolder(string Path, string Include, string Exclude);
public sealed record LibraryEntry(string ContentHash, string Title, string? OriginalUrl, DateTimeOffset IngestedUtc);
public sealed record PersonalCorpusConfig(bool Enabled);
