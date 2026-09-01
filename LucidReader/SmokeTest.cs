using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LucidReader.Core.Feeds;
using LucidReader.Core.Model;
using LucidReader.Core.Net;

namespace LucidReader;

/// <summary>
/// What `mylo --smoke-test` runs. Exercises the packaged binary end to end and
/// exits 0 or 1, so a packaging script can refuse to ship a build that cannot
/// do its job.
///
/// WHY THIS EXISTS, since a test entry point in a Release binary is otherwise
/// against the rule that dev and test tooling stays in Debug.
///
/// 0.2.4 shipped a build that aborted the process on its first HTTPS request.
/// EnableCompressionInSingleFile corrupted the published binary (the whole
/// story is on the property in LucidReader.csproj), so every install seeded
/// five starter feeds, fetched nothing, and died on Refresh All. The unit
/// suite was 1514 green the entire time, and it could not have been anything
/// else: the fault existed only in the published single-file artifact, and
/// nothing anywhere ran that artifact. `dotnet test` runs loose assemblies,
/// where the same source is fine, and CI's verification step checked that the
/// executable existed and printed its size.
///
/// So the gap was not a missing assertion. It was that the thing we ship was
/// never once executed before we shipped it. The only way to close that is to
/// run the shipped file, which means the shipped file needs a way to be run
/// without a human at a window. That is this.
///
/// WHAT IT COVERS, chosen to be the things that only break in a real build:
///
///   - a TLS handshake, through SslStream and X509Chain, which is precisely
///     where 0.2.4 died (X509ChainPolicy.get_ExtraStore)
///   - HappyEyeballsConnector, configured as ReaderServices configures it, so
///     the custom ConnectCallback under every request is on the path
///   - SQLite through its native library, by opening a real database and
///     running the migrations
///   - the feed parser, on a real document off a real socket
///   - a write and a read back through the real repositories
///
/// It is deliberately NOT a substitute for the unit suite. It asserts almost
/// nothing about behaviour. It answers one question: does this binary work at
/// all. That question had no owner, and the answer was no for a whole release.
///
/// TWO PHASES, and the split is not tidiness.
///
///   - The loopback phase talks to an HTTPS server inside this process, with a
///     certificate generated here and thrown away at exit. It needs no network
///     and it catches gross breakage: a missing native library, a database
///     that will not open, a parser that cannot read its own document.
///   - The network phase makes concurrent requests to a real host, because
///     that is the only thing measured to catch the 0.2.4 fault. The loopback
///     phase was tried against a deliberately broken build first and passed
///     it. See <see cref="RealNetworkAsync"/> for the numbers and why a real
///     certificate chain turns out to be the part that matters.
/// </summary>
internal static class SmokeTest
{
    /// <summary>
    /// Whole-run budget. Generous, because a cold single-file binary on a
    /// loaded CI runner spends real time before it reaches any of this, and
    /// tight enough that a hang fails the packaging step rather than sitting
    /// on a runner until the job times out with nothing said about why.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);

    private const string FeedPath = "/smoke/feed.xml";

    /// <summary>
    /// Concurrent requests in the network phase.
    ///
    /// Five, because five is what was measured to reproduce. The 0.2.4 fault
    /// is memory corruption, so whether it surfaces depends on how hard the
    /// path is driven, and the boundary was established by experiment against
    /// a deliberately broken build rather than guessed:
    ///
    ///   one request to a real host           passes (does NOT catch it)
    ///   five concurrent to one real host     aborts (catches it)
    ///   ninety-six concurrent to loopback    passes (does NOT catch it)
    ///
    /// The last line is the important one and it is why this file has a
    /// network phase at all. See <see cref="RealNetworkAsync"/>.
    /// </summary>
    private const int NetworkConcurrency = 5;

    /// <summary>
    /// The host the network phase talks to. The maintainer's own site, which
    /// is already one of the starter feeds, so this depends on infrastructure
    /// this project controls rather than on a third party's goodwill.
    /// Overridable so a fork, or a runner behind a proxy with one address
    /// allowed, can point it somewhere else.
    /// </summary>
    private static string NetworkProbeUrl =>
        Environment.GetEnvironmentVariable("MYLO_SMOKE_URL")
        ?? "https://www.mostlylucid.net/rss";

    /// <summary>
    /// The document the loopback server returns. Small, and shaped so the
    /// assertions below can be exact: two items, both with a guid, so a parse
    /// that silently produced nothing cannot pass.
    /// </summary>
    private const string FeedXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <title>mylo smoke feed</title>
            <link>https://smoke.invalid/</link>
            <description>Not a real publication.</description>
            <item>
              <title>First smoke item</title>
              <link>https://smoke.invalid/one</link>
              <guid isPermaLink="false">smoke-item-1</guid>
              <pubDate>Mon, 01 Sep 2025 10:00:00 GMT</pubDate>
              <description>One.</description>
            </item>
            <item>
              <title>Second smoke item</title>
              <link>https://smoke.invalid/two</link>
              <guid isPermaLink="false">smoke-item-2</guid>
              <pubDate>Mon, 01 Sep 2025 11:00:00 GMT</pubDate>
              <description>Two.</description>
            </item>
          </channel>
        </rss>
        """;

    public static async Task<int> RunAsync()
    {
        // Its own directory, and never the user's profile: this writes a
        // database and must not be able to touch a real one, on a developer's
        // machine or on a runner that reuses a home directory between jobs.
        var profile = Path.Combine(
            Path.GetTempPath(),
            "mylo-smoke-" + Guid.NewGuid().ToString("n"));

        using var deadline = new CancellationTokenSource(Budget);
        var started = Stopwatch.StartNew();

        Console.Out.WriteLine($"[smoke] mylo smoke test, profile {profile}");

        try
        {
            Directory.CreateDirectory(profile);
            await RunStepsAsync(profile, deadline.Token);

            Console.Out.WriteLine($"[smoke] PASS in {started.ElapsedMilliseconds} ms");
            return 0;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"[smoke] FAIL: did not finish within {Budget.TotalSeconds:0} seconds");
            return 1;
        }
        catch (Exception ex)
        {
            // Full exception, not just the message. This output is the only
            // thing a person looking at a failed packaging run will have.
            Console.Error.WriteLine($"[smoke] FAIL: {ex}");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
            }
            catch (Exception ex)
            {
                // Losing a temp directory is not a reason to fail a build that
                // otherwise passed, but say so rather than swallowing it.
                Console.Error.WriteLine($"[smoke] could not remove {profile}: {ex.Message}");
            }
        }
    }

    private static async Task RunStepsAsync(string profile, CancellationToken ct)
    {
        // FIRST, before anything else touches TLS, and that ordering is the
        // whole difference between a check that works and one that does not.
        //
        // Measured, not assumed. With this phase running after the loopback
        // server had already completed a handshake, the deliberately broken
        // build PASSED three times out of three. Moved to the front, it aborts.
        // The fault needs the TLS and certificate code to be COLD: the loopback
        // handshake warms the very paths the corruption sits in, and a warmed
        // path does not fault. That also matches how the bug reached users -
        // the app died on its first HTTPS request after launch, not its tenth.
        //
        // So: nothing may be added above this line that opens a TLS connection.
        await RealNetworkAsync(ct);

        using var certificate = CreateLoopbackCertificate();
        using var server = new LoopbackFeedServer(certificate);

        var url = server.Start();
        Console.Out.WriteLine($"[smoke] loopback https server on {url}");

        // The database, through the real open path: creates the file, runs
        // every migration, and loads the native SQLite library. A packaged
        // build that cannot find or load libe_sqlite3 fails here.
        await using var services = await ReaderServices.StartAsync(
            Path.Combine(profile, "reader.db"),
            Path.Combine(profile, "settings.json"),
            ct: ct);

        Console.Out.WriteLine("[smoke] database opened and migrated");

        // The loopback fetch. Gross breakage only: this phase was measured
        // NOT to catch the 0.2.4 fault even at ninety-six concurrent
        // handshakes, which is why RealNetworkAsync above exists and runs
        // first.
        using var http = CreateClient(certificate);

        var document = await http.GetStringAsync(url, ct);

        if (document.Length == 0)
            throw new InvalidOperationException("the loopback server returned an empty body");

        Console.Out.WriteLine($"[smoke] fetched {document.Length} bytes over TLS");

        // The parser, on what actually came off the socket rather than on a
        // string constant, so the encoding handling is on the path too.
        var parser = new FeedParser();
        if (!parser.CanParse(document))
            throw new InvalidOperationException("the parser did not recognise its own smoke feed");

        var parsed = parser.Parse(document, new Uri(url));

        if (parsed.Items.Count != 2)
            throw new InvalidOperationException(
                $"expected 2 parsed items, got {parsed.Items.Count}");

        Console.Out.WriteLine($"[smoke] parsed '{parsed.Title}' with {parsed.Items.Count} items");

        // A write and a read back, through the same repositories the app uses,
        // so a build whose storage layer is broken cannot pass by having
        // merely fetched something.
        var feedId = await services.Feeds.AddAsync(new Feed
        {
            FeedUrl = url,
            Title = parsed.Title,
            SiteUrl = parsed.SiteUrl
        }, ct);

        var written = 0;
        foreach (var item in parsed.Items)
        {
            await services.Items.UpsertAsync(new FeedItem
            {
                FeedId = feedId,
                Guid = item.Guid ?? item.Link ?? Guid.NewGuid().ToString(),
                Link = item.Link,
                Title = item.Title,
                Summary = item.Summary,
                PublishedUtc = item.PublishedUtc,
                FirstSeenUtc = DateTimeOffset.UtcNow
            }, ct);
            written++;
        }

        var storedFeeds = await services.Feeds.GetAllAsync(ct);
        if (storedFeeds.Count != 1)
            throw new InvalidOperationException(
                $"expected 1 stored feed, got {storedFeeds.Count}");

        Console.Out.WriteLine($"[smoke] stored {written} items against feed {feedId}");
    }

    /// <summary>
    /// Concurrent HTTPS against a real host, which is the phase that actually
    /// catches the fault this file was written for.
    ///
    /// This started out hermetic, on the reasoning that a check reaching the
    /// internet goes red for reasons unrelated to the build and then gets
    /// switched off. That reasoning is still right, and it still lost, because
    /// the hermetic version was measured against a deliberately broken build
    /// and PASSED it. Ninety-six concurrent handshakes against the loopback
    /// server in this process were not enough; five against a real host abort
    /// immediately.
    ///
    /// The difference is what a real handshake does that a self-signed one on
    /// loopback does not: build and validate a genuine certificate chain
    /// against the operating system's trust store. That is exactly where 0.2.4
    /// died, at X509ChainPolicy.get_ExtraStore, and no amount of loopback
    /// traffic goes near it. A hermetic check that cannot detect the thing it
    /// exists to detect is not a safer check, it is a decorative one.
    ///
    /// Absence of network is tolerated, deliberately loudly. A runner with no
    /// egress is a real situation and failing the build for it would get this
    /// disabled within a week, but a silent skip is how a check quietly stops
    /// protecting anything, so it says plainly that the build went out
    /// unverified.
    /// </summary>
    private static async Task RealNetworkAsync(CancellationToken ct)
    {
        var url = NetworkProbeUrl;

        // The app's own handler configuration, this time with the certificate
        // validation left entirely alone: the default path through SslStream
        // and X509Chain is the code under test, so replacing any of it would
        // defeat the phase.
        using var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = new HappyEyeballsConnector().ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        // The burst goes FIRST. Not one request to check the network is up and
        // then a burst: that was tried, and it passed the broken build three
        // times out of three, because the single request warms the very code
        // the corruption sits in and a warmed path does not fault.
        //
        // The fault needs concurrent handshakes through COLD TLS and
        // certificate code. So these requests have to be the first network
        // activity this process performs, and whether the host was reachable
        // has to be worked out afterwards, from how they failed.
        var results = new Task<bool>[NetworkConcurrency];
        for (var i = 0; i < results.Length; i++) results[i] = Attempt(http, url, ct);

        var reached = await Task.WhenAll(results);

        // Surviving is the assertion. A build that cannot do this does not
        // reach the next line, because an AccessViolationException terminates
        // the process rather than being catchable.
        if (reached.Any(ok => ok))
        {
            Console.Out.WriteLine(
                $"[smoke] survived {NetworkConcurrency} concurrent cold TLS handshakes");
            return;
        }

        // Nothing got through. Tolerated, because a runner with no egress is a
        // real situation and failing for it would get this check deleted, but
        // never quietly: a silent skip is how a check stops protecting
        // anything while still looking green.
        Console.Out.WriteLine(
            $"[smoke] WARNING: no request to {url} succeeded. The network phase " +
            "did not run, so this build has NOT been checked against the failure " +
            "that shipped in 0.2.4. Set MYLO_SMOKE_URL to a reachable address to " +
            "restore the check.");

        static async Task<bool> Attempt(HttpClient http, string url, CancellationToken ct)
        {
            try
            {
                using var response = await http.GetAsync(url, ct);
                await response.Content.ReadAsByteArrayAsync(ct);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // A rate limit, a reset, a slow response, or no network at
                // all. None of these say anything about whether this binary
                // works, and the caller decides what a whole failed batch
                // means.
                return false;
            }
        }
    }

    /// <summary>
    /// An HttpClient configured the way ReaderServices configures the app's,
    /// minus PolicyHttpHandler. The gate is left out because it is doing its
    /// job: FeedUrlPolicy refuses loopback addresses on purpose, so a request
    /// through it could never reach a server in this process. What matters
    /// here is that HappyEyeballsConnector, the custom ConnectCallback under
    /// every real request, is on the path.
    ///
    /// The certificate check is replaced by an exact match against the
    /// certificate this process just generated. Not a blanket accept: an
    /// unconditional "return true" would leave a binary shipping with a
    /// callback that trusts anything, and this is the one part of the app
    /// where being casual about that is worst. The custom callback does not
    /// skip the code 0.2.4 died in - SslStream still builds the chain and
    /// calls CertificateValidationPal.GetRemoteCertificate before it consults
    /// any callback.
    /// </summary>
    private static HttpClient CreateClient(X509Certificate2 expected)
    {
        var expectedThumbprint = expected.Thumbprint;

        return new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = new HappyEyeballsConnector().ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, presented, _, _) =>
                    presented is X509Certificate2 c && c.Thumbprint == expectedThumbprint
            }
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// A self-signed certificate for loopback, valid for the few seconds this
    /// process needs it. Exported and reloaded as PKCS#12 because a
    /// certificate straight out of CreateSelfSigned is not usable as a server
    /// credential on every platform, and the round trip is what gives it a key
    /// SslStream will accept.
    /// </summary>
    private static X509Certificate2 CreateLoopbackCertificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=mylo-smoke-test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(1));

        return X509CertificateLoader.LoadPkcs12(
            generated.Export(X509ContentType.Pfx), password: null);
    }

    /// <summary>
    /// The smallest HTTPS server that can answer one request.
    ///
    /// A TcpListener and an SslStream rather than HttpListener, because
    /// HttpListener's HTTPS support needs a certificate bound at the operating
    /// system level, which is not something a packaging script can arrange on
    /// a runner. Speaking HTTP/1.1 by hand is a dozen lines and works
    /// identically on all three platforms.
    ///
    /// Dual mode, so the listener answers on both 127.0.0.1 and ::1. That is
    /// not incidental: HappyEyeballsConnector splits the resolved addresses by
    /// family and races them, and "localhost" resolves to both, so a
    /// single-family listener would leave half of the connector untested.
    /// </summary>
    private sealed class LoopbackFeedServer(X509Certificate2 certificate) : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.IPv6Any, 0);
        private readonly CancellationTokenSource _stopping = new();
        private Task? _loop;

        public string Start()
        {
            _listener.Server.DualMode = true;
            _listener.Start();

            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(() => AcceptLoopAsync(_stopping.Token));

            return $"https://localhost:{port}{FeedPath}";
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // The listener is closing, or a connection died between
                    // being queued and being accepted. Neither ends the loop.
                    continue;
                }

                // Not awaited: the connector opens more than one connection
                // when it races families, and a serial accept loop would leave
                // the loser waiting on a handshake that never comes.
                _ = ServeAsync(client, ct);
            }
        }

        private async Task ServeAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                    await tls.AuthenticateAsServerAsync(certificate, false, checkCertificateRevocation: false);

                    await ReadRequestHeadAsync(tls, ct);

                    var body = Encoding.UTF8.GetBytes(FeedXml);
                    var head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: application/rss+xml; charset=utf-8\r\n" +
                        $"Content-Length: {body.Length}\r\n" +
                        "Connection: close\r\n" +
                        "\r\n");

                    await tls.WriteAsync(head, ct);
                    await tls.WriteAsync(body, ct);
                    await tls.FlushAsync(ct);
                }
                catch (Exception)
                {
                    // A connection the client abandoned mid-handshake is the
                    // normal outcome for the losing half of the family race.
                    // The run is judged by what the client got, not here.
                }
            }
        }

        /// <summary>
        /// Reads until the blank line that ends the request head, so the reply
        /// is not written into a socket the client is still writing to. The
        /// body is ignored: this only ever answers GETs.
        /// </summary>
        private static async Task ReadRequestHeadAsync(SslStream tls, CancellationToken ct)
        {
            var buffer = new byte[4096];
            var seen = new StringBuilder();

            while (seen.Length < 16 * 1024)
            {
                var read = await tls.ReadAsync(buffer, ct);
                if (read == 0) return;

                seen.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (seen.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) return;
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Dispose();

            try
            {
                _loop?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Already reported by whatever the run itself concluded.
            }

            _stopping.Dispose();
        }
    }
}
