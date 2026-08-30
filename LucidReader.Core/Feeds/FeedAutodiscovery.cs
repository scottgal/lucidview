using System.Text;
using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

/// <summary>
/// One feed the user could subscribe to.
///
/// IsAlternate marks a feed that carries the same articles as another feed in
/// the same result, in a different format - the RSS half of an RSS-and-Atom
/// pair, say. It is still offered, because a user may have a reason to want
/// that particular format, but the dialog leaves it unticked so the default
/// action does not create two subscriptions to one publication.
/// AlternateOfUrl names the feed it duplicates, so the dialog can say so
/// rather than leaving an unticked row unexplained.
/// </summary>
public readonly record struct DiscoveredFeed(
    string FeedUrl,
    string? Title,
    string? IconUrl,
    bool IsAlternate = false,
    string? AlternateOfUrl = null);

/// <summary>
/// Turns whatever the user pasted into feed URLs worth subscribing to.
///
/// Uses a regex over the head rather than a full HTML parser on purpose: the
/// only thing being read is link elements, and pulling AngleSharp into this
/// path to do it would be a heavier dependency than the job deserves. A missed
/// link costs the user one manual paste of the feed URL.
/// </summary>
public sealed partial class FeedAutodiscovery(HttpClient http)
{
    private const int MaxDiscoveryBytes = 8 * 1024 * 1024;
    private const int ReadBufferSize = 8192;

    /// <summary>
    /// How many extra requests one call to DiscoverAsync is allowed to make
    /// beyond the single fetch of the page itself. Both fallback stages draw
    /// on the same allowance, so a page linking fifty feed-shaped anchors
    /// still costs a bounded number of round trips.
    /// </summary>
    private const int MaxProbeRequests = 8;

    private static readonly string[] FeedMediaTypes =
    [
        "application/rss+xml",
        "application/atom+xml",
        "application/rdf+xml",
        "text/xml",
        "application/xml"
    ];

    /// <summary>
    /// Paths tried only when neither a link element nor an anchor produced a
    /// working feed. Kept short and conventional on purpose: each entry is a
    /// request against a host that has given no indication it publishes a
    /// feed at all.
    /// </summary>
    private static readonly string[] WellKnownFeedPaths =
    [
        "/rss",
        "/atom",
        "/feed",
        "/feed.xml",
        "/rss.xml",
        "/atom.xml",
        "/index.xml"
    ];

    /// <summary>
    /// Final path segments that look like a feed. The bare names cover the
    /// extensionless routes most static site generators and blog engines use;
    /// the suffixed forms cover the same names served as files.
    /// </summary>
    private static readonly string[] FeedPathNames = BuildFeedPathNames();

    private static string[] BuildFeedPathNames()
    {
        string[] stems = ["rss", "atom", "feed"];
        string[] suffixes = ["", ".xml", ".rss", ".atom"];
        return stems.SelectMany(stem => suffixes.Select(suffix => stem + suffix)).ToArray();
    }

    public async Task<IReadOnlyList<DiscoveredFeed>> DiscoverAsync(
        string inputUrl,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(inputUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Array.Empty<DiscoveredFeed>();

        string body;
        Uri effectiveUri;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "application/atom+xml, application/rss+xml, text/html;q=0.9, */*;q=0.8");

            // ResponseHeadersRead, not the default ResponseContentRead: the
            // latter buffers the whole body before this method gets a chance
            // to look at it, which is the same unbounded-buffering gap
            // ArticleFetcher and FeedFetcher had to close. This class is the
            // most exposed of the three - it fires straight off a string the
            // user pasted, with no prior evidence the target is even
            // feed-shaped - so it needs the same bound.
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<DiscoveredFeed>();

            // Redirects (bare domain to www, http upgraded to https, etc.)
            // mean the URL we end up reading is not always the one we asked
            // for. Relative hrefs in the page, and the feed's own URL when
            // the page IS the feed, must resolve against wherever the
            // response actually came from, or a relative link resolves
            // against the wrong host and produces a subscription that 404s.
            // A redirect to a non-http(s) scheme is not re-checked here: the
            // BCL's handler pipeline refuses to follow one before this code
            // ever sees a response, so that protection is implicit rather
            // than something this method needs to duplicate.
            effectiveUri = response.RequestMessage?.RequestUri ?? uri;

            // Cheap early rejection when the server declares its length up
            // front. Most HTML is served chunked, which never sets
            // Content-Length, so this alone is not the real cap - see
            // ReadBoundedAsync for the one that actually is.
            if (response.Content.Headers.ContentLength > MaxDiscoveryBytes)
                return Array.Empty<DiscoveredFeed>();

            var bounded = await ReadBoundedAsync(response.Content, ct);
            if (bounded is null) return Array.Empty<DiscoveredFeed>();
            body = bounded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<DiscoveredFeed>();
        }

        // Already a feed? Then there is nothing to discover. There is also no
        // page here for SiteMetadataExtractor to read, so the icon is a bare
        // /favicon.ico guess at the feed URL's own host - a URL to record,
        // not a fetch to perform; whether it actually exists is the
        // rendering layer's problem.
        var parser = new FeedParser();
        if (parser.CanParse(body))
        {
            // effectiveUri is where the response actually came from, which is
            // not necessarily where we aimed: a redirect chose it. Everything
            // this method returns is offered to the user pre-ticked and then
            // fetched unattended forever after, so no address leaves this
            // method without passing FeedUrlPolicy first.
            if (!FeedUrlPolicy.IsAllowed(effectiveUri.ToString()))
                return Array.Empty<DiscoveredFeed>();

            string? title = null;
            try { title = parser.Parse(body, effectiveUri).Title; }
            catch (FeedParseException) { }
            return [new DiscoveredFeed(effectiveUri.ToString(), title, FaviconGuess(effectiveUri))];
        }

        // The page body is already in hand for feed-link discovery, so the
        // same body is read again in memory (no second fetch) for the site
        // icon: FeedAutodiscovery downloads this page anyway, and the
        // favicon link is in the same <head>.
        var siteMetadata = SiteMetadataExtractor.Extract(body, effectiveUri);
        var siteIcon = siteMetadata.IconUrl ?? FaviconGuess(effectiveUri);

        var found = new List<DiscoveredFeed>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The type attribute each link element declared, kept so
        // MarkAlternatesAsync can tell the Atom half of a pair from the RSS
        // half without having to guess from the address.
        var declaredTypes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LinkTagPattern().Matches(body))
        {
            var tag = match.Value;

            if (!HasAlternateToken(AttributeValue(tag, "rel")))
                continue;

            var type = AttributeValue(tag, "type");
            if (!FeedMediaTypes.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            var href = AttributeValue(tag, "href");
            if (href.Length == 0) continue;
            if (!Uri.TryCreate(effectiveUri, href, out var absolute)) continue;

            // The href came out of a downloaded page, so it gets the same gate
            // the anchor and well-known stages apply through
            // ConfirmCandidatesAsync. A scheme check alone is not that gate: it
            // would still hand back http://169.254.169.254/ or an address
            // carrying embedded credentials, pre-ticked in the chooser, stored,
            // and then fetched on every scheduler tick from then on.
            if (!FeedUrlPolicy.IsAllowed(absolute.ToString())) continue;
            if (!seen.Add(absolute.ToString())) continue;

            var linkTitle = AttributeValue(tag, "title");
            declaredTypes[absolute.ToString()] = type;
            found.Add(new DiscoveredFeed(
                absolute.ToString(),
                linkTitle.Length > 0 ? linkTitle : null,
                siteIcon));
        }

        if (found.Count > 0) return await MarkAlternatesAsync(found, declaredTypes, ct);

        // Plenty of sites never declare their feed in the head at all and
        // link it only from the page furniture, as an ordinary anchor in a
        // footer or a nav bar. Nothing about such an anchor says "feed"
        // except its address, so each candidate has to be fetched and shown
        // to parse before it is offered: a site with a catch-all route
        // answers 200 with HTML for any path at all, and offering those would
        // subscribe the user to something that is not a feed.
        var anchorCandidates = AnchorFeedCandidates(body, effectiveUri, seen);
        var confirmed = await ConfirmCandidatesAsync(anchorCandidates, siteIcon, seen, ct);
        if (confirmed.Count > 0) return confirmed;

        // Last resort, and only because the two stages above found nothing:
        // guess at the conventional addresses. Every one of these is a
        // request to a host that has not said it has a feed, which is why
        // this runs last and against a short fixed list.
        var wellKnown = WellKnownCandidates(effectiveUri, seen);
        return await ConfirmCandidatesAsync(wellKnown, siteIcon, seen, ct);
    }

    /// <summary>
    /// Same-host anchors whose path looks like a feed address, resolved the
    /// same way the link element scan resolves its hrefs. Restricted to the
    /// page's own host because an anchor to somebody else's site is a link to
    /// their site, not a feed this page publishes.
    /// </summary>
    private static List<Uri> AnchorFeedCandidates(
        string body, Uri effectiveUri, HashSet<string> seen)
    {
        var candidates = new List<Uri>();
        var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AnchorTagPattern().Matches(body))
        {
            var href = AttributeValue(match.Value, "href");
            if (href.Length == 0) continue;
            if (!Uri.TryCreate(effectiveUri, href, out var absolute)) continue;
            if (!absolute.Host.Equals(effectiveUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
            if (!LooksLikeFeedPath(absolute)) continue;
            if (seen.Contains(absolute.ToString())) continue;
            if (!proposed.Add(absolute.ToString())) continue;

            candidates.Add(absolute);
            if (candidates.Count == MaxProbeRequests) break;
        }

        return candidates;
    }

    private static List<Uri> WellKnownCandidates(Uri effectiveUri, HashSet<string> seen)
    {
        var candidates = new List<Uri>();
        var proposed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in WellKnownFeedPaths)
        {
            if (!Uri.TryCreate(effectiveUri, path, out var absolute)) continue;
            if (seen.Contains(absolute.ToString())) continue;
            if (!proposed.Add(absolute.ToString())) continue;

            candidates.Add(absolute);
            if (candidates.Count == MaxProbeRequests) break;
        }

        return candidates;
    }

    private static bool LooksLikeFeedPath(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        var segment = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        return FeedPathNames.Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches every candidate at once and keeps the ones that turn out to be
    /// feeds, in the order the candidates were proposed rather than the order
    /// the responses happened to arrive.
    ///
    /// Every address goes through FeedUrlPolicy first. These URLs came out of
    /// a downloaded document or out of a host name the user typed, so this is
    /// exactly the "follow a URL somebody else chose" path the policy exists
    /// to keep away from loopback, link-local and private addresses.
    /// </summary>
    private async Task<List<DiscoveredFeed>> ConfirmCandidatesAsync(
        List<Uri> candidates,
        string? siteIcon,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var allowed = candidates.Where(c => FeedUrlPolicy.IsAllowed(c.ToString())).ToList();
        if (allowed.Count == 0) return [];

        var probes = allowed.Select(c => ProbeAsync(c, ct)).ToArray();
        var results = await Task.WhenAll(probes);

        var confirmed = new List<DiscoveredFeed>();
        var kept = new List<FeedAlternateCandidate>();
        var bodies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in results)
        {
            if (result is null) continue;
            if (!seen.Add(result.Url)) continue;

            // A site that serves the same feed at more than one address (both
            // /rss and /rss.xml, say) must not appear twice in the list the
            // user is asked to choose from, and the addresses alone cannot
            // tell us that. Comparing the bodies can.
            if (!bodies.Add(result.Body)) continue;

            confirmed.Add(new DiscoveredFeed(result.Url, result.Title, siteIcon));
            kept.Add(new FeedAlternateCandidate(
                result.Url, null, result.SiteUrl, result.ItemLinks));
        }

        // Byte-identical bodies were already collapsed above. This catches the
        // other shape: two addresses serving the SAME ARTICLES in different
        // formats, whose bodies share nothing. No extra requests are needed,
        // since these candidates have already been fetched and parsed.
        var verdicts = FeedAlternates.Classify(kept)
            .ToDictionary(v => v.FeedUrl, StringComparer.Ordinal);

        return confirmed
            .Select(f => verdicts.TryGetValue(f.FeedUrl, out var verdict) && verdict.IsAlternate
                ? f with { IsAlternate = true, AlternateOfUrl = verdict.AlternateOfUrl }
                : f)
            .ToList();
    }

    /// <summary>
    /// Decides which of several discovered feeds are alternate formats of one
    /// another, and returns the same list with those marked.
    ///
    /// The feeds are fetched to do it. That is a real cost on a path that
    /// previously made one request, so it is bounded three ways: it only runs
    /// when more than one feed was found, it never fetches more than
    /// MaxProbeRequests of them, and a fetch that fails costs nothing because
    /// FeedAlternates falls back to weaker evidence for any candidate it has
    /// no contents for. The cost buys the one thing address shapes cannot
    /// give: proof that two feeds carry the same articles. Subscribing to both
    /// halves of a pair stores every article twice, forever, on every refresh,
    /// which is a much larger cost than a handful of requests to a site the
    /// user is in the act of subscribing to anyway.
    /// </summary>
    private async Task<List<DiscoveredFeed>> MarkAlternatesAsync(
        List<DiscoveredFeed> found,
        IReadOnlyDictionary<string, string?> declaredTypes,
        CancellationToken ct)
    {
        if (found.Count < 2) return found;

        var probeTargets = found
            .Take(MaxProbeRequests)
            .Select(f => Uri.TryCreate(f.FeedUrl, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null && FeedUrlPolicy.IsAllowed(uri.ToString()))
            .Select(uri => uri!)
            .ToList();

        var probes = await Task.WhenAll(probeTargets.Select(uri => ProbeAsync(uri, ct)));

        var identities = new Dictionary<string, ProbeResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var probe in probes)
            if (probe is not null) identities[probe.Url] = probe;

        var candidates = found
            .Select(f => new FeedAlternateCandidate(
                f.FeedUrl,
                declaredTypes.GetValueOrDefault(f.FeedUrl),
                identities.GetValueOrDefault(f.FeedUrl)?.SiteUrl,
                identities.GetValueOrDefault(f.FeedUrl)?.ItemLinks))
            .ToList();

        var verdicts = FeedAlternates.Classify(candidates)
            .ToDictionary(v => v.FeedUrl, StringComparer.Ordinal);

        return found
            .Select(f => verdicts.TryGetValue(f.FeedUrl, out var verdict) && verdict.IsAlternate
                ? f with { IsAlternate = true, AlternateOfUrl = verdict.AlternateOfUrl }
                : f)
            .ToList();
    }

    private sealed record ProbeResult(
        string Url,
        string? Title,
        string Body,
        string? SiteUrl,
        IReadOnlyList<string> ItemLinks);

    private async Task<ProbeResult?> ProbeAsync(Uri candidate, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation(
                "Accept", "application/atom+xml, application/rss+xml, application/xml;q=0.9, */*;q=0.5");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            if (response.Content.Headers.ContentLength > MaxDiscoveryBytes) return null;

            var body = await ReadBoundedAsync(response.Content, ct);
            if (body is null) return null;

            var parser = new FeedParser();
            if (!parser.CanParse(body)) return null;

            // The recorded URL is the one the response came from, so a redirect
            // means the address about to be stored is not the address the
            // policy was applied to above. Check the one actually being kept.
            var url = (response.RequestMessage?.RequestUri ?? candidate).ToString();
            if (!FeedUrlPolicy.IsAllowed(url)) return null;

            string? title = null;
            string? siteUrl = null;
            IReadOnlyList<string> itemLinks = [];
            try
            {
                var feed = parser.Parse(body, response.RequestMessage?.RequestUri ?? candidate);
                title = feed.Title;
                siteUrl = feed.SiteUrl;
                itemLinks = feed.Items
                    .Select(i => i.Link)
                    .Where(link => !string.IsNullOrWhiteSpace(link))
                    .Select(link => link!)
                    .ToList();
            }
            catch (FeedParseException) { }

            return new ProbeResult(url, title, body, siteUrl, itemLinks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FaviconGuess(Uri uri) => $"{uri.Scheme}://{uri.Authority}/favicon.ico";

    /// <summary>
    /// Reads the body a chunk at a time and abandons the read the moment the
    /// total exceeds MaxDiscoveryBytes, rather than calling
    /// HttpContent.ReadAsStringAsync and trusting Content-Length: a chunked
    /// response never sets that header, so the fast-path check above is
    /// skipped entirely and an unbounded read would buffer whatever the
    /// server sends. Mirrors ArticleFetcher.ReadBoundedAsync.
    /// </summary>
    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferSize];

        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxDiscoveryBytes) return null;
        }

        var encoding = GetEncoding(content.Headers.ContentType?.CharSet) ?? Encoding.UTF8;
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Encoding? GetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return null;
        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// "alternate" must be a whitespace-separated token in rel, not merely a
    /// substring: rel is a space-separated list per the HTML spec (a link can
    /// legitimately be rel="alternate home"), and a substring check would
    /// also false-positive on any token that happens to contain the word.
    /// </summary>
    private static bool HasAlternateToken(string rel) =>
        HtmlAttributeParsing.HasToken(rel, "alternate");

    private static string AttributeValue(string tag, string attribute) =>
        HtmlAttributeParsing.AttributeValue(tag, attribute);

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTagPattern();

    [GeneratedRegex(@"<a\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorTagPattern();
}
