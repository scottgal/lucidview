using System.Net;
using System.Net.Http.Headers;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Serves canned responses and records the requests it saw, so tests can
/// assert on conditional headers without touching the network.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _respond;
    private readonly TaskCompletionSource<HttpResponseMessage>? _gate;
    private readonly bool _blockUntilCancelled;

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    private StubHttpHandler(TaskCompletionSource<HttpResponseMessage> gate) =>
        _gate = gate;

    private StubHttpHandler(bool blockUntilCancelled) =>
        _blockUntilCancelled = blockUntilCancelled;

    public static StubHttpHandler Returning(
        HttpStatusCode status,
        string? body = null,
        string? etag = null,
        string? lastModified = null) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (body is not null) response.Content = new StringContent(body);
            if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
            if (lastModified is not null)
                response.Content?.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
            return response;
        });

    /// <summary>
    /// Serves raw bytes with an explicit (or absent) charset on the
    /// Content-Type header, for exercising body-encoding detection: the
    /// happy-path Returning() above always goes through StringContent, which
    /// forces UTF-8 and can't represent "no charset header at all".
    /// </summary>
    public static StubHttpHandler ReturningBytes(
        byte[] body,
        HttpStatusCode status = HttpStatusCode.OK,
        string? mediaType = "application/xml",
        string? charset = null) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body)
            };
            if (mediaType is not null)
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType)
                {
                    CharSet = charset
                };
            return response;
        });

    public static StubHttpHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    /// <summary>
    /// Records the request, then hangs until the request's own cancellation
    /// token is cancelled, at which point HttpClient.SendAsync surfaces that
    /// as an OperationCanceledException - the same shape a genuinely stalled
    /// server produces. Used to exercise a per-fetch timeout without any real
    /// network delay.
    /// </summary>
    public static StubHttpHandler Blocking() => new(blockUntilCancelled: true);

    /// <summary>
    /// Records the request, then waits for the test to complete the returned
    /// TaskCompletionSource before responding. Used to land a database write
    /// from outside the handler at a precise point mid-fetch, rather than
    /// racing a Task.Delay against the refresh.
    /// </summary>
    public static (StubHttpHandler Handler, TaskCompletionSource<HttpResponseMessage> Gate) Gated()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return (new StubHttpHandler(gate), gate);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_gate is not null)
            return await _gate.Task.WaitAsync(cancellationToken);

        if (_blockUntilCancelled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return _respond!(request);
    }

    public HttpClient CreateClient() => new(this);
}
