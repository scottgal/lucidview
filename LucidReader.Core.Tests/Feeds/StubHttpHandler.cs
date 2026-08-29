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

    private readonly Lock _sync = new();
    private readonly List<HttpRequestMessage> _requests = [];

    /// <summary>
    /// A snapshot of what has been asked for so far. A copy, and taken under
    /// the same lock the handler records under, because discovery now fires
    /// several probes concurrently and a bare List would be torn by that.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_sync) return _requests.ToList();
        }
    }

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
        string? lastModified = null,
        string? mediaType = null,
        Uri? finalRequestUri = null) =>
        new(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (body is not null) response.Content = new StringContent(body);
            if (mediaType is not null && response.Content is not null)
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
            if (lastModified is not null)
                response.Content?.Headers.TryAddWithoutValidation("Last-Modified", lastModified);
            // Simulates the request URI HttpClient reports after following
            // redirects: real redirect-following handlers set
            // response.RequestMessage to the final request, not the one the
            // caller originally issued. This stub never follows redirects
            // itself, so a test that needs "the final URI differs from the
            // one we started with" sets this directly.
            if (finalRequestUri is not null)
                response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalRequestUri);
            return response;
        });

    /// <summary>
    /// Serves a body of the given byte count with no Content-Length header,
    /// the same shape a chunked-transfer-encoded response takes: content
    /// whose length is unknown until fully read. Used to prove a size cap
    /// that only checks Content-Length would miss it entirely.
    /// </summary>
    public static StubHttpHandler ReturningUnboundedLength(
        long byteCount, string mediaType = "text/html") =>
        new(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new RepeatingStream((byte)'a', byteCount))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return response;
        });

    /// <summary>
    /// A non-seekable stream that yields <paramref name="length"/> bytes of a
    /// single repeated value. Non-seekable so StreamContent cannot compute a
    /// Content-Length from it (StreamContent only sets that header when the
    /// underlying stream reports CanSeek), which is exactly the shape a real
    /// chunked response takes.
    /// </summary>
    private sealed class RepeatingStream(byte value, long length) : Stream
    {
        private long _remaining = length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var toWrite = (int)Math.Min(count, _remaining);
            Array.Fill(buffer, value, offset, toWrite);
            _remaining -= toWrite;
            return toWrite;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

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

    /// <summary>
    /// Builds one canned response. Exists so a per-path handler can be
    /// written as a short switch over the request URI instead of six lines of
    /// header assembly per branch.
    /// </summary>
    public static HttpResponseMessage Response(
        string body,
        string mediaType,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

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
        lock (_sync) _requests.Add(request);

        if (_gate is not null)
            return await _gate.Task.WaitAsync(cancellationToken);

        if (_blockUntilCancelled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return _respond!(request);
    }

    public HttpClient CreateClient() => new(this);
}
