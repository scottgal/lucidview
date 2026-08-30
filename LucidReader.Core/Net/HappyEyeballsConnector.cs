using System.Net;
using System.Net.Sockets;

namespace LucidReader.Core.Net;

/// <summary>
/// Opens the TCP connection under every HTTP request mylo makes, racing the
/// address families the way a browser does instead of trying one address and
/// waiting.
///
/// This exists because of a hang that made whole sites unusable. .NET's
/// SocketsHttpHandler connects to the first address DNS returns and waits for
/// it; there is no Happy Eyeballs in the framework, and ConnectTimeout
/// defaults to Infinite. news.ycombinator.com publishes an AAAA record that is
/// unreachable from many networks, so the connect attempt to it simply never
/// returned and the only thing that ever ended the request was the caller's
/// own per-operation budget: 30 seconds for feed discovery, 60 for a refresh,
/// 180 for an article download. Nothing about that is specific to that host.
/// Any dual-stack site whose IPv6 is unreachable from where the user is sitting
/// stalls for the whole budget, and on a network with no working IPv6 at all
/// that is most of the modern web.
///
/// The shape here follows RFC 8305 rather than being a flat race over every
/// address at once. The addresses are split by family, the preferred family
/// (IPv6, as the RFC asks) starts first, and the other family starts after a
/// short head start - or immediately, if every attempt in the first family has
/// already failed. That keeps the ordinary case honest: on a working IPv6
/// network nothing else is ever dialled, and on a broken one the cost of
/// finding that out is the head start rather than a stalled minute. A flat race
/// would work too, but it opens a connection to every address of every host on
/// every request, which is rude to the servers involved and pointless when the
/// first family answers.
///
/// Losers are closed, not abandoned. Once an attempt wins, the shared token is
/// cancelled and every other attempt is awaited to completion; any socket that
/// connected in the meantime is disposed before this returns.
/// </summary>
public sealed class HappyEyeballsConnector
{
    /// <summary>
    /// How long the preferred family gets on its own before the other family
    /// is dialled as well. RFC 8305 calls this the Connection Attempt Delay and
    /// recommends 250ms, with a floor of 100ms.
    ///
    /// The value is a trade. Too short and every request on a healthy network
    /// opens two connections where one would do. Too long and a dead IPv6
    /// address costs the user that long on every new connection.
    /// </summary>
    public static readonly TimeSpan DefaultFamilyHeadStart = TimeSpan.FromMilliseconds(250);

    private readonly TimeSpan _headStart;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<IPAddress, int, CancellationToken, Task<Socket>> _connect;

    public HappyEyeballsConnector(TimeSpan? familyHeadStart = null)
        : this(familyHeadStart, null, null)
    {
    }

    /// <summary>
    /// Test seam. The resolver and the connect step are injectable so the
    /// racing, the head start, the cancellation behaviour and the disposal of
    /// losers can all be exercised without depending on what the machine
    /// running the tests can reach. Internal rather than public: this is not
    /// part of the app's surface.
    /// </summary>
    internal HappyEyeballsConnector(
        TimeSpan? familyHeadStart,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve,
        Func<IPAddress, int, CancellationToken, Task<Socket>>? connect)
    {
        _headStart = familyHeadStart ?? DefaultFamilyHeadStart;
        _resolve = resolve ?? DefaultResolveAsync;
        _connect = connect ?? DefaultConnectAsync;
    }

    /// <summary>
    /// The delegate to hand to SocketsHttpHandler.ConnectCallback. Returns a
    /// NetworkStream that owns its socket, so disposing the stream (which the
    /// handler does when it retires the connection) closes the socket.
    /// </summary>
    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        var socket = await ConnectAsync(endPoint.Host, endPoint.Port, cancellationToken)
            .ConfigureAwait(false);

        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// Resolves the host and returns a connected socket, or throws. The caller
    /// owns the socket.
    /// </summary>
    public async Task<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var addresses = await _resolve(host, cancellationToken).ConfigureAwait(false);

        return await ConnectToAnyAsync(addresses, port, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The race itself, over an already-resolved address list. Public so it can
    /// be driven directly, which is also how it is tested: the interesting
    /// behaviour is here and none of it needs DNS.
    /// </summary>
    public async Task<Socket> ConnectToAnyAsync(
        IReadOnlyList<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (addresses.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        var (first, second) = SplitByFamily(addresses);

        // Every attempt reads this token, so cancelling it once a winner is
        // found is what stops the rest rather than leaving them to run on.
        using var attempts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var failures = new List<Exception>();
        var gate = new object();
        var running = new List<Task<Socket?>>(addresses.Count);

        foreach (var address in first)
            running.Add(AttemptAsync(address, port, failures, gate, attempts.Token));

        if (second.Count > 0)
        {
            await WaitForHeadStartAsync(running, cancellationToken).ConfigureAwait(false);

            // Skipped entirely when the preferred family has already answered.
            // This is the whole point of the head start: on a working network
            // the second family is never dialled at all.
            if (!running.Any(t => t.IsCompletedSuccessfully && t.Result is not null))
            {
                foreach (var address in second)
                    running.Add(AttemptAsync(address, port, failures, gate, attempts.Token));
            }
        }

        Socket? winner = null;
        var pending = new List<Task<Socket?>>(running);

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(finished);

            var socket = await finished.ConfigureAwait(false);
            if (socket is null) continue;

            winner = socket;
            break;
        }

        // Before the drain, not after: the losers are only going to finish
        // quickly if something tells them to stop.
        await attempts.CancelAsync().ConfigureAwait(false);

        foreach (var task in pending)
        {
            var loser = await task.ConfigureAwait(false);
            loser?.Dispose();
        }

        if (winner is not null) return winner;

        // A caller-requested stop reads as cancellation, not as a connect
        // failure: every attempt will have unwound with an
        // OperationCanceledException that AttemptAsync deliberately did not
        // record, so without this the throw below would be a SocketException
        // with nothing in it.
        cancellationToken.ThrowIfCancellationRequested();

        throw Describe(failures);
    }

    /// <summary>
    /// Waits for the preferred family's head start, or for that family to give
    /// up first, whichever comes sooner. A family whose addresses all fail in
    /// 3ms should not hold the other one back for the full 250.
    /// </summary>
    private async Task WaitForHeadStartAsync(
        IReadOnlyCollection<Task<Socket?>> running,
        CancellationToken cancellationToken)
    {
        if (running.Count == 0) return;

        using var headStart = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var delay = Task.Delay(_headStart, headStart.Token);
        await Task.WhenAny(Task.WhenAll(running), delay).ConfigureAwait(false);

        // Stops the timer when the family settled first, so a long head start
        // does not leave a pending Task.Delay behind for every request.
        await headStart.CancelAsync().ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// One attempt. Never throws: a failure is recorded and reported as a null
    /// socket, so a single dead address cannot fault the whole race before the
    /// other addresses have had their turn. The socket is disposed here on any
    /// failure, so the only socket that ever escapes this method is a connected
    /// one the caller is responsible for.
    /// </summary>
    private async Task<Socket?> AttemptAsync(
        IPAddress address,
        int port,
        List<Exception> failures,
        object gate,
        CancellationToken cancellationToken)
    {
        Socket? socket = null;
        try
        {
            socket = await _connect(address, port, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch (OperationCanceledException)
        {
            socket?.Dispose();

            // Not recorded. This is either the winner telling the losers to
            // stop or the caller cancelling, and neither is a reason this
            // address could not be reached.
            return null;
        }
        catch (Exception ex)
        {
            socket?.Dispose();
            lock (gate) failures.Add(ex);
            return null;
        }
    }

    /// <summary>
    /// Turns the collected failures into the one exception the caller sees. A
    /// single failure is handed back as itself, because for the common
    /// single-address case its message is already exactly right. Several
    /// failures still surface as a SocketException when every one of them was
    /// one, since a caller catching SocketException must not stop catching it
    /// just because the host happened to publish two addresses.
    /// </summary>
    private static Exception Describe(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 0) return new SocketException((int)SocketError.HostUnreachable);
        if (failures.Count == 1) return failures[0];

        if (failures.All(f => f is SocketException))
            return (SocketException)failures[0];

        return new AggregateException("Could not connect to any address for this host.", failures);
    }

    /// <summary>
    /// Splits the addresses into the family that goes first and the family that
    /// follows, preserving the order DNS gave within each.
    ///
    /// IPv6 leads, per RFC 8305 section 4, and that is the right way round even
    /// though a dead AAAA record is the reason this class exists: preferring
    /// IPv4 would quietly opt every user out of IPv6 forever, whereas leading
    /// with IPv6 costs a working network nothing and costs a broken one the
    /// head start.
    /// </summary>
    internal static (IReadOnlyList<IPAddress> First, IReadOnlyList<IPAddress> Second) SplitByFamily(
        IReadOnlyList<IPAddress> addresses)
    {
        var v6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();
        var v4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();

        return v6.Count > 0 ? (v6, v4) : (v4, v6);
    }

    private static async Task<IPAddress[]> DefaultResolveAsync(string host, CancellationToken ct)
    {
        // A literal address must not be put through DNS: GetHostAddressesAsync
        // would answer, but only after a round trip nobody needs.
        if (IPAddress.TryParse(host, out var literal)) return [literal];

        return await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
    }

    private static async Task<Socket> DefaultConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            // Nagle off, matching what SocketsHttpHandler's own connect does.
            // A request body written in two pieces should not wait on an ack.
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
