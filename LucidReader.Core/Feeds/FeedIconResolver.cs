using System.Text;
using LucidReader.Core.Model;

namespace LucidReader.Core.Feeds;

/// <summary>
/// Finds an icon for a subscription that has none.
///
/// Icons used to be discovered in exactly one place, <see cref="FeedAutodiscovery"/>,
/// and so only ever for feeds added through the Add Feed dialog. Everything
/// else - the starter subscriptions a first run seeds, an OPML import, a feed
/// URL pasted straight in, anything the catalogue adds - was written with a
/// null icon_path and stayed that way forever, showing the grey placeholder in
/// the sidebar for the life of the profile. There was no path that could ever
/// fix it, because nothing but the add dialog ever looked.
///
/// This runs on refresh instead, which every subscription goes through however
/// it was created. Three sources, cheapest first:
///
///   1. The feed's own icon - an RSS channel's image, an Atom icon or logo.
///      Free: it came in the document the refresh just fetched and parsed.
///   2. The site's declared icon, read out of the home page by
///      <see cref="SiteMetadataExtractor"/>. One request, and the only one this
///      class ever makes.
///   3. /favicon.ico at the site's host, or the feed's own host when there is
///      no site link. A URL to record rather than a fetch to perform - whether
///      it resolves to an image is the rendering layer's problem, exactly as it
///      is for the guess FeedAutodiscovery already records.
///
/// Every candidate goes through <see cref="FeedUrlPolicy"/>, including the ones
/// that came out of the feed document and the site page: these are addresses
/// the app will fetch unattended, from remote content, which is the whole
/// reason that gate exists.
///
/// Gated on CacheImages. With image caching off, <c>ImageResolver</c> refuses
/// to fetch a favicon at all, so an icon recorded here could never be shown; a
/// request made to find one would be a request made for nothing.
/// </summary>
public sealed class FeedIconResolver(HttpClient http, Func<ReaderSettings> settings)
{
    /// <summary>
    /// How much of a home page is read before the attempt is abandoned. An icon
    /// link lives in the head, so this never needs to be generous, and the
    /// budget is what keeps a hostile or broken server from being able to spend
    /// this app's memory on a best-effort nicety.
    /// </summary>
    private const int MaxPageBytes = 512 * 1024;

    private const int ReadBufferSize = 8192;

    /// <summary>
    /// How long the one page fetch is given. Short on purpose: this is
    /// housekeeping running alongside a refresh, and a slow host must cost the
    /// refresh nothing.
    /// </summary>
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// An icon URL for this feed, or null if none could be settled on.
    ///
    /// Never throws. A caller is doing this beside real work and the answer is
    /// a nicety; a failure here must not become a failed refresh, so everything
    /// below funnels into null. Cancellation is the exception, and is allowed
    /// out: that is the app stopping, not this lookup failing.
    /// </summary>
    public async Task<string?> ResolveAsync(
        string feedUrl,
        string? siteUrl,
        string? feedDeclaredIconUrl,
        CancellationToken ct = default)
    {
        if (!settings().CacheImages) return null;

        if (Allowed(feedDeclaredIconUrl) is { } declared) return declared;

        var siteBase = Absolute(siteUrl) ?? Absolute(feedUrl);
        if (siteBase is null) return null;

        if (await SiteDeclaredIconAsync(siteBase, ct) is { } fromPage) return fromPage;

        return Allowed($"{siteBase.Scheme}://{siteBase.Authority}/favicon.ico");
    }

    /// <summary>
    /// The icon the site's own page declares. One GET, bounded in size and in
    /// time, parsed by the same extractor FeedAutodiscovery uses on a page it
    /// had already downloaded.
    /// </summary>
    private async Task<string?> SiteDeclaredIconAsync(Uri siteBase, CancellationToken ct)
    {
        if (!FeedUrlPolicy.IsAllowed(siteBase.ToString())) return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PageTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, siteBase);
            request.Headers.TryAddWithoutValidation("User-Agent", FeedFetcher.UserAgentString);
            request.Headers.TryAddWithoutValidation("Accept", "text/html;q=0.9, */*;q=0.5");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > MaxPageBytes) return null;

            var body = await ReadBoundedAsync(response.Content, timeout.Token);
            if (body is null) return null;

            // Resolved against wherever the response actually came from: a
            // redirect from a bare domain to www means a relative icon href
            // belongs to the second host, not the first.
            var effective = response.RequestMessage?.RequestUri ?? siteBase;
            return Allowed(SiteMetadataExtractor.Extract(body, effective).IconUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Including our own PageTimeout firing. There are two cheaper
            // sources either side of this one; none of them is worth a failed
            // refresh.
            return null;
        }
    }

    private static string? Allowed(string? url) =>
        FeedUrlPolicy.TryValidate(url, out var uri, out _) && uri is not null
            ? uri.ToString()
            : null;

    private static Uri? Absolute(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    /// <summary>
    /// Reads the body a chunk at a time and gives up the moment it passes
    /// MaxPageBytes, rather than trusting Content-Length - a chunked response
    /// never sets that header. Mirrors FeedAutodiscovery.ReadBoundedAsync.
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
            if (buffer.Length > MaxPageBytes) return null;
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
