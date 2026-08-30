using System.Net;
using LucidReader.Core.Feeds;
using LucidReader.Core.Offline;
using Xunit;

namespace LucidReader.Core.Tests.Feeds;

/// <summary>
/// Every policy check elsewhere in the app runs before the request goes out,
/// on the first URL only. These cover the hop the checks could not see.
///
/// The last two use a real listener bound to loopback rather than a stub,
/// because the thing being proved is what the HTTP stack does with a
/// Location header, and a stub that never redirects cannot prove it. Nothing
/// leaves this machine.
/// </summary>
public class PolicyHttpHandlerTests
{
    private static HttpClient CreateClient(HttpMessageHandler inner, int maxRedirects = 5) =>
        new(new PolicyHttpHandler(inner, maxRedirects));

    [Fact]
    public async Task A_request_to_a_private_address_never_reaches_the_inner_handler()
    {
        var inner = StubHttpHandler.Returning(HttpStatusCode.OK, "secret");
        using var client = CreateClient(inner);

        await Assert.ThrowsAsync<PolicyHttpHandler.RefusedException>(
            () => client.GetAsync("http://169.254.169.254/latest/meta-data/"));

        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task An_ordinary_public_request_passes_straight_through()
    {
        var inner = StubHttpHandler.Returning(HttpStatusCode.OK, "hello");
        using var client = CreateClient(inner);

        var response = await client.GetAsync("https://example.com/feed.xml");

        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task A_redirect_onto_a_private_address_is_refused_and_never_requested()
    {
        var inner = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.Host == "example.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("http://127.0.0.1:9200/_cluster/settings");
                return redirect;
            }

            return StubHttpHandler.Response("internal", "application/json");
        });
        using var client = CreateClient(inner);

        await Assert.ThrowsAsync<PolicyHttpHandler.RefusedException>(
            () => client.GetAsync("https://example.com/feed.xml"));

        Assert.Single(inner.Requests);
        Assert.Equal("example.com", inner.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task A_public_redirect_is_followed_and_reported_as_the_final_address()
    {
        var inner = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/old")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("https://example.com/new");
                return redirect;
            }

            return StubHttpHandler.Response("moved here", "application/rss+xml");
        });
        using var client = CreateClient(inner);

        var response = await client.GetAsync("https://example.com/old");

        Assert.Equal("moved here", await response.Content.ReadAsStringAsync());
        Assert.Equal("https://example.com/new", response.RequestMessage!.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_redirect_loop_stops_at_the_hop_limit()
    {
        var inner = new StubHttpHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://example.com/again");
            return redirect;
        });
        using var client = CreateClient(inner, maxRedirects: 3);

        await Assert.ThrowsAsync<PolicyHttpHandler.RefusedException>(
            () => client.GetAsync("https://example.com/start"));

        // The first request plus three follows, and then it gives up.
        Assert.Equal(4, inner.Requests.Count);
    }

    [Fact]
    public async Task The_headers_a_caller_set_survive_a_redirect()
    {
        var inner = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/old")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://example.com/new");
                return redirect;
            }

            return StubHttpHandler.Response("body", "text/html");
        });
        using var client = CreateClient(inner);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/old");
        request.Headers.TryAddWithoutValidation("User-Agent", "lucidREADER-test");
        await client.SendAsync(request);

        var followed = inner.Requests[1];
        Assert.Equal("lucidREADER-test", string.Join("", followed.Headers.GetValues("User-Agent")));
    }

    /// <summary>
    /// The reviewer's own reproduction, over a real socket: a policy-clean
    /// address that answers 302 with a loopback Location. The connection is
    /// routed to a loopback listener by a ConnectCallback rather than by DNS,
    /// so the request really is made, really is answered, and nothing leaves
    /// this machine; what the fetch sees is a genuinely public-looking first
    /// hop, which is the case the pre-request checks cannot cover.
    ///
    /// Before this handler existed, the body served at /secret came back as
    /// though it were the article and was stored as such.
    /// </summary>
    [Fact]
    public async Task A_real_redirect_onto_a_private_address_yields_nothing_to_the_article_fetcher()
    {
        using var server = new LoopbackHttpServer();

        var fetcher = new ArticleFetcher(new HttpClient(
            new PolicyHttpHandler(server.ConnectedHandler(allowAutoRedirect: false))));

        var fetched = await fetcher.FetchArticleAsync(LoopbackHttpServer.PublicUrl);

        Assert.Null(fetched);
        Assert.False(server.SecretWasServed);
    }

    /// <summary>
    /// The same server with the handler configuration this app used to have.
    /// Proves the redirect is real and the internal body genuinely reachable,
    /// so the assertion above is about the handler rather than about a
    /// listener nothing could have talked to anyway.
    /// </summary>
    [Fact]
    public async Task The_same_redirect_is_followed_when_nothing_validates_the_hops()
    {
        using var server = new LoopbackHttpServer();
        using var client = new HttpClient(server.ConnectedHandler(allowAutoRedirect: true));

        var body = await client.GetStringAsync(LoopbackHttpServer.PublicUrl);

        Assert.Equal(LoopbackHttpServer.SecretBody, body);
        Assert.True(server.SecretWasServed);
    }

    /// <summary>
    /// A minimal HTTP/1.1 server on 127.0.0.1 speaking two paths: /public
    /// answers 302 with a Location of http://127.0.0.1:port/secret, and
    /// /secret answers a body no policy-respecting fetch should ever see.
    /// Written over TcpListener rather than HttpListener because the requests
    /// carry a Host header of a name that does not resolve, which is how the
    /// first hop is made to look public without any DNS involved.
    /// </summary>
    private sealed class LoopbackHttpServer : IDisposable
    {
        public const string SecretBody = "internal-only";

        private readonly System.Net.Sockets.TcpListener _listener;
        private readonly Task _loop;
        private volatile bool _stopped;

        public LoopbackHttpServer()
        {
            _listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(ServeAsync);
        }

        public int Port { get; }

        /// <summary>
        /// A name that resolves nowhere. Nothing ever looks it up: the
        /// handler below connects every request to the loopback listener.
        /// </summary>
        public const string PublicUrl = "http://feeds.public.invalid/public";

        public bool SecretWasServed { get; private set; }

        public SocketsHttpHandler ConnectedHandler(bool allowAutoRedirect) =>
            new()
            {
                AllowAutoRedirect = allowAutoRedirect,
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
                    await socket.ConnectAsync(IPAddress.Loopback, Port, ct);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
            };

        private async Task ServeAsync()
        {
            while (!_stopped)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; }

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        await using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, leaveOpen: true);
                        var requestLine = await reader.ReadLineAsync() ?? "";

                        var body = requestLine.Contains("/secret", StringComparison.Ordinal)
                            ? SecretBody
                            : null;
                        if (body is not null) SecretWasServed = true;

                        var response = body is not null
                            ? "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n" +
                              $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"
                            : $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{Port}/secret\r\n" +
                              "Content-Length: 0\r\nConnection: close\r\n\r\n";

                        var bytes = System.Text.Encoding.ASCII.GetBytes(response);
                        await stream.WriteAsync(bytes);
                        await stream.FlushAsync();
                    }
                });
            }
        }

        public void Dispose()
        {
            _stopped = true;
            _listener.Stop();
            try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        }
    }
}
