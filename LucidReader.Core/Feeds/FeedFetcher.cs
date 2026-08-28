using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LucidReader.Core.Feeds;

/// <summary>
/// One conditional GET. Does not parse and does not write to the database:
/// this class knows about HTTP and nothing else.
/// </summary>
public sealed class FeedFetcher(HttpClient http)
{
    public const string UserAgentString =
        "lucidREADER/1.0 (+https://www.mostlylucid.net)";

    // System.Text.Encoding.CodePages ships legacy code pages (windows-1252
    // among them) that are not registered by default on .NET Core and
    // later. Feeds routinely declare exactly these encodings in their XML
    // declaration, so the provider has to be live before FetchAsync ever
    // runs. Registering it here, once, statically, means every caller gets
    // correct decoding without having to know this detail exists.
    static FeedFetcher()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly Regex XmlDeclarationEncoding = new(
        """<\?xml[^>]*\bencoding\s*=\s*["']([^"']+)["']""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<FeedFetchResult> FetchAsync(
        string feedUrl,
        string? etag,
        string? lastModified,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new FeedFetchResult.Failed($"Not a usable feed URL: {feedUrl}", false);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgentString);
        request.Headers.TryAddWithoutValidation(
            "Accept", "application/atom+xml, application/rss+xml, application/xml;q=0.9, */*;q=0.8");

        if (!string.IsNullOrWhiteSpace(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        if (!string.IsNullOrWhiteSpace(lastModified)
            && DateTimeOffset.TryParse(
                lastModified, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var since))
            request.Headers.IfModifiedSince = since;

        try
        {
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new FeedFetchResult.NotModified();

            if (!response.IsSuccessStatusCode)
                return new FeedFetchResult.Failed(
                    $"{(int)response.StatusCode} {response.ReasonPhrase}",
                    IsTransient(response.StatusCode));

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var content = DecodeBody(bytes, response.Content.Headers.ContentType?.CharSet);

            return new FeedFetchResult.Fetched(
                content,
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified?.ToString("r"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked us to stop. That is not a feed failure, so it
            // must not be recorded as one.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation too.
            return new FeedFetchResult.Failed("The request timed out.", true);
        }
        catch (HttpRequestException ex)
        {
            return new FeedFetchResult.Failed(ex.Message, true);
        }
    }

    /// <summary>
    /// Decodes the raw response bytes into text using, in order: the
    /// charset declared on the HTTP Content-Type header; the encoding
    /// declared in the body's own XML declaration; a byte order mark if one
    /// is present; and finally UTF-8. ReadAsStringAsync only ever does the
    /// first and the last of those, so feeds that declare their encoding
    /// only in the XML declaration (common, and legal per the RSS/Atom
    /// specs) get silently mojibaked by it.
    /// </summary>
    internal static string DecodeBody(byte[] bytes, string? headerCharset)
    {
        var (bomEncoding, bomLength) = DetectBom(bytes);

        Encoding? encoding = null;
        if (!string.IsNullOrWhiteSpace(headerCharset))
            TryGetEncoding(headerCharset.Trim().Trim('"'), out encoding);

        if (encoding is null)
        {
            var declared = ExtractXmlDeclarationEncoding(bytes);
            if (declared is not null)
                TryGetEncoding(declared, out encoding);
        }

        encoding ??= bomEncoding ?? Encoding.UTF8;

        // Strip a recognised BOM regardless of which source won the
        // encoding, so it never gets decoded as a literal U+FEFF character
        // that leaks into the parsed title or content.
        return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
    }

    private static string? ExtractXmlDeclarationEncoding(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, 200);
        // Latin1 is used here only to read the ASCII characters that make
        // up an XML declaration; it says nothing about the encoding of the
        // document's actual content.
        var prefix = Encoding.Latin1.GetString(bytes, 0, length);
        var match = XmlDeclarationEncoding.Match(prefix);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static (Encoding? Encoding, int Length) DetectBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8, 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode, 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode, 2);
        return (null, 0);
    }

    private static bool TryGetEncoding(string name, out Encoding? encoding)
    {
        try
        {
            encoding = Encoding.GetEncoding(name);
            return true;
        }
        catch (ArgumentException)
        {
            // An unrecognised or unsupported charset name must not blow up
            // the fetch; falling through to UTF-8 is the safer default.
            encoding = null;
            return false;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
}
