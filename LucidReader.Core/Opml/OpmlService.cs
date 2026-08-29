using LucidReader.Core.Model;
using LucidReader.Core.Storage;

namespace LucidReader.Core.Opml;

public readonly record struct OpmlImportResult(int FoldersCreated, int FeedsAdded, int FeedsSkipped);

public sealed class OpmlService(FolderRepository folders, FeedRepository feeds)
{
    /// <summary>
    /// Imports subscriptions. Parsing happens first and completely, so a file
    /// that turns out not to be OPML cannot leave a half-imported list behind.
    ///
    /// The schema supports one level of folders. Outlines nested deeper are
    /// flattened onto their outermost folder rather than dropped, because a
    /// feed the user can find in the wrong folder beats a feed that silently
    /// did not import.
    /// </summary>
    public async Task<OpmlImportResult> ImportAsync(string opml, CancellationToken ct = default)
    {
        var outlines = OpmlReader.Parse(opml);

        var existing = (await feeds.GetAllAsync(ct))
            .Select(f => f.FeedUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingFolders = (await folders.GetAllAsync(ct))
            .ToDictionary(f => f.Name, f => f.Id, StringComparer.OrdinalIgnoreCase);

        var foldersCreated = 0;
        var added = 0;
        var skipped = 0;

        async Task ImportLevelAsync(IReadOnlyList<OpmlOutline> level, long? folderId, string? folderName)
        {
            foreach (var outline in level)
            {
                if (outline.FeedUrl is { } feedUrl)
                {
                    if (!existing.Add(feedUrl))
                    {
                        skipped++;
                        continue;
                    }

                    await feeds.AddAsync(new Feed
                    {
                        FeedUrl = feedUrl,
                        SiteUrl = outline.SiteUrl,
                        Title = outline.Title,
                        FolderId = folderId
                    }, ct);
                    added++;
                    continue;
                }

                if (outline.Children.Count == 0) continue;

                // Already inside a folder: keep the current one rather than
                // creating a second level the schema cannot represent.
                if (folderId is not null)
                {
                    await ImportLevelAsync(outline.Children, folderId, folderName);
                    continue;
                }

                if (!existingFolders.TryGetValue(outline.Title, out var id))
                {
                    id = await folders.AddAsync(outline.Title, null, ct);
                    existingFolders[outline.Title] = id;
                    foldersCreated++;
                }

                await ImportLevelAsync(outline.Children, id, outline.Title);
            }
        }

        await ImportLevelAsync(outlines, null, null);

        return new OpmlImportResult(foldersCreated, added, skipped);
    }

    public async Task<string> ExportAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var allFolders = await folders.GetAllAsync(ct);
        var allFeeds = await feeds.GetAllAsync(ct);

        static OpmlOutline ToOutline(Feed feed) =>
            new(feed.DisplayTitle, feed.FeedUrl, feed.SiteUrl, []);

        var outlines = new List<OpmlOutline>();

        foreach (var folder in allFolders)
        {
            var children = allFeeds
                .Where(f => f.FolderId == folder.Id)
                .Select(ToOutline)
                .ToList();

            outlines.Add(new OpmlOutline(folder.Name, null, null, children));
        }

        outlines.AddRange(allFeeds.Where(f => f.FolderId is null).Select(ToOutline));

        return OpmlWriter.Write(outlines, "lucidREADER subscriptions", nowUtc);
    }
}
