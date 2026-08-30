using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Storage;

namespace LucidReader.Core.Opml;

/// <summary>
/// The reason one feed did not import. Carries the failing exception's type
/// name and message rather than swallowing them, so a genuine bug (a null
/// reference, a bad SQL binding) is distinguishable from a routine
/// constraint violation instead of both looking like an unremarkable entry
/// in a list of URLs.
/// </summary>
public sealed record OpmlImportFailure(string FeedUrl, string ExceptionType, string Message);

/// <summary>
/// FailedFeeds lists exactly the feeds that did not import, and why, so the
/// UI can tell the user precisely what to retry and a developer can tell a
/// collision apart from a bug, rather than reporting a single opaque count.
///
/// AddedFeedIds is the ids this import actually wrote, in order. The caller
/// wants to fetch what it just imported and nothing else: a sweep over every
/// never-fetched feed in the database would also re-fire feeds that have
/// failed for weeks, bypassing the backoff that is holding them off.
/// </summary>
public readonly record struct OpmlImportResult(
    int FoldersCreated,
    int FeedsAdded,
    int FeedsSkipped,
    int FeedsFailed,
    IReadOnlyList<OpmlImportFailure> FailedFeeds,
    IReadOnlyList<long> AddedFeedIds);

public sealed class OpmlService(FolderRepository folders, FeedRepository feeds)
{
    // Mirrors OpmlReader's own guard. OpmlReader.Parse already enforces this
    // before ImportAsync ever sees an outline tree, but this recursion has
    // its own guard too rather than trusting that invariant to hold forever,
    // since an OpmlOutline tree could in principle be built some other way.
    private const int MaxDepth = 100;

    /// <summary>
    /// Imports subscriptions. Parsing happens first and completely, so a file
    /// that turns out not to be OPML cannot leave a half-imported list behind.
    ///
    /// The schema supports one level of folders. Outlines nested deeper are
    /// flattened onto their outermost folder rather than dropped, because a
    /// feed the user can find in the wrong folder beats a feed that silently
    /// did not import.
    ///
    /// A write failure on one feed or folder does not abort the whole import:
    /// it is counted and reported in the result, and the rest keeps going.
    /// Import is idempotent (an already-subscribed feed is skipped), so
    /// re-running it after a partial failure is always safe and cheap, which
    /// is why this does not attempt to roll every write back into one
    /// transaction. Cancellation is the only thing that aborts outright.
    ///
    /// Every xmlUrl is put through <see cref="FeedUrlPolicy"/> before it is
    /// written. An OPML file is attacker-supplied and every feed it adds gets
    /// fetched unattended afterwards, so an address naming loopback, the
    /// link-local metadata range or a private network is refused here rather
    /// than stored and then requested. A refused address is counted as a
    /// failure with its reason attached, so it is reported to the user
    /// instead of vanishing.
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
        var failed = 0;
        var failures = new List<OpmlImportFailure>();
        var addedIds = new List<long>();

        async Task ImportLevelAsync(IReadOnlyList<OpmlOutline> level, long? folderId, int depth)
        {
            if (depth > MaxDepth)
                throw new OpmlParseException(
                    $"The OPML outline tree is nested more than {MaxDepth} levels deep.");

            foreach (var outline in level)
            {
                ct.ThrowIfCancellationRequested();

                if (outline.FeedUrl is { } feedUrl)
                {
                    if (!existing.Add(feedUrl))
                    {
                        skipped++;
                    }
                    else if (!FeedUrlPolicy.TryValidate(feedUrl, out _, out var refusal))
                    {
                        failed++;
                        failures.Add(new OpmlImportFailure(feedUrl, "RefusedAddress", $"Refused because {refusal}."));
                    }
                    else
                    {
                        try
                        {
                            // A rename comes back into title_override, the
                            // column that owns it. The outline's text is the
                            // displayed name, so when an override is present
                            // the text is that same override and storing it
                            // in feeds.title as well would just plant the
                            // user's name in the publisher's column, to be
                            // overwritten by the first successful refresh.
                            // Title is left null in that case and the
                            // publisher's own title is adopted on that
                            // refresh, with the override still winning in
                            // DisplayTitle.
                            var feedId = await feeds.AddAsync(new Feed
                            {
                                FeedUrl = feedUrl,
                                SiteUrl = outline.SiteUrl,
                                Title = outline.TitleOverride is null ? outline.Title : null,
                                TitleOverride = outline.TitleOverride,
                                FolderId = folderId
                            }, ct);
                            added++;
                            addedIds.Add(feedId);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            failed++;
                            failures.Add(new OpmlImportFailure(feedUrl, ex.GetType().Name, ex.Message));
                        }
                    }

                    // A feed outline can also be a container: some exporters
                    // nest child subscriptions under a parent feed rather than
                    // a plain folder outline. Keep descending so those
                    // children are not silently dropped.
                    if (outline.Children.Count > 0)
                        await ImportLevelAsync(outline.Children, folderId, depth + 1);

                    continue;
                }

                if (outline.Children.Count == 0) continue;

                // Already inside a folder: keep the current one rather than
                // creating a second level the schema cannot represent.
                if (folderId is not null)
                {
                    await ImportLevelAsync(outline.Children, folderId, depth + 1);
                    continue;
                }

                long id;
                if (existingFolders.TryGetValue(outline.Title, out var existingId))
                {
                    id = existingId;
                }
                else
                {
                    try
                    {
                        id = await folders.AddAsync(outline.Title, null, ct);
                        existingFolders[outline.Title] = id;
                        foldersCreated++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Without a folder, the feeds nested under it cannot be
                        // placed anywhere sensible. Count every feed in the
                        // subtree as failed rather than silently reparenting
                        // them to the top level or losing them outright. Each
                        // one keeps the folder-creation exception's own type
                        // and message, since that is the actual reason none
                        // of them could be placed.
                        var exceptionType = ex.GetType().Name;
                        var message = $"Folder \"{outline.Title}\" could not be created: {ex.Message}";
                        foreach (var url in CollectFeedUrls(outline.Children))
                        {
                            failed++;
                            failures.Add(new OpmlImportFailure(url, exceptionType, message));
                        }
                        continue;
                    }
                }

                await ImportLevelAsync(outline.Children, id, depth + 1);
            }
        }

        await ImportLevelAsync(outlines, null, 0);

        return new OpmlImportResult(foldersCreated, added, skipped, failed, failures, addedIds);
    }

    private static IEnumerable<string> CollectFeedUrls(IReadOnlyList<OpmlOutline> outlines)
    {
        foreach (var outline in outlines)
        {
            if (outline.FeedUrl is { } url) yield return url;
            foreach (var nested in CollectFeedUrls(outline.Children))
                yield return nested;
        }
    }

    public async Task<string> ExportAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var allFolders = await folders.GetAllAsync(ct);
        var allFeeds = await feeds.GetAllAsync(ct);

        // text carries the displayed name so other readers show what this one
        // shows; the override is carried separately so importing this file
        // back can put it in the column that owns it rather than in
        // feeds.title, where the next refresh would overwrite it.
        static OpmlOutline ToOutline(Feed feed) =>
            new(feed.DisplayTitle, feed.FeedUrl, feed.SiteUrl, [], feed.TitleOverride);

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

        return OpmlWriter.Write(outlines, "mylo subscriptions", nowUtc);
    }
}
