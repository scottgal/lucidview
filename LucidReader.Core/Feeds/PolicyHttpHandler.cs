using System.Net;

namespace LucidReader.Core.Feeds;

/// <summary>
/// The last gate an outbound request passes, and the only one that sees
/// redirects.
///
/// Every policy check elsewhere in the app is a pre-request check on the first
/// URL only: OPML import checks the xmlUrl it is about to store, autodiscovery
/// checks the candidates it found, ArticleFetcher checks the link it was
/// given. A handler with AllowAutoRedirect on defeats all three at once, since
/// a policy-clean public address answering 302 with a Location of
/// http://127.0.0.1/ is followed silently and the internal body comes back as
/// though it were the response to the original request. Worse, the URL the app
/// then records for the feed is response.RequestMessage.RequestUri, the
/// post-redirect one, so the internal address is what ends up stored and
/// re-fetched on every scheduler tick.
///
/// So redirects are not followed by the inner handler at all. They are
/// followed here, one hop at a time, with <see cref="FeedUrlPolicy"/> applied
/// to every hop including the first. That also puts a policy check
/// underneath every caller in the app, whether or not the caller remembered
/// to make one.
/// </summary>
public sealed class PolicyHttpHandler(HttpMessageHandler inner, int maxRedirects = 5)
    : DelegatingHandler(inner)
{
    /// <summary>
    /// Thrown rather than returned as a status code so a refused address can
    /// never be mistaken for a server's own answer. Every caller in this app
    /// already turns an exception from SendAsync into "this fetch failed",
    /// which is the correct outcome here.
    /// </summary>
    public sealed class RefusedException(string message) : HttpRequestException(message);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await SendCheckedAsync(request, cancellationToken);

        for (var hop = 0; IsRedirect(response) && response.Headers.Location is not null; hop++)
        {
            if (hop >= maxRedirects)
            {
                response.Dispose();
                throw new RefusedException(
                    $"The address redirected more than {maxRedirects} times.");
            }

            // A relative Location is normal and resolves against the URL that
            // produced it, which is the one this hop was actually sent to.
            var target = new Uri(request.RequestUri!, response.Headers.Location);
            var next = CloneForRedirect(request, response.StatusCode, target);
            response.Dispose();
            request.Dispose();
            request = next;

            response = await SendCheckedAsync(request, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendCheckedAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (!FeedUrlPolicy.TryValidate(request.RequestUri?.ToString(), out _, out var reason))
            throw new RefusedException($"Refused to request {request.RequestUri}: {reason}.");

        var response = await base.SendAsync(request, ct);

        // Callers read response.RequestMessage.RequestUri to learn where the
        // body actually came from, and several of them store it (a discovered
        // feed's URL, the base a relative href resolves against). Since
        // redirects are followed here rather than by the inner handler, this
        // is the only place that knows which request produced this response.
        response.RequestMessage = request;
        return response;
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Builds the follow-up request. Headers are carried over because the
    /// callers in this app set a User-Agent and an Accept they need honoured
    /// at the destination too; the body is not, since 301, 302 and 303 all
    /// turn into a GET the way every browser and the BCL's own redirect
    /// handling do, and 307/308 preserve the method but are only ever used
    /// here for the GETs this app makes.
    /// </summary>
    private static HttpRequestMessage CloneForRedirect(
        HttpRequestMessage original, HttpStatusCode status, Uri target)
    {
        var method = status is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
            ? original.Method
            : HttpMethod.Get;

        var clone = new HttpRequestMessage(method, target) { Version = original.Version };

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
