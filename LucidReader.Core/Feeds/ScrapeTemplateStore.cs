using AngleSharp.Dom;
using StyloExtract.Abstractions;
using StyloExtract.Fingerprint;
using StyloExtract.Heuristics;
using StyloExtract.Templates;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Where a scraped feed's learned template lives between refreshes.
///
/// This is StyloExtract's own template index, in its own file beside
/// reader.db. Its own file on purpose: reader.db carries the user's
/// subscriptions, their read state and their starred items, it is migrated
/// forward-only, and it is the file a user would think to back up. A cache of
/// selectors learned from somebody else's markup is none of those things. It
/// can be deleted at any moment and the only cost is that the next refresh of
/// each scraped feed runs the detector again, which is what every refresh did
/// before this existed.
///
/// Lookup is by structural fingerprint under a per-host key, which is what the
/// index was built for and what makes a redesign miss rather than match: a
/// page whose shape has changed does not fingerprint like the page the
/// template was learned from, so the store simply has nothing for it and the
/// caller learns it again.
///
/// The host key is derived from a fixed key rather than a random one. The
/// hasher's default generates a new key per process, which would make every
/// stored template unreachable the moment the app restarts, which is every
/// template mylo ever stores.
/// </summary>
public sealed class ScrapeTemplateStore : IAsyncDisposable
{
    /// <summary>
    /// The file name under the profile directory. Deleting it is safe.
    /// </summary>
    public const string FileName = "scrape-templates.db";

    /// <summary>
    /// Fixed key for the host hash, so a host hashes to the same bytes in
    /// every process on every machine. This is not a secret and is not
    /// protecting anything: the index hashes hosts so a shared corpus does not
    /// carry a plain list of what somebody reads, and mylo's copy is local,
    /// single-user and holds hosts the user typed in themselves.
    /// </summary>
    private static readonly byte[] HostKey =
        System.Text.Encoding.UTF8.GetBytes("mylo-scrape-template-host-key-v1");

    /// <summary>
    /// How alike two fetches of the same page have to fingerprint before the
    /// stored template is reused. The index's own default; a page that has had
    /// its list re-templated falls below it and a page that has merely
    /// published a new entry does not.
    /// </summary>
    private const double FastPathThreshold = 0.85;
    private const double SlowPathThreshold = 0.75;

    private readonly Lazy<SqliteTemplateIndex> _index;
    private readonly Lazy<HostHasher> _hosts;
    private readonly Lazy<IStructuralFingerprinter> _fingerprinter;

    /// <summary>
    /// Nothing is opened and no file is created until the first scraped feed
    /// is refreshed. Most people subscribe to feeds that exist, and a reader
    /// that creates a database at startup for a feature that user will never
    /// reach has spent their startup time and left a file they have to wonder
    /// about, both for nothing.
    /// </summary>
    public ScrapeTemplateStore(string databasePath)
    {
        _index = new Lazy<SqliteTemplateIndex>(() =>
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            return new SqliteTemplateIndex($"Data Source={databasePath}");
        });
        _hosts = new Lazy<HostHasher>(() => new HostHasher(HostKey));
        _fingerprinter = new Lazy<IStructuralFingerprinter>(NewFingerprinter);
    }

    public static IStructuralFingerprinter NewFingerprinter()
    {
        var noise = ClassNoiseFilter.LoadFromEmbeddedResource();
        return new StructuralFingerprinter(
            new ShingleGenerator(noise, 3),
            new MinHashSketcher(128),
            new LshBander(16, 8),
            new AnchorPathFingerprinter(noise, new MinHashSketcher(128)),
            new PqGramExtractor());
    }

    /// <summary>
    /// The template learned for this page, or null when there is none for this
    /// host or when the page no longer looks like the one it was learned from.
    /// </summary>
    public async Task<LearnedExtractor?> FindAsync(Uri pageUri, IDocument document, CancellationToken ct)
    {
        var host = _hosts.Value.Hash(pageUri.Host);
        var fingerprint = _fingerprinter.Value.Compute(document);
        var index = _index.Value;

        var fast = await index.ProbeFastPathAsync(host, fingerprint, FastPathThreshold, ct);
        if (fast is { } hit) return await index.GetExtractorAsync(hit, ct);

        var slow = await index.ProbeSlowPathAsync(host, fingerprint, SlowPathThreshold, ct);
        return slow is { } near ? await index.GetExtractorAsync(near.TemplateId, ct) : null;
    }

    /// <summary>
    /// Keep this template as the one for the page's host, replacing whatever
    /// was there.
    /// </summary>
    public async Task StoreAsync(Uri pageUri, IDocument document, LearnedExtractor extractor, CancellationToken ct)
    {
        var host = _hosts.Value.Hash(pageUri.Host);
        var fingerprint = _fingerprinter.Value.Compute(document);
        var stored = await _index.Value.RegisterAsync(host, fingerprint, extractor, ct);

        // One template per host, and this is what keeps it to one. Registering
        // always inserts, and mylo re-registers whenever a stored template
        // stopped matching, so a page whose shape wanders enough to miss the
        // probe would otherwise add a row on every poll, forever, and slow the
        // probe down as it went.
        await _index.Value.PruneHostAsync(host, stored, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hosts.IsValueCreated) await _hosts.Value.DisposeAsync();
        if (_index.IsValueCreated) await _index.Value.DisposeAsync();
    }
}
